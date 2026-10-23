# DECISIONS（AI 原生化）

> 格式：編號、日期（台北）、決定、理由。Jacky 回來可以推翻任何一條。

- **D-001（2026-10-24）功能名稱「Studio AI」**：與網站名 Jacky Studio 一致，不使用其他公司的產品名（Copilot 等）。
- **D-002（2026-10-24）工具由我們的伺服器執行（client tools + 我們當 MCP client）**，Anthropic 的 MCP connector 只保留給可信、唯讀的 tw-stock。理由：Anthropic connector 在他們那邊直接執行，無法做寫入前確認、無法包裝不可信結果、不支援 resources（MCP Apps）、不能用本站自己的工具。
- **D-003（2026-10-24）Finance 不加入使用者 connector**：維持現有行為與成本；Studio 與 Data Analysis 才用使用者 connector。之後要加是一個 flag。
- **D-004（2026-10-24，評審後）確認在同一個 SSE 請求內暫停等待**（`TaskCompletionSource` + 3 分鐘逾時 + 每 20 秒 `: ping`），不另建 pending 表。理由：不用把含 thinking signature 的 SDK block 存進 DB、輪數與計費自然延續、沒有懸掛狀態。代價：重新整理頁面會失去該確認。
- **D-005（2026-10-24，評審後）模組**：新 module Connectors（may use Platform）、Assistant（`Services.Chatbot` 原封搬入 + `Services.Assistant`；may use Connectors, Platform）；Finance may use Assistant, Connectors, MarketData, Platform；DataAnalysis may use Assistant, Connectors, Platform；English／Portfolio may use Connectors, Platform。tw-stock 描述放 Connectors。`MarketBackground` 搬到 `Services.Finance.Assistant`。
- **D-006（2026-10-24，評審後）Finance 既有測試斷言不改，但允許機械式改指向**（類別、using、建構子），因為測試直接呼叫 `AnthropicChatService` 的靜態方法。重構前先加 golden 測試。
- **D-007（2026-10-24，評審後）Data Analysis 的 AI 工具 v1 用 `ask_data(question)` 包既有 NL 管線**（gpt-6-luna，inline 收 `NlSummarize` 成本），不讓 Claude 直接寫 `SummarizeRequestDto`。理由：既有管線有驗證、一次修正、`Unmatched` 規則（DECISION-025）。
- **D-008（2026-10-24，評審後）SSE 一定送 `done`**；新事件 `tool_result`、`approval_required`、`approval_resolved` 只新增不改舊的。前端先修跨 chunk 半行的 bug。
- **D-009（2026-10-24，評審後）一個 migration**：`ChatConversations.Profile`（預設 finance）、`ChatMessages.CardsJson`、`ConnectorInstallations`（`(UserId, DefinitionKey)` filtered unique，排除 custom）。只新增，不改既有欄位。
- **D-010（2026-10-24，評審後）優先序**：垂直切片（Data Analysis 對話 + 免認證／API key MCP + 確認卡）先於 MCP OAuth；MCP OAuth 是 Jacky 的明確要求，排在 M3，不砍。「以後都允許」、工具變更偵測、MCP Apps、本站 MCP server 列為延伸。
- **D-011（2026-10-24）Connectors 重新在首頁 portal 顯示**（原本因 Google scope 問題隱藏），因為它成了 AI 的核心入口；一行可改回。
- **D-012（2026-10-24 04:10）設計提早定稿**：第 1 輪就完成研究、草稿與評審修正，依指示提早開工（M1），不等到 10/25。
- **D-013（2026-10-24 04:15，Jacky 建議，不改主線）知識庫（KB）connector 列為 M4 延伸**：不自架 vector DB，讓使用者接自己的 KB。(1) Atlassian 直接用官方 Atlassian Remote MCP Server 放進目錄（OAuth，順便當 M3 MCP OAuth 的真實案例）；(2) AWS Bedrock Knowledge Bases 原生 connector：使用者填 region、KB ID、IAM 金鑰（加密存），唯讀工具 `kb_search` 呼叫 Retrieve API；(3) PostgreSQL + pgvector 原生 connector：連線字串、表／欄位、embedding 模型（查詢必須用與入庫相同的模型 embedding），唯讀 `kb_search`。連線資料一律走 SafeHttp／SSRF 規則（pgvector 的主機也要擋內網）。
