# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Development Commands

### Frontend (React + TypeScript)
```bash
cd jackyaiapp.client
npm run dev          # Start development server (https://localhost:5173)
npm run build        # TypeScript compilation + Vite production build
npm run lint         # ESLint with TypeScript, max 0 warnings
npm run preview      # Preview production build
```

### Backend (.NET 8)
```bash
cd JackyAIApp.Server
dotnet run                    # Start development server (https://localhost:7034)
dotnet build --configuration Release
dotnet test                   # Currently no tests configured
dotnet publish --configuration Release
```

### Database Migrations (Entity Framework)
```bash
cd JackyAIApp.Server
dotnet ef migrations add [MigrationName]
dotnet ef database update
```

## Architecture Overview

**Full-stack AI-powered application** with feature-based organization:

### Frontend Stack
- **React 18** with TypeScript strict mode
- **Material-UI v5** for UI components with dark theme support
- **Redux Toolkit** with RTK Query for state/API management
- **React Router v6** for navigation
- **Vite** as build tool with SPA proxy to backend

### Backend Stack
- **.NET 8 Web API** with Swagger documentation
- **Entity Framework Core** with SQL Server (migrated from Cosmos DB)
- **Azure integrations**: Key Vault for secrets, App Service hosting
- **OpenAI integration** via Betalgo.Ranul.OpenAI package
- **Google OAuth** authentication

### Key Features & Modules
1. **Dictionary** (`/dictionary`) - English learning with word lookup, audio, testing, word repository
2. **Finance** (`/finance`) - AI-generated daily financial news summaries  
3. **Effort Planner** (`/effortPlanner`) - Task management with Jira API integration
4. **PDF Unlocker** (`/pdf`) - Password removal utility
5. **Exam System** - Translation and cloze tests with AI grading
6. **Repository** - Personal word collection management

## Code Organization Patterns

### Frontend Structure
```
jackyaiapp.client/src/
├── apis/           # RTK Query API slices (dictionaryApis, financeApis, etc.)
├── [Feature]/      # Feature-based components (Dictionary/, Finance/, etc.)
├── components/     # Reusable UI components
├── redux/          # Store configuration
├── auth/           # Authentication components
└── constants/      # App configuration
```

### Backend Structure
```
JackyAIApp.Server/
├── Controllers/    # API controllers (RESTful, base route: /api/[controller])
├── Services/       # Business logic with dependency injection
├── Data/           # EF Context, Models, Migrations
├── DTO/            # Data transfer objects
├── Prompt/         # AI prompt templates by feature
└── Common/         # Error handling middleware, response factory
```

### Database Architecture
- **User-centric design** with Google OAuth IDs as primary keys
- **Many-to-many relationships**: Users ↔ Words via UserWord junction table
- **Feature-specific entities**: JiraConfig per user, WordMeaning with nested Definitions/Examples
- **Test entities**: ClozeTest, TranslationTest with user associations

### API Communication Pattern
- **RTK Query slices** for each feature domain
- **TypeScript interfaces** for all API contracts in `apis/[feature]/types.ts`
- **Safety type checking** utility for runtime validation
- **Consistent error handling** via custom middleware

## State Management Architecture

### Frontend State
- **Server state**: RTK Query with automatic caching/invalidation
- **Client state**: React hooks (useState, useContext)
- **No global client state library** - prefer feature isolation

### Authentication Flow
- Google OAuth via backend `/api/account/google-signin`
- User profile stored in Redux with RequireAuth wrapper
- Azure Key Vault manages OAuth secrets

## AI Integration Patterns

### Prompt Management
- **Template files** in `JackyAIApp.Server/Prompt/[Feature]/`
- **System prompts** for consistent AI behavior
- **Feature-specific prompts**: ClozeSystem.txt, TranslationSystem.txt, etc.

### AI Service Integration
- **OpenAI client** configured in dependency injection
- **Rate limiting and error handling** for AI service calls
- **Response validation** before returning to frontend

## Development Workflow

### Local Development Setup
1. **Backend**: `dotnet run` in JackyAIApp.Server (auto-starts with HTTPS)
2. **Frontend**: `npm run dev` in jackyaiapp.client (proxies API calls to backend)
3. **Database**: Local SQL Server or connection to Azure SQL Database
4. **Secrets**: Azure Key Vault integration or local appsettings.Development.json

### Code Quality
- **Frontend**: ESLint + Prettier with TypeScript strict mode
- **Backend**: .NET 8 nullable reference types enabled
- **Documentation**: Swagger auto-generation enabled
- **Logging**: NLog with structured logging patterns

### Deployment
- **CI/CD**: GitHub Actions with manual trigger (`workflow_dispatch`)
- **Target**: Azure App Service (`JackyAI`)
- **Process**: Build → Test → Publish → Deploy with Azure publish profile

## Important Notes

### Security Considerations
- **Authentication**: Google OAuth only, no local user registration
- **Secrets**: Never commit API keys - use Azure Key Vault
- **PDF Processing**: Sanitize user inputs for PDF operations
- **HTTPS**: Enforced in all environments

### Database Migration Status
- **Recently migrated** from Azure Cosmos DB to SQL Server
- **Migration controller** available for data transitions
- **Dual context support** during migration period (AzureCosmosDBContext + AzureSQLDBContext)

### Testing Status
- **Current**: No test framework configured (E2E directory exists but empty)
- **CI/CD**: Includes `dotnet test` step but no actual tests
- **Recommendation**: Implement unit tests for Services/ and integration tests for Controllers/

### Common Troubleshooting
- **HTTPS certificates**: Auto-generated for local development
- **SPA proxy**: Configured for React dev server to backend API
- **Entity Framework**: Use `dotnet ef` commands from JackyAIApp.Server directory
- **NPM issues**: Delete node_modules and package-lock.json, then `npm install`