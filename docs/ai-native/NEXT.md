# NEXT — 下一輪從最上面開始

> 設計已定稿（PLAN.md）。M1–M3 完成；M4 必要項目完成（第 7 輪）；M4 延伸「本站對外的 MCP server＋OAuth」完成（第 8 輪）。接下來是收尾與打磨；11/4 起功能凍結。

## 0. 每輪固定動作

- **開工時與收尾前都同步 master**：`git fetch origin master` → 有新 commit 就 `git merge origin/master`（**不用 rebase**，D-014）。
- **先看 CI**：`gh run list -R twjackysu/JackyAIApp --branch feature/ai-native --limit 3`；紅的先修（annotation 看法見排程指示）。被後一次 push 取消（cancelled）是正常的，看最新一次。
- **後端改動先跑離線型別檢查**（D-021）：`sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0`，然後 `CSCHECK_OUT=/tmp/cscheck .loop/cscheck/run.sh origin/feature/ai-native`（**以 branch 為基準**，只列這輪新出現的診斷；以 master 為基準會列出 30 多個已知雜訊）。已知雜訊：EF 擴充方法（`AsNoTracking`、`FirstOrDefaultAsync`…）、`SaveChangesAsync` 的 CS1503、`[GeneratedRegex]` 的 CS8795、`using var db = new AzureSQLDBContext` 的 CS1674。
- **畫面驗證**：`npm run build` 後用 playwright（`NODE_PATH=$(npm root -g)`）`page.route` 從 `dist/` 回檔案、`/api/*` 回假資料；SSE 用 `route.fulfill({ contentType: 'text/event-stream', body })`，事件 `start`／`tool_use`／`tool_result`（含 `card`）／`delta`／`done`。第 6 輪的兩個腳本樣子：/connectors（`/api/connectors/catalog` 回 `{ data: { entries, installed, customLimit } }`、`/api/connectors/status` 回 `[]`、probe 回 `{ data: {...} }`），Studio（首頁 → `Control+k` → 填 textbox → Enter）。

## 1. CI 狀態

- 第 8 輪最後一次含程式碼的 push 是「OAuth 同意頁」（d156d82），CI 40085800201 綠。之後只有文件 commit（不跑 CI）。開工時照 §0 看最新一次。

## 2. 下一步（依序）

M4 必要項目與兩個延伸（本站 MCP server＋OAuth）都完成了。11/4 起功能凍結，剩下的輪數用在「讓 demo 更穩、更驚艷」與打磨。

1. **本站 MCP server 的收尾**（小）：
   - `tools/call` 的結果：連結卡的相對網址（例如 `/english/repository`）在外部 client 沒有意義——在文字後面附上絕對網址（issuer + href），讓別家 AI 能給使用者連結。table 卡片可考慮放進 `structuredContent`。
   - `SiteOAuthController` 的 register／token 加 rate limit（匿名端點；目前只有 PAT 的 MCP 端點有）。
   - /connectors 的 token 清單：OAuth 發的 token 標示「經 OAuth」（目前只看名稱；要加欄位就是改 `AddSiteAccessTokens` migration——它還沒進 production，可以直接改，Designer／snapshot 一起改）。
2. **Studio AI 的引導**：問「怎麼讓 Claude 用這個網站？」時，`open_page` 到 /connectors 並說明「讓其他 AI 使用本站」（`StudioChatProfile` 的提示與 `SitePages` 說明加一句）。
3. **或：MCP Apps**（`ui://` 資源在 sandboxed iframe 顯示）：規格新、client 端複雜，評估後再決定；時間不夠就列為後續。
4. **打磨**：
   - playwright 走一次 /connectors：「本站功能」下的「Google 日曆」組、`ConnectorDetailDrawer` 的「測試連線」、OAuth 回呼中文訊息。
   - Studio 在 /data-analysis 以外問資料問題時的引導。
5. **SHOWCASE 定稿**（11/4–11/5）：補 Data Analysis 存 dashboard 的確認卡截圖、手機截圖、最終統計（輪數、commit 數）。

## 3. 已經做好的零件

- 後端：`SafeHttp`、`OutboundAddressPolicy`、`McpServerUrl`、`ToolNames`、`IChatProfile`／`ChatTurnRequest`、agent loop（`ClientToolRunner`：確認、權限、taint、卡片守門）、`ChatApprovalRegistry`、`ConnectorInstallations`＋`ConnectorCatalogService`／`ConnectorInstallService`／`ConnectorToolSource`／`ConnectorSecrets`／`McpToolClient`、`BuiltInMcpConnectors`（tw-stock、DeepWiki、Notion、Linear）、**MCP OAuth**（`McpOAuthDiscovery`、`McpOAuthFlow`、`McpOAuthTokens`、`McpOAuthEndpoints`、`McpOAuthUrls`、`McpOAuthCallbackController`）、`SiteToolSet`／`ISiteTool`、`StudioChatProfile`＋`OpenPageTool`、English 的 `WordTools`／`LearningProgressTool`、Portfolio 的 `PortfolioSummaryTool`、Data Analysis 的 `AskDataTool`／`DescribeDataTool`／`SaveToDashboardTool`＋`DataAnalysisCharts`、Connectors 的 `GoogleCalendarTool`（原生 connector）、`AiToolDescriptor.OutsideText`、step-up（`McpServerException.RequiredScope`）、`CachedTools.Changed`（rug pull）、**本站 MCP server**（`Services/Connectors/SiteMcp`：`SiteMcpServer`、`SiteAccessTokenService`、`SiteTokenAuthenticationHandler`、`SiteOAuthServer`／`SiteOAuthClients`／`SiteOAuthCodes`／`Pkce`；controllers `SiteMcpController`、`SiteAccessTokensController`、`SiteOAuthController`）。測試的假網路在 `TestHelpers`：`FakeMcpServer`、`FakeAuthorizationServer`、`FakeWeb`、`TestStateService`；本站工具的一致性由 `SiteToolDefinitionsTests` 檢查。
- 前端：`components/aiChat`（`AiChatConfig`、`variant="sheet"`、ToolActivity、ApprovalCard〔含「以後直接執行」〕、卡片）、`Connectors/callbackNotice`、`components/StudioAssistant`、/connectors v2（含 OAuth 路徑）、/data-analysis 的 AI 對話、`Connectors/SiteMcpSection`（token 與設定）、`OAuthConsent`（/oauth/authorize）。

## 4. 之後

design/07：M4 → 11/3（TestKit、Google Calendar 原生範例），11/4–11/5 打磨與 SHOWCASE 定稿。延伸：MCP Apps、跨輪 taint（本站對外 MCP server 已在第 8 輪完成）。
