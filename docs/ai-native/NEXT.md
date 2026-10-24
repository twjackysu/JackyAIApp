# NEXT — 下一輪從最上面開始

> 設計已定稿（PLAN.md）。M1–M4 與延伸（本站 MCP server、OAuth、MCP App）都完成；第 10 輪加了跨訊息 taint、AI 今日簡報、語音輸入、MCP App 按鈕；第 11 輪加了接著問、圖表卡片換圖、「帶我去」；第 12 輪加了「存完打開」、dashboard 的「AI 解讀」（`read_dashboard`）、確認卡精簡、手機 dashboard 一欄；第 13 輪加了 ⌘K 指令列（`quickActions`）與 /finance 工具中文名稱（`toolLabels`）。11/4 起功能凍結；在那之前還有很多輪，可以再加「少而精」的驚艷點，但每一個都要能 demo、有測試。

## 0. 每輪固定動作

- **開工時與收尾前都同步 master**：`git fetch origin master` → 有新 commit 就 `git merge origin/master`（**不用 rebase**，D-014）。
- **先看 CI**：`gh run list -R twjackysu/JackyAIApp --branch feature/ai-native --limit 3`；紅的先修（annotation 看法見排程指示）。被後一次 push 取消（cancelled）是正常的，看最新一次。
- **後端改動先跑離線型別檢查**（D-021）：`sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0`，然後 `CSCHECK_OUT=/tmp/cscheck .loop/cscheck/run.sh origin/feature/ai-native`（**以 branch 為基準**，只列這輪新出現的診斷；以 master 為基準會列出 30 多個已知雜訊）。已知雜訊：EF 擴充方法（`AsNoTracking`、`FirstOrDefaultAsync`…）、`SaveChangesAsync` 的 CS1503、`[GeneratedRegex]` 的 CS8795、`using var db = new AzureSQLDBContext` 的 CS1674。
- **畫面驗證**：`npm run build` 後用 playwright（`NODE_PATH=$(npm root -g)`）`page.route` 從 `dist/` 回檔案、`/api/*` 回假資料；SSE 用 `route.fulfill({ contentType: 'text/event-stream', body })`，事件 `start`／`tool_use`／`tool_result`（含 `card`）／`delta`／`done`。第 6 輪的兩個腳本樣子：/connectors（`/api/connectors/catalog` 回 `{ data: { entries, installed, customLimit } }`、`/api/connectors/status` 回 `[]`、probe 回 `{ data: {...} }`），Studio（首頁 → `Control+k` → 填 textbox → Enter）。

## 1. CI 狀態

- 第 13 輪最後一次含程式碼的 push 是「審查修正（指令列）」（CI 40129836280 綠）。開工時照 §0 看最新一次。
- clone 可能是 shallow：開工先 `git fetch --unshallow origin`（否則 merge-base 與 `rev-list --count` 不對）。
- 截圖：`.loop/shots/chat.cjs`（Studio／**指令列**／Data Analysis／今日簡報／語音／帶我去／換圖＋接著問／dashboard 的 AI 解讀／**/finance**，桌機＋手機）、`connectors.cjs`（目錄、**抽屜**、site-mcp；`EMPTY=1` 回空目錄）、`mcp-app.cjs`。chat.cjs 全跑約 3 分鐘；只想拍一個情境，可以照第 13 輪的做法把其他情境切掉另存一份在 scratch。
- **前端改動一定要跑 `npm run build`**（vitest 不做型別檢查），**改了 `SitePages.cs` 也要跑前端測試**（`quickActions.test.ts` 讀它）。

## 2. 下一步（依序）

1. **巡檢剩下的頁面（小）**：/english 的字典頁（Studio 的建議）、/portfolio 上的 Studio、/dashboards 列表頁的 Studio 建議（目前只有共用的）；`connectors.cjs` 再拍「新增 MCP server」對話框三個步驟的手機版。發現的問題直接修。
2. **指令列的延伸（小，選做）**：在 Studio 的歡迎畫面加一行提示「打頁面名稱可以直接前往」；或在 /dashboards/{id} 以外，打 dashboard 名稱就出現「打開『業績追蹤』」（要讀 `dashboardApis` 的列表，只列自己的；先評估是否值得）。
3. **SHOWCASE 定稿**（11/4–11/5）：最終統計（輪數、commit 數：`git rev-list --count --no-merges origin/master..HEAD`）、demo 腳本走一遍、合併檢查清單再對一次 migration 清單（`git diff --stat origin/master -- JackyAIApp.Server/Data/Migrations`）。

