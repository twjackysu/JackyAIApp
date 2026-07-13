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
