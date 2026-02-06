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
- **Entity Framework Core** with Azure SQL Database
- **Azure integrations**: Key Vault for secrets, App Service hosting
- **OpenAI integration** via Betalgo.Ranul.OpenAI package (v9.0.3)
- **Google OAuth** authentication
- **External API integrations**: Taiwan Stock Exchange (TWSE) Open API

### Key Features & Modules
1. **Dictionary** (`/dictionary`) - English learning with word lookup, audio, pronunciation, word repository
   - Hunspell spell checking
   - Bilingual definitions (English/Chinese)
   - Synonyms, antonyms, related words
   - Example sentences with translations
   - Recent search history
   - Daily word recommendations
   - Audio pronunciation support

2. **Repository** (`/repository`) - Personal word collection management
   - User personal vocabulary database
   - Word bookmarking and organization
   - Learning progress tracking
   - Integration with exam system

3. **Exam System** (`/exam`) - Multiple English testing modes
   - **Cloze Test**: Fill-in-the-blank questions based on user's vocabulary
   - **Translation Test**: Chinese-English translation practice with AI grading
   - **Conversation Test**: AI-powered scenario-based dialogue practice
     - Pre-defined scenario templates (restaurant, hotel, shopping, interview, etc.)
     - Custom scenario creation
     - Voice recording and transcription
     - Real-time grammar correction
     - No dependency on user vocabulary (can be used without saved words)
   - **Sentence Formation Test**: (In development)

4. **Finance** (`/finance`) - Taiwan stock market analysis and insights
   - Daily financial news AI summaries from Taiwan Stock Exchange
   - Individual stock trend analysis with short/medium/long-term predictions
   - Taiwan Stock Exchange (TWSE) Open API integration
   - Strategic insights generation with bullish/bearish recommendations
   - AI-powered stock data analysis using OpenAI GPT-4o-mini

5. **PDF Unlocker** (`/pdf`) - PDF processing utilities
   - Password removal from protected PDFs
   - Batch PDF processing
   - File compression features

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
- **Core entities**:
  - **User**: Basic info, credit balance, admin privileges
  - **Word**: Word text, KK phonetics, timestamps, validity flags
  - **WordMeaning**: Part of speech, definitions, examples, synonyms/antonyms
  - **UserWord**: User-word many-to-many relationship
- **Test entities**: ClozeTest, TranslationTest, ClozeTestOption with user associations

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
- **OpenAI client** configured in dependency injection using Betalgo.Ranul.OpenAI
- **Models used**: 
  - GPT-4o-mini for conversations and text analysis
  - Whisper for audio transcription (STT)
  - TTS-1 and TTS-1-HD for text-to-speech synthesis
- **Rate limiting and error handling** for AI service calls
- **Response validation** before returning to frontend
- **Audio features**: 
  - Voice recording and transcription for conversation practice
  - Text-to-speech with multiple voice options and speeds
- **Conversation features**: Real-time dialogue, grammar correction, scenario-based practice

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

### Recent Updates & Features
- **Finance Module Enhancement**:
  - Added individual stock analysis with AI-powered trend predictions
  - Integrated Taiwan Stock Exchange (TWSE) Open API
  - Three-timeframe analysis: short-term (1-3 months), medium-term (3-12 months), long-term (1-3 years)
  - Investment recommendations with confidence levels
- **Conversation Test Enhancement**: 
  - Added scenario-based dialogue system with pre-defined templates
  - Supports custom scenario creation
  - Removed dependency on user vocabulary words
  - Integrated voice recording with Whisper transcription
  - Real-time grammar correction and feedback
- **Audio System Updates**:
  - Enhanced TTS integration with multiple voice options and speeds
  - Improved audio quality and performance
- **UI/UX Improvements**:
  - Redesigned exam selection interface with flexible button layout
  - Added floating back button for better navigation
  - Enhanced responsive design across all components
- **API Architecture Updates**:
  - Refactored conversation API to accept scenarios and roles directly
  - Updated prompt templates for better AI responses
  - Improved error handling and validation

### Security Considerations
- **Authentication**: Google OAuth only, no local user registration
- **Secrets**: Never commit API keys - use Azure Key Vault
- **PDF Processing**: Sanitize user inputs for PDF operations
- **HTTPS**: Enforced in all environments
- **Input Validation**: Comprehensive validation on all API endpoints

### Database Migration Status
- **Successfully migrated** from Azure Cosmos DB to SQL Server
- **Migration completed** - now using Azure SQL Database as primary data store
- **Entity Framework**: Full Code First approach with migrations
- **Legacy migration controller** available for reference (migration period completed)

### Testing Status
- **Current**: No test framework configured (E2E directory exists but empty)
- **CI/CD**: Includes `dotnet test` step but no actual tests
- **Recommendation**: Implement unit tests for Services/ and integration tests for Controllers/
- **Manual Testing**: Comprehensive manual testing of all features during development

### Performance & Optimization
- **Caching**: RTK Query automatic caching for API responses
- **Lazy Loading**: Route-based code splitting with React Router
- **Bundle Optimization**: Vite build optimization for production
- **Database Indexing**: Proper indexes on frequently queried fields

### Common Troubleshooting
- **HTTPS certificates**: Auto-generated for local development
- **SPA proxy**: Configured for React dev server to backend API
- **Entity Framework**: Use `dotnet ef` commands from JackyAIApp.Server directory
- **NPM issues**: Delete node_modules and package-lock.json, then `npm install`
- **Azure Key Vault**: Ensure proper authentication for local development
- **CORS Issues**: Configured for development proxy, check production settings