# CLAUDE.md (jackyaiapp.client)

Guidance specific to the frontend package. See the [root CLAUDE.md](../CLAUDE.md) for architecture, features, and cross-cutting conventions.

## Development Commands

```bash
npm run dev          # Start development server (https://localhost:5173)
npm run build        # TypeScript compilation + Vite production build
npm run lint         # ESLint with TypeScript, max 0 warnings
npm run preview      # Preview production build
```

## Frontend Structure

```
jackyaiapp.client/src/
├── apis/           # RTK Query: the shared api.ts and the feature slices (dictionaryApis, financeApis, etc.)
├── [Feature]/      # Feature-based components (Dictionary/, Finance/, etc.)
├── components/     # Reusable UI components
├── redux/          # Store configuration
├── auth/           # Authentication components
└── constants/      # App configuration
```

## API slices

- A feature in the shared API (`apis/api.ts`: repository, review, stats, daily challenge, exam)
  adds endpoints with `api.injectEndpoints` in its own `apis/[feature]/index.ts`, paths relative to
  `/api`. A new tag goes in `TAG_TYPES` there.
- When a mutation changes data another feature shows, it invalidates that data's tag with
  `invalidatesTags: onSuccess(...)` (not a bare array: RTK Query also applies that to a mutation the
  server answered with an error). `apis/api.test.ts` pins which mutation refreshes what.

## Component structure

- A component that outgrows one file becomes a folder of the same name: `index.tsx` keeps the
  composition (and the export, so imports do not change), each section is its own file, and a rule the
  JSX was deciding inline — which notice wins, what is shown when — is a pure function with its own
  tests. `Portfolio/DrawdownBrakeCard/` is the pattern: why advice is held back is `noAdviceReason`
  (one reason, in order), the prices it is computed at `useStrategyPrices`
- A feature's page is already its folder's `index.tsx`, so its sections become files beside it and the
  page keeps only its states (loading, error, empty, the dashboard) — `Portfolio/index.tsx` with
  `PortfolioHeader`, `PortfolioDashboard`, `NetWorthCard`, `AllocationCard`. A section takes the DTO it
  shows (`summary`), not one prop per field
