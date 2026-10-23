# CLAUDE.md (JackyAIApp.Server)

Guidance specific to the backend package. See the [root CLAUDE.md](../CLAUDE.md) for architecture, features, and cross-cutting conventions.

## Development Commands

```bash
dotnet run                    # Start development server (https://localhost:7034)
dotnet build --configuration Release
dotnet test                   # JackyAIApp.Server.Tests — offline unit tests + LiveContract tests
dotnet publish --configuration Release
```

## Database Migrations (Entity Framework)

```bash
dotnet ef migrations add [MigrationName]
dotnet ef database update
```

- An entity's keys, relationships, indexes and column types go in its own `IEntityTypeConfiguration<T>`
  in `Data/Configurations` (one file per feature), never in `OnModelCreating`, which only applies them
- Changing an entity or a configuration needs a migration: `ModelMatchesMigrationsTests` fails while the
  model has changes no migration covers (startup's `Database.Migrate()` would refuse to run)
- A migration that fills a column it adds wraps the `UPDATE` in `EXEC('…')` (`migrationBuilder.Sql`): in a
  generated script both share one batch, which SQL Server compiles before the column exists. Try it on a
  copy holding real-shaped rows first (`dotnet ef migrations script <previous> <new>` against a local DB)

## Backend Structure

```
JackyAIApp.Server/
├── Controllers/    # API controllers (RESTful, base route: /api/[controller])
├── Services/       # Business logic with dependency injection
├── Data/           # EF Context, Models, Configurations, Migrations
├── DTO/            # Data transfer objects
├── Prompt/         # AI prompt templates by feature
└── Common/         # Error handling middleware, response factory
```

## Modules

The server is one project in modules, each a set of namespaces; `Architecture/ModuleBoundariesTests`
holds the map and fails on any use the table does not allow (a `using` or a fully qualified name in
code; comments do not count). A new `Services.Something` namespace is in no module until given one.

| Module | Namespaces | May use |
|---|---|---|
| Platform | `Common`, `Configuration`, `Data`, `Services` (only what is directly in it: current user, accounts, usage limits, connectors, `DrawdownBrakeAccess`), `Services.AiUsage`, `.OpenAI`, `.Prompt`, `.Speech`, `.ExternalApi` (the gateway) | — |
| MarketData | `Services.Market` (prices, history and backfill, fundamentals/chips/macro providers, TW symbol list, MIS TAIEX quote, trading calendar, `TwStockCode`), `Services.ExternalApi.Twse` | Platform |
| English | `Services.Dictionary`, `.Repository`, `.Exam`, `.Listening`, `.SpacedRepetition`, `.DailyChallenge`, `.WordOfTheDay`, `.SpellChecker`, `Mappers` | Platform |
| Finance | `Services.Finance` (analysis, indicators, scoring, predictions, digest), `Services.Chatbot` | MarketData, Platform |
| Portfolio | `Services.Portfolio` | MarketData, Platform |
| DataAnalysis | `Services.DataAnalysis` | Platform |
| Tools | `Services.Job` | Platform |

- Controllers, `Extensions` (the DI registration) and `Program` wire modules together and DTOs are plain
  data, so none is held to the table. A file that is about one feature lives in that feature's module,
  even a middleware or a filter (the word-page middlewares are English's)
- Data two features both need goes in a module both may use (that is why MarketData exists), not in one
  of them for the other to reach into
- The market-data namespace is `Services.Market`, not `Services.MarketData`: a namespace of that name
  under `Services` would hide the DTO class `MarketData` from every service
- One `AzureSQLDBContext` for all modules (each module's entities have their own configuration file),
  but each table belongs to one module and only its code touches it (`TableOwners` in the same test;
  a new `DbSet` needs an owner there). Another module asks the owner's services: prices come from
  MarketData, not from `DailyStockPrices`. A table holding two modules' data is split, as Portfolio's
  AI classification was out of `StockSymbolInfos` into `StockClassifications`
- `Users` is the one table every module may touch: the signed-in user's row. English also writes it,
  because the XP and streak fields live on it, a known exception (moving them to an English table
  would need a data migration). `User` has no navigation collections: a module's entity points at its
  user (`HasOne(x => x.User).WithMany()`), the user does not list them

## Controllers

- A controller does HTTP only: binding, routing attributes, answering a service's result with
  `responseFactory.ToActionResult(result)`. Queries, rules and writes live in a service. A
  controller never takes `AzureSQLDBContext` or `IDbContextFactory` — `ControllersDoNotTouchTheDatabaseTests`
  fails if one does
- An endpoint only admins may call takes `[AdminOnly]` (`Common/AdminOnlyAttribute.cs`, 403 for anyone
  else) next to `[Authorize]`; the action never checks `IsAdmin` itself. `AdminOnlyEndpointsTests` lists
  the admin endpoints, so adding or dropping one is a deliberate test change
- Who may see or change something is written once, in a policy next to its service (e.g.
  `DashboardAccessPolicy`): EF expressions for the queries, a plain method for an entity already
  loaded. Something you may not see answers NotFound, not Forbidden, so its existence isn't leaked

## Service results

- A service method that a controller turns into a response returns `ServiceResult<T>` with the
  real payload type: `ServiceResult.Ok(dto)`, `ServiceResult.Ok()` (no payload, answered as
  `{ "data": null }`), or `ServiceResult.Fail(code, message)`, which converts to any `T`. Pass a
  failure of another payload type on with `result.AsFailure()`. Controllers answer with
  `responseFactory.ToActionResult(result)`
- Never return an anonymous object or read `Data` through `dynamic`/casts: give the payload a
  record (its camelCase JSON is what the client reads)
- There is deliberately no implicit conversion from a payload to a result — with `T = object` it
  would accept anything, another `ServiceResult` included, and wrap it
- `OpenAIPromptService` keeps its own `(T? Result, string? Error)`: it is the AI client, with no
  HTTP error code of its own; the service calling it decides the `ErrorCodes`
- An `ErrorCodes` value's middle three digits are its HTTP status (`1040400` → 404): an upstream
  failure is `ExternalApiError` (502), something the user asked for that does not exist `NotFound`.
  `ErrorCodesTests` checks every code answers 400–599 and none 405

## Current user

- `ICurrentUser` (`Services/ICurrentUser.cs`) is the only way to ask who is signed in — in
  controllers and services alike. `Id` is null for an anonymous request
- Behind `[Authorize]`, take the id with `currentUser.RequireId()` and write no null check: it
  throws `NotSignedInException`, which `ExceptionMiddleware` answers as 401 in the error envelope
- A valid sign-in whose `Users` row is missing returns `UserAccountFailures.Missing` (401, "sign in
  again"): signing in recreates the row. A service that only needs the row to exist asks
  `IUserAccountService.ExistsAsync()` rather than loading it

## Caching

- `IMemoryCache` is the only cache (there is no `IExtendedMemoryCache`)
- A read of shared upstream data goes through `cache.GetOrCreateSingleFlightAsync(key, ttl, factory, ct)`
  (`Common/CacheExtensions.cs`): concurrent misses for one key on one cache share a single `factory` run. Return null
  for a failure (not cached), and pass `shouldCache` to keep an answer from being cached (an empty list).
  The factory is shared, so it uses `CancellationToken.None` and no request-scoped disposables (take an
  `IDbContextFactory` context, not the request's `AzureSQLDBContext`)
- A cached object is shared by every reader: never modify one in place (copy it first)
- Work that must finish after its request — an AI answer others will read, which is paid for either way —
  runs in a scope of its own (`IServiceScopeFactory.CreateAsyncScope()`), never with the request's scoped
  services: those (its DbContext, the OpenAI client, `AiUsageLogger`) are disposed when the request ends.
  `WordGenerator` is the pattern: a singleton holding one run per key (`SingleFlight`), each in its own scope
- To drop several entries together, set them with `options.AddExpirationToken(cache.GroupToken(group))`
  and call `cache.InvalidateGroup(group)`

## External data

- Every TWSE OpenAPI dataset is read through `ITwseOpenApi` (`Services/ExternalApi/Twse`): add a dataset
  there — its URL, cache time and typed row — rather than fetching openapi.twse.com.tw from a service.
  `TwseOpenApiOnlyReaderTests` fails if any other server source names that host. Field names come
  trimmed (TWSE pads some, e.g. `"主旨 "`), so read a field by its plain name with `row.Value(field)`.
  Callers keep their own reaction to a null (unreadable) dataset
- TWStockLib (prices, history, realtime quotes) is registered once, in `FinanceServiceExtensions`: its
  named client (`TwStockOptions.HttpClientName`) gets the app's 30s timeout and read-only retries, and
  the library's own rate limit stays on at its default, 3 requests in any 5s per host
  (`TwStockRegistrationTests` pins all three). History goes through the gateway, whose exchange lanes
  keep within that limit; code asking for the realtime quotes of many stocks batches them first with
  `ITwQuotePrefetcher` (one MIS request per 50), as `PortfolioPriceLoader.FetchStocksAsync` does, rather
  than one request per stock
- Every TW daily volume the app stores or hands out is in shares, on both exchanges (TWStockLib converts
  TPEx's lots since 2.3; rows stored before were converted by the `ConvertOtcVolumeToShares` migration).
  Only the realtime quote's `TotalVolume` is still lots: multiply by `TwVolume.SharesPerLot`
- In tests, set up the quote call as the interface method,
  `GetRealtimeQuoteAsync(code, market, QuoteOptions?, CancellationToken)`: the shorter overloads are
  extension methods, which Moq cannot set up

## JSON

- System.Text.Json only. Newtonsoft.Json still arrives transitively, but a test fails if the server
  references it
- JSON exchanged with an AI model — a few-shot example or payload written into a prompt, a model's
  answer parsed back — goes through `AiJson.Serialize` / `AiJson.Deserialize` (`Common/AiJson.cs`).
  Its options read what models actually write (any-case names, numbers in quotes, a missing
  `required` field as null, enum names, non-ISO dates) and keep Chinese unescaped in prompts

## Taipei time and the trading calendar

- Every TWSE rule runs on Taipei time: use `time.GetTaipeiNow()` / `GetTaipeiToday()`
  (`Common/TaipeiTime.cs`, extensions on the injected `TimeProvider`), never `UtcNow.AddHours(8)`.
  `DateTime.Now` is the server's time zone (likely UTC on App Service), not Taiwan's
- Which days TWSE trades: `ITradingCalendar` (`Services/Market`). `IsMarketClosed` answers from
  memory — the holiday schedule is loaded in the background — and returns null for an unknown
  weekday; `IsTradingDay` / `PreviousTradingDay` count that as a trading day.
  `WeekdayTradingCalendar.Instance` is the weekdays-only stand-in (tests)

## Exams

- Each exam type has its own service in `Services/Exam` and its own controller, all under `api/Exam`;
  there is no `ExamService`. A new exam type gets its own pair rather than joining another one
- What the exams share: `IExamWordPicker` (which of the user's words to ask about), `IExamBankPicker`
  (a shared question bank, skipping the user's last 30 attempts), `IExamSpeech` with
  `ExamControllerBase.SpeakAsync` (the unbilled audio of a question's text), and `IExamResultRecorder`
  (the attempt log and the SM-2 schedules). Which exam endpoints charge is pinned by `UsageLimitEndpointTests`
- The vocabulary exams (cloze, translation, sentence) are one `IWordExamGenerator<TQuestion>` each, returning
  their own DTO, and one typed method each on `IWordExamService`, which picks the word for all of them

## Portfolio

- What an `AssetType` is — market-priced or an amount, its sign, whether it has a cost, counts as
  liquid, pays dividends, its bucket and labels, where its price and FX rate come from, how a saved
  row is normalized, how its latest or as-of price is read — comes from
  `AssetValuationPolicy.For(type)` (`Services/Portfolio/AssetValuationPolicy.cs`). Add a fact there
  rather than a new `AssetType is …` check
- The summary (`GetPortfolioSummaryAsync`) only orchestrates. Which rate converts a position is
  `PositionFxResolver`; one position's value, cost and P&L is `PositionValuation.Value`; the TW sell cost
  is `TwSellCost`; the sums and headline ratios are `PortfolioTotals`. Change a rule in its unit, each of
  which has its own tests, rather than in the orchestration
- The day loops (benchmark comparison, net worth history) walk days forward with `ForwardPriceCursor` (a
  price on or before each day) and `SnapshotCursor` (the snapshot in effect); a benchmark is a `BenchmarkLeg`
  and every curve's return a `TimeWeightedReturn`. The net worth history's own missing-price rules are
  `NetWorthDayValuation`, its series keys `NetWorthGrouping`
- How a screen values a day, and what it does when a price is missing, stays with that screen:
  the summary, drawdown/day change, net worth and benchmark comparison differ on purpose, pinned by
  `PortfolioAsOfValuationTests` and `PortfolioSummaryPriceTests`
- Tests build the Portfolio services with `TestHelpers/PortfolioServiceBuilder` (set only what the
  test cares about; data sources it doesn't exercise default to `AbsentPortfolioSources`), never
  by calling their constructors, so a constructor change touches one file

## Finance AI assistant (chat)

- `AnthropicChatService.StreamChatAsync` only orchestrates a turn. Its requests and the reading of the
  stream (the deltas to the reader, the pause_turn retries, uploading the files again when the Files API
  no longer has one) are `ModelTurn`, which ends in a `TurnOutcome`; what an outcome comes to (answered,
  its error code, cut at the output cap, what is kept as the assistant's message) is `TurnVerdict.Of`; a
  turn's tokens over its requests are `TokenTotals`; the event stream is `SseStream`. Change a rule in its
  unit, each of which has its own tests
- The Anthropic client comes from `IAnthropicClientFactory`, so `AnthropicChatStreamTests` can give the real
  SDK an `HttpClient` serving recorded API streams: a whole turn is tested offline, from the SSE the reader
  gets to what is saved and logged. A change to how a turn reads the stream gets a case there
