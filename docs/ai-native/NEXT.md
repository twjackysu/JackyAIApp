# NEXT — 下一輪從最上面開始

> 設計已定稿（PLAN.md）。M1–M4 與延伸（本站 MCP server、OAuth、MCP App）都完成；第 10 輪加了跨訊息 taint、AI 今日簡報、語音輸入、MCP App 按鈕。11/4 起功能凍結；在那之前還有很多輪，可以再加「少而精」的驚艷點，但每一個都要能 demo、有測試。

## 0. 每輪固定動作

- **開工時與收尾前都同步 master**：`git fetch origin master` → 有新 commit 就 `git merge origin/master`（**不用 rebase**，D-014）。
- **先看 CI**：`gh run list -R twjackysu/JackyAIApp --branch feature/ai-native --limit 3`；紅的先修（annotation 看法見排程指示）。被後一次 push 取消（cancelled）是正常的，看最新一次。
- **後端改動先跑離線型別檢查**（D-021）：`sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0`，然後 `CSCHECK_OUT=/tmp/cscheck .loop/cscheck/run.sh origin/feature/ai-native`（**以 branch 為基準**，只列這輪新出現的診斷；以 master 為基準會列出 30 多個已知雜訊）。已知雜訊：EF 擴充方法（`AsNoTracking`、`FirstOrDefaultAsync`…）、`SaveChangesAsync` 的 CS1503、`[GeneratedRegex]` 的 CS8795、`using var db = new AzureSQLDBContext` 的 CS1674。
- **畫面驗證**：`npm run build` 後用 playwright（`NODE_PATH=$(npm root -g)`）`page.route` 從 `dist/` 回檔案、`/api/*` 回假資料；SSE 用 `route.fulfill({ contentType: 'text/event-stream', body })`，事件 `start`／`tool_use`／`tool_result`（含 `card`）／`delta`／`done`。第 6 輪的兩個腳本樣子：/connectors（`/api/connectors/catalog` 回 `{ data: { entries, installed, customLimit } }`、`/api/connectors/status` 回 `[]`、probe 回 `{ data: {...} }`），Studio（首頁 → `Control+k` → 填 textbox → Enter）。

## 1. CI 狀態

- 第 10 輪最後一次含程式碼的 push 是審查修正（`16504570`），CI 40103374636 綠。開工時照 §0 看最新一次。
- cscheck 對 EF 相關檔案幾乎沒用（base／head 都 999 種診斷），第 10 輪沒有跑，直接以 CI 為準（每次 2–3 分鐘）。
- 截圖：`.loop/shots/chat.cjs`（Studio／Data Analysis／今日簡報／語音，桌機＋手機）、`connectors.cjs`、`mcp-app.cjs`（自己扮 host，含按鈕的 `tools/call`）。用法寫在各檔開頭。

## 2. 下一步（依序）

1. **今日簡報開新對話**（小）：`askStudio` 加 `{ fresh: true }`，簡報不要接在畫面上的舊對話後面（舊對話整段歷史重送很貴，而且簡報讀了日曆會讓那個對話之後的寫入都要確認，D-055）。注意 `useChatConversation.newChat()` 之後同一個 tick 的 `send` 會不會拿到舊的 conversationId——先寫測試。
2. **建議的下一步問題（follow-up chips）**（中）：回答結束後顯示 2–3 個可點的後續問題（Data Analysis：「改成長條圖」「只看北區」「存到 dashboard」；Studio：依用到的工具）。做法候選：前端依 `toolSteps` 決定（不花 token，可測）；或請模型在最後輸出固定格式再解析（較聰明但要處理串流與不可信）。先做前端規則版。
3. **Data Analysis 助理的深度**（中）：問完圖表後「換成圓餅圖」——`ask_data` 已回 chart 卡片，前端 `ChartCardView` 能不能讓使用者自己切 viz（不再問 AI）？看 `VizSwitcher` 能否重用。
4. **（選做）Studio 能操作頁面**：client-side 工具（例如在 /data-analysis 幫使用者填好資料源），需要設計「前端工具」的 SSE 往返，較大，先寫 design/04 的小節再決定。
5. **合併前審查的尾巴**（小）：Data Analysis 的 dashboard 存取程式（master 原有，風險低）可以請 subagent 看一次。
6. **SHOWCASE 定稿**（11/4–11/5）：最終統計（輪數、commit 數：`git rev-list --count --no-merges origin/master..HEAD`）、所有截圖再拍一次、demo 腳本走一遍。

