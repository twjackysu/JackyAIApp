# NEXT — 下一輪從最上面開始

> 設計已定稿（PLAN.md）。M1–M3 完成；M4 必要項目完成（第 7 輪：TestKit、Google 日曆、step-up、確認卡「以後直接執行」、rug pull 防護）。接下來是 M4 延伸與打磨；11/4 起功能凍結。

## 0. 每輪固定動作

- **開工時與收尾前都同步 master**：`git fetch origin master` → 有新 commit 就 `git merge origin/master`（**不用 rebase**，D-014）。
- **先看 CI**：`gh run list -R twjackysu/JackyAIApp --branch feature/ai-native --limit 3`；紅的先修（annotation 看法見排程指示）。被後一次 push 取消（cancelled）是正常的，看最新一次。
- **後端改動先跑離線型別檢查**（D-021）：`sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0`，然後 `CSCHECK_OUT=/tmp/cscheck .loop/cscheck/run.sh origin/feature/ai-native`（**以 branch 為基準**，只列這輪新出現的診斷；以 master 為基準會列出 30 多個已知雜訊）。已知雜訊：EF 擴充方法（`AsNoTracking`、`FirstOrDefaultAsync`…）、`SaveChangesAsync` 的 CS1503、`[GeneratedRegex]` 的 CS8795、`using var db = new AzureSQLDBContext` 的 CS1674。
- **畫面驗證**：`npm run build` 後用 playwright（`NODE_PATH=$(npm root -g)`）`page.route` 從 `dist/` 回檔案、`/api/*` 回假資料；SSE 用 `route.fulfill({ contentType: 'text/event-stream', body })`，事件 `start`／`tool_use`／`tool_result`（含 `card`）／`delta`／`done`。第 6 輪的兩個腳本樣子：/connectors（`/api/connectors/catalog` 回 `{ data: { entries, installed, customLimit } }`、`/api/connectors/status` 回 `[]`、probe 回 `{ data: {...} }`），Studio（首頁 → `Control+k` → 填 textbox → Enter）。

## 1. CI 狀態

- 第 7 輪最後一次含程式碼的 push 是「table 卡片 nowrap」，CI 40078794952 綠。開工時照 §0 看最新一次。

## 2. 下一步（依序）

M4 的必要項目都完成了（TestKit、Google 日曆原生範例、CONTRIBUTING、CLAUDE.md），403 step-up 也做了。剩下的是 M4 延伸（挑最驚艷的一個做深）與打磨；11/4 起功能凍結。

1. **本站對外的 MCP server（M4 延伸，建議先做）**：讓 Claude Desktop／Cursor／ChatGPT 直接用 Jacky Studio 的工具（`ISiteTool` 全部：查字典、加單字、學習進度、投資組合、Google 日曆）——「網站本身就是 MCP server」。
   - 先研究：`ModelContextProtocol.AspNetCore`（官方 SDK 的 server 端，版本要跟 csproj 裡的 `ModelContextProtocol.Core` 一致；本機不能 restore，加套件後靠 CI 驗證，cscheck 抓不到它的型別）。不能用就自己寫最小 Streamable HTTP server（`initialize`、`tools/list`、`tools/call`，JSON 回應、無 session）。
   - 認證：個人存取 token（PAT，使用者在 /connectors 產生、只顯示一次、存 SHA-256 雜湊；新表要手寫 migration＋Designer＋snapshot），`Authorization: Bearer`。之後再考慮 OAuth（要自己當授權伺服器，太大）。
   - 安全：寫入型工具在外部 client 上沒有本站的確認卡 → **只開放讀取型工具**，或寫入要求 PAT 有 write 範圍；usage 照 `UsageCosts`；每個 PAT 可撤銷、記最後使用時間。
   - UX：/connectors 新增一區「讓其他 AI 使用本站」：產生 token、複製設定（Claude Desktop 的 JSON、`claude mcp add` 指令）。
2. **或：MCP Apps**（`ui://` 資源在 sandboxed iframe 顯示）：比較炫但規格新、client 端複雜（postMessage bridge、CSP）。上面那項做完還有時間再評估。
3. **打磨**：
   - playwright 走一次 /connectors：「本站功能」下多了「Google 日曆」組、`ConnectorDetailDrawer` 的「測試連線」、OAuth 回呼的中文訊息（`?success=true&provider=Notion&kind=mcp`）。
   - Studio 在 /data-analysis 以外問資料問題時的引導（提示已說到 /data-analysis 問，可加 open_page 範例）。
4. **SHOWCASE 定稿**（11/4–11/5）：補 Data Analysis 存 dashboard 的確認卡截圖、手機截圖、最終統計（輪數、commit 數）。

## 3. 已經做好的零件

- 後端：`SafeHttp`、`OutboundAddressPolicy`、`McpServerUrl`、`ToolNames`、`IChatProfile`／`ChatTurnRequest`、agent loop（`ClientToolRunner`：確認、權限、taint、卡片守門）、`ChatApprovalRegistry`、`ConnectorInstallations`＋`ConnectorCatalogService`／`ConnectorInstallService`／`ConnectorToolSource`／`ConnectorSecrets`／`McpToolClient`、`BuiltInMcpConnectors`（tw-stock、DeepWiki、Notion、Linear）、**MCP OAuth**（`McpOAuthDiscovery`、`McpOAuthFlow`、`McpOAuthTokens`、`McpOAuthEndpoints`、`McpOAuthUrls`、`McpOAuthCallbackController`）、`SiteToolSet`／`ISiteTool`、`StudioChatProfile`＋`OpenPageTool`、English 的 `WordTools`／`LearningProgressTool`、Portfolio 的 `PortfolioSummaryTool`、Data Analysis 的 `AskDataTool`／`DescribeDataTool`／`SaveToDashboardTool`＋`DataAnalysisCharts`、Connectors 的 `GoogleCalendarTool`（原生 connector）、`AiToolDescriptor.OutsideText`、step-up（`McpServerException.RequiredScope`）、`CachedTools.Changed`（rug pull）。測試的假網路在 `TestHelpers`：`FakeMcpServer`、`FakeAuthorizationServer`、`FakeWeb`、`TestStateService`；本站工具的一致性由 `SiteToolDefinitionsTests` 檢查。
- 前端：`components/aiChat`（`AiChatConfig`、`variant="sheet"`、ToolActivity、ApprovalCard〔含「以後直接執行」〕、卡片）、`Connectors/callbackNotice`、`components/StudioAssistant`、/connectors v2（含 OAuth 路徑）、/data-analysis 的 AI 對話。

## 4. 之後

design/07：M4 → 11/3（TestKit、Google Calendar 原生範例），11/4–11/5 打磨與 SHOWCASE 定稿。延伸：MCP Apps、本站對外 MCP server、跨輪 taint。
