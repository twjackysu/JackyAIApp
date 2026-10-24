# NEXT — 下一輪從最上面開始

> 設計已定稿（PLAN.md）。M1、M2 完成；M3 大致完成（Studio AI、本站工具、MCP OAuth、Notion／Linear 目錄）。接下來是 M4（擴充與貢獻）與打磨；11/4 起功能凍結。

## 0. 每輪固定動作

- **開工時與收尾前都同步 master**：`git fetch origin master` → 有新 commit 就 `git merge origin/master`（**不用 rebase**，D-014）。
- **先看 CI**：`gh run list -R twjackysu/JackyAIApp --branch feature/ai-native --limit 3`；紅的先修（annotation 看法見排程指示）。被後一次 push 取消（cancelled）是正常的，看最新一次。
- **後端改動先跑離線型別檢查**（D-021）：`sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0`，然後 `CSCHECK_OUT=/tmp/cscheck .loop/cscheck/run.sh origin/feature/ai-native`（**以 branch 為基準**，只列這輪新出現的診斷；以 master 為基準會列出 30 多個已知雜訊）。已知雜訊：EF 擴充方法（`AsNoTracking`、`FirstOrDefaultAsync`…）、`SaveChangesAsync` 的 CS1503、`[GeneratedRegex]` 的 CS8795、`using var db = new AzureSQLDBContext` 的 CS1674。
- **畫面驗證**：`npm run build` 後用 playwright（`NODE_PATH=$(npm root -g)`）`page.route` 從 `dist/` 回檔案、`/api/*` 回假資料；SSE 用 `route.fulfill({ contentType: 'text/event-stream', body })`，事件 `start`／`tool_use`／`tool_result`（含 `card`）／`delta`／`done`。第 6 輪的兩個腳本樣子：/connectors（`/api/connectors/catalog` 回 `{ data: { entries, installed, customLimit } }`、`/api/connectors/status` 回 `[]`、probe 回 `{ data: {...} }`），Studio（首頁 → `Control+k` → 填 textbox → Enter）。

## 1. CI 狀態

- 第 6 輪最後一次含程式碼的 push 是「確認卡顯示工具自己的說明」（見 PROGRESS 第 6 輪的 run id）。開工時照 §0 看最新一次。

## 2. 下一步（依序）

1. **M4：TestKit 與貢獻樣板**（design/07）：把測試裡重複的假 MCP server（`McpToolClientTests.FakeMcpServer`）與假網站（`McpOAuthDiscoveryTests.FakeWeb`）整理成 `TestHelpers/FakeMcpServer`、`TestHelpers/FakeAuthorizationServer`（一個 handler 同時扮 MCP server 401 + PRM + AS + token 端點），寫一個「完整 OAuth 流程」的整合測試（probe → create → authorize → callback → 列工具 → refresh），並在 CONTRIBUTING-CONNECTORS 提到它。
2. **Google Calendar 原生範例（M4）**：沿用既有 Google 帳號連線（`ConnectorService` 的 token），做一個 `ISiteTool` 型的原生 connector（列出今天／這週行程，唯讀）。先確認既有 Google 連線的 scope 是否含 Calendar（`appsettings.json` 的 `Connectors:Providers:Google:Scopes`）；不含的話只做框架與假實作，FOR_JACKY 註記要加 scope。
3. **403 step-up**（MCP 授權，可選）：工具呼叫回 403 `insufficient_scope` 時把 `scope` 併進 grant 並標 NeedsAuth，重新授權時帶新 scope。需要從 `McpToolClient` 拿到 `WWW-Authenticate`（SDK 的例外沒有 header：可能要在 `IMcpHttpHandlerSource` 包一層 DelegatingHandler 記下最後一個 401/403 的 header）。
4. **打磨**：Studio 在 /data-analysis 以外問資料問題時的引導；`ConnectorDetailDrawer` 的「測試連線」用 playwright 走一次；/connectors 的 OAuth 回呼訊息（現在是既有的英文訊息）考慮對 AI connector 顯示中文。
5. **SHOWCASE 定稿**（11/4–11/5）：補 Data Analysis 存 dashboard 的確認卡截圖、手機截圖、最終統計（輪數、commit 數）。

## 3. 已經做好的零件

- 後端：`SafeHttp`、`OutboundAddressPolicy`、`McpServerUrl`、`ToolNames`、`IChatProfile`／`ChatTurnRequest`、agent loop（`ClientToolRunner`：確認、權限、taint、卡片守門）、`ChatApprovalRegistry`、`ConnectorInstallations`＋`ConnectorCatalogService`／`ConnectorInstallService`／`ConnectorToolSource`／`ConnectorSecrets`／`McpToolClient`、`BuiltInMcpConnectors`（tw-stock、DeepWiki、Notion、Linear）、**MCP OAuth**（`McpOAuthDiscovery`、`McpOAuthFlow`、`McpOAuthTokens`、`McpOAuthEndpoints`、`McpOAuthUrls`、`McpOAuthCallbackController`）、`SiteToolSet`／`ISiteTool`、`StudioChatProfile`＋`OpenPageTool`、English 的 `WordTools`／`LearningProgressTool`、Portfolio 的 `PortfolioSummaryTool`、Data Analysis 的 `AskDataTool`／`DescribeDataTool`／`SaveToDashboardTool`＋`DataAnalysisCharts`。
- 前端：`components/aiChat`（`AiChatConfig`、`variant="sheet"`、ToolActivity、ApprovalCard、卡片）、`components/StudioAssistant`、/connectors v2（含 OAuth 路徑）、/data-analysis 的 AI 對話。

## 4. 之後

design/07：M4 → 11/3（TestKit、Google Calendar 原生範例），11/4–11/5 打磨與 SHOWCASE 定稿。延伸：MCP Apps、本站對外 MCP server、跨輪 taint。
