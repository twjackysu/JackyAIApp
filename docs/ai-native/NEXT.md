# NEXT — 下一輪從最上面開始

> 設計已定稿（PLAN.md）。M1–M3 完成；M4 必要項目完成（第 7 輪）；M4 延伸「本站對外的 MCP server＋OAuth」完成（第 8 輪）、「MCP App」完成（第 9 輪）。接下來是收尾與打磨；11/4 起功能凍結。

## 0. 每輪固定動作

- **開工時與收尾前都同步 master**：`git fetch origin master` → 有新 commit 就 `git merge origin/master`（**不用 rebase**，D-014）。
- **先看 CI**：`gh run list -R twjackysu/JackyAIApp --branch feature/ai-native --limit 3`；紅的先修（annotation 看法見排程指示）。被後一次 push 取消（cancelled）是正常的，看最新一次。
- **後端改動先跑離線型別檢查**（D-021）：`sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0`，然後 `CSCHECK_OUT=/tmp/cscheck .loop/cscheck/run.sh origin/feature/ai-native`（**以 branch 為基準**，只列這輪新出現的診斷；以 master 為基準會列出 30 多個已知雜訊）。已知雜訊：EF 擴充方法（`AsNoTracking`、`FirstOrDefaultAsync`…）、`SaveChangesAsync` 的 CS1503、`[GeneratedRegex]` 的 CS8795、`using var db = new AzureSQLDBContext` 的 CS1674。
- **畫面驗證**：`npm run build` 後用 playwright（`NODE_PATH=$(npm root -g)`）`page.route` 從 `dist/` 回檔案、`/api/*` 回假資料；SSE 用 `route.fulfill({ contentType: 'text/event-stream', body })`，事件 `start`／`tool_use`／`tool_result`（含 `card`）／`delta`／`done`。第 6 輪的兩個腳本樣子：/connectors（`/api/connectors/catalog` 回 `{ data: { entries, installed, customLimit } }`、`/api/connectors/status` 回 `[]`、probe 回 `{ data: {...} }`），Studio（首頁 → `Control+k` → 填 textbox → Enter）。

## 1. CI 狀態

- 第 9 輪最後一次含程式碼的 push 見 PROGRESS 第 9 輪（serverInfo 圖示）。開工時照 §0 看最新一次。
- **cscheck 這一輪起變弱**：base／head 都是 999 種診斷，EF 的型別幾乎都綁不到（連 `CancellationToken` 都出現兩種），所以 EF 相關檔案的錯誤會被雜訊蓋住；非 EF 檔案（SiteMcp、RateLimitPolicies 等）仍有效。可以試著刪掉 `/tmp/cscheck/src/efcore` 重新 clone，或接受現狀、以 CI 為準。

## 2. 下一步（依序）

M4 必要項目與延伸（本站 MCP server、OAuth、MCP App）都完成了。11/4 起功能凍結，剩下的輪數用在「讓 demo 更穩、更驚艷」與合併前的品質。

1. **合併前的整體審查**（中）：請一個 subagent 對 `git diff origin/master...HEAD`（排除 docs/、.loop/）做對抗式 code review：CLAUDE.md 規範（模組邊界、ServiceResult、ICurrentUser、TaipeiTime、AiJson、controller 不碰 DB）、安全（SSRF、token、OAuth、prompt injection）、migration 只新增。把確認過的問題修掉。
2. **用 `.loop/shots/` 做畫面巡檢**（小～中）：照 `connectors.cjs` 的寫法再加 Studio（⌘K → 問答 → 確認卡）與 /data-analysis 的腳本，桌機＋手機各一張，檢查文字溢出、按鈕擠壓、深色模式；更新 SHOWCASE 截圖。
3. **（選做）MCP App 的互動**：單字卡上「加入單字庫」按鈕從 iframe 經 host 的 `tools/call` 呼叫 `add_word_to_repository`（只有可寫入的 token 才顯示；工具 `_meta.ui.visibility`）。規格的 app→host `tools/call` 細節要先查；做不完就列為後續。
4. **（選做）跨輪 taint**：讀過外部內容的對話，之後幾輪的寫入也先問（存在 `ChatConversations`，要 migration——還沒進 production 的 migration 可以直接改，見 D-051 的做法）。
5. **SHOWCASE 定稿**（11/4–11/5）：補 Data Analysis 存 dashboard 的確認卡截圖、手機截圖、最終統計（輪數、commit 數：`git rev-list --count --no-merges origin/master..HEAD`）。

## 3. 已經做好的零件

- 後端：`SafeHttp`、`OutboundAddressPolicy`、`McpServerUrl`、`ToolNames`、`IChatProfile`／`ChatTurnRequest`、agent loop（`ClientToolRunner`：確認、權限、taint、卡片守門）、`ChatApprovalRegistry`、`ConnectorInstallations`＋`ConnectorCatalogService`／`ConnectorInstallService`／`ConnectorToolSource`／`ConnectorSecrets`／`McpToolClient`、`BuiltInMcpConnectors`（tw-stock、DeepWiki、Notion、Linear）、**MCP OAuth**（`McpOAuthDiscovery`、`McpOAuthFlow`、`McpOAuthTokens`、`McpOAuthEndpoints`、`McpOAuthUrls`、`McpOAuthCallbackController`）、`SiteToolSet`／`ISiteTool`、`StudioChatProfile`＋`OpenPageTool`、English 的 `WordTools`／`LearningProgressTool`、Portfolio 的 `PortfolioSummaryTool`、Data Analysis 的 `AskDataTool`／`DescribeDataTool`／`SaveToDashboardTool`＋`DataAnalysisCharts`、Connectors 的 `GoogleCalendarTool`（原生 connector）、`AiToolDescriptor.OutsideText`、step-up（`McpServerException.RequiredScope`）、`CachedTools.Changed`（rug pull）、**本站 MCP server**（`Services/Connectors/SiteMcp`：`SiteMcpServer`、`SiteAccessTokenService`、`SiteTokenAuthenticationHandler`、`SiteOAuthServer`／`SiteOAuthClients`／`SiteOAuthCodes`／`Pkce`；controllers `SiteMcpController`、`SiteAccessTokensController`、`SiteOAuthController`）。測試的假網路在 `TestHelpers`：`FakeMcpServer`、`FakeAuthorizationServer`、`FakeWeb`、`TestStateService`；本站工具的一致性由 `SiteToolDefinitionsTests` 檢查。
- 本站 MCP server 的 MCP App：`SiteMcpApp`（`ui://jacky-studio/card.html`），畫面用 `.loop/shots/mcp-app.cjs`（自己扮 host，含 `<img onerror>` 當資料）驗證；/connectors 用 `.loop/shots/connectors.cjs`。
- 前端：`components/aiChat`（`AiChatConfig`、`variant="sheet"`、ToolActivity、ApprovalCard〔含「以後直接執行」〕、卡片）、`Connectors/callbackNotice`、`components/StudioAssistant`、/connectors v2（含 OAuth 路徑）、/data-analysis 的 AI 對話、`Connectors/SiteMcpSection`（token 與設定）、`OAuthConsent`（/oauth/authorize）。

## 4. 之後

design/07：M4 → 11/3（TestKit、Google Calendar 原生範例），11/4–11/5 打磨與 SHOWCASE 定稿。延伸：MCP Apps、跨輪 taint（本站對外 MCP server 已在第 8 輪完成）。
