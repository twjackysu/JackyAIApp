# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Development Commands

Frontend commands live in [jackyaiapp.client/CLAUDE.md](jackyaiapp.client/CLAUDE.md); backend and EF migration commands live in [JackyAIApp.Server/CLAUDE.md](JackyAIApp.Server/CLAUDE.md).

## Architecture Overview

**Full-stack AI-powered application** with feature-based organization:

### Frontend Stack
- **React 19** with TypeScript strict mode
- **MUI v9** for UI components with dark theme support
- **Redux Toolkit** with RTK Query for state/API management
- **React Router v7** for navigation
- **Vite** as build tool with SPA proxy to backend

### Backend Stack
- **.NET 10 Web API** with Swagger documentation
- **Entity Framework Core 10** with Azure SQL Database
- **Azure integrations**: Key Vault for secrets, App Service hosting
- **OpenAI integration** via Betalgo.Ranul.OpenAI package (v9.2)
- **Anthropic integration** via the `Anthropic` SDK (Finance AI assistant, see AI Integration Patterns)
- **Google OAuth** authentication
- **External API integrations**: Taiwan Stock Exchange (TWSE) Open API, Yahoo Finance, FundRich (基富通), TWStockLib (NuGet package, own test suite — see Testing Status)
- **Resilience**: Polly retry + circuit breaker (`Microsoft.Extensions.Http.Resilience`) on the named `"ExternalReadOnly"` client and the bare default client (used by TWStockLib), scoped away from the OpenAI/Anthropic clients to avoid retrying non-idempotent calls
- **Third-party gateway** (`Services/ExternalApi/ThirdPartyGateway`): every request to TWSE/TPEx history goes through `IThirdPartyGateway.RunAsync(source, key, priority, fetch)` — identical in-flight requests share one fetch, Interactive is served before Background, and a per-source token bucket paces it (config section `ThirdPartyGateway`, live reload; the defaults are conservative guesses, no official limit is published). It does not cache — callers keep their own cache in front. The "token" is this rate-limit budget, nothing to do with LLM tokens

### Key Features & Modules
The home portal (`/`) is the only place users switch between apps; the registry is
`jackyaiapp.client/src/constants/apps.tsx`. The English-learning app (items 1-3 and 7) lives under
`/english/...`.

1. **Dictionary** (`/english/dictionary`) - English learning with word lookup, audio, pronunciation, word repository
   - Hunspell spell checking
   - Bilingual definitions (English/Chinese)
   - Synonyms, antonyms, related words
   - Example sentences with translations
   - Recent search history
   - Daily word recommendations
   - Audio pronunciation support

2. **Repository** (`/english/repository`) - Personal word collection management
   - User personal vocabulary database
   - Word bookmarking and organization
   - Learning progress tracking
   - Integration with exam system

3. **Exam System** (`/english/exam`) - Multiple English testing modes
   - **Cloze Test**: Fill-in-the-blank questions based on user's vocabulary
   - **Translation Test**: Chinese-English translation practice with AI grading
   - **Conversation Test**: AI-powered scenario-based dialogue practice
     - Pre-defined scenario templates (restaurant, hotel, shopping, interview, etc.)
     - Custom scenario creation
     - Voice recording and transcription
     - Real-time grammar correction
     - No dependency on user vocabulary (can be used without saved words)
   - **Sentence Formation Test**: build a sentence with a saved word, AI-evaluated
   - **Listening / Listening Response**: picture-based questions (images in Blob Storage, question
     bank managed at `/admin/listening`) and pick-the-right-reply questions
   - **Reading Comprehension**: AI-generated passages, validated before they are served
   - **Writing**: topic prompt with AI grading
   - **Dictation**: listen to a word and type it; graded locally (edit distance), no AI cost

4. **Finance** (`/finance`) - Taiwan stock market analysis and insights
   - Daily financial news AI summaries from Taiwan Stock Exchange
   - Individual stock trend analysis with short/medium/long-term predictions
   - Taiwan Stock Exchange (TWSE) Open API integration
   - Strategic insights generation with bullish/bearish recommendations
   - AI-powered stock data analysis using OpenAI gpt-6-luna
   - **AI assistant** (signed-in users; model and storage under AI Integration Patterns): at the top
     of the page as a one-line box marked as a chat (not a search); focusing it opens the full
     conversation in place — history column on `md`+, a drawer below that — and its header folds
     it back. Folding keeps the conversation (and a reply still streaming). Below it, three tabs:
     綜合量化分析 (default) / AI 趨勢分析 / 今日市場AI摘要

