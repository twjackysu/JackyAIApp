# CLAUDE.md (JackyAIApp.Server)

Guidance specific to the backend package. See the [root CLAUDE.md](../CLAUDE.md) for architecture, features, and cross-cutting conventions.

## Development Commands

```bash
dotnet run                    # Start development server (https://localhost:7034)
dotnet build --configuration Release
dotnet test                   # JackyAIApp.Server.Tests — offline unit tests + LiveContract tests
dotnet publish --configuration Release
```

- A warning fails the build, in the server and the test project alike (`TreatWarningsAsErrors`): fix it
  rather than adding it to `NoWarn`, which holds only the doc-comment rules the code does not follow on
  purpose. A NuGet vulnerability advisory (NU1900–NU1904) stays a warning, so a new one does not stop a
  deploy; it still needs acting on (`dotnet list package --vulnerable`)

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
| English | `Services.Dictionary` (with `.Assistant`: its tools for Studio AI), `.Repository`, `.Exam`, `.Listening`, `.SpacedRepetition`, `.DailyChallenge`, `.WordOfTheDay`, `.SpellChecker`, `Mappers` | Connectors, Platform |
| Connectors | `Services.Connectors` (what the AI may use: the tool contract `IAiTool`, the site's own tools as features register them, the reader's connectors and tool permissions, the hosted MCP servers, e.g. `TwStockMcp`; see docs/ai-native/design/03) | Platform |
| Assistant | `Services.Assistant` (chat profiles, the turn's request), `Services.Chatbot` (the shared chat engine: turns, stream, history, attachments) | Connectors, Platform |
| Finance | `Services.Finance` (analysis, indicators, scoring, predictions, digest; `Services.Finance.Assistant`: the /finance assistant's profile and market background) | Assistant, Connectors, MarketData, Platform |
| Portfolio | `Services.Portfolio` (with `.Assistant`: its tool for Studio AI) | Connectors, MarketData, Platform |
| DataAnalysis | `Services.DataAnalysis` | Assistant, Connectors, Platform |
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
  (`Common/TaipeiTime.cs`, extensions on the injected `TimeProvider`), never `UtcNow.AddHours(8)`
- The server never reads the local clock: `DateTime.Now` / `Today` are the host's zone (UTC on App
  Service, Taipei on a developer's machine), and `NoLocalClockTests` fails on one. What a reader in
  Taiwan calls today — the market's day, the day an analysis, digest or indicator insight is cached
  for — is `GetTaipeiToday()`. The usage budget's day (so also the market digest's once-a-day charge)
  and stored timestamps are UTC, `time.GetUtcNow()`
- A day that turns at the wrong hour is easy to miss: test it across midnight in Taipei, which is
  16:00 UTC the day before (`TestHelpers/TestClock`)
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

## AI assistant (chat)

- One chat engine for every app's assistant (`Services.Chatbot`, module Assistant); an app brings an
  `IChatProfile` (`Services/Assistant/IChatProfile.cs`): its key, the feature its tokens are logged under,
  and per turn a `ChatProfileSetup` — system prompt, a background cached for every reader, whether the
  tw-stock MCP server goes with it. The request for a turn is `ChatTurnRequest.Build` (model, effort,
  output cap and cache breakpoints are the same for every profile). A profile lives in its app's module
  and is registered there (`services.AddScoped<IChatProfile, …>()`); Finance's is `FinanceChatProfile`
  (`Services/Finance/Assistant`), whose request `FinanceChatRequestTests` pins field by field
- A conversation belongs to one profile (`ChatConversations.Profile`, "finance" for every one from before
  there were others). The chat body names it (`ChatRequest.Profile`, default "finance"; an unknown one is
  400 before any charge), the list takes `?profile=` (same default), and a conversation of another profile is
  never continued (a new one is started) nor rewound for an edit: its history would be answered with this
  profile's prompt and tools. A message also carries the page's context (`ChatRequest.Context`, at most 8
  short entries, `ChatbotController.PageContext`), which the profile gets in `ChatProfileContext.Page`
- A profile's own tools (`ChatProfileSetup.Tools`, each an `IAiTool` in `Services/Connectors/AiTools.cs`: a
  descriptor — name, display name, description, input schema, `ToolEffect`, the connector it belongs to,
  `site` for the site's own — and `RunAsync`) are run by the
  server, unlike tw-stock's, which Anthropic's MCP connector runs. With any, `ModelTurn` is an agent loop: a
  reply that stops with tool_use is collected whole while it streams (the SDK's `BetaMessageContentAggregator`),
  `ClientToolRunner` runs its calls one by one (30s each; the reader sees `tool_use` with `id`/`displayName`,
  then `tool_result` with `ok`, `card`, `durationMs`), and the next request sends the reply back unchanged
  (thinking signatures included) with the results, at most `ModelTurn.MaxToolRounds` requests a turn. A result
  goes back wrapped as untrusted data (`ClientToolRunner.Wrap`, `<tool_output trust="untrusted">`, which the
  content cannot close), and the request's system prompt says what that means
  (`ChatTurnRequest.UntrustedToolOutputInstruction`). What the AI may do with each tool is the reader's
  choice on the connectors page (`IToolPermissionSource`, `ToolPermissions`: by default a read runs on its own
  and anything else asks first; a destructive one can never run on its own): a tool they turned off is not
  offered, and a call of one that asks first runs only once the reader allows it (and once a turn has read an
  outside connector's result, every call that is not a read asks first, whatever its permission; the
  question then says `tainted`): a reply's such calls are put to them together (SSE `approval_required` with `pendingId` and the
  calls: id, connector, tool, display name, effect, input, and a `summary` in words when a site tool says one,
  `IDescribesCall`, never a connector's), and the turn waits **in its own request**
  (`ChatApprovalRegistry`, a singleton in memory like `ChatTurnRegistry`; at most
  `ClientToolRunner.ApprovalTimeout`, 3 minutes, writing a `: ping` comment line every 20s so Cloudflare does
  not close the idle stream) until `POST conversations/{id}/approvals/{pendingId}` brings the decisions (only
  that user, that conversation, once; a call left out is a no; NotFound otherwise), then `approval_resolved`.
  A declined or unanswered call is reported to the model as not done; stopping the turn while it waits ends
  it with nothing run. Nothing of the turn is stored while it waits, so reloading the page loses the
  question. A failing tool is reported to the model as failed, never with its exception's text. A tool's card
  is held to what the client draws before it is sent (`ToolCardGuard`: a known shape of kind, a table cut to
  12 columns and 200 rows, a link only to this site or https, 200k characters in all), and the cards a turn's tools showed are kept
  with its answer (`ChatMessages.CardsJson`, a JSON array as streamed) and come back as `ChatMessageDto.Cards`
  when the conversation is reopened; the model still gets the text only. Without tools (Finance) a turn is
  one request, read exactly as before
- Studio AI, the assistant on every page, is the profile `studio` (`Services/Assistant/Studio`,
  `StudioChatProfile`): the site's pages (`SitePages`, kept in step with the client's app registry) and the one
  the reader is on (page context `path`) in its prompt, `open_page` (`OpenPageTool`, a link card to a page, a
  word's dictionary page included; a word that is not plain letters is dropped), every `ISiteTool` a feature
  registers (English's `look_up_word`, `add_word_to_repository`, a write, so asked first, and `learning_progress`,
  the words due for review, the daily challenge, streak and XP: `Services/Dictionary/Assistant`; Portfolio's `portfolio_summary`, the reader's own portfolio as
  /portfolio values it, a table card of the positions: `Services/Portfolio/Assistant`) and the reader's connectors' tools. No tw-stock: /finance has its own assistant for the market
- `AnthropicChatService.StreamChatAsync` only orchestrates a turn. Its requests and the reading of the
  stream (the deltas to the reader, the pause_turn retries, uploading the files again when the Files API
  no longer has one) are `ModelTurn`, which ends in a `TurnOutcome`; what an outcome comes to (answered,
  its error code, cut at the output cap, what is kept as the assistant's message) is `TurnVerdict.Of`; a
  turn's tokens over its requests are `TokenTotals`; the event stream is `SseStream`. Change a rule in its
  unit, each of which has its own tests
- What a turn keeps as the assistant's message: an answer (one cut at the output cap included); a turn
  the reader stopped, its text so far ending in `（已中斷）`; nothing for a turn that failed — an API error
  even after some text, no answer text, a decline. The client shows a failed turn only as its error and
  drops any text it had, so reopening the conversation shows what the reader saw, and the model never
  takes a cut-off answer for a finished one. Server and client keep to this together (`TurnVerdictTests`,
  and the assistant panel's test for a reply that fails after some text)
- The Anthropic client comes from `IAnthropicClientFactory`, so `AnthropicChatStreamTests` can give the real
  SDK an `HttpClient` serving recorded API streams: a whole turn is tested offline, from the SSE the reader
  gets to what is saved and logged. A change to how a turn reads the stream gets a case there

## Connectors (what the AI may use)

- docs/ai-native/design/03 is the design. A user's connectors are `ConnectorInstallations` (module
  Connectors): one row per catalog entry they added (a filtered unique index on `(UserId, DefinitionKey)`
  leaves out `custom`, an MCP server of their own, which may be many), with its credentials encrypted, on or
  off, the tools it last listed (`ToolsCacheJson`, read leniently by `CachedTools`: it came from an outside
  server) and the reader's permission for each tool (`ToolSettingsJson`, `ToolPermissions.Parse`)
- The site's own tools are a connector that is always there (`SiteConnector`, key `site`). A feature lists
  its tools for the connectors page with `services.AddSingleton(new SiteToolSet(group, [descriptors]))` next
  to its profile (a tool's descriptor is a `static readonly Definition`, so the list and the tool agree); the
  tools themselves still come with the profile, since one may need the page it is asked from. The site has
  a row only once the reader changed one of its tools' permissions
- The catalog's remote MCP servers are `BuiltInMcpConnectors` (tw-stock, DeepWiki with no sign-in; Notion, Linear
  with OAuth): adding one is a line there, checked by `BuiltInMcpConnectorsTests`. Each is added once per reader,
  always at its own URL and its own way of signing in (`ConnectorInstallService`, which also adds a server of the
  reader's own: no credentials, an API key in a header of theirs, a bearer token, or OAuth; stored with
  `ConnectorSecrets`). The MCP client is `McpToolClient` (the official SDK over Streamable HTTP, every request
  through `SafeHttp`, a session per request); the chat gets a reader's connectors' tools from
  `ConnectorToolSource` (named `m1__…` per turn, their connector the installation's id). A call the server
  refuses for its credentials marks the connector NeedsAuth
- Signing in to an MCP server (MCP authorization, design/03 §5.2) is three units. `McpOAuthDiscovery` asks the
  server without credentials and follows its 401 (`WWW-Authenticate` `resource_metadata`, `scope`) to its
  protected resource metadata (RFC 9728; its `resource` must be the server's origin and a path the server is
  within) and the authorization server's metadata (RFC 8414, then OpenID discovery; its `issuer` must match, and
  without PKCE S256 it is refused); every fetch is https only, through `SafeHttp`, at most 64KB. `McpOAuthFlow`
  becomes its client when the connector is added (dynamic registration when the server offers it; else our Client
  ID Metadata Document, `GET api/connectors/oauth/client-metadata`, when the server takes one and the site is on a
  public https address; else the client id the reader typed — the reader's own id wins), sends the reader there (`POST
  installations/{id}/authorize`: PKCE S256, `resource`, `scope`, a signed `state`; the verifier stays in
  `IMemoryCache` on the server, 10 minutes, once), and takes them back at `GET api/connectors/callback/mcp` (a
  literal route, matched before the account connections' `callback/{provider}`): the state, then `iss` (RFC 9207),
  then the code for tokens, kept encrypted as a `McpOAuthGrant`. `McpOAuthTokens` gives each call its credentials,
  refreshing a token due within 60s, one refresh at a time per connector (a rotated refresh token spent twice
  would end the sign-in); a refused refresh of an expired token marks the connector NeedsAuth and keeps the
  client. The redirect URI and the metadata URL come from `Connectors:CallbackBaseUrl` (`McpOAuthUrls`); an
  authorization server's own words never reach the reader, only an OAuth error code
- `ConnectorCatalogService` answers the page (`AiConnectorsController`, `api/connectors/catalog`,
  `tool-permissions`, `installations/{id}/enabled`, `installations/{id}`, in the `{ data }` envelope; the
  account connections' endpoints of `ConnectorsController`, under the same route, are unchanged) and, as
  `IToolPermissionSource`, each chat turn. Someone else's connector, or the site's own for turning off or
  removing, is NotFound

## Data analysis

- `DataAnalysisController` does HTTP only. The engine queries (schema, max date, distinct values,
  summarize, details, CSV export) are `IDataAnalysisQueryService`, the question in plain language is
  `INlSummarizeService`. They and dashboard widgets take the source, the schema gate, the summarize and the
  answer to a failed query from `EngineSources`: the default source's driver text stays in the log, and a
  custom source's goes only to its owner. A widget resolves its source as the dashboard's owner, so any
  allowed viewer sees it, and passes `viewerIsOwner` so no other viewer gets that text. Each answer is a record (`MaxDateDto`, `DistinctValuesDto`, `SummarizeRowsDto`, `DetailsPageDto`).
  An export hands its rows to the controller's CSV writer while the service still holds the gate that lets
  two run at once
- Tests build the controller with `TestHelpers/DataAnalysisControllerBuilder`, never its constructor;
  `DataAnalysisControllerResponseTests` pins what each endpoint answers
- The page's AI assistant is the chat profile `data-analysis` (`Services/DataAnalysis/Assistant`,
  `DataAnalysisChatProfile`). `describe_data` (`DescribeDataTool`, free) lists the columns, their types and the
  latest date of up to 3 date columns; `ask_data` (`AskDataTool`) answers a question in plain language
  through `INlSummarizeService` — the same checked plan, correction and notes as 問 AI, never a query of the
  model's own — charging each call as `UsageFeature.NlSummarize`, and shows the result as a `chart` card (the
  client's viz id, the rows, group/date hints, and the plan with the page's source, so it can become a
  dashboard widget; none for an uploaded file's session). A chart with a plan is also kept for a while by a short id the
  model is told (`chartId`, `DataAnalysisCharts`, in memory per reader and conversation), so `save_to_dashboard`
  (`SaveToDashboardTool`, a write, so asked first) saves exactly the chart the reader saw to the dashboard they named
  (one they own of that name, or a new one) without asking the data again. Which data it reads is the page context the client
  sends with each message (`sourceId`/`tableName`, or `sessionId`; none = the default source), read by
  `DataAnalysisPage.From`; whether the reader may read that source is the query's own check
- `NlQueryPlanner.BuildPlanAsync` only orchestrates. Which columns are String or date columns is
  `NlSchema`; which String columns' real values are read (300 a column) and listed in the prompt (2000 in
  all), and which can check a plan against all their values, is `NlPromptValues`; the wording sent to the
  LLM is `NlPrompt`; the LLM calls of one question — a plan checked against the data, and its one
  correction — are `NlPlanRun`. The rules for filter values and notes stay in `NlPlanValidator`, the
  relative dates in `NlDateRanges`. Change a rule in its unit, each of which has its own tests
- `NlQueryPlannerPipelineTests` pins the whole pipeline against a mocked LLM and value source: what each
  call is told and asked, what the source is queried for, and which plan and notes come back. A change to
  what the LLM sees gets a case there
