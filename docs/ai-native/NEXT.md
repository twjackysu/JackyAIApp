# NEXT — 下一輪從最上面開始

> 設計已定稿（PLAN.md）。M1–M4 與延伸（本站 MCP server、OAuth、MCP App）都完成；第 10 輪加了跨訊息 taint、AI 今日簡報、語音輸入、MCP App 按鈕；第 11 輪加了接著問、圖表卡片換圖、「帶我去」；第 12 輪加了「存完打開」、dashboard 的「AI 解讀」（`read_dashboard`）、確認卡精簡、手機 dashboard 一欄。11/4 起功能凍結；在那之前還有很多輪，可以再加「少而精」的驚艷點，但每一個都要能 demo、有測試。

## 0. 每輪固定動作

- **開工時與收尾前都同步 master**：`git fetch origin master` → 有新 commit 就 `git merge origin/master`（**不用 rebase**，D-014）。
- **先看 CI**：`gh run list -R twjackysu/JackyAIApp --branch feature/ai-native --limit 3`；紅的先修（annotation 看法見排程指示）。被後一次 push 取消（cancelled）是正常的，看最新一次。
- **後端改動先跑離線型別檢查**（D-021）：`sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0`，然後 `CSCHECK_OUT=/tmp/cscheck .loop/cscheck/run.sh origin/feature/ai-native`（**以 branch 為基準**，只列這輪新出現的診斷；以 master 為基準會列出 30 多個已知雜訊）。已知雜訊：EF 擴充方法（`AsNoTracking`、`FirstOrDefaultAsync`…）、`SaveChangesAsync` 的 CS1503、`[GeneratedRegex]` 的 CS8795、`using var db = new AzureSQLDBContext` 的 CS1674。
- **畫面驗證**：`npm run build` 後用 playwright（`NODE_PATH=$(npm root -g)`）`page.route` 從 `dist/` 回檔案、`/api/*` 回假資料；SSE 用 `route.fulfill({ contentType: 'text/event-stream', body })`，事件 `start`／`tool_use`／`tool_result`（含 `card`）／`delta`／`done`。第 6 輪的兩個腳本樣子：/connectors（`/api/connectors/catalog` 回 `{ data: { entries, installed, customLimit } }`、`/api/connectors/status` 回 `[]`、probe 回 `{ data: {...} }`），Studio（首頁 → `Control+k` → 填 textbox → Enter）。

## 1. CI 狀態

- 第 12 輪最後一次含程式碼的 push 是「回答被停止或失敗時不換頁」（b5636f0b，CI 40120425041 綠）。開工時照 §0 看最新一次。
- cscheck 對 EF 相關檔案幾乎沒用，第 10–12 輪都直接以 CI 為準（每次 2–3 分鐘）；這輪三個後端 commit 都第一次就綠。
- 截圖：`.loop/shots/chat.cjs`（Studio／Data Analysis／今日簡報／語音／帶我去／換圖＋接著問／**dashboard 的 AI 解讀**，桌機＋手機）、`connectors.cjs`、`mcp-app.cjs`。用法寫在各檔開頭。
- **前端改動一定要跑 `npm run build`**：vitest 不做型別檢查（例如 `Array.prototype.at` 在 tsc 的 lib 裡沒有）。

## 2. 下一步（依序）

1. **全站巡檢的其餘頁面（中）**：這輪看過 Studio／Data Analysis／dashboard／connectors 的 site-mcp 區塊／MCP App。還沒拍的：/connectors 的目錄與詳情抽屜（`connectors.cjs` 的 catalog 目前回空陣列——加幾個目錄項目與一個已安裝的 connector 再拍）、/finance 的助理（行為不能退化：打開、送一則、看工具 chip）、首頁 portal 的手機版下半部。發現的問題直接修。
2. **Studio 的接著問在 dashboard 頁**（小，選做）：讀過 dashboard 後的接著問目前是固定兩句；可以考慮「把北區的趨勢畫成新的圖」這種會帶去 /data-analysis 的（要用 `askStudio` 以外的方式把問題帶過去，先評估）。
3. **SHOWCASE 定稿**（11/4–11/5）：最終統計（輪數、commit 數：`git rev-list --count --no-merges origin/master..HEAD`）、demo 腳本走一遍、合併檢查清單再對一次 migration 清單（`git diff --stat origin/master -- JackyAIApp.Server/Data/Migrations`）。

