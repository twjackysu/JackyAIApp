# NEXT — 下一輪從最上面開始

> 設計已定稿（PLAN.md）。M1、M2 完成；M3 進行中：Studio AI（⌘K）已上線，剩 MCP OAuth 與更多本站工具。

## 0. 每輪固定動作

- **開工時與收尾前都同步 master**：`git fetch origin master` → 有新 commit 就 `git merge origin/master`（**不用 rebase**，D-014）。
- **先看 CI**：`gh run list -R twjackysu/JackyAIApp --branch feature/ai-native --limit 3`；紅的先修（annotation 看法見排程指示）。
- **後端改動先跑離線型別檢查**（D-021）：`sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0`，然後 `CSCHECK_OUT=/tmp/cscheck .loop/cscheck/run.sh`（第一次會 clone Anthropic SDK、xunit、Moq、EF Core、MCP SDK 原始碼）。已知雜訊：`*.Designer.cs`／migration 的 CS0411/CS1503、EF 擴充方法（`AsNoTracking`、`FirstOrDefaultAsync`、`CountAsync`、`AnyAsync`、DbSet 上的 LINQ）與其連帶的 CS8130、新 DbSet 的 CS8618、`IEntityTypeConfiguration` 的 CS0535、`[GeneratedRegex]` 的 CS8795、MCP SDK 型別的 `HttpClient`／`ReadOnlyMemory` CS1503 與 CS8410。其他都要修。
- **畫面驗證**：`npm run build` 後用 playwright（`require('playwright')`，`NODE_PATH=$(npm root -g)`）`page.route` 從 `dist/` 回檔案、`/api/*` 回假資料；chat 的 SSE 用 `route.fulfill({ contentType: 'text/event-stream', body })`。上一輪的腳本樣子：`/api/account/info` 回 `{ data: { id, name, isAdmin, drawdownBrakeEnabled } }`、`/api/chatbot/conversations` 回 `{ data: [] }`、`/api/connectors/catalog` 回 `{ data: catalog }`、`/api/connectors/status` 回 `[]`。

## 1. CI 狀態

- 第 5 輪最後一次含程式碼的 push `3f4cb78`：CI run 40062618473 綠。開工時照 §0 看最新一次。

## 2. 下一步（依序）

1. **MCP OAuth（M3，Jacky 的明確要求）**，design/03 §5.2：`installations/probe` 在 401 時讀 `WWW-Authenticate` 的 `resource_metadata`（現在 `McpToolClient` 只拿到 401，需要另寫一個小的「不帶 token 打一次 initialize」探測，用 `SafeHttp`）、PRM → AS metadata（RFC 8414 → OIDC）、CIMD 端點 `GET api/connectors/oauth/client-metadata`（匿名）、DCR、手填 client、PKCE S256、`resource`、`iss`、`api/connectors/callback/mcp`（literal 路由，既有 provider 流程不動）、token 加密存進 `EncryptedSecret`、refresh（每個安裝 single-flight）、`POST installations/{id}/authorize`。前端已就緒（`AddMcpServerDialog` 的 oauth 路徑、`?success=true&provider=` 回呼訊息）。目前 `AuthFrom` 遇到 oauth 回 `OAuthNotYet`、probe 401 回 `apiKey`（D-030）——做完要改。測試用假的 AS／MCP（HttpMessageHandler）。
2. **Data Analysis 的寫入工具**：`add_widget`／`create_dashboard`（Write → 確認卡），讓「存成 dashboard」也能用說的（目前靠圖表卡片上的按鈕）。
3. **Portfolio 工具**（`ISiteTool`）：`portfolio_summary`（唯讀，table 卡片）；English 再加 `review_summary`（今天要複習幾個）。Portfolio 模組要能用 Connectors（模組表與 `ModuleBoundariesTests`）。
4. **SHOWCASE.md 初稿**：故事線（design/02）＋demo 腳本：(1) ⌘K → 「serendipity 是什麼？幫我加入單字庫」→ 查字典卡片 → 確認卡 → 加入；(2) /connectors 一鍵加入 tw-stock → /data-analysis 問「台積電近月營收跟我的資料比」；(3) 新增自己的 MCP server（DeepWiki）→ Studio 問某個 repo。附 `screenshots/` 的圖、mermaid 架構圖、合併檢查清單（migration 兩個已列）。
5. 小事：確認卡在 Studio 抽屜裡的 HTML 預覽（`usePreview`）是否正常；`ConnectorDetailDrawer` 的「測試連線」接上後端後實測一次 playwright；connector 的 `LastStatus` 在工具呼叫遇到 401 時可以改成 NeedsAuth（現在只記 log）。

## 3. 已經做好的零件

- 後端：`SafeHttp`、`OutboundAddressPolicy`、`McpServerUrl`、`ToolNames`、`IChatProfile`／`ChatTurnRequest`、agent loop（`ClientToolRunner`：確認、權限、taint、卡片守門）、`ChatApprovalRegistry`、`ConnectorInstallations`＋`ConnectorCatalogService`／`ConnectorInstallService`／`ConnectorToolSource`／`ConnectorSecrets`／`McpToolClient`、`BuiltInMcpConnectors`、`SiteToolSet`／`ISiteTool`、`StudioChatProfile`＋`OpenPageTool`、English 的 `WordTools`、Data Analysis 的 `AskDataTool`／`DescribeDataTool`。
- 前端：`components/aiChat`（`AiChatConfig.title`、`variant="sheet"`、ToolActivity、ApprovalCard、卡片）、`components/StudioAssistant`、/connectors v2（已回到 portal）、/data-analysis 的 AI 對話。

## 4. 之後

design/07：M3 → 11/1（剩 MCP OAuth、Portfolio 工具），M4 → 11/3（Google Calendar 原生範例、TestKit），11/4–11/5 打磨與 SHOWCASE。延伸：MCP Apps、本站對外 MCP server、跨輪 taint。
