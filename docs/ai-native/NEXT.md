# NEXT — 下一輪從最上面開始

> 設計已定稿（PLAN.md）。M1–M4 與延伸（本站 MCP server、OAuth、MCP App）都完成；第 10 輪加了跨訊息 taint、AI 今日簡報、語音輸入、MCP App 按鈕；第 11 輪加了接著問、圖表卡片換圖、「帶我去」；第 12 輪加了「存完打開」、dashboard 的「AI 解讀」（`read_dashboard`）、確認卡精簡、手機 dashboard 一欄；第 13 輪加了 ⌘K 指令列（`quickActions`）與 /finance 工具中文名稱（`toolLabels`）；第 14 輪加了指令列打開 dashboard、字典頁與 /dashboards 的 Studio 建議、**複習小測驗**（`review_quiz`＋可作答的 quiz 卡片）。第 15 輪加了 **`saved_words`**（用自己的單字寫短文，單字庫頁的第一個建議）與回答的 **「朗讀」**（`useSpeechOutput`，英中分聲音）。11/4 起功能凍結；在那之前還有很多輪，可以再加「少而精」的驚艷點，但每一個都要能 demo、有測試。

## 0. 每輪固定動作

- **開工時與收尾前都同步 master**：`git fetch origin master` → 有新 commit 就 `git merge origin/master`（**不用 rebase**，D-014）。
- **先看 CI**：`gh run list -R twjackysu/JackyAIApp --branch feature/ai-native --limit 3`；紅的先修（annotation 看法見排程指示）。被後一次 push 取消（cancelled）是正常的，看最新一次。
- **後端改動先跑離線型別檢查**（D-021）：`sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0`，然後 `CSCHECK_OUT=/tmp/cscheck .loop/cscheck/run.sh origin/feature/ai-native`（**以 branch 為基準**，只列這輪新出現的診斷；以 master 為基準會列出 30 多個已知雜訊）。已知雜訊：EF 擴充方法（`AsNoTracking`、`FirstOrDefaultAsync`…）、`SaveChangesAsync` 的 CS1503、`[GeneratedRegex]` 的 CS8795、`using var db = new AzureSQLDBContext` 的 CS1674。
- **畫面驗證**：`npm run build` 後用 playwright（`NODE_PATH=$(npm root -g)`）`page.route` 從 `dist/` 回檔案、`/api/*` 回假資料；SSE 用 `route.fulfill({ contentType: 'text/event-stream', body })`，事件 `start`／`tool_use`／`tool_result`（含 `card`）／`delta`／`done`。第 6 輪的兩個腳本樣子：/connectors（`/api/connectors/catalog` 回 `{ data: { entries, installed, customLimit } }`、`/api/connectors/status` 回 `[]`、probe 回 `{ data: {...} }`），Studio（首頁 → `Control+k` → 填 textbox → Enter）。

## 1. CI 狀態

- 第 15 輪最後一次含程式碼的 push 是「朗讀的審查修正」（CI 40145843957）。開工時照 §0 看最新一次。
- clone 可能是 shallow：開工先 `git fetch --unshallow origin`（否則 merge-base 與 `rev-list --count` 不對）。
- 截圖：`.loop/shots/chat.cjs`（Studio／指令列／測驗／**用自己的單字寫短文＋朗讀**／Data Analysis／今日簡報／語音／帶我去／換圖＋接著問／dashboard 的 AI 解讀／/finance，桌機＋手機）、`connectors.cjs`、`mcp-app.cjs`。只想拍一個情境：用 python 取 chat.cjs 開頭到 `(async () => {` 之前（dist 路徑改成絕對路徑），加上只含那一段的 IIFE，另存在 scratch 跑（第 15 輪的做法）。
- **前端改動一定要跑 `npm run build`**（vitest 不做型別檢查），**改了 `SitePages.cs` 也要跑前端測試**（`quickActions.test.ts` 讀它）。`npm run lint`＋`npm run test` 加起來超過 2 分鐘，放背景跑再看 log。**regex 裡的 `\uXXXX` 用 Write 工具寫會變成真的字元**（eslint no-irregular-whitespace），寫完 grep 確認。

## 2. 下一步（依序）

1. **巡檢**（小）：/english/exam、/english/repository 手機版的 Studio（建議、短文卡片）；/finance 的回答下方現在也有「朗讀」，確認 `chat.cjs` 的 /finance 截圖看起來正常（按鈕順序：複製、朗讀）。朗讀在真的瀏覽器的聲音無法在這裡驗證，寫進 SHOWCASE 已知限制（Chrome／Edge／Safari 的聲音依裝置而定）。
2. **選做**：每日挑戰在 Studio 裡做？（固定題目＋送出記分，跟 review_quiz 不同，要另外評估；不值得就記在 DECISIONS 說不做）。或 `saved_words` 的短文直接「把短文裡我不認識的字加入單字庫」——已經可以用 look_up_word＋add_word_to_repository（多個寫入一起出現在同一張確認卡），可拍成 demo 截圖。
3. **SHOWCASE 定稿**（11/4–11/5）：最終統計（輪數、commit 數：`git rev-list --count --no-merges origin/master..HEAD`）、demo 腳本走一遍、合併檢查清單再對一次 migration 清單（`git diff --stat origin/master -- JackyAIApp.Server/Data/Migrations`；第 14、15 輪沒有新 migration）。

