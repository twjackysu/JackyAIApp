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

## Current user

- `ICurrentUser` (`Services/ICurrentUser.cs`) is the only way to ask who is signed in — in
  controllers and services alike. `Id` is null for an anonymous request
- Behind `[Authorize]`, take the id with `currentUser.RequireId()` and write no null check: it
  throws `NotSignedInException`, which `ExceptionMiddleware` answers as 401 in the error envelope
- A valid sign-in whose `Users` row is missing returns `UserAccountFailures.Missing` (401, "sign in
  again"): signing in recreates the row

## Caching

- `IMemoryCache` is the only cache (there is no `IExtendedMemoryCache`)
- A read of shared upstream data goes through `cache.GetOrCreateSingleFlightAsync(key, ttl, factory, ct)`
  (`Common/CacheExtensions.cs`): concurrent misses for one key share a single `factory` run. Return null
  for a failure (not cached), and pass `shouldCache` to keep an answer from being cached (an empty list).
  The factory is shared, so it uses `CancellationToken.None` and no request-scoped disposables (take an
  `IDbContextFactory` context, not the request's `AzureSQLDBContext`)
- A cached object is shared by every reader: never modify one in place (copy it first)
- To drop several entries together, set them with `options.AddExpirationToken(cache.GroupToken(group))`
  and call `cache.InvalidateGroup(group)`

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

## Portfolio

- What an `AssetType` is — market-priced or an amount, its sign, whether it has a cost, counts as
  liquid, pays dividends, its bucket and labels, where its price and FX rate come from, how a saved
  row is normalized, how its latest or as-of price is read — comes from
  `AssetValuationPolicy.For(type)` (`Services/Portfolio/AssetValuationPolicy.cs`). Add a fact there
  rather than a new `AssetType is …` check
- How a screen values a day, and what it does when a price is missing, stays with that screen:
  the summary, drawdown/day change, net worth and benchmark comparison differ on purpose, pinned by
  `PortfolioAsOfValuationTests` and `PortfolioSummaryPriceTests`
- Tests build the Portfolio services with `TestHelpers/PortfolioServiceBuilder` (set only what the
  test cares about; data sources it doesn't exercise default to `AbsentPortfolioSources`), never
  by calling their constructors, so a constructor change touches one file
