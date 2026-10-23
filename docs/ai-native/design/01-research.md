# 01 — 研究：現況與 AI 趨勢

> 階段 0 產出。2026-10-24（台北）第 1 輪撰寫。現況部分全部從程式碼讀出，趨勢部分附來源。

## A. 現有架構（與本功能相關的部分）

### A1. Stock Intelligence 的 AI assistant（`/finance`）

**後端**：`Services/Chatbot`（目前屬 Finance 模組，三張表 `ChatConversations` / `ChatMessages` / `ChatAttachments` 也歸 Finance）。

| 單元 | 職責 |
|---|---|
| `AnthropicChatService.StreamChatAsync` | 一輪的編排：檢查 key → 存使用者訊息、讀整段歷史 → 市場背景 → 組 request → 上傳檔案 → `ModelTurn` → `TurnVerdict` → 存答案 → log |
| `AnthropicChatService.BuildRequest` | `claude-haiku-5-5`、`MaxTokens=32000`、`Effort.Medium`、beta `mcp-client-2025-11-20`、`McpServers=[tw-stock]`、tools = regex tool search + `BetaMcpToolset`（全部 defer，`AlwaysLoadedTools` 5 個除外）、system = 提示 +（快取的）市場背景 |
| `ModelTurn` | 讀串流：text delta → SSE `delta`；`BetaMcpToolUse` → SSE `tool_use`；`message_start` → `UsageLedger.MarkAiBilled()`；`pause_turn` 重試 2 次；檔案遺失重傳一次。回 `TurnOutcome` |
| `TurnVerdict.Of` | 成功／截斷／中斷／失敗各存什麼（失敗不存半截答案） |
| `TokenTotals`、`SseStream`、`ChatTurnRegistry`、`ChatErrorCodes` | token 加總、SSE 寫出、同對話新一輪取消舊的（記憶體內，單機）、錯誤碼 |
| `MarketBackground` | **Finance 專屬**：把 /finance 的市場資料轉成 system prompt 第二段，快取 30 分 |
| 附件 | `ChatAttachmentService`、`AttachmentFileProcessor`（圖片只讀 header）、`AnthropicFileStore`（Files API，失敗回 base64） |

**協定**：`POST /api/chatbot/chat`（SSE），事件 `start` / `delta` / `tool_use` / `done` / `error`。對話 CRUD、停止、附件在同一個 controller。

**計費**：`UsageCosts.ForChatMessage`（5 + 附件），controller 內 inline `TryConsumeAsync`；`message_start` 就算 AI 已計費，之後失敗不退款（`UsageLedger` + `UsageSettlementMiddleware`）。

**前端**：`Finance/components/aiAssistant/`（Launcher → 一行框 → 展開面板；歷史欄／抽屜；`useChatConversation`、`usePendingAttachments`、`usePreview`；62 個 panel 測試）。`hooks/useChatStreaming.ts` 用 fetch + reader 讀 SSE（**已知缺陷：跨 chunk 的半行沒有 buffer，事件可能被丟掉**）。`apis/chatbotApis` 是獨立 `createApi`。

**要參數化才能共用的地方**：模型與工具常數、system prompt、市場背景、usage log key、對話表沒有「哪個 app」欄位、`/api/chatbot` 寫死、範例問題／錯誤文案（提到台股）、配色。

### A2. Connected Services（`/connectors`）

- `ConnectorService`：三個寫死的 OAuth provider（Microsoft、Atlassian、Google，白名單在 `Common/ConnectorProviders.cs`，設定在 `Connectors:Providers:*`）。Authorization Code（**沒有 PKCE**）、refresh token、使用者可自填 client id/secret。
- `TokenEncryptionService`：AES-256-CBC + HMAC-SHA256（encrypt-then-MAC），金鑰 `TokenEncryption:Key`（Key Vault）。Data Analysis 也拿它加密資料源設定。
- `StateService`：HMAC 簽章的 state，10 分鐘過期。
- `UserConnectors` 表（Platform），唯一索引 `(UserId, ProviderName)`。
- **今天沒有任何伺服器程式使用這些 token**；唯一用途是前端「複製 access token」按鈕（給外部 MCP server 貼用）。portal 也刻意隱藏了這個 app。
- 沒有任何 ConnectorService／加密／callback 的測試。