5. **PDF Unlocker** (`/pdf`) - PDF processing utilities
   - Password removal from protected PDFs
   - Batch PDF processing
   - File compression features

6. **Portfolio** (`/portfolio`) - Holdings tracking (stock/fund/cash/liability/real estate)
   - Snapshot-based model, **not** a transaction log — each save replaces the latest snapshot for that date
   - Asset types: STOCK, FUND, CASH, LIABILITY (debt; negated at the single valuation point in `PortfolioService`, see code comments for the sign-flip invariant), REAL_ESTATE (its quantity *is* its current value, see `ValuationMethod`; kept out of P&L, XIRR and the liquid-asset figures)
   - TW stock pricing via TWStockLib; during 9:00-13:30 trading hours, overlays a realtime quote (bid/ask midpoint fallback) on top of the last historical close
   - US stock pricing + dividends via Yahoo Finance v8 chart API; fund NAV/dividends via FundRich
   - Total (含息) return: dividend history walked against the snapshot timeline so a dividend counts against whatever quantity was held on its ex-date; XIRR includes dividends as inflows
   - Exposure ratio accounts for leveraged/inverse ETF factors (`PortfolioExposure.Factor`)
   - Drawdown and benchmark charts reach back up to 3 years, but the providers only keep ~6 months of prices. Older **TW** months are filled in the background by `TwHistoryBackfillService` (one month at a time, newest first across all stocks, Background priority, each month stored as it arrives; progress in `StockSymbolInfo.HistoryCoveredFrom`) and added to a request's prices by `DeepHistoryService`, which only reads — it never waits on an exchange. The responses carry `backfill { state, coveredFrom, targetFrom }` so the client can show progress and poll. The exchanges answer "no data" with a *successful empty* month (also for a code that does not exist), so an empty month for a stock the user held is treated as a failure, never as coverage. US stocks are not backfilled yet

7. **Daily Challenge / Review / Stats** (`/english/daily`, `/english/review`, `/english/stats`) - Retention & gamification
   - Daily Challenge: 5-question date-seeded quiz generated from the word DB (no AI cost), always free
   - Review: SM-2 spaced-repetition flashcards (`SpacedRepetitionService`), shared scheduling with exam results
   - Streak + XP + level system; weekly report; anonymized leaderboard. The streak and level rules
     live once, in `Services/DailyChallenge/StreakRules.cs` and `XpLevels.cs` — the submit path and
     every display (stats, weekly report, leaderboard) go through them

8. **Data Analysis** (`/data-analysis`) - Query and summarize tabular data (`DataAnalysisLibrary` NuGet packages)
   - Built-in default source plus user-saved custom sources (SQL Server, Azure Data Explorer), and uploaded files held in a per-session store
   - "問 AI": natural-language question → validated summarize plan (`NlQueryPlanner`)

9. **Data Dashboard** (`/dashboards`) - Dashboards built from Data Analysis summarize results
   - Each widget stores a re-runnable summarize plan; data is re-fetched on view
   - Visibility Private / Public (any signed-in user) / Restricted (shared users) / Anyone (no sign-in)

10. **Job Finder** (`/jobs`) - Job listings aggregated from 104 and Cake

11. **Folder Slideshow** (`/slideshow`) - Plays the photos in a folder the user picks; runs entirely in the browser, no API

12. **Connected Services** (`/connectors`) - OAuth connections to Atlassian (Jira) / Microsoft / Google, tokens stored encrypted

