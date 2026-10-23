# 03 — Connector 架構

> 目標：任何數位化的東西都能變成 AI 的工具；MCP 是一等公民；現有 `/connectors` 演進而來，現有連線繼續能用；新增一個 connector 不用改核心程式。

## 1. 名詞

| 名詞 | 意思 |
|---|---|
| **Connector 定義**（definition） | 「一種」connector 的描述：名稱、圖示、分類、怎麼認證、工具從哪來。程式碼裡註冊的（內建）或使用者自填的（自訂 MCP） |
| **Connector 安裝**（installation） | 某個使用者「連上」某個定義：認證資料（加密）、啟用狀態、每個工具的權限、工具清單快取、最後狀態 |
| **工具**（tool） | AI 能呼叫的一個動作：名稱、說明、JSON schema、效果（讀／寫／破壞性）、可選的顯示方式 |
| **工具來源**（tool source） | 給一個使用者在某次對話中「現在有哪些工具」的東西。每個安裝對應一個來源；本站功能也是來源 |
| **執行方式** | `Local`：我們的伺服器執行（可確認、可包裝結果）；`AnthropicHosted`：交給 Anthropic MCP connector 執行（只給可信唯讀的伺服器，例如 tw-stock） |

## 2. 三種 connector，一套抽象

```mermaid
flowchart LR
  subgraph Definitions[Connector 定義（DI 註冊 / 使用者自填）]
    S[本站工具 SiteTools<br/>English / Portfolio / DataAnalysis]
    M[Remote MCP<br/>描述檔：URL + 認證方式]
    N[原生 connector<br/>實作 INativeConnector<br/>例：Google Calendar]
  end
  Definitions --> R[ConnectorCatalog]
  R --> T[ToolResolver<br/>某使用者、某 profile 的工具組]
  I[(ConnectorInstallations)] --> T
  U[(UserConnectors<br/>既有 OAuth)] --> N
  T --> A[Agent loop<br/>AnthropicChatService]
  A -->|Local| X[ToolExecutor<br/>權限・確認・不可信包裝]
  A -->|AnthropicHosted| H[Anthropic MCP connector]
  X --> MC[McpToolClient<br/>SafeHttp（防 SSRF）]
  X --> NT[原生/本站工具 C# 呼叫]
```

### 2.1 核心介面（`Services.Connectors`，草案）

```csharp
// 一種 connector。內建的用 DI 註冊（services.AddConnector<T>() 或 AddMcpConnector(descriptor)）。
public interface IConnectorDefinition
{
    string Key { get; }                    // 穩定、小寫 kebab："tw-stock"、"google-calendar"、"site"
    ConnectorInfo Info { get; }            // 顯示名稱、說明、分類、圖示 key、文件連結、發行者
    ConnectorAuthSpec Auth { get; }        // None | ApiKey(header, prefix) | Bearer | OAuth2(...) | McpOAuth(自動探索) | ExistingProvider("Google")
    ConnectorExecution Execution { get; }  // Local | AnthropicHosted
    bool AlwaysOn { get; }                 // 本站工具：不用安裝、不能中斷
}

// remote MCP：只要一份描述，工具在執行時從 tools/list 讀
public sealed record McpConnectorDescriptor(string Key, ConnectorInfo Info, Uri ServerUrl,
    ConnectorAuthSpec Auth, ConnectorExecution Execution = ConnectorExecution.Local) : IConnectorDefinition;

// 原生 connector（或本站工具）：自己提供工具
public interface INativeConnector : IConnectorDefinition
{
    IReadOnlyList<IAiTool> Tools { get; }
}

public interface IAiTool
{
    AiToolDescriptor Descriptor { get; }   // Name, Description, InputSchema (JsonElement), Effect, Display?
    Task<AiToolResult> InvokeAsync(JsonElement input, AiToolContext context, CancellationToken ct);
}

public enum ToolEffect { Read, Write, Destructive }
public enum ToolPermission { Auto, Confirm, Disabled }

// AiToolContext：UserId、該安裝的 access token 取得器（自動 refresh）、TimeProvider、log
// AiToolResult：給模型的內容（文字/JSON）＋ 可選的 Card（generative UI，給前端畫）＋ IsError
```

- 本站工具（`site`）也是 `INativeConnector`，但每個功能模組各自提供自己的工具（`IAiTool` 實作放在該模組，例如 `Services.DataAnalysis.AiTools`），用 DI 標記「屬於 site connector」。這樣 English 的工具歸 English 模組，Assistant 只透過 DI 拿到，不引用它。
- 既有的 Google／Microsoft／Atlassian：定義的 `Auth = ExistingProvider("Google")`，token 由既有 `IConnectorService.GetAccessTokenAsync` 取得（**不搬資料、不改既有端點**）。今天沒有工具；階段 1 會附一個原生範例 connector（Google Calendar 讀取行程）證明這條路。

### 2.2 預設權限