## 3. 已經做好的零件

- 後端：`SafeHttp`、`OutboundAddressPolicy`、`McpServerUrl`、`ToolNames`、`IChatProfile`／`ChatTurnRequest`、agent loop（`ClientToolRunner`：確認、權限、taint、卡片守門）、`ChatApprovalRegistry`、`ChatConversations.ReadOutsideText`（跨訊息 taint）、`ConnectorInstallations`＋`ConnectorCatalogService`／`ConnectorInstallService`／`ConnectorToolSource`／`ConnectorSecrets`／`McpToolClient`、`BuiltInMcpConnectors`（tw-stock、DeepWiki、Notion、Linear）、**MCP OAuth**（`McpOAuthDiscovery`、`McpOAuthFlow`、`McpOAuthTokens`、`McpOAuthEndpoints`、`McpOAuthUrls`、`McpOAuthCallbackController`）、`SiteToolSet`／`ISiteTool`、`StudioChatProfile`＋`OpenPageTool`、English 的 `WordTools`（含 `SavedWordsTool`、`IRepositoryService.GetRecentWordsAsync`）／`LearningProgressTool`／`ReviewQuizTool`（＋`IReviewQuizService`、`IDailyChallengeQuestionGenerator.GenerateFor`）、Portfolio 的 `PortfolioSummaryTool`、Data Analysis 的 `AskDataTool`／`DescribeDataTool`／`SaveToDashboardTool`（含 `open`）＋`DataAnalysisCharts`、`ReadDashboardTool`（Studio 與本站 MCP server 用）、Connectors 的 `GoogleCalendarTool`（原生 connector）、`AiToolDescriptor.OutsideText`、step-up（`McpServerException.RequiredScope`）、`CachedTools.Changed`（rug pull）、**本站 MCP server**（`Services/Connectors/SiteMcp`：`SiteMcpServer`、`SiteAccessTokenService`、`SiteTokenAuthenticationHandler`、`SiteOAuthServer`／`SiteOAuthClients`／`SiteOAuthCodes`／`Pkce`；controllers `SiteMcpController`、`SiteAccessTokensController`、`SiteOAuthController`）。測試的假網路在 `TestHelpers`：`FakeMcpServer`、`FakeAuthorizationServer`、`FakeWeb`、`TestStateService`；本站工具的一致性由 `SiteToolDefinitionsTests` 檢查。
- 本站 MCP server 的 MCP App：`SiteMcpApp`（`ui://jacky-studio/card.html`；卡片按鈕 `IOffersAppActions`／`AddActions`），畫面用 `.loop/shots/mcp-app.cjs`（自己扮 host，含 `<img onerror>` 當資料）驗證；/connectors 用 `.loop/shots/connectors.cjs`。
- 前端：`components/aiChat`（`AiChatConfig`、`variant="sheet"`、ToolActivity、ApprovalCard〔含「以後直接執行」；等待時在文字之後〕、卡片、`hooks/useSpeechInput` 語音、`hooks/useSpeechOutput`＋`utils/speechText` 朗讀、`ask` prop〔含 `fresh`〕、`utils/followUps`＋`FollowUps`、`onNavigate`＋`sitePathOf`；`AiAssistantLauncher` 的 `onNavigate` 等回答正常結束）、`Dashboard/viewLayout`、dashboard 的「AI 解讀」（`READ_DASHBOARD_PROMPT`）、`hooks/chatStatus`、`StudioAssistant/askStudio`、`StudioAssistant/quickActions`＋`QuickActions`（面板的 `inputAccessory`；含讀者的 dashboard）、`StudioAssistant/QuizCardView`＋`quizCard`（卡片 renderer 的 `saved`）、`AiChatConfig.toolLabels`＋`withToolLabel`、`Portal/BriefingCard`、`Connectors/callbackNotice`、`components/StudioAssistant`、/connectors v2（含 OAuth 路徑）、/data-analysis 的 AI 對話、`Connectors/SiteMcpSection`（token 與設定）、`OAuthConsent`（/oauth/authorize）。

## 4. 之後

design/07：M4 → 11/3（TestKit、Google Calendar 原生範例），11/4–11/5 打磨與 SHOWCASE 定稿。延伸：MCP Apps、跨輪 taint（本站對外 MCP server 已在第 8 輪完成）。