13. **Usage Limits** (DECISION-006/007) - No monetization; cost control only
   - The old Credits & Payments system (prepaid credit balance, Stripe/PayPal/ECPay) is removed —
     the app has no payment/credit UI and looks free to every user
   - Every AI-powered endpoint is instead gated by a per-user, per-UTC-day weighted usage budget
     (`IUsageLimitService`, `[UsageLimit(UsageFeature.X)]`; each `UsageFeature` has exactly one
     cost, in `Common/UsageCosts.cs` `UsageCosts.Of`, so a charge names the feature, never a
     cost); exceeding it returns HTTP 429 + `ErrorCodes.UsageLimitExceeded`,
     surfaced client-side by `UsageLimitModal`: the AI cost is paid by the author, the budget is a
     free daily trial (resets 00:00 UTC = 08:00 Taiwan), please sponsor or email the author with
     your account to have it reset or raised (sponsor button appears once
     `constants/author.ts` `SPONSOR_URL` is set)
   - The budget is a **trial allowance, not unlimited use**. Charge on the endpoint with
     `[UsageLimit]`; charging before the action validates is fine, because of the refund rule:
     a charge is refunded only when the request failed (status ≥ 400, an exception, or
     `UsageLedger.MarkFailed` for a stream that already sent 200) **and** no AI provider billed.
     Once a provider billed (`AiUsageLogger` reports it; chat reports it at `message_start`), a
     failure keeps the charge. The rule lives only in `Services/UsageLedger.cs`, settled by
     `Common/UsageSettlementMiddleware.cs`; no endpoint refunds by hand. Only three endpoints
     charge inline, each for a reason: TTS (cache hits and anonymous use are free), the market AI
     digest (once per user per day), chat (cost depends on attachments)
   - `UsageLimits:DailyBudget` (default 60) sets the shared default; `User.UsageLimitMultiplier`
     lets the author raise an individual user's effective limit directly in the DB (no admin UI).
     Resetting today's usage is `UPDATE DailyUsages SET Used = 0` for that `UserId` and today's
     UTC `[Date]`

## Code Organization Patterns

Frontend and backend directory trees live in [jackyaiapp.client/CLAUDE.md](jackyaiapp.client/CLAUDE.md) and [JackyAIApp.Server/CLAUDE.md](JackyAIApp.Server/CLAUDE.md) respectively.

### Database Architecture
- **User-centric design** with Google OAuth IDs as primary keys
- **Many-to-many relationships**: Users ↔ Words via UserWord junction table
- **Core entities**:
  - **User**: Basic info, admin privileges, usage-limit multiplier
  - **Word**: Word text, KK phonetics, timestamps, validity flags
  - **WordMeaning**: Part of speech, definitions, examples, synonyms/antonyms
  - **UserWord**: User-word many-to-many relationship
- **Test entities**: ClozeTest, TranslationTest, ClozeTestOption with user associations

### API Communication Pattern
- **RTK Query slices** for each feature domain — each is its own `createApi`, so a tag one slice
  invalidates never reaches another. Where a server write changes another slice's data (adding a
  word changes the review queue, finishing a challenge changes the stats), the mutation sends the
  invalidation across explicitly: `onQueryStarted: alsoRefresh(refreshReviewQueue, ...)`
  (`apis/utils/alsoRefresh.ts`), with each affected slice exporting its own `refresh…` function