- A component whose bulk is state rather than markup splits into hooks instead: the component keeps
  the layout and wires them together. `components/aiChat` (the AI chat shared by the apps; Finance's assistant is its first user) is the pattern: the conversation
  and its queue are `useChatConversation`, the picked files `usePendingAttachments`, the preview
  `usePreview`, speaking a message instead of typing it `useSpeechInput` (the browser's Web Speech API,
  zh-TW; the words go into the box after what was typed and the reader still sends; no button where the browser
  cannot listen, e.g. Firefox), and reading an answer aloud `useSpeechOutput` (the answer's button 朗讀: the browser's speech
  synthesis, nothing sent to the server and no usage; `utils/speechText` turns the Markdown into words and cuts them into
  runs of English and Chinese, each read in a voice of its language, an abbreviation in a Chinese sentence staying Chinese,
  in pieces of at most 200 characters; one answer reads at a time, and only the one reading may stop the browser's speech).
  Asked by voice, answered by voice (D-120): a message whose draft still holds the words the microphone heard (`ChatInput`'s `onSpoken`, the
  panel's `heardWords`, forgotten when the box is emptied; `utils/spokenDraft`'s `stillSpoken`: half of them still there in order, so a
  fixed misheard word keeps it spoken and deleting them for typed words does not, D-129), or an ask with `readAloud` (`askStudio`, `ChatAsk`), goes with
  `SendOptions.readAloud`, and its answer (`Message.readAloud`) reads itself aloud once it is `answered` — never a stopped or
  failed one, nor a reopened conversation; once per message id (`useSpeechOutput`'s `autoReadKey`, started a tick later so
  StrictMode's second mount reads it once), its buttons kept shown while it reads so 停止朗讀 is there; only while the panel is
  `active` (folding it or closing the drawer stops a reading), never over a reading the reader started nor into a listening
  microphone (`isListening`), and pressing the microphone stops the answer being read (`stopReading`, D-121). The presses that ask
  for a reading later (the microphone, 用聽的) first say an empty utterance, `unlockSpeech`, since iOS Safari only speaks after a
  start in a gesture. Nothing of it reaches the server.
  What differs between apps' assistants (greeting, example questions, suggestions,
  placeholders, the server profile, error wording, words for tools the server names no display name for,
  `toolLabels`: Finance's hosted tw-stock tools, `FINANCE_TOOL_LABELS`, so its chips and header read 「個股歷史股價」
  rather than "stock history"; the ready-made requests a draft starting with `/` lists over the box, `slashCommands`, D-114:
  Finance's market questions that name no stock, `FINANCE_SLASH_COMMANDS`, each one of its example questions, and Data Analysis's
  columns, highlights, trend and a whole dashboard, that last left out for an uploaded file; matched as Studio AI's are,
  `utils/slashCommands`' `matchSlashCommands`, drawn by the same chips, `components/QuickChips`, and a press empties the box and
  sends one in the conversation on screen; so does Enter (or the send button) on a `/` draft, for the first one listed, which the chip marks ↵,
  through the panel's `onSendDraft`, D-115; with none listed, or with files picked, the text goes as typed) is an `AiChatConfig` (`components/aiChat/config.ts`)
  that the app passes to `AiAssistantLauncher` (Finance's is `Finance/aiChatConfig.ts`; Data Analysis's depends on what
  is on screen, `dataAnalysisAiChatFor`: nothing about dashboards for an uploaded file, whose charts cannot be saved); the chat's
  parts read it with `useAiChatConfig()`, so their tests render under an `AiChatConfigContext`. The model's
  thinking (the stream's `thinking` pieces, D-136) is `ThinkingTraceView` above the answer: folded to 「思考中… 3 秒」 with
  its latest line while it thinks, 「已思考 8 秒」 once the answer, a tool or a question to the reader took over (`utils/thinking`:
  `addThinking`, `pauseThinking`; thinking that resumes after a tool is a new paragraph), unfolded on a press; plain text, never
  Markdown or links (it may repeat what a tool read); the header says 💭 思考中… and no dots are drawn meanwhile; this session
  only (`Message.thinking`), and a failed turn drops it with its text. The tools
  an answer used are `ToolActivity` (from the stream's `tool_use`/`tool_result`, `utils/toolActivity`),
  a write waiting for the reader is `ApprovalCard` (`approval_required`, `utils/approval`; several calls are decided one
  by one or together, 全部拒絕 always and 全部允許 only when all are the site's own tools, none destructive, and the conversation read no outside content,
  and 「以後直接執行」 is offered once per tool; MessageArea keys it by `pendingId`, so a later question is a new card; a call the site describes in words,
  `summary`, shows that with its raw arguments behind 「顯示送出的參數」, an outside connector's always shows them; while it waits it comes after the
  answer's text, with no thinking dots, and once answered it stays above the text as a record; the header says
  what the turn is doing, `hooks/chatStatus`, a tool by its display name and「等你確認」while it waits; while a card waits it says the time left to answer (`approvalTimeLeft`, from when it arrived, `askedAt`, to the server's 3 minutes less 5 seconds, `APPROVAL_TIMEOUT_MS`, which a test holds to `ClientToolRunner.cs`; seconds in the last minute, D-106; one the time ran out on says so, not 已拒絕, `resolveApproval`, D-108), and (`hasOpenApproval`) reloading or closing the page asks the browser's 「確定要離開？」 first (`useWarnBeforeLeaving`, D-105), since leaving ends the turn the card waits in, and the tab's title starts with 「（等你確認）」 (`useWaitingTitle`, counted across chats, put back on a title a page sets meanwhile, only its own mark taken off, D-107); a card still open when its turn ends says nothing ran (D-109); for a site tool's write, outside a
  conversation that read outside content, it offers to let the tool run on its own from now on, `canAlwaysAllow`, which
  sets the same permission as the connectors page). Under the last answer that finished
  in this session (`Message.answered`), `FollowUps` offers up to three questions to carry on with, which a press sends:
  `AiChatConfig.followUps`, rules by the tools the answer used, picked by `utils/followUps` on the client (no tokens, and
  nothing a tool read can steer them). What a tool's
  result shows in the answer is a card (`tool_result.card`, `cards/`): it comes from a tool, so it is
  untrusted, and `parseToolCard` keeps only plain text and numbers within limits and links to this site
  or https. The chat draws `table` and `link`; another kind is drawn by the app's renderer for it
  (`AiChatConfig.cardRenderers`), or not at all. A card that is a table (a table card, or an app's card `AiChatConfig.cardTables` reads as one:
  Data Analysis's chart) has 複製表格 and 下載 CSV under it (`cards/TableCardActions`, `cards/tableExport`, D-150): tab-separated
  cells for a spreadsheet's paste, or a CSV with a BOM (so Excel reads Chinese), numbers plain, the rows on screen only (and it says
  when the tool found more); a text cell or header a spreadsheet would run as a formula (=, +, -, @, a tab or CR first, and not a
  number) gets a leading apostrophe, the server's `CsvExportWriter` rule, since the cells are a tool's, and so does a code with
  leading zeros (0050, else read as 50); a pasted cell with a `"` in it is quoted, since a paste unquotes a cell that starts with one
  (D-151). A table card's header sorts by its column (`cards/tableSort`, D-152: a number column from the largest first, text
  in zh-TW order, numbers written as text — 49.5%, 1,200 — by value, empty cells last, a third press back to the tool's order); only
  on screen, and the two buttons take that order. Nothing goes to the server. `utils/clipboard`'s `copyText` throws when nothing was copied, so no button says 已複製 then. Each message is `memo(MessageItem)` (D-138): while an answer streams (every piece of text or thinking a new
  `messages` array, the other messages kept as the same objects by `useChatConversation`) and while the reader types,
  only the message that changed is drawn again; the page's callbacks reach the messages through one `handlers` object
  `MessageArea` keeps for the conversation's life (calling the latest ones). A new `MessageItem` prop must not be an
  object or function made anew each render, or every earlier answer's Markdown and charts are drawn again with it
  A failed last answer that asking again may fix offers 重試 under it (D-139, D-140; `Message.retryable`, `useChatConversation`'s
  `retry`): a failure `isRetryableChatError` counts so (the service busy, no answer, a cut stream — not a conversation too long, a
  request too large, a decline or the assistant switched off), only once the server said `start` (it stored the question) or when
  the request never reached it (fetch rejected): a refused request or one cut before `start` may have been stored with its files
  unannounced, and sending it again would leave a second copy and lose the files. Never after one of the site's or a connector's
  tools ran in it (a step with an `id`): a failed turn is not stored, so the model asked again would not know a write happened.
  Its question goes again in its place: as an edit of itself when the server stored it (the server re-attaching its files), as the
  edit it was when it was one the server never got (`Message.replacesServerId`), otherwise anew with its files, its quoting of page
  text (`Message.quotesPageText`), its handed-over mark and its asking to be read aloud
- A new conversation is named by the AI once its first answer is in (D-141): `useChatConversation` asks
  `nameConversation` (`POST conversations/{id}/title`) after the `done` of a turn with no answer before it in the
  conversation (a stored one has a `serverId`; a failed one has none, so a first question that failed and is asked again
  is still named; a stopped answer is stored and counts), and the list is read again, so the history column's row changes from the question's
  first words to a short title (「持股的外資買賣超」) a moment later. Every assistant, no settings
  With six or more conversations the history column has a search box (D-143): titles holding every word typed
  (`utils/conversationSearch`' `matchConversations`, case and full-width forms ignored), on the client only; Escape
  empties it before it closes anything. Each row's pencil (shown as its delete button is) turns its title into a box,
  `ConversationTitleEditor` (D-144): Enter or leaving the box saves, Escape keeps the title and goes no further;
  `utils/conversationTitle`' `renamedTitle` sends one line, nothing when empty, unchanged or past `MAX_TITLE_LENGTH`
  (held to the server's by its test). `renameConversation` patches the list at once and puts it back if the server refuses,
  and the column then says so for a moment (`RENAME_FAILED_TEXT`, D-146); a box ended with Enter or Escape gives the focus
  back to its row's pencil (`onClose(byKey)`) on that commit only, one left with the mouse does not
- The header's 匯出對話 (`ExportConversationButton`, D-147) copies the conversation on screen as Markdown or downloads it as a
  .md file: `utils/conversationMarkdown` writes the history column's title (the list read when the menu opens), the time in
  Taipei, each question and answer under its heading, the tools an answer used, its table cards as Markdown tables (numbers and
  empty cells as `TableCardView` shows them, `|` escaped) and its link cards whole (a path on this site gets the origin); a card
  an app draws itself is written as a table when the app says how (`AiChatConfig.cardTables`, D-149: Data Analysis's chart is its
  rows, `chartCardTable`), else named in one line (one this chat has no renderer for is not on screen, so not either),
  and the thinking and confirmation cards are left out. A message's own Markdown keeps the document whole (`messageBody`,
  D-148): its headings two levels under the turns' and a code block it left open (an answer cut short) closed at its end; a
  card's words are escaped so they open no link, emphasis or markup. Nothing goes to the server; disabled while an answer
  is written and before a first question
- Studio AI (`components/StudioAssistant`, mounted once in `App.tsx`) is the same chat as a sheet: a button
  in the lower right, or ⌘K / Ctrl+K, opens `AiAssistantPanel` with `variant="sheet"` (fills a right-hand
  drawer, the history behind its button, the header's last action closes) under the `studio` profile, with
  the path as its page context. Only for a signed-in reader, and not where a page has an assistant of its own
  (/finance, /data-analysis) or fills the screen; which pages, the suggestions per page and the shortcut are
  `studioChat`. It is the site's command bar too: while the reader types, a few words naming a page ("複習",
  "portfolio", "帶我去 dashboards") or one English word offer to go straight there (`quickActionsFor`, shown over the
  box by `QuickActionChips` through the panel's `inputAccessory`): a press navigates, empties the box and closes the
  drawer, with no AI and no usage; Enter still sends the text to the AI. A draft starting with `/` lists Studio AI's ready-made
  requests instead (`slashCommands`, matched by the chat's `matchSlashCommands` like every assistant's, D-111: 今日簡報, 考我單字, 用我的單字寫短文, 找出我的弱點, 投資組合健檢, …, narrowed by what follows
  the slash, in either language or by the site MCP server's prompt name, `/quiz`): a press sends one as if typed, the drawer
  staying open (the briefing as a new conversation, as the home page sends it). The page's own come first
  (`pageSlashCommands`, D-113): 解釋 {word} on a word's dictionary page, 記一筆交易 on /portfolio, 做一個新的 dashboard on /dashboards, 解讀這個 dashboard on one of the reader's own
  dashboards (in the /dashboards list with `isOwner`; not on a shared one, nor before the list is read). These ask the AI, so the chips say 現成指令
  rather than 不用問 AI. Those that are also the server's MCP prompts keep its names, which their test holds to the server's files. Its page list, `QUICK_PAGES`, is held to the
  server's `SitePages.cs` by its test; the reader's dashboards (the /dashboards list, read each time the drawer opens)
  are offered by their name or two characters of it (「打開『業績追蹤』」), and on /dashboards the first suggestion reads the
  first of the reader's own (「幫我解讀『業績追蹤』這個 dashboard」). On a word's dictionary page its suggestions are about
  that word (`dictionaryWordOf`). On the repository the first is writing a passage with the reader's own words (`saved_words`); on the stats and exam
  pages, finding where they went wrong lately (`weak_spots`, `FIND_MY_WEAK_SPOTS`, also the `/` command 找出我的弱點, the server's MCP prompt `my_weak_spots`). A review quiz (`review_quiz`) comes as a `quiz` card, Studio AI's own renderer
  (`QuizCardView`, `quizCard.parseQuizCard`): one question at a time, right or wrong shown at once, and after the last the
  answers go to the review schedule as the review page sends them (`review/submit`, right 4, wrong 1), no AI involved —
  only for words that are due (each question's `due`; a quiz on words the reader named may mix them, D-119), and only the first time: a card drawn again from a reopened conversation (a renderer's
  `saved`, which `ToolCards` passes for stored cards) and a quiz on words not yet due are practice, recording nothing. A page opens it with a message sent through `askStudio(text, { fresh? })` (`StudioAssistant/askStudio`, a window
  event; the panel's `ask` prop sends each id once, and its owner holds the asks in order until each is sent,
  `aiChat/hooks/useAskQueue`, so two asked before the lazy panel has loaded both go; `fresh` sends it as the first message of a new conversation,
  `useChatConversation`'s `send(…, { fresh: true })`, queued like any message while an answer is written): the home
  page's 「AI 今日簡報」 (`Portal/BriefingCard`, signed in only, nothing asked until clicked) sends `BRIEFING_PROMPT`,
  always fresh, so it neither resends an old conversation's history nor inherits that conversation's tainted writes. That card is
  `StudioAssistant/AskStudioCard` (icon, title, description, the request; pressed, it asks fresh; with `listen`, the briefing's,
  a 「用聽的」 button beside it asks the same with `readAloud`, where the browser can speak, D-120), which the stats page shows too as
  「AI 弱點分析」 (`FIND_MY_WEAK_SPOTS`, D-118). A dashboard's owner gets
  「AI 解讀」 (`READ_DASHBOARD_PROMPT`, fresh; the server names the dashboard on screen and `read_dashboard` reads it), which is
  also Studio AI's first suggestion on a dashboard. A tool result's `navigate` (Studio AI's `open_page` with `go`, a path on this site only, `sitePathOf`, as it streams,
  never from a reopened conversation) moves the page under the drawer through the panel's `onNavigate`; on a phone the
  drawer then closes once the answer is done. A result going to /finance or /data-analysis can hand that page's own assistant
the reader's task (`tool_result.handoff`, `aiChat/utils/handoff`, D-095: 「幫我做一個業績總覽 dashboard」 asked of Studio AI is
made by /data-analysis's assistant, which has the data tools): once the answer is done as an answer (not stopped or failed)
the page goes there with the question in the router's location state, which only the site's code can set, and that
page's `AiAssistantLauncher` opens and asks it once as a new conversation, then replaces the history entry without it,
so going back or reloading asks nothing again. That message is marked 「由 Studio AI 轉來」 over its bubble (`SendOptions.handedOverBy`,
`Message.handedOverBy`, D-099), so the conversation is not one that started by itself; the mark is the client's alone, never
sent or stored, so a reopened conversation shows a plain message. A site tool's write that worked (`tool_result` with `ok`, the panel's
  `onToolDone`, as it streams) has the page under the drawer read again what it changed (`refreshAfterTool`:
  `update_holding` the portfolio's `Holdings`/`Summary`, `add_word_to_repository` what adding a word on the repository
  page refreshes), so the change shows there at once. Selecting text on a page also asks it (`aiChat/selection`: `SelectionBar`, `readSelection`, `selectionActions`, shared
  with the apps' own assistants, below): once a selection
  settles (250ms), a bar over it offers 解釋 / 翻成中文 or 翻成英文 / 問 AI, or for one English word 查字典 (straight to
  its page, no AI) / 加入單字庫 / 問 AI. 問 AI puts 「關於「…」，」 in the box after what was typed (from the page, the box then
  sends with `quotesPageText`, the panel's `inputQuotesPageText`, until it is emptied); the others send at once.
  From the page they go as a new conversation with `quotesPageText` (the chat request's flag: page text may be someone
  else's words, so the server counts the conversation as having read outside text and its writes ask first); from one of
  Studio AI's own answers (the drawer is `[data-ai-assistant]`; its header and list of conversations are not answers and
  are marked `noSelectionAsk`) they continue the conversation on screen; what each press
  does is `selectionStepOf`, the same for every assistant. Text in a box,
  a dialog, another drawer or a menu is left alone, and so is what a page marks with `noSelectionAsk(on)` (`data-no-selection-ask`):
  a question still being answered, where explaining or translating it would give the answer away (`ExamRoundShell`'s
  question, the daily challenge's `QuestionCard`, the review `Flashcard` before it is turned, `QuizCardView` before its
  question is answered; D-088), and a dashboard's grid while its layout is edited; the bar follows the selection as things scroll, goes on Escape (taken
  before MUI's, so the drawer stays open) or when the selection does, stays while pressed (a touch browser may clear the
  selection on the tap), is kept on the screen, and sits
  under the selection on a touch screen, where the browser's own menu is above. On a page without the button only the button goes: the drawer, and the conversation in it, stay
  mounted once opened (one of its own link cards may lead to /finance). An app's own assistant (`AiAssistantLauncher`) is asked the same way on its page (D-089): selecting the
  page's text (/finance's analyses and digest, /data-analysis's results) or its answers (its box is `[data-ai-assistant]`)
  shows the same bar, named after it, which opens the conversation as `askStudio` opens Studio AI's (`ask`, from the page
  `fresh` with `quotesPageText`); it has no repository tool, so one English word offers 解釋 / 查字典 / 問 AI
  (`canSaveWord: false`). It moves the page only when
  the page passes `onNavigate` (/data-analysis: `save_to_dashboard` asked to open what it saved), and only once the answer is
  done (`AssistantActivity.answered`: not when the reader stopped it, or it failed), since the assistant is part of the page
  it would leave. Leaving a page that holds a chat stops
  its answer (`useChatStreaming` stops on unmount), and a stream that ends without `done` or `error` is a failure
- A dashboard is read on a phone one chart a row (`Dashboard/viewLayout`, below 600px, in the saved layout's reading
  order): only the view; editing, and wider screens, keep the layout as saved
- An answer's HTML preview (`HtmlViewer`) is a `sandbox=""` iframe, never markup in the page: the answer can
  echo what a tool read (someone else's words)
- `/connectors` (`Connectors/index.tsx`) keeps the account connections (Google/Microsoft/Atlassian,
  `ConnectorCard`, unchanged) and puts above them what the AI may use, `AiConnectorsSection`: the
  reader's connectors (`InstalledSection`, each opening `ConnectorDetailDrawer` with the tools'
  `ToolPermissionToggle`), the catalog (`CatalogSection`) and adding an MCP server of their own
  (`AddMcpServerDialog`: URL → credentials → tools; a reader without a URL searches the official MCP Registry right there,
  `RegistrySearch`, folded until asked or opened from the catalog's last card 找更多 MCP server, D-122: picking one checks its URL as if
  pasted, named by its title, an API key's header filled in, `registryView`; one the catalog has is connected from the catalog).
  `/connectors?registry=<search>` (`connectorsRegistryPath`, `registrySearchOf`, D-124) opens that dialog with the search filled in,
  once (a second one while it is open starts the dialog over with it: the form is keyed by the search), and drops the parameter (at the custom-server limit it says so instead): Studio AI's `find_mcp_server` answers with an
  `mcp-servers` card (`McpServersCardView`, `mcpServersCard.parseMcpServersCard`, Studio's own renderer beside `quiz`) whose rows'
  加入 go there with the server's name, and Studio AI's drawer steps aside on such an address, keeping the conversation. A server the reader has
  already added (its address, `connectorView.serverUrlKey`, among `installedServerUrls`) shows as added there and in the dialog's registry
  search, not offered again (D-127). A connector the reader has added, working and with a tool the AI may see (`connectorView.canTryInStudio`), offers
  「在 Studio AI 試試看」 on the dialog's last step and in its detail drawer (D-126): the dialog or drawer closes, then
  `askStudio(tryConnectorPrompt(name), { fresh: true, quotesPageText: true })` asks Studio AI, in a conversation of its own (whose tools are read anew,
  the new connector's among them), to use one of its reading tools once and say what else it can do; the name, possibly a
  publisher's, goes as one plain line within quotes, and the message counts as quoting outside words (`AskStudioOptions.quotesPageText`, D-128). Which entries show, in what order, and what
  「連線」does for one are `connectorView`; the dialog's checks and the request it sends are
  `AddMcpServerDialog/addMcpServer`. The catalog failing to load leaves the account connections working.
  Below it, `MemorySection` lists what the reader asked Studio AI to remember (`assistantMemoryApis`, in the shared API,
  tag `AssistantMemory`, which Studio's `remember`/`forget` also refresh through `refreshAfterTool`) and forgets one on a
  second press; keeping one is only Studio AI's 「記住…」, asked first in the chat (D-100). A tool the server marks `alwaysAsks`
  (`remember`) is like one that cannot be undone there: no 自動 on the connectors page (`allowedPermissions`), no 「以後直接執行」
  (`canAlwaysAllow`) and no 全部允許 on the card (D-101).
  Below that, `SiteMcpSection` lets other AIs use the site: the site's MCP address, the reader's tokens (revoked after a
  second click) and `CreateTokenDialog`, which shows a new token once, with the settings for Claude Code, Claude
  Desktop, Cursor and VS Code filled in (`clientConfigs`), and for a client whose way to them is known, where it shows the server's
  ready-made requests (MCP prompts: `ClientConfig.prompts`, naming `BRIEFING_PROMPT`, which its test holds to the server's
  `StudioSitePrompts.cs`); what a token's row says is `tokenView`
- `/oauth/authorize` (`OAuthConsent`) is where another AI's OAuth sign-in to the site's MCP server asks the reader:
  who asks, where they go back to, whether writes are allowed. Not under `RequireAuth`, which would drop the query: a
  signed-out visitor signs in from the page and comes back with the whole request (`authorizeRequestOf` keeps only
  OAuth's fields). Studio AI's button stays off it
- Split without changing what renders: the component's existing tests stay as they are and must pass.
  One without tests gets them first, run against the code as it is (`Portfolio/index.test.tsx`)

## UI Conventions

### No hover-only tooltips

MUI's `<Tooltip>` is not used directly in feature code — on touch screens it only opens
after a long press, so anything living inside one is effectively invisible on mobile.
Use `@/components/hints` instead:

| Component | Use for | Behaviour |
| --- | --- | --- |
| `ActionTooltip` | Naming an icon-only control ("Refresh", "Close") | Hover tooltip on pointer devices; on touch it renders the child as-is and contributes an `aria-label` |
| `InfoPopover` | Explanation next to a label or an already-clickable control (formulas, grading scales) | Renders a "?" button that opens a click/tap/keyboard-triggered `Popover` |
| `ClickPopover` | Explanation attached to a non-interactive element (stat card, chip, heatmap cell) | Makes the element itself tappable to reveal the copy |

A **disabled** control gets neither hover nor tap, so the reason it is disabled belongs in
visible caption text next to it — not in any of the above.