## 3. 已經做好的零件

- 後端：`SafeHttp`、`OutboundAddressPolicy`、`McpServerUrl`、`ToolNames`、`IChatProfile`／`ChatTurnRequest`、agent loop（`ClientToolRunner`：確認、權限、taint、卡片守門）、`ChatApprovalRegistry`、`ChatConversations.ReadOutsideText`（跨訊息 taint）、`ConnectorInstallations`＋`ConnectorCatalogService`／`ConnectorInstallService`／`ConnectorToolSource`／`ConnectorSecrets`／`McpToolClient`、`BuiltInMcpConnectors`（tw-stock、DeepWiki、Notion、Linear）、**MCP OAuth**（`McpOAuthDiscovery`、`McpOAuthFlow`、`McpOAuthTokens`、`McpOAuthEndpoints`、`McpOAuthUrls`、`McpOAuthCallbackController`）、`SiteToolSet`／`ISiteTool`、`StudioChatProfile`＋`OpenPageTool`、English 的 `WordTools`／`LearningProgressTool`、Portfolio 的 `PortfolioSummaryTool`、Data Analysis 的 `AskDataTool`／`DescribeDataTool`／`SaveToDashboardTool`＋`DataAnalysisCharts`、Connectors 的 `GoogleCalendarTool`（原生 connector）、`AiToolDescriptor.OutsideText`、step-up（`McpServerException.RequiredScope`）、`CachedTools.Changed`（rug pull）、**本站 MCP server**（`Services/Connectors/SiteMcp`：`SiteMcpServer`、`SiteAccessTokenService`、`SiteTokenAuthenticationHandler`、`SiteOAuthServer`／`SiteOAuthClients`／`SiteOAuthCodes`／`Pkce`；controllers `SiteMcpController`、`SiteAccessTokensController`、`SiteOAuthController`）。測試的假網路在 `TestHelpers`：`FakeMcpServer`、`FakeAuthorizationServer`、`FakeWeb`、`TestStateService`；本站工具的一致性由 `SiteToolDefinitionsTests` 檢查。
- 本站 MCP server 的 MCP App：`SiteMcpApp`（`ui://jacky-studio/card.html`；卡片按鈕 `IOffersAppActions`／`AddActions`），畫面用 `.loop/shots/mcp-app.cjs`（自己扮 host，含 `<img onerror>` 當資料）驗證；/connectors 用 `.loop/shots/connectors.cjs`。
- 前端：`components/aiChat`（`AiChatConfig`、`variant="sheet"`、ToolActivity、ApprovalCard〔含「以後直接執行」；等待時在文字之後〕、卡片、`hooks/useSpeechInput` 語音、`ask` prop）、`hooks/chatStatus`、`StudioAssistant/askStudio`、`Portal/BriefingCard`、`Connectors/callbackNotice`、`components/StudioAssistant`、/connectors v2（含 OAuth 路徑）、/data-analysis 的 AI 對話、`Connectors/SiteMcpSection`（token 與設定）、`OAuthConsent`（/oauth/authorize）。

## 4. 之後

design/07：M4 → 11/3（TestKit、Google Calendar 原生範例），11/4–11/5 打磨與 SHOWCASE 定稿。延伸：MCP Apps、跨輪 taint（本站對外 MCP server 已在第 8 輪完成）。