- **TypeScript interfaces** for all API contracts in `apis/[feature]/types.ts`
- **Error message extraction** via `apis/utils/errorMessage.ts` — the only supported way to turn
  an RTK Query error into display text, since `FetchBaseQueryError.data` is not guaranteed to hold
  the API's `{ error: { code, message } }` envelope
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
  - gpt-6-luna (`NewModels.GPT_6_LUNA`) for every chat/JSON completion **except the Finance AI
    assistant below**; it is reasoning-family, so `OpenAIPromptService` sends
    `reasoning_effort=low`, never `temperature` or `max_tokens`, and caps output with
    `max_completion_tokens` (16384, output only — prompts can be far longer)
  - **Finance AI assistant** (`/finance`; `AnthropicChatService`): Claude Haiku 5.5
    (`claude-haiku-5-5`, SDK `Anthropic` NuGet) called directly through the Anthropic API (key
    `Settings:AnthropicApiKey`) — not Dify (removed) and not OpenAI. It uses Anthropic's MCP
    connector to attach the remote `tw-stock` MCP server (`TW-Stock-MCP-Server.fastmcp.app`), which
    Anthropic's servers call, not ours. Its tools are deferred behind the regex tool search (not
    BM25, which misses Chinese queries), except a few always loaded
    (`AnthropicChatService.AlwaysLoadedTools`; the turn log names the tools each turn called, to
    keep that list honest). Haiku 5.5 thinks by default (adaptive) and the thinking
    counts toward `max_tokens`; effort is set explicitly to `medium`; it can decline a request
    (`stop_reason: refusal`, no server-side fallback for Haiku), which is reported to the user as
    such. The system prompt carries today's Taipei date. The API is stateless, so conversations
    live in our own SQL tables (`ChatConversations`, `ChatMessages`) and the whole history is
    re-sent every turn, each turn with its role but as its visible text only (earlier tool calls
    and thinking are not kept); replies stream to the browser as SSE. Costs
    `UsageCosts.ForChatMessage` per message (the message plus its attachments), refunded only if
    Anthropic never started the message (the usage refund rule)
  - Whisper for audio transcription (STT)
  - TTS-1 and TTS-1-HD for text-to-speech synthesis — **fallback only**: when `AzureSpeech:Key`
    is configured, `ITextToSpeechService` resolves to `AzureSpeechTextToSpeechService` (Azure AI
    Speech REST, F0 free tier) instead, so TTS no longer dies with the OpenAI credit balance.
    Callers are unaware: both take the same OpenAI-flavoured (voice, model, speed) triple, share
    one 30-day cache (`TtsCache`), and return mp3. The OpenAI voice names are mapped onto Azure's
    deliberate counterparts (`alloy` → `en-US-AlloyTurboMultilingualNeural`, etc.)
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
- **Backend**: .NET 10, nullable reference types enabled
- **Documentation**: Swagger auto-generation enabled
- **Logging**: NLog with structured logging patterns