## 3. 已經做好的零件

- 後端：`SafeHttp`、`OutboundAddressPolicy`、`McpServerUrl`、`ToolNames`、`IChatProfile`／`ChatTurnRequest`、agent loop（`ClientToolRunner`：確認、權限、taint、卡片守門）、`ChatApprovalRegistry`、`ChatConversations.ReadOutsideText`（跨訊息 taint）、`ConnectorInstallations`＋`ConnectorCatalogService`／`ConnectorInstallService`／`ConnectorToolSource`／`ConnectorSecrets`／`McpToolClient`、`BuiltInMcpConnectors`（tw-stock、DeepWiki、Notion、Linear）、**MCP OAuth**（`McpOAuthDiscovery`、`McpOAuthFlow`、`McpOAuthTokens`、`McpOAuthEndpoints`、`McpOAuthUrls`、`McpOAuthCallbackController`）、`SiteToolSet`／`ISiteTool`、`StudioChatProfile`＋`OpenPageTool`、English 的 `WordTools`／`LearningProgressTool`、Portfolio 的 `PortfolioSummaryTool`、Data Analysis 的 `AskDataTool`／`DescribeDataTool`／`SaveToDashboardTool`（含 `open`）＋`DataAnalysisCharts`、`ReadDashboardTool`（Studio 與本站 MCP server 用）、Connectors 的 `GoogleCalendarTool`（原生 connector）、`AiToolDescriptor.OutsideText`、step-up（`McpServerException.RequiredScope`）、`CachedTools.Changed`（rug pull）、**本站 MCP server**（`Services/Connectors/SiteMcp`：`SiteMcpServer`、`SiteAccessTokenService`、`SiteTokenAuthenticationHandler`、`SiteOAuthServer`／`SiteOAuthClients`／`SiteOAuthCodes`／`Pkce`；controllers `SiteMcpController`、`SiteAccessTokensController`、`SiteOAuthController`）。測試的假網路在 `TestHelpers`：`FakeMcpServer`、`FakeAuthorizationServer`、`FakeWeb`、`TestStateService`；本站工具的一致性由 `SiteToolDefinitionsTests` 檢查。
- 本站 MCP server 的 MCP App：`SiteMcpApp`（`ui://jacky-studio/card.html`；卡片按鈕 `IOffersAppActions`／`AddActions`），畫面用 `.loop/shots/mcp-app.cjs`（自己扮 host，含 `<img onerror>` 當資料）驗證；/connectors 用 `.loop/shots/connectors.cjs`。
- 前端：`components/aiChat`（`AiChatConfig`、`variant="sheet"`、ToolActivity、ApprovalCard〔含「以後直接執行」；等待時在文字之後〕、卡片、`hooks/useSpeechInput` 語音、`ask` prop〔含 `fresh`〕、`utils/followUps`＋`FollowUps`、`onNavigate`＋`sitePathOf`；`AiAssistantLauncher` 的 `onNavigate` 等回答正常結束）、`Dashboard/viewLayout`、dashboard 的「AI 解讀」（`READ_DASHBOARD_PROMPT`）、`hooks/chatStatus`、`StudioAssistant/askStudio`、`Portal/BriefingCard`、`Connectors/callbackNotice`、`components/StudioAssistant`、/connectors v2（含 OAuth 路徑）、/data-analysis 的 AI 對話、`Connectors/SiteMcpSection`（token 與設定）、`OAuthConsent`（/oauth/authorize）。

## 4. 之後

design/07：M4 → 11/3（TestKit、Google Calendar 原生範例），11/4–11/5 打磨與 SHOWCASE 定稿。延伸：MCP Apps、跨輪 taint（本站對外 MCP server 已在第 8 輪完成）。
