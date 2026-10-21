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

## Backend Structure

```
JackyAIApp.Server/
├── Controllers/    # API controllers (RESTful, base route: /api/[controller])
├── Services/       # Business logic with dependency injection
├── Data/           # EF Context, Models, Migrations
├── DTO/            # Data transfer objects
├── Prompt/         # AI prompt templates by feature
└── Common/         # Error handling middleware, response factory
```

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

## Taipei time and the trading calendar

- Every TWSE rule runs on Taipei time: use `time.GetTaipeiNow()` / `GetTaipeiToday()`
  (`Common/TaipeiTime.cs`, extensions on the injected `TimeProvider`), never `UtcNow.AddHours(8)`.
  `DateTime.Now` is the server's time zone (likely UTC on App Service), not Taiwan's
- Which days TWSE trades: `ITradingCalendar` (`Services/Market`). `IsMarketClosed` answers from
  memory — the holiday schedule is loaded in the background — and returns null for an unknown
  weekday; `IsTradingDay` / `PreviousTradingDay` count that as a trading day.
  `WeekdayTradingCalendar.Instance` is the weekdays-only stand-in (tests, optional parameters)