### A3. Data Analysis（`/data-analysis`）與 Dashboard

- `IDataAnalysisQueryService`：schema、max date、distinct values、summarize、details、export，全部回 `ServiceResult<T>`。
- 「問 AI」：`INlSummarizeService` → `NlQueryPlanner`（gpt-6-luna 產 `SummarizeRequestDto` 計畫，驗證、一次修正、`Unmatched` 不默默丟掉，DECISION-025）。
- `EngineSources`：解析資料源、欄位白名單（`DataAnalysisFieldGuard`）、custom source 的錯誤文字只給擁有者看。
- Dashboard：widget 存 `PlanJson`（原始 `SummarizeRequestDto`），檢視時用擁有者身分重跑。`DashboardAccessPolicy`。
- 很自然的 agent 工具：`list_sources`、`get_schema`、`get_distinct_values`、`get_max_date`、`summarize`、`get_details`、`list_dashboards`、`create_dashboard`、`add_widget`——都已有對應 service 方法。
- 缺口：session（上傳檔）的計畫沒有 context，伺服器端沒擋它被存成 widget（只有前端擋）。

### A4. 其他

- App 註冊表 `constants/apps.tsx`：`AppDef { name, path, icon, allPaths, color, description, standalone? }`。
- 用量：DECISION-006/007——沒有付費，只有每人每天的加權預算（預設 60），超過 429 + `UsageLimitModal`；`UsageFeature` 一個 feature 一個成本。
- 架構測試：`ModuleBoundariesTests`（模組、`TableOwners`）、`NoLocalClockTests`、`ControllersDoNotTouchTheDatabaseTests`、`AdminOnlyEndpointsTests`、`ModelMatchesMigrationsTests`、`ErrorCodesTests`、不准用 Newtonsoft。

## B. AI 趨勢（2026-10）

### B1. MCP 規格現況

- **最新正式版 2026-07-28「stateless-first」**：拿掉 `initialize` 握手與 `Mcp-Session-Id`，每個 request 在 `_meta` 帶版本與能力；可選的 `server/discover`；Streamable HTTP 請求要帶 `Mcp-Method`、`Mcp-Name` header；通知改由 `subscriptions/listen`；舊 HTTP+SSE transport 正式 deprecated（一年緩衝）；`tools/list` 等回 `ttlMs`／`cacheScope`、順序固定（利於 prompt cache）；Tasks、MCP Apps、Enterprise Managed Authorization 成為正式 extension；Roots／Sampling／Logging deprecated；elicitation 改成 MRTR（`resultType: "input_required"`）。
- **授權（OAuth 2.1）**：
  - MCP server 必須提供 RFC 9728 Protected Resource Metadata（PRM），client 必須用 PRM 找 authorization server（401 的 `WWW-Authenticate: Bearer resource_metadata=..., scope=...`，或 well-known 路徑）。
  - AS metadata：RFC 8414 與 OIDC Discovery 兩種都要支援。
  - Client 註冊優先序：**CIMD（Client ID Metadata Document，client_id 本身是一個 HTTPS URL）** > 預先註冊 > DCR（RFC 7591，**已 deprecated** 但仍可用）。
  - PKCE（S256）、RFC 8707 `resource` 參數（授權與換 token 都要帶，值為 server canonical URI）。
  - RFC 9207：記下 AS 的 `issuer`，回呼時驗 `iss`（規則表見規格）。
  - Scope：先用 401 challenge 的 `scope`，沒有才用 `scopes_supported`；執行中 403 `insufficient_scope` → step-up（新舊 scope 聯集，重試次數有上限）。
  - token 一律放 `Authorization: Bearer`，不能放 query string；不能把別的 AS 的 token 送給 MCP server。
