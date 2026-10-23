# 07 — 實作計畫（階段 1 里程碑）

> 日期為台北時間。每個里程碑結束時回頭看 02 的「驚艷檢查表」。後端只能靠 branch CI 驗證（每次 3–5 分鐘），所以每一步都要小、先寫測試、批次 push。

## 原則

- **先鎖住、再搬家**：任何重構前先有 golden／characterization test 綠燈。
- **一個 migration**：`Profile`、`CardsJson`、`ConnectorInstallations` 在 M1 一次加齊（手寫 Designer + snapshot）。
- **垂直切片優先**：10/29 前要能在 /data-analysis 對話、用到一個 MCP connector 的工具、看到確認卡。
- **可裁剪**：每個里程碑的「必要」做完才碰「延伸」。

## M1 引擎與共用元件（設計定稿後 → 10/27）

必要：
1. **Finance golden test**（server）：`BuildRequest` 序列化 JSON 比對檔＋ SSE 事件序列。單獨 commit、CI 綠。
2. **`parseSseChunk`**（client）：重現跨 chunk 半行 bug 的測試 → 修正 → `useChatStreaming` 改用它。
3. **NuGet spike**：加 `ModelContextProtocol.Core`，CI restore/build 過（若不相容，記錄並改用自寫最小 Streamable HTTP client，要支援 `initialize` + `Mcp-Session-Id` 的舊版伺服器）。
4. **Agent loop spike**：`TurnBlocks` 累積器 + 錄製串流測試（thinking → client tool_use → tool_result → 回答），證明 SDK 能原樣回送。
5. **模組重整**：新 module Connectors、Assistant；`Services.Chatbot` 改歸 Assistant；`TwStockMcp` 描述搬到 Connectors；`IChatProfile` + `FinanceChatProfile`（`MarketBackground` 搬到 `Services.Finance.Assistant`）；`ModuleBoundariesTests` 與 server `CLAUDE.md` 模組表同步。golden test 必須仍綠。
6. **Migration**：`ChatConversations.Profile`（預設 finance）、`ChatMessages.CardsJson`、`ConnectorInstallations`；對話端點加 profile 檢查。
7. **前端搬家**：`Finance/components/aiAssistant` → `components/aiChat` + `AiChatConfig`；Finance wrapper；台股文案參數化；既有測試只改 import。

## M2 垂直切片（→ 10/29）

必要：
1. **工具框架**：`IAiTool`、`ToolEffect`/`ToolPermission`、`SiteToolSource`、`ToolExecutor`（權限、依序執行、逾時、結果截斷、不可信包裝、taint）、agent loop 接上（最多 8 輪、`ChatToolRound` 計費）。
2. **同請求確認**：`ChatApprovalRegistry`、`approval_required`/`approval_resolved`、`POST approvals`、`: ping`。
3. **Data Analysis 工具**：`list_sources`、`get_schema`、`get_distinct_values`、`ask_data`（包 `INlSummarizeService`，回 rows + 圖表 card + plan）、`get_details`、`list_dashboards`、`create_dashboard`（Write）、`add_widget`（Write）。伺服器端擋「無 context 的 session 計畫」存成 widget。
4. **`DataAnalysisChatProfile`** + /data-analysis 最上方 Launcher；圖表 card 用既有 viz renderer，附「加到 dashboard」。
5. **Connector 框架**：`SafeHttp`（SSRF，含測試）、`McpToolClient`、`ConnectorInstallations` CRUD（None／ApiKey／Bearer）、測試連線、工具權限；catalog 端點；內建目錄放 tw-stock 與 1–2 個公開免認證 MCP（例：Microsoft Learn MCP）。
6. **/connectors v2 UI**：已連線、目錄、新增 MCP 對話框（偵測 → 認證 → 完成）、詳情抽屜（工具權限）。既有 OAuth 卡片保留。
7. 使用者 connector 的工具進入 DataAnalysis 與 Studio profile。

## M3 全站助理與 MCP OAuth（→ 11/1）

必要：
1. **Studio AI**：右下角按鈕 + ⌘K、右側抽屜／手機全螢幕、page context、依頁面的建議。
2. **English 工具**：`dictionary_lookup`（word card）、`repository_add_word`（Write）、`repository_list`、`review_summary`；**Portfolio**：`portfolio_summary`（card）；**導覽**：`open_page`（link card，不自動跳頁）。
3. **MCP OAuth**：PRM 探索（401 challenge → well-known）、AS metadata（RFC 8414 → OIDC）、CIMD 端點、DCR、手填 client、PKCE S256、`resource`、`iss` 驗證、refresh（single-flight）、403 step-up。測試用假的 AS/MCP（`HttpMessageHandler`）。
4. 一般 OAuth 2.0 的內建 connector 走同一套（PKCE 可選）。

## M4 擴充與貢獻（→ 11/3）

必要：
1. **原生範例 connector**：Google Calendar（讀取行程，用既有 Google 連線的 token）。
2. **`CONTRIBUTING-CONNECTORS.md`** + `ConnectorTestKit`（schema 合法、Effect 齊全、目錄資料完整）。
3. 文件：root／server／client `CLAUDE.md` 寫入新架構。

延伸（有時間才做，依驚艷程度排序）：
- 知識庫 connector（D-013）：目錄加入 Atlassian Remote MCP；Bedrock Knowledge Bases 與 pgvector 原生 connector（唯讀 `kb_search`）。
- MCP Apps：支援 `ui://` 資源，在 sandboxed iframe 顯示（我們是 MCP client，能 `resources/read`）。
- 工具 schema 變更偵測（rug pull）。
- 「以後都允許」。
- 本站對外的 MCP server（personal access token 驗證）。

## 階段 2 打磨（11/4 → 11/5 中午最終版）

功能凍結。修 bug、UX 細節、手機檢查、`SHOWCASE.md`（demo 腳本、截圖、架構圖、合併檢查清單、統計）。

## 風險與退路

| 風險 | 退路 |
|---|---|
| SDK 回送 thinking／tool block 失敗 | 存原始 JSON 建 Param；再不行，client 工具輪改用非串流請求取得完整 content |
| `ModelContextProtocol.Core` 不相容 | 自寫最小 Streamable HTTP client（`initialize`、session header、`tools/list`、`tools/call`） |
| 預設資料源 `dbo.Orders` 欄位不適合 demo | demo 改用上傳的範例 CSV（`docs/ai-native/demo/`），chat 用 page context 指向 session |
| 時間不夠 | 依序砍：M4 延伸 → 原生範例改成更簡單的（免外部 API）→ MCP OAuth 的 DCR（保留 CIMD + 手填）|