### Deployment
- **CI/CD**: GitHub Actions, "Build and deploy" workflow runs on every **push to `master`**
  (a merged PR is a push), plus manual `workflow_dispatch`. A push that touches **only**
  `.agent/**` or `*.md` files is ignored (`paths-ignore`) and does not deploy; one code file in the
  push is enough to deploy. Closing a PR without merging does not deploy (nothing lands on `master`)
  - Every run deploys the **tip of `master` as of the moment its checkout starts** (`ref: master`),
    not the commit that triggered it. That makes deploy order irrelevant: a run that waited in the
    queue never ships an older build over a newer one, and a repeat of the same tip is harmless.
    (The old `pull_request: closed` trigger deployed `merge_commit_sha`, which for a PR GitHub
    marks merged only through ancestry is its own, possibly much older branch head — that rolled
    production back on 2026-10-11, #443/#445.) `workflow_dispatch` deploys `github.ref`, the branch
    picked in the dialog
  - Deploys share one `production-deploy` concurrency group (`cancel-in-progress: false`): one at
    a time, and of several waiting runs only the newest is kept
- **Target**: Linux App Service `jackyai-linux` (B1, Always On) in `MyAppResourceGroup` (East
  US), secret `JACKYAI_LINUX_PUBLISH_PROFILE`; its system identity has Key Vault secrets
  Get/List. Running a branch through `workflow_dispatch` deploys it to production, including
  any EF migration it carries (they run at startup)
  - Public address **https://apps.jacky-studio.com**: Cloudflare DNS (CNAME `apps` →
    jackyai-linux.azurewebsites.net, proxied) with SSL mode Full (strict), and a Cloudflare Origin
    Certificate (`*.jacky-studio.com`, to 2041) bound on the App Service. Keep the TXT
    `asuid.apps` record: with the CNAME proxied, it is how Azure verifies the domain on any
    rebinding. OAuth redirect URIs (`/signin-google`, `/api/connectors/callback/{Provider}`)
    and the `Connectors__CallbackBaseUrl` app setting point at this address
  - Access restrictions: the main site only accepts Cloudflare's IP ranges
    (https://www.cloudflare.com/ips), so jackyai-linux.azurewebsites.net answers 403 to anyone
    else; test through apps.jacky-studio.com. The scm (deploy) site keeps its own rules and is
    not restricted, which is what lets GitHub Actions deploy
  - The Windows App Service it replaced (`JackyAI`, jackyai.azurewebsites.net) was deleted on
    2026-10-09, with its F1 plan, its Key Vault access policy and its publish-profile secrets.
    jackyai.azurewebsites.net no longer exists and anyone can now claim the name, so no DNS
    record may point at it (the old `www` CNAME was removed for that reason). Under
    jacky-studio.com only `apps` (and its `asuid.apps` TXT) is defined; the apex and `www`
    have no records
- **Process**: Build → Test → Publish → Deploy with Azure publish profile (ubuntu runner)
- **Linux**: no Windows-only APIs (System.Drawing.Common throws there; images use ImageSharp),
  and file paths are case-sensitive
- **Azure resources** (`MyAppResourceGroup`, East US unless noted), all in use unless marked:
  - `jackyai-linux` + plan `ASP-JackyAI-Linux` (B1) — the app
  - SQL server `jackyai` / database `JackyAI` (Basic, 5 DTU)
  - Key Vault `JackyAIAppKeyVault` — access policies: the owner's account, and the app's
    identity with secrets Get/List only. Soft delete, 90 days: the payment-era secrets
    (ECPay/PayPal/PayUni), `ConnectionStrings--AzureCosmosDBConnection` and
    `ConnectionStrings--DefaultConnection` were deleted 2026-10-09 and stay recoverable until
    early January 2027
  - Storage `jackyaiappstorage` — container `listening-images` (listening-question images)
  - Application Insights `JackyAI` (workspace-based, on `DefaultWorkspace-…-EUS`) — shares the
    deleted Windows app's name but is what the Linux app reports to; keep it, its
    smart-detection alert rule and action group
  - Azure AI Speech `JackyAISpeech` (Japan East) — TTS
  - Cosmos DB `jacky-ai-nosql` — **unused** since the move to SQL (free tier), kept only
    because deleting it loses its data
  - Resource group `jacky-sites-rg` — a separate, stopped project, unrelated to this app
- **Gotcha**: when several PRs merge in quick succession, queued deploy runs that get superseded
  show as *cancelled* — expected, not a failure. The last run deploys `master`'s tip, which contains
  everything merged so far. A direct push to `master` deploys like a merge does (unless it only
  touches `.agent/**` / `*.md`); there is no longer a way to land code on `master` without
  deploying it. If the final run for the newest push is itself red, re-run it (or
  `workflow_dispatch` from `master`) — never from an older branch

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
- **`JackyAIApp.Server.Tests`** (xUnit + Moq): two categories, do not conflate them
  - **Offline unit tests**: fixture/mock-based, run in every PR CI (`--filter "Category!=LiveContract"`)
  - **Live contract tests** (`LiveContracts/`, `[Trait("Category", "LiveContract")]`): hit real third-party APIs — Yahoo Finance (incl. ^TWII 6y daily history), FundRich, open.er-api.com (FX), TWSE OpenAPI + BOT/CBC CSV, TWSE `rwd` MFI94U (發行量加權股價報酬指數, the 回落煞車 signal) + OpenAPI `holidaySchedule` + MIS `getStockInfo.jsp?ex_ch=tse_t00.tw` (加權指數即時, the 回落煞車 intraday estimate; read through TWStockLib), TPEx OpenAPI (listed-company list), SEC EDGAR (fundamentals + insider-trading HTML scrape), Finnhub, FRED, 104, Cake — asserting response *shape* not exact values. Never run in PR CI — only on the scheduled `contract-tests.yml` workflow (daily cron + manual dispatch). Deliberately excludes anything sourced through TWStockLib (that package has its own test suite) — except MIS `t00`, whose index-shaped payload (`z`/`y`/`tlong`, no `u`/`w`/order book) is the app's own use of it. Finnhub/FRED soft-skip without their API key set
  - **Known noisy one**: 104's live test can fail from datacenter-IP sources (GitHub Actions runners included) due to Cloudflare bot-challenge — confirmed the actual prod app (Azure App Service, different IP) still works fine when this was investigated 2026-07, so a red 104 result in the scheduled run isn't automatically a prod incident; check the *feature itself* before assuming it's down
- **E2E**: directory exists but empty
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