| 條件 | 預設 |
|---|---|
| 本站工具 `Read` | Auto |
| 本站工具 `Write` | Confirm |
| `Destructive`（任何來源） | Confirm，且不能設成 Auto |
| 外部 MCP 工具，`annotations.readOnlyHint == true` | Auto |
| 外部 MCP 工具，其他 | Confirm |
| 對話已讀過外部（不可信）內容後的任何 `Write` | 一律 Confirm（不看使用者設定的 Auto），見 06 |

使用者可以在 connector 頁逐一調整（Disabled 的工具不送給模型）。

## 3. 資料表（只新增，不動現有表）

`ConnectorInstallations`（新 module **Connectors** 擁有）：

| 欄位 | 型別 | 說明 |
|---|---|---|
| Id | Guid | PK |
| UserId | string(255) | FK Users，cascade |
| DefinitionKey | string(100) | 內建定義的 key；自訂 MCP 為 `custom` |
| DisplayName | string(100) | 自訂的才需要；內建的取定義 |
| ServerUrl | string(500)? | 自訂 MCP 的 URL（canonical，無 fragment、無尾斜線） |
| AuthKind | string(20) | None / ApiKey / Bearer / OAuth |
| EncryptedSecret | string? | `TokenEncryptionService` 加密的 JSON：api key（header 名）、bearer、或 OAuth 的 access/refresh token、到期、client 註冊結果、issuer、granted scopes |
| Enabled | bool | 中斷 = false 並清掉 secret（保留工具設定） |
| ToolSettingsJson | string? | `{ "toolName": "Auto" | "Confirm" | "Disabled" }` |
| ToolsCacheJson | string? | 最後一次 `tools/list` 的名稱、說明、schema、annotations |
| ToolsFetchedAt | DateTime? | UTC |
| LastStatus | string(20) | Ok / NeedsAuth / Error / Unknown |
| LastError | string(500)? | 給擁有者看的人話（不含 token） |
| CreatedAt / UpdatedAt | DateTime | UTC |

索引：`(UserId, DefinitionKey)` **filtered unique**（`WHERE DefinitionKey <> 'custom'`），`custom` 可多筆；另一般索引 `(UserId)`。每人自訂 MCP 上限 20 個（防濫用）。

**沒有安裝列的定義**（`site` 永遠開著；既有 Google／Microsoft／Atlassian 的連線在 `UserConnectors`）：使用者第一次調整它的工具權限時，才建立一列（`AuthKind=None`、`EncryptedSecret=null`），只用來存 `ToolSettingsJson`。沒有列 = 全部用預設權限。

## 4. MCP client（`McpToolClient`）

- 用官方 `ModelContextProtocol.Core`（HTTP client transport），傳入我們的 `SafeHttp` client（06 的 SSRF 防護）。
- 能力：`ListToolsAsync`（快取在 `ToolsCacheJson`，依 `ttlMs` 或 1 小時）、`CallToolAsync`、測試連線（列工具 + 回應時間）。
- 401 → 安裝狀態 `NeedsAuth`，對話中回「請到 Connectors 重新連線」的工具錯誤（不是整輪失敗）。
- 403 `insufficient_scope` → 狀態 `NeedsAuth`，記下需要的 scope，重新授權時用聯集（step-up）。
- 呼叫逾時 30 秒；結果大小上限 100 KB（截斷並註明）。
- 若 SDK 在 CI 上有相容性問題，退路是自己寫最小的 Streamable HTTP JSON-RPC（`tools/list`、`tools/call`）——規格 2026-07-28 是 stateless，最小實作很小。

## 5. 認證流程

### 5.1 免認證／API key／Bearer

使用者在新增表單填入；存 `EncryptedSecret`；每次呼叫帶上（`Authorization: Bearer` 或自訂 header）。

### 5.2 MCP OAuth（依 MCP 授權規格）

```mermaid
sequenceDiagram
  participant B as 瀏覽器
  participant S as 我們的伺服器
  participant M as MCP server
  participant A as Authorization server
  B->>S: POST /api/connectors/installations（URL）
  S->>M: 不帶 token 打一次（SafeHttp）
  M-->>S: 401 WWW-Authenticate resource_metadata, scope
  S->>M: GET PRM（或 well-known 路徑）
  S->>A: GET AS metadata（RFC 8414 → OIDC 順序）
  alt AS 支援 CIMD
    Note over S: client_id = https://apps.jacky-studio.com/api/connectors/oauth/client-metadata
  else AS 有 registration_endpoint
    S->>A: DCR 註冊（存加密）
  else
    S-->>B: 請使用者填 client id / secret
  end
  S-->>B: 授權 URL（PKCE S256、resource、scope、state）
  B->>A: 使用者同意
  A-->>B: redirect /api/connectors/callback/mcp?code&state&iss
  B->>S: callback
  S->>S: 驗 state、驗 iss（RFC 9207 表）
  S->>A: 換 token（code_verifier、resource）
  S->>S: 加密存入安裝，LastStatus=Ok，列工具
  S-->>B: redirect /connectors?connected=…
```

