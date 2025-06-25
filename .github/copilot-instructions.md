# GitHub Copilot Instructions for JackyAIApp

## Project Overview

JackyAIApp is a full-stack AI-powered application built with React 18 frontend and .NET 8 backend. The application provides multiple AI-powered features including finance news summaries, English dictionary learning tools, PDF utilities, and task management with Jira integration.

## Architecture

- **Frontend**: React 18 with TypeScript, Material-UI, Redux Toolkit, React Router
- **Backend**: .NET 8 Web API with Entity Framework Core
- **Database**: SQL Server (migrated from Azure Cosmos DB)
- **Cloud**: Azure (App Service, Key Vault)
- **Authentication**: Google OAuth

## Key Features

1. **Finance App** (`/finance`): Daily financial news summaries powered by AI
2. **Dictionary App** (`/dictionary`): English learning with word lookup, audio, and testing
3. **PDF Unlocker** (`/pdf`): PDF password removal utility
4. **Effort Planner** (`/effortPlanner`): Task management with Jira synchronization

## Code Style & Conventions

### Frontend (React/TypeScript)

- Use functional components with hooks
- TypeScript strict mode enabled
- Material-UI for UI components
- Redux Toolkit for state management
- Follow existing file structure in `src/` with feature-based organization
- API calls in `src/apis/` with proper TypeScript interfaces
- ESLint with Prettier for code formatting

### Backend (C#/.NET)

- .NET 8 Web API with controllers
- Entity Framework Core for database operations
- Dependency injection pattern
- Repository pattern for data access
- Proper error handling with custom middleware
- Swagger documentation enabled
- NLog for logging

### Database

- SQL Server with Entity Framework migrations
- Models in `Data/Models/` directory
- Database contexts in `Data/` directory
- Migration files in `Data/Migrations/`

## Important Directories

- `jackyaiapp.client/src/` - React frontend source
- `JackyAIApp.Server/Controllers/` - API controllers
- `JackyAIApp.Server/Services/` - Business logic services
- `JackyAIApp.Server/Data/` - Database context and models
- `JackyAIApp.Server/DTO/` - Data transfer objects
- `JackyAIApp.Server/Prompt/` - AI prompt templates

## Development Guidelines

1. Follow existing naming conventions and project structure
2. Use proper TypeScript interfaces for all API communications
3. Implement proper error handling and logging
4. Write unit tests for new features
5. Follow RESTful API design principles
6. Use Azure Key Vault for sensitive configuration
7. Maintain database migrations for schema changes

## Build & Scripts

- Frontend: `npm run build`, `npm run lint`
- Backend: Standard .NET build process
- E2E tests available in `JackyAIApp.E2ETest/`

## Security Considerations

- Never commit secrets or API keys
- Use Azure Key Vault for sensitive data
- Implement proper authentication and authorization
- Sanitize user inputs, especially for PDF processing
- Use HTTPS for all communications

## AI Integration

- OpenAI integration for various AI features
- Prompt templates stored in `Prompt/` directory
- AI responses should be validated and sanitized
- Implement proper rate limiting and error handling for AI services
