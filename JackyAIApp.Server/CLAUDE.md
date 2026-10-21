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
