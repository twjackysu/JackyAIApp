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
| Connectors | `Services.Connectors` (what the AI may use: the tool contract `IAiTool`, the site's own tools as features register them, the reader's connectors and tool permissions, the hosted MCP servers, e.g. `TwStockMcp`; native connectors, `.Google`, `.Atlassian`, `.Outlook` (never `.Microsoft`: it would hide the framework's `Microsoft.*` in this module); the site's own MCP server, `.SiteMcp`; see docs/ai-native/design/03) | Platform |
| Assistant | `Services.Assistant` (chat profiles, the turn's request; `.Memory`: what the reader asked Studio AI to remember), `Services.Chatbot` (the shared chat engine: turns, stream, history, attachments) | Connectors, Platform |
| Finance | `Services.Finance` (analysis, indicators, scoring, predictions, digest; `Services.Finance.Assistant`: the /finance assistant's profile and market background) | Assistant, Connectors, MarketData, Platform |
| Portfolio | `Services.Portfolio` (with `.Assistant`: its tools for Studio AI) | Connectors, MarketData, Platform |
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
- A limit counted per visitor without a sign-in goes by `RateLimitPolicies.ClientAddress`: the address Cloudflare saw
  (`CF-Connecting-IP`), an IPv6 one by its /64 (`Subscriber`, D-196), so one household cannot step through its
  addresses to multiply its share

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
- The one exception is text to speech's clips: `TtsCache` (`Services/Speech`, a singleton both providers share) is its own
  `MemoryCache` bounded by the bytes it holds (`MaxBytes`, 128 MB; past it a new clip is not kept), since `api/audio` takes a
  visitor's text and every new text is a new clip of up to a megabyte kept 30 days. That endpoint is also held to 30 requests a
  minute per address without a sign-in, 120 per reader (`RateLimitPolicies.TextToSpeech`; D-173)

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
- What a reader writes for an AI-graded exam (translation, sentence, writing, conversation) is measured as the JSON the prompt
  carries and refused past `ExamInputLimits` (4,000 / 20,000 / 40,000 characters) before the model is asked, so the charge is
  refunded (a conversation first drops its oldest turns to fit, `TrimOldestTurns`: the page sends the whole history each turn); those endpoints and `record-result` hold their request body with `[RequestSizeLimit]` (Kestrel otherwise reads about
  28 MB first). A writing result feeds at most `WritingExamService.RequiredWordCount` words to the review schedule, in one query (D-165)
- A spoken answer's transcription (`whisper/transcribe`) costs one unit whatever its length while Whisper bills by the minute,
  so a recording is held to `ConversationPracticeController.MaxAudioBytes` (4 MB, refused before Whisper is asked, so the
  charge is refunded); the exam pages record at 64 kbps and stop at two minutes, about 1 MB (the client's
  `useVoiceRecording`, its test holding the two numbers together; D-159)

## Portfolio

- A save of holdings is bounded (`HoldingsLimits`, D-165): its request 512 KB, at most 300 rows, a symbol 40 characters,
  a name 200, a market, currency or valuation method 20, and a reader keeps at most 2000 snapshots (a save on a date already
  kept replaces it, so only a new date counts) — beyond them a BadRequest and nothing saved. `SaveHoldingsAsync` answers a
  `ServiceResult<bool>`, which `update_holding` reports as a failed call. A benchmark comparison draws at most 4 symbols
  (`BenchmarkComparisonService.MaxBenchmarks`): each is a deep history through the exchange's shared lane
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
  (thinking signatures included) with the results, at most `ModelTurn.MaxToolRounds` requests a turn (a turn still
  calling tools at the last one is not an answer, `no_answer`, even after a lead-in line: its calls never ran; D-178). A result
  goes back wrapped as untrusted data (`ClientToolRunner.Wrap`, `<tool_output trust="untrusted">`, which the
  content cannot close), and the request's system prompt says what that means
  (`ChatTurnRequest.UntrustedToolOutputInstruction`). What the AI may do with each tool is the reader's
  choice on the connectors page (`IToolPermissionSource`, `ToolPermissions`: by default a read runs on its own
  and anything else asks first; a destructive one can never run on its own): a tool they turned off is not
  offered, and a call of one that asks first runs only once the reader allows it (and once a turn has read an
  outside connector's result, or a site tool's marked `OutsideText` because it holds other people's words, a
  calendar invite's, `AiToolDescriptor.ReadsOutsideText`, or a result its tool marks so though the tool's results usually are not, `AiToolResult.OutsideText`, as the data page's tools do over an uploaded file, D-163, every call that is not a read asks first, whatever its
  permission, in that turn and every later turn of the conversation, `ChatConversations.ReadOutsideText`: its answers may
  repeat that text and are sent again as history; the question then says `tainted`; a message that quotes text the reader
  selected on a page, `ChatRequest.QuotesPageText` from Studio AI's selection bar, marks the conversation so before its turn
  runs, since that text may be someone else's words too): a reply's such calls are put to them together (SSE `approval_required` with `pendingId` and the
  calls: id, connector, tool, display name, effect, input, and a `summary` in words when a site tool says one,
  `IDescribesCall`, never a connector's), and the turn waits **in its own request**
  (`ChatApprovalRegistry`, a singleton in memory like `ChatTurnRegistry`; at most
  `ClientToolRunner.ApprovalTimeout`, 3 minutes, writing a `: ping` comment line every 20s so Cloudflare does
  not close the idle stream) until `POST conversations/{id}/approvals/{pendingId}` brings the decisions (only
  that user, that conversation, once; a call left out is a no; NotFound otherwise), then `approval_resolved`.
  A declined or unanswered call is reported to the model as not done; stopping the turn while it waits ends
  it with nothing run. Nothing of the turn is stored while it waits, so reloading the page loses the
  question (the turn ends with the request; the client asks before the page unloads, D-105). A failing tool is reported to the model as failed, never with its exception's text. A tool's card
  is held to what the client draws before it is sent (`ToolCardGuard`: a known shape of kind, a table cut to
  12 columns and 200 rows, a link only to this site or https, 200k characters in all), and the cards a turn's tools showed are kept
  with its answer (`ChatMessages.CardsJson`, a JSON array as streamed) and come back as `ChatMessageDto.Cards`
  when the conversation is reopened; the model still gets the text only. Without tools (Finance) a turn is
  one request, read exactly as before
- Studio AI, the assistant on every page, is the profile `studio` (`Services/Assistant/Studio`,
  `StudioChatProfile`): the site's pages (`SitePages`, kept in step with the client's app registry) and the one
  the reader is on (page context `path`) in its prompt, `open_page` (`OpenPageTool`, a link card to a page, a
  word's dictionary page included; a word that is not plain letters is dropped; with `go`, when the reader asked to be
  taken there, the result's `AiToolResult.Navigate` also moves their page: `tool_result.navigate`, which `ClientToolRunner`
  sends only for a site tool's call and only as a plain path on this site, never a connector's, and never once the conversation
  has read outside text, when the model is told the page did not move, `NotMovedNote`; going to /finance or /data-analysis
  with `ask`, `OpenPageTool.HandsOverTo`, it hands the reader's task to that page's own assistant: `AiToolResult.Handoff`, sent
  as `tool_result.handoff` only with that navigation, trimmed to 500 characters, which that page asks as a new conversation, D-095; the market side of the reader's own holdings is read with
  `portfolio_summary` and handed to /finance as their codes, D-097), every `ISiteTool` a feature
  registers (English's `look_up_word`, `add_word_to_repository`, a write, so asked first, `saved_words`, the words the
  reader saved most recently with one meaning each and how many they have (`IRepositoryService.GetRecentWordsAsync`, at most
  30), so it can write a passage with the reader's own words, `learning_progress`,
  the words due for review, the daily challenge, streak and XP, and `review_quiz`, a few multiple-choice questions on
  the reader's due words, else those due soonest (`IReviewQuizService`, `Services/SpacedRepetition`: the daily challenge's
  generator, `GenerateFor`, no AI; the wrong options from the reader's other saved words, topped up from the dictionary by
  id), shown as a `quiz` card the reader answers on, which sends the answers to the review schedule itself when the words
  are due; with `words` (「考我最常錯的這幾個字」 after `weak_spots`, `IReviewQuizService.CreateForAsync`) it asks those words, in
  their order, only those among the reader's saved words; each question says whether its word is due (`ReviewQuizQuestion.Due`,
  the card's `due`), and only those answers go to the schedule (D-119); none saved, a link card to the repository; a read, since making it changes nothing; the model is told the words, and the questions and answers only
  for a client that cannot draw the card; and `weak_spots`, where the reader went wrong lately: their own exam attempts of
  the last 7–90 days (default 30; `IStatsService.GetWeakSpotsAsync`, the most recent 2000 at most), each exam type's right
  answers and the words answered wrong most (at most 10, those still wrong at the latest attempt first, then by misses), as a
  table card, so the model names what to work on and offers a `review_quiz` on those words; with no attempts, a link card to the exams:
  `Services/Dictionary/Assistant`; Portfolio's `portfolio_summary`, the reader's own portfolio as
  /portfolio values it, a table card of the positions, and `update_holding`, a write, so asked first: one holding already in
  the latest snapshot gets a new quantity (`quantity`, or `change` for a trade), the snapshot saved again as today's in
  Taipei (or the latest's date if that is later) the way /portfolio saves one, so the history keeps the earlier one; the cost
  follows the average-cost rule (a sale keeps the average, a purchase adds what was paid, so buying more of a holding with a
  cost needs its `price`, or the total `cost` paid, which is the only one taken when the cost is kept in another currency than
  the price, and is refused without one; a second change on a day replaces that day's snapshot, and the reply says so); the prompt has a trade confirmation the reader attached
  (a broker's screenshot) read out trade by trade and each one put to them as its own `update_holding` call; a new holding, or a name that matches two, is refused with a link to
  /portfolio, never guessed: `Services/Portfolio/Assistant`; Data Analysis's `read_dashboard`, below; Connectors' `google_calendar_events`, `outlook_calendar_events`, `jira_issues` and `jira_add_comment`)
  and the reader's connectors' tools. No tw-stock: /finance has its own assistant for the market. Its prompt also says
  what the morning briefing is (the home page's 「給我今天的簡報」: the calendar, `learning_progress` and
  `portfolio_summary` in one reply, three short parts, and with words due, an offer of a `review_quiz`; `StudioChatProfileTests` pins the tool names it uses). On a
  dashboard's page (`/dashboards/{id}`, `StudioChatProfile.DashboardOf`, an id that parses or nothing) the prompt names that
  dashboard, so "this dashboard" is the one on screen
- Studio AI also finds MCP servers (`find_mcp_server`, `FindMcpServerTool`, Studio's own `IAiTool` beside `open_page`, not an
  `ISiteTool`, so not the site's MCP server's; D-124): a search of the official registry's list (`IMcpRegistry`, below) for the
  reader who asks for a server or for something neither the site's tools nor a page's assistant can do, shown as an
  `mcp-servers` card (at most 6: name, title, host, what it needs, whether the catalog has it, and the address, for the client only to tell a server the reader has already added, D-127; `more` when there were more), whose
  rows send the reader to `/connectors?registry=<name>` to add it themselves; the AI never installs one. The registry's text is
  anyone's, so it is `OutsideText` (the conversation is tainted: no more navigation or handoff, writes ask first; the prompt has
  it hand off first), the model gets hosts, never URLs, and the site's own instructions before the publishers' words. It waits
  20s for the registry (a first walk can take longer) and then tells the model the directory is still loading (D-125).
  A server the reader has already added, turned on or not (`IConnectorToolSource.ServerUrlsAsync`, compared by
  `McpServerUrl.Key`), is told to the model as added, so it does not send them to add it again (D-198)
- Studio AI remembers what the reader asked it to (docs/ai-native/design/08, `Services/Assistant/Memory`): short notes in
  their words, `AssistantMemories` (module Assistant), one line each, at most 200 characters and 30 a reader, the same words
  kept once (`AssistantMemoryService`). Every note is in each Studio prompt (`StudioChatProfile.MemoryPrompt`), inside
  `<user_memories>` with `<`/`>` made full-width so no note can close it, and called the reader's notes, not instructions:
  a note lasts into every later conversation, so one slipped in by someone else's words would too (an attachment does not taint
  a conversation). Hence `remember` is a write that `AlwaysAsks` (`AiToolDescriptor.AlwaysAsks`: `ToolPermissions.For(descriptor, …)`
  never gives it Auto, the connectors page refuses to set it so, and the site's MCP server never offers it; the client gets the
  flag on the tool, `ConnectorToolDto.AlwaysAsks`, and on the call, `approval_required`'s `alwaysAsks`), its card saying the whole
  note (`IDescribesCall`; `<`/`>` are kept full-width, as the prompt shows them), and `forget` names a note by its words as kept (so its card
  shows exactly what goes) and is `Destructive` (always asked; never offered by the site's MCP server). The connectors page
  lists and forgets them (`GET`/`DELETE api/assistant/memories`, `AssistantMemoriesController` → `AssistantMemoryPage`,
  someone else's NotFound). Only Studio AI keeps them; the /data-analysis assistant reads them too, with no `remember` or `forget`
  (`MemoryNotesPrompt.ReadOnlyFor`: the same block, `MemoryNotesPrompt.Block`, and a line sending the reader to Studio AI or the
  connectors page to change one; without notes its prompt is as before), and /finance's does not (its prompt is pinned word for
  word, design/08 §8). The morning briefing's last suggestion uses a note that bears on today. Studio AI may also offer to keep a note the reader did not ask
  for (a goal or preference they mention in passing): a `remember` call after its answer, so a confirmation card, once a conversation (D-103)
- Every turn asks for the summary of the model's thinking (`ChatTurnRequest.ShownThinking`: adaptive, as Haiku 5.5
  thinks anyway, with `display: summarized` in place of its default `omitted`; billed the same, D-136), and `ModelTurn`
  streams each piece that has text as SSE `thinking`. It is not the answer: not added to the text, not counted as the
  answer having started (a turn with thinking and no answer text is still a failure), not stored, never sent again in
  the history; within a turn the tool rounds still send the reply back whole, thinking blocks and signatures included
- A conversation's title is its first question's first 30 characters (`AnthropicChatService.TitleFrom`) until the AI names
  it (D-141): after a new conversation's first answer the client asks `POST conversations/{id}/title` once, and
  `ConversationTitler` (`IConversationTitler`) has Haiku 5.5 read the question and the start of the answer (600 and 800
  characters, as data inside `<question>`/`<answer>` with `<`/`>` full-width, `AsData`, since the answer may repeat a tool's
  result; effort low, no tools, 20s, not on the request's token, so closing the page right after does not lose a paid-for title)
  and keeps its reply cleaned into a title (`ConversationTitler.Clean`: the first line, no markdown, quotes, label or end
  punctuation, `<`/`>` full-width, at most 24 characters). Only the reader's own conversation (else NotFound), only once its
  first two messages are a question and an answer and it still has the title its question gave it (one already named keeps
  its name). One try a conversation while the process remembers it (`IMemoryCache`, a day; two requests at once ask once,
  `InFlight`), so a failed one is not asked again and again — after a restart or a day one still under its question's title
  may be named once more; without the API key or on any failure (deleted meanwhile included) the title stays as it was, still
  200, a reply with no title logged with its stop reason. Not charged on its own: a conversation exists only after a charged
  message, and the call is a few hundred tokens (logged as `chatbot.title`)
- The reader can also give a conversation a title of their own (D-144): `PUT conversations/{id}/title` with `{ title }`
  (`ChatConversationService.RenameAsync`): only their own (else NotFound), kept on one line (`CleanTitle`: every run of
  white space or control characters one space), at most `MaxTitleLength` (40) characters counted as text elements, an
  empty or longer one refused (400), never cut. It leaves `UpdatedAt` alone, so a renamed conversation keeps its place in
  the list; no AI, no charge. A renamed conversation is no longer under its question's title, so the AI does not name it;
  one renamed (or deleted) while the AI was being asked keeps the reader's title: the titler reads the row again before it saves (D-145)
- The history column also finds a conversation by what was said in it (D-191): `GET conversations/search?profile=&q=`
  (`ChatConversationService.SearchAsync`): among the reader's `SearchedConversations` (300) most recently updated
  conversations of that profile, those with a message (question or answer, the stored visible text; attachments are not read)
  holding every word, most recently updated first, at most `MaxSearchHits` (20), leaving out one whose title holds every word
  (the client's title search shows it already, and it would take a place), each with its first such message's id (the client
  scrolls to it, D-193) and a snippet of that message around
  the first word (`SnippetOf`: one line, markdown's marks dropped, from the word too, `SnippetLength` characters with … where
  cut, never inside a surrogate pair). Words are split on white space, NFKC'd, without characters outside the BMP (the default
  collation gives an emoji no weight, so it would match everything), at most `MaxSearchWords` (5); a query past
  `MaxSearchLength` (100) is 400, an empty one finds nothing. Case and width are left to SQL Server's collation (the tests'
  SQLite is case-sensitive). 20 a minute per reader (`RateLimitPolicies.ChatSearch`); no AI, no charge (D-192)
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
- A conversation answers one turn at a time, in order: a new message or an edit first stops a turn still running in that
  conversation and waits until it has saved what it keeps (`ChatTurnRegistry.SettleAsync`, at most `SettleTimeout`, 5s;
  in `ChatbotController.Chat`, once charged, before an edit rewinds the history), so the stopped answer never lands after
  the new question — the reader's "stop and ask this instead" sends the next message at once, and a second tab may be on the
  same conversation (D-177)
- What a reader sends is bounded before it costs memory or is stored (D-158): a message not empty or only white space
  (the API refuses such a text block, so the conversation could never be answered again), at most
  `ChatbotController.MaxMessageLength` (20,000 UTF-16 code units, the client's `MAX_MESSAGE_LENGTH`, held to it by its test)
  and its request 1 MB, refused with 400 before any charge, since a message is stored before the model is asked and kept
  when the answer fails; an attachment's upload 20 a minute per reader (`RateLimitPolicies.ChatAttachmentUpload`); a Word or
  Excel attachment read with each XML part at most `AttachmentTextExtractor.MaxCharactersInPart` (a zip unzips to far more
  than its 10 MB), a cell past Excel's last column (`MaxColumns`) left out rather than padded to, and reading stopped just
  past `MaxChars`. The anonymous PDF unlocker is held to 10 a minute per address (`RateLimitPolicies.PdfUnlock`)
- The Anthropic client comes from `IAnthropicClientFactory`, so `AnthropicChatStreamTests` can give the real
  SDK an `HttpClient` serving recorded API streams: a whole turn is tested offline, from the SSE the reader
  gets to what is saved and logged. A change to how a turn reads the stream gets a case there

## Connectors (what the AI may use)

- docs/ai-native/design/03 is the design. A user's connectors are `ConnectorInstallations` (module
  Connectors): one row per catalog entry they added (a filtered unique index on `(UserId, DefinitionKey)`
  leaves out `custom`, an MCP server of their own, which may be many), with its credentials encrypted, on or
  off, the tools it last listed (`ToolsCacheJson`, read leniently by `CachedTools`: it came from an outside
  server) and the reader's permission for each tool (`ToolSettingsJson`, `ToolPermissions.Parse`). When the tools are
  listed again, one the reader let run on its own whose title, description, schema or effect changed asks first again
  (`CachedTools.Changed`, `InstallationTools.AskFirstWhereChanged`: a server may change what a tool says once trusted)
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
  through `SafeHttp`, a session per request, each response held to 8 MB, `ResponseSizeLimit`; a tool list read at most 5 pages, each name once,
  a tool whose input schema is over 32k characters left out, 400k of schema in all, so no server can make what is stored
  and sent to the model each turn huge); the chat gets a reader's connectors' tools from
  `ConnectorToolSource` (named `m1__…` per turn, their connector the installation's id; the reader's permission for one is
  kept and looked up under the server's own name, `AiToolDescriptor.PermissionName`; two that come to one name are offered once). A call the server
  refuses for its credentials marks the connector NeedsAuth. Probing, adding and testing one (what makes the server reach a
  host a reader named) are held to 20 a minute per reader (`RateLimitPolicies.ConnectorProbe`)
- A reader who does not know a server's URL finds it in the official MCP Registry (`IMcpRegistry`, `McpRegistry`;
  `GET api/connectors/registry?search=`, D-122): the registry's `GET /v0.1/servers?version=latest` read whole, page by page
  by `nextCursor` (at most 40 pages in 60s; a walk cut short keeps what it read), into one list cached an hour for every
  reader and every search (single flight; a first page that cannot be read is 502 and read again next time), and a search
  is a filter of that list (every word, any case, in the name, title or description; name and title matches first, 30
  shown), so no reader's typing reaches the registry or grows the cache.
  Only an active entry with a Streamable HTTP remote at a fixed https URL is listed (canonical as `McpServerUrl` keeps it,
  once an address), with the headers it says are required and the catalog entry at the same address if there is one;
  what was published is read leniently and kept plain (one line, cut, https links only). It is only a pointer: the
  client puts the URL in the add dialog, which probes and adds it like a typed one, so the same `SafeHttp` rules apply.
  A search for a server's whole name puts that server first (Studio AI's card searches by name). The only way an entry reaches
  an AI is Studio AI's `find_mcp_server` (above), as outside text. No usage charge
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
  literal route, matched before the account connections' `callback/{provider}`): the state, then that the reader signed
  in here is the one who started it (else `wrong_account`: a sign-in URL sent to someone else must not put their account
  into the sender's connector; the account connections' callback checks the same), then `iss` (RFC 9207), then the code for tokens, kept encrypted as a `McpOAuthGrant`. `McpOAuthTokens` gives each call its credentials,
  refreshing a token due within 60s, one refresh at a time per connector (a rotated refresh token spent twice
  would end the sign-in); a refused refresh of an expired token marks the connector NeedsAuth and keeps the
  client. The redirect URI and the metadata URL come from `Connectors:CallbackBaseUrl` (`McpOAuthUrls`); an
  authorization server's own words never reach the reader, only an OAuth error code
- A native connector is the site's own code calling a service's API with the reader's account connection (the
  token from `IConnectorService`): an `ISiteTool` in `Services/Connectors/<Service>`. `GoogleCalendarTool`
  (`google_calendar_events`, `Services/Connectors/Google`) reads the primary calendar (today, tomorrow or 7 days,
  whole Taipei days; scope `calendar.readonly`) as a table card; without a Google connection, or one Google refuses,
  it answers with a link card to /connectors rather than an error. Its results are `OutsideText`. `OutlookCalendarTool`
  (`outlook_calendar_events`, `Services/Connectors/Outlook`, D-134) is the same days, events and card from the Microsoft
  connection (scope `Calendars.Read`): Graph's `me/calendarView` for the range's whole Taipei days, with
  `Prefer: outlook.timezone="Taipei Standard Time"` (a time Graph still gives in UTC is converted); `CalendarRange`,
  `CalendarEvent` and the text and card (`GoogleCalendarTool.TextOf`/`CardOf` with the calendar's name) are shared. The
  briefing calls both calendars with `ifConnected` (`CalendarRange.IfConnected`): a calendar not connected then answers a quiet
  line and no card, so a reader who connected one is not asked every morning to connect the other. An all-day event's day is
  its nearest midnight (`OutlookCalendarTool.DayOf`: Graph gives another zone's midnight in Taipei time, hours off).
  `JiraIssuesTool`
  (`jira_issues`, `Services/Connectors/Atlassian`, D-130) reads Jira through the Atlassian connection (scope `read:jira-work`):
  the token's accessible resources give the first site granted Jira (a cloud id of letters, digits and dashes only, an https
  address), searched with the enhanced JQL search (`/rest/api/3/search/jql`, 20 issues) — the model's JQL, on one line and cut
  to 500 characters, or by default the reader's own issues not done; a table card, and one line an issue with its
  `/browse/{key}` link for the model (keys checked to look like Jira's). A JQL Jira refuses (400) is a failed call carrying
  Jira's `errorMessages` (cut), so the model can fix it; any other refusal or failure is said as for the calendar. `OutsideText`.
  `JiraAddCommentTool` (`jira_add_comment`, D-132) is the first native connector that writes: a comment on one issue in the
  reader's name (scope `write:jira-work`, which the connection already asks for), on the same site `jira_issues` reads (the first
  granted Jira; refused when that one is not granted writes, so a comment never lands on another site's issue of the same key),
  a `Write`, so asked first, its card saying the issue and the whole comment (`IDescribesCall`; the card's summary,
  `ClientToolRunner.MaxSummary`, is long enough for it, since the card shows the summary in place of the arguments). A 403 on
  the post is the project's permissions, said as such, not a reason to reconnect. The comment is posted exactly as confirmed:
  at most 2000 characters and refused beyond, never cut; one Atlassian document paragraph a line, text only. It is posted with
  the app's default client (timeout, no retries: `ExternalReadOnly`'s retries could post it twice), and an answer that is
  neither a success nor a refusal (5xx, a timeout) is reported as possibly posted, with the issue's link, so it is not simply
  tried again. Its result is a link card to the comment (`/browse/{key}?focusedCommentId={id}`, the id digits only). A
  conversation that read Jira is tainted, so the call always asks there
- Tests of the connectors' outbound requests use the fakes in `TestHelpers` (each an `IMcpHttpHandlerSource`):
  `FakeMcpServer` (a Streamable HTTP MCP server for the real SDK), `FakeAuthorizationServer` (a whole MCP sign-in:
  401 with `resource_metadata`, PRM, AS metadata, registration, a token endpoint checking PKCE and rotating refresh
  tokens; `Approve` plays the reader), `FakeWeb` (fixed answers by URL). `McpOAuthSignInTests` runs a sign-in end
  to end with every real unit, one DbContext a request as the app has
- The site is an MCP server too, for another AI (Claude Desktop, Claude Code, Cursor) to use the site's tools in a
  reader's name (`Services/Connectors/SiteMcp`, design/03 §9). `POST api/mcp` (`SiteMcpController` → `SiteMcpServer`) is
  Streamable HTTP without sessions: one JSON-RPC message a POST, answered with JSON (`initialize`, `ping`, `tools/list`,
  `tools/call`; a notification is 202), and a request with an `Origin` of another site is refused. It signs in only by a
  personal access token (`Authorization: Bearer jsk_…`, the `SiteToken` scheme, `SiteTokenAuthenticationHandler`), named
  on that endpoint alone: the cookie is not read there and the token opens nothing else, and its principal has the
  cookie's `NameIdentifier`, so `ICurrentUser` works as for the reader. A token (`SiteAccessTokens`, only its SHA-256
  kept, `SiteAccessTokenService`) is made, listed and revoked on the connectors page (`api/connectors/site-tokens`, by
  cookie), shown once, lasts 30, 90 or 365 days, ten a reader. What it is offered: every `ISiteTool` the reader did not
  turn off, the reads always, the writes only with a token made to allow them, nothing that cannot be undone; a tool
  not offered is an unknown tool. There is no confirmation card out there, so the other client's own asking is what
  guards a write. 120 requests a minute per token (`RateLimitPolicies.SiteMcp`); no usage charge, since the other
  client's model does the AI work. `SiteMcpServerTests` also runs the real SDK's client against it
- For clients that sign in to MCP servers only with OAuth (Claude.ai, ChatGPT), the site is that server's authorization
  server too (`SiteOAuthServer`, `SiteOAuthController`, design/03 §9.1): the 401 names its protected resource metadata
  (RFC 9728, `/.well-known/oauth-protected-resource[/api/mcp]`), which names the site as the authorization server
  (RFC 8414, `/.well-known/oauth-authorization-server`; the issuer is the origin of `Connectors:CallbackBaseUrl`). A
  client registers (`POST api/oauth/register`, public clients only, redirects https or loopback) with no table: its
  client id is its registration under data protection (`SiteOAuthClients`). The reader answers on the client's
  `/oauth/authorize` page (`GET`/`POST api/oauth/consent`, by cookie; a request whose client or redirect does not check
  out stays on the site, never sent back), choosing whether writes are allowed; the code (`SiteOAuthCodes`, in memory,
  5 minutes) is spent by the first try at `POST api/oauth/token` (form; PKCE S256, same client and redirect,
  `resource`), which answers with one of the reader's personal access tokens, named after the client, 90 days, no
  refresh token. The anonymous `register` and `token` are held to 30 requests a minute per address
  (`RateLimitPolicies.SiteOAuth`, by `CF-Connecting-IP`: the site takes only Cloudflare's traffic), answered in OAuth's
  error shape; a token made this way is `ViaOAuth`, which the connectors page shows. Only tokens not yet expired count
  toward a reader's limit of 10 (expired rows stay listed, and every sign-in makes one). The consent page shows the
  client's self-given name without control or format characters (`NameOf`) and the redirect's host in ASCII (`HostOf`,
  `IdnHost`: a lookalike name shows as punycode); its answer is held to 20 a minute per reader
  (`RateLimitPolicies.SiteOAuthConsent`: each approval holds a code in memory), and `/oauth/authorize` is served with
  `X-Frame-Options: DENY` and `frame-ancestors 'none'` (`FrameGuardMiddleware`, clickjacking), D-190
- The site's MCP server is also an MCP App (the `io.modelcontextprotocol/ui` extension, design/03 §9.2), so a client that
  draws apps (Claude, ChatGPT) shows a tool's card the way the site's own chat does: `SiteMcpApp` is one self-contained
  HTML page (`ui://jacky-studio/card.html`, script and style inline, values written as text, links only through the
  host's `ui/open-link`), every tool names it in `_meta.ui`, and a result with a table or link card carries it as
  `structuredContent.card` (a link's address made whole). Being sessionless, the server cannot tell whether the client
  said it draws apps, so it always offers it; others ignore it. A link card's address also follows the text in full
- The server also offers ready-made requests, MCP prompts (`prompts/list`, `prompts/get`; design/03 §9.3, D-110): Claude Code
  shows each as a slash command (`/mcp__jacky-studio__daily_briefing`). A prompt is a `SitePrompt` (`Services/Connectors/SitePrompts.cs`):
  a `public static readonly` field next to the tools it asks for (the morning briefing in `Services/Assistant/Studio/StudioSitePrompts`, English's
  `explain_word`/`quiz_me`/`write_with_my_words`/`my_weak_spots`, Portfolio's `portfolio_checkup`, Data Analysis's `read_my_dashboard`), listed in its class's `All`
  and registered with `services.AddSingleton(prompt)` in its module's registration; `SitePromptDefinitionsTests` holds each to the same
  shape (a key a client can use, its tools site tools named in its text, every argument in it). It is offered only when the token is offered
  every tool it names, and one not offered is an unknown prompt (-32602). Only its own arguments are read, as strings, each put on one line,
  trimmed and cut to 100 characters (`SitePrompts.Clean`); a required one left empty is -32602. No usage charge, as for a call
- A card can carry buttons there (`structuredContent.actions`: `{ tool, label, arguments }`): a site tool that is
  `IOffersAppActions` names the next step (the dictionary's lookup: add the word to the repository), and the server keeps
  only those whose tool the token may call (`SiteMcpApp.AddActions`). Pressing one sends `tools/call` from the app through
  the host; the reader's press is the asking, with no model in between
- `ConnectorCatalogService` answers the page (`AiConnectorsController`, `api/connectors/catalog`,
  `tool-permissions`, `installations/{id}/enabled`, `installations/{id}`, in the `{ data }` envelope; the
  account connections' endpoints of `ConnectorsController`, under the same route, are unchanged) and, as
  `IToolPermissionSource`, each chat turn. Someone else's connector, or the site's own for turning off or
  removing, is NotFound

## Job Finder

- `GET api/job/listings` takes no sign-in, and each new search is a round of calls to 104 and Cake from the App Service's one
  address and a 30-minute cache entry: held to 20 a minute per address (`RateLimitPolicies.JobSearch`), a search at most
  `JobAggregatorService.MaxKeywordsLength` (100) characters (the client's `JobSearch/limits.ts`, held to it by its test), and
  cached under the platforms actually asked, never the caller's words for them. A Cake external posting's link is kept only
  when it is https (`CakeJobsProvider.HttpsUrl`; D-165)

## Data analysis

- `DataAnalysisController` does HTTP only. The engine queries (schema, max date, distinct values,
  summarize, details, CSV export) are `IDataAnalysisQueryService`, the question in plain language is
  `INlSummarizeService`. They and dashboard widgets take the source, the schema gate, the summarize and the
  answer to a failed query from `EngineSources`: the default source's driver text stays in the log, and a
  custom source's goes only to its owner. A widget resolves its source as the dashboard's owner, so any
  allowed viewer sees it, and passes `viewerIsOwner` so no other viewer gets that text. Each answer is a record (`MaxDateDto`, `DistinctValuesDto`, `SummarizeRowsDto`, `DetailsPageDto`).
  An export hands its rows to the controller's CSV writer while the service still holds the gate that lets
  two run at once
- The anonymous session upload's rows are read by `SessionRowsJsonConverter`, which counts their estimated size in memory as
  `SessionDataStore.Measure` does while it reads and stops past `SessionUploadOptions.MaxReadBytes` (a 400): the admission
  filter only sees the body's length, and tiny cells cost far more in memory than in JSON (D-158)
- Each caller's live sessions may hold at most `SessionUploadOptions.MaxBytesPerCaller` (128 MB, half the 256 MB budget,
  two of the largest uploads) of the session budget (`SessionUploadBudget.TryReserve(bytes, caller)`, given back when a
  session expires): the caller is the signed-in user, else the address Cloudflare saw
  (`DataAnalysisController.SessionUploadCaller`), so one visitor cannot hold the whole budget for two hours (D-159). Over
  their share is 429 with `CallerShareMessage`; over the whole budget still `BusyMessage`
- A custom SQL Server source names a server on the internet, over TCP (`DataSourceConfigValidator.ValidateServer`, on save and
  before every connection, D-164): no named pipe, shared memory, local name, bare machine name, or address
  `OutboundAddressPolicy` refuses (private, link-local, the metadata service), for the server and its failover partner alike, and
  no `Enclave Attestation Url`, since the driver would reach those from the App Service and its owner is told how each try failed.
  Before each connection `DataSourceResolver` also resolves the server's names (`IServerAddressCheck`, `DnsServerAddressCheck`:
  every address public, 5s at most) and refuses one that points inside (`PrivateServerMessage`, 400); the driver resolves again
  when it connects, so a DNS answer that changes in between is not stopped (it cannot be handed the checked addresses, as
  `SafeHttp` connects to them), nor can a login the server routes elsewhere (TDS routing). So the owner is never told how a
  connection failed (refused, timed out, reset, not SQL Server): those driver texts become `EngineSources.ConnectionFailedMessage`
  (`EngineSources.OwnerMessage`); a login, permission or query error is still shown as the driver says it. Saving one is held to
  `RateLimitPolicies.ConnectorProbe`. Tests give the resolver
  `TestHelpers/PublicAddresses`
- A dashboard widget's rows (`GET api/dashboard/{id}/widgets/{widgetId}/data`, anonymous for an "Anyone" dashboard) run the
  owner's saved query again on the owner's source, uncharged, so they are held to 600 a minute per reader and 120 per address
  without a sign-in (`RateLimitPolicies.DashboardWidgetData`, D-163); a page asks once per widget when it opens
- A dashboard read by anyone but its owner (`DashboardService.GetAsync`) carries of each widget's plan only its group-bys'
  output names and kinds (`ViewerPlan`, D-195: alias, else column, and `type`, which the chart needs for a time axis), never
  the source, table, measures or filter values: an "Anyone" link reaches strangers. A widget is refused past what it can
  store (`MaxWidgetTitleLength`, `MaxVizTypeLength`, `MaxPlanLength` 16 KB) and a dashboard past `MaxWidgetsPerDashboard` (50)
- Tests build the controller with `TestHelpers/DataAnalysisControllerBuilder`, never its constructor;
  `DataAnalysisControllerResponseTests` pins what each endpoint answers
- The page's AI assistant is the chat profile `data-analysis` (`Services/DataAnalysis/Assistant`,
  `DataAnalysisChatProfile`). `describe_data` (`DescribeDataTool`, free) lists the columns, their types and the
  latest date of up to 3 date columns; `ask_data` (`AskDataTool`) answers a question in plain language
  through `INlSummarizeService` — the same checked plan, correction and notes as 問 AI, never a query of the
  model's own — charging each call as `UsageFeature.NlSummarize`, and shows the result as a `chart` card (the
  client's viz id, the rows, group/date hints, and the plan with the page's source, so it can become a
  dashboard widget; none for an uploaded file's session). Over an uploaded file both tools' results are outside text
  (`AiToolResult.OutsideText`: its cells and headers are whoever made the file's words, D-163); over a source they are not, since
  it is the reader's own database (or the site's) and tainting it would stop 「存完打開」 (D-156). A chart with a plan is also kept for a while by a short id the
  model is told (`chartId`, `DataAnalysisCharts`, in memory per reader and conversation), so `save_to_dashboard`
  (`SaveToDashboardTool`, a write, so asked first) saves exactly the chart the reader saw to the dashboard they named
  (one they own of that name, or a new one) without asking the data again; with `open` (the reader asked to see it once
  saved) its result also takes the page there (`AiToolResult.Navigate`, as Studio AI's `open_page` with `go`). Asked for a whole
  dashboard (「幫我做一個業績總覽 dashboard」), the prompt has it ask 3–4 charts in one reply and save them in the next, all to
  one name (the calls run in order, so the first creates it and the rest find it), `open` on the last only: one
  confirmation card for all of them.
  `read_dashboard` (`ReadDashboardTool`, an `ISiteTool`, so Studio AI's and the site MCP server's, not this page's) reads one of
  the reader's dashboards as it is now: each chart's rows re-run through `IDashboardService.GetWidgetDataAsync` (the first 6
  charts, 40 rows each, within 20s: a chart not done by then and those after it are reported as not read), by id or name,
  and only a dashboard they own: a shared one holds someone else's titles and data. Its rows are `OutsideText` (a source can
  hold other people's words) Which data it reads is the page context the client
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