## 3. 已經做好的零件

- 後端：`SafeHttp`、`OutboundAddressPolicy`、`McpServerUrl`、`ToolNames`、`IChatProfile`／`ChatTurnRequest`、agent loop（`ClientToolRunner`：確認、權限、taint、卡片守門）、`ChatApprovalRegistry`、`ChatConversations.ReadOutsideText`（跨訊息 taint）、`ConnectorInstallations`＋`ConnectorCatalogService`／`ConnectorInstallService`／`ConnectorToolSource`／`ConnectorSecrets`／`McpToolClient`、`BuiltInMcpConnectors`（tw-stock、DeepWiki、Notion、Linear）、**MCP OAuth**（`McpOAuthDiscovery`、`McpOAuthFlow`、`McpOAuthTokens`、`McpOAuthEndpoints`、`McpOAuthUrls`、`McpOAuthCallbackController`）、`SiteToolSet`／`ISiteTool`、`StudioChatProfile`＋`OpenPageTool`、English 的 `WordTools`／`LearningProgressTool`、Portfolio 的 `PortfolioSummaryTool`、Data Analysis 的 `AskDataTool`／`DescribeDataTool`／`SaveToDashboardTool`（含 `open`）＋`DataAnalysisCharts`、`ReadDashboardTool`（Studio 與本站 MCP server 用）、Connectors 的 `GoogleCalendarTool`（原生 connector）、`AiToolDescriptor.OutsideText`、step-up（`McpServerException.RequiredScope`）、`CachedTools.Changed`（rug pull）、**本站 MCP server**（`Services/Connectors/SiteMcp`：`SiteMcpServer`、`SiteAccessTokenService`、`SiteTokenAuthenticationHandler`、`SiteOAuthServer`／`SiteOAuthClients`／`SiteOAuthCodes`／`Pkce`；controllers `SiteMcpController`、`SiteAccessTokensController`、`SiteOAuthController`）。測試的假網路在 `TestHelpers`：`FakeMcpServer`、`FakeAuthorizationServer`、`FakeWeb`、`TestStateService`；本站工具的一致性由 `SiteToolDefinitionsTests` 檢查。
- 本站 MCP server 的 MCP App：`SiteMcpApp`（`ui://jacky-studio/card.html`；卡片按鈕 `IOffersAppActions`／`AddActions`），畫面用 `.loop/shots/mcp-app.cjs`（自己扮 host，含 `<img onerror>` 當資料）驗證；/connectors 用 `.loop/shots/connectors.cjs`。
- 前端：`components/aiChat`（`AiChatConfig`、`variant="sheet"`、ToolActivity、ApprovalCard〔含「以後直接執行」；等待時在文字之後〕、卡片、`hooks/useSpeechInput` 語音、`ask` prop〔含 `fresh`〕、`utils/followUps`＋`FollowUps`、`onNavigate`＋`sitePathOf`；`AiAssistantLauncher` 的 `onNavigate` 等回答正常結束）、`Dashboard/viewLayout`、dashboard 的「AI 解讀」（`READ_DASHBOARD_PROMPT`）、`hooks/chatStatus`、`StudioAssistant/askStudio`、`StudioAssistant/quickActions`＋`QuickActions`（面板的 `inputAccessory`）、`AiChatConfig.toolLabels`＋`withToolLabel`、`Portal/BriefingCard`、`Connectors/callbackNotice`、`components/StudioAssistant`、/connectors v2（含 OAuth 路徑）、/data-analysis 的 AI 對話、`Connectors/SiteMcpSection`（token 與設定）、`OAuthConsent`（/oauth/authorize）。

## 4. 之後

design/07：M4 → 11/3（TestKit、Google Calendar 原生範例），11/4–11/5 打磨與 SHOWCASE 定稿。延伸：MCP Apps、跨輪 taint（本站對外 MCP server 已在第 8 輪完成）。