- state：沿用 `IStateService.GenerateState(userId, provider)`，provider 傳 `mcp:{installationId}`（介面不變）；PKCE verifier 與 expected issuer 存在**伺服器端**（`IMemoryCache`，10 分鐘，key 為 nonce），不放在瀏覽器。
- CIMD 文件由我們的端點提供（`client_name`、`redirect_uris`、`grant_types`、`token_endpoint_auth_method: "none"`）。這是 public client，靠 PKCE。
- refresh：取 token 時若 60 秒內到期就 refresh（每個安裝 single-flight，避免同時 refresh 兩次把 refresh token 用掉）。refresh 失敗 → `NeedsAuth`。
- callback 路由：既有 `api/connectors/callback/{provider}` 的 provider 白名單不含 `mcp`，新增一個明確的 `api/connectors/callback/mcp` 路由（ASP.NET 會優先比對字面路由），既有三個 provider 的流程完全不動。

### 5.3 既有 OAuth provider

不變。另外修兩個既有小問題（不改行為）：`CustomClientId` 註解說加密但其實沒有（只改註解）；補 `ConnectorService`／`TokenEncryptionService`／`StateService` 的單元測試（之前完全沒有）。

## 6. 讓別人容易貢獻（`CONTRIBUTING-CONNECTORS.md` 會寫）

- **加一個 remote MCP 到目錄**：在 `Services/Connectors/Catalog/BuiltInMcpConnectors.cs` 加一筆 `McpConnectorDescriptor`（key、名稱、URL、認證方式），完成。測試樣板：`CatalogTests` 自動檢查 key 唯一、URL 是 https、資訊齊全。
- **加一個原生 connector**：新增一個資料夾 `Services/Connectors/Native/<Name>/`，實作 `INativeConnector` 與數個 `IAiTool`，在 `ConnectorServiceExtensions` 一行 `services.AddNativeConnector<GoogleCalendarConnector>()`。測試樣板：`NativeConnectorTestKit`（假的 token、假的 HttpMessageHandler、驗 schema 是合法 JSON Schema、驗每個工具都有 Effect）。
- **加一個本站工具**：在該功能模組實作 `IAiTool`，`services.AddSiteTool<T>()`。同一個 TestKit。

## 7. 端點（草案）

| 方法 | 路由 | 說明 |
|---|---|---|
| GET | `api/connectors/catalog` | 目錄（含本站工具、內建 MCP、既有 OAuth provider）＋ 我的安裝狀態 |
| POST | `api/connectors/installations` | 安裝內建 MCP 或新增自訂 MCP（URL、認證） |
| POST | `api/connectors/installations/{id}/authorize` | 開始 OAuth，回 redirect URL |
| POST | `api/connectors/installations/{id}/test` | 測試連線，回工具清單與耗時 |
| PUT | `api/connectors/installations/{id}/tools` | 每個工具的權限 |
| PUT | `api/connectors/installations/{id}/enabled` | 啟用／停用 |
| DELETE | `api/connectors/installations/{id}` | 移除（清 secret） |
| GET | `api/connectors/callback/mcp` | OAuth 回呼 |
| GET | `api/connectors/oauth/client-metadata` | CIMD 文件（匿名） |

既有的 `api/connectors/status`、`{provider}/connect` 等全部保留。

## 8. 模組（評審後修正：不能有環、直接引用才算）

| Module | Namespaces | May use |
|---|---|---|
| Connectors（新） | `Services.Connectors`（定義、目錄、安裝、`IAiTool` 等介面、`McpToolClient`、`SafeHttp`、內建 MCP 描述含 **tw-stock**） | Platform |
| Assistant（新） | `Services.Chatbot`（原屬 Finance，整個改歸這裡，namespace 不改名以減少變動）、`Services.Assistant`（profile 介面、agent loop、`StudioChatProfile`、`site` connector 的彙整） | Connectors, Platform |
| Finance | 原有 + `Services.Finance.Assistant`（`FinanceChatProfile`、`MarketBackground` 從 Chatbot 搬來，因為它用 MarketData） | Assistant, Connectors, MarketData, Platform |
| DataAnalysis | 原有 + `Services.DataAnalysis.Assistant`（`DataAnalysisChatProfile`、data-analysis 工具） | Assistant, Connectors, Platform |
| English / Portfolio | 原有 + `*.AiTools` | Connectors, Platform |

- tw-stock 的 URL、beta、`AlwaysLoadedTools` 搬到 Connectors 的 `TwStockMcp`（內建描述，`Execution = AnthropicHosted`），Finance 與 Studio profile 都引用它，Assistant 不必引用 Finance。
- 本站工具：各模組實作 `IAiTool`，用 `services.AddSiteTool<T>(group)` 註冊；Assistant 的 `SiteToolSource` 從 DI 拿 `IEnumerable<SiteToolRegistration>`，不引用任何功能模組。
- `ChatConversations` / `ChatMessages` / `ChatAttachments` 改由 Assistant 擁有（`TableOwners` 更新）；`ConnectorInstallations` 歸 Connectors；`UserConnectors` 仍歸 Platform（不搬）。
- 這張表同步寫進 server `CLAUDE.md` 的 Modules 表（實作時）。