- **MCP Apps（SEP-1865，Final）**：tool 用 metadata 指向預先宣告的 `ui://` 資源（`text/html;profile=mcp-app`），host 用 sandboxed iframe 顯示，iframe 與 host 之間走 MCP JSON-RPC（postMessage）；UI 發起的 tool call 可要求使用者同意。ChatGPT（Apps SDK）、Claude 等都採用。

### B2. Anthropic Messages API 的 MCP connector

- `mcp_servers: [{ type: "url", url, name, authorization_token? }]` + `tools: [{ type: "mcp_toolset", mcp_server_name, default_config: { enabled, defer_loading }, configs: {...} }]`。新 beta `mcp-client-2026-09-15` 加上 tool-list pinning（回應可能先有 `mcp_tool_listing` block）。
- **限制**：只支援 tools（沒有 resources／prompts，所以**不能做 MCP Apps**）；只支援公開 HTTPS；OAuth 要自己做完、自己 refresh；**工具在 Anthropic 那邊直接執行，沒有任何確認機制**——官方建議想要人工確認就 denylist 寫入工具，或自己處理 tool use。
- Tool search（regex／BM25）可搭配 `defer_loading`，自訂（client）工具也能 defer。

**結論（影響架構最大的一點）**：要做到「寫入前先確認」、「工具結果當不可信資料包起來」、「MCP Apps」、「本站自己的工具」，**工具必須由我們的伺服器執行**（client tools + 我們當 MCP client）。Anthropic 的 MCP connector 只適合「可信、唯讀、免互動」的伺服器——現有的 tw-stock 正是這種，保留原樣以免 Finance 退化。

### B3. Agent 與工具呼叫的設計模式

- **Human-in-the-loop**：讀取自動、寫入要確認；確認卡片要顯示「哪個 connector、哪個工具、確切參數」，可選「這個工具以後都允許」。Claude／ChatGPT 的 connector 都有「每個工具：自動允許／需要確認／停用」的設定。
- **工具註解**：MCP tool annotations（`readOnlyHint`、`destructiveHint`、`idempotentHint`、`openWorldHint`）可當預設權限的依據，但**不可信**（是 server 自己說的），只用來決定「更嚴格」，不用來放寬。
- **Prompt injection**：外部工具回傳的內容一律標記為資料；被外部內容「污染」的對話，後續的寫入一律要確認（taint 追蹤）；不讓模型把 token、內部網址回顯。
- **Generative UI**：工具回結構化資料，前端用註冊好的 renderer 畫成卡片（表格、圖表、單字卡、確認卡），而不是讓模型吐 HTML。MCP Apps 是同一個概念的標準化版本。
- **Connector 目錄 UX**（Claude、ChatGPT、Cursor）：目錄（官方審核過的）+「新增自訂 connector」（名稱 + URL，進階設定可填 OAuth client id/secret）+ 每個對話可開關 connector + 每個工具的權限 + 測試連線／疑難排解提示。

### B4. 可用的套件

- 官方 C# MCP SDK：`ModelContextProtocol.Core` 1.x（NuGet，已支援 2026-07-28 規格，含 HTTP client transport 與 OAuth 輔助），可以傳入我們自己的 `HttpClient`（→ 裝 SSRF 防護）。
- Anthropic C# SDK 12.60.0（已在用），有 beta messages、tool use、tool search 型別。

## 來源

- MCP 2026-07-28 規格公告：https://blog.modelcontextprotocol.io/posts/2026-07-28/
- MCP 授權規格（latest）：https://modelcontextprotocol.io/specification/latest/basic/authorization
- SEP-1865 MCP Apps：https://modelcontextprotocol.io/seps/1865-mcp-apps-interactive-user-interfaces-for-mcp
- Anthropic MCP connector：https://platform.claude.com/docs/en/agents-and-tools/mcp-connector
- Claude connectors 使用者面向整理：https://sunpeak.ai/blogs/what-are-claude-connectors/
- ModelContextProtocol NuGet：https://www.nuget.org/packages/ModelContextProtocol.Core
