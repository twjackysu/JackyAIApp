# 貢獻 Connector：讓 AI 多一個能用的工具

> 架構見 [design/03-connector-architecture.md](design/03-connector-architecture.md)；後端規範見 `JackyAIApp.Server/CLAUDE.md` 的「Connectors」與「AI assistant (chat)」。

AI 能用的東西分三種，貢獻方式由簡到繁：

| 想加的東西 | 要做的事 | 範例 |
|---|---|---|
| 一個現成的 **remote MCP server**（免登入） | `BuiltInMcpConnectors.All` 加一行 | `tw-stock`、`deepwiki` |
| 一個 **本站功能**，讓 Studio AI 能用 | 實作 `ISiteTool`、註冊兩行 | `LookUpWordTool`、`AddWordToRepositoryTool` |
| 一個 **原生 connector**：本站程式直接呼叫某個服務的 API，用使用者既有的帳號連線 | 一樣是 `ISiteTool`，token 從 `IConnectorService` 拿 | `GoogleCalendarTool`（Google 日曆，唯讀） |
| 一個只在某個 app 的助理裡用、需要頁面狀態的工具 | 實作 `IAiTool`，由該 app 的 `IChatProfile` 每輪建立 | `AskDataTool`、`DescribeDataTool` |

使用者自己的 MCP server 不用改程式：到 `/connectors` →「新增 MCP server」貼網址即可（免登入、API key、Bearer token）。

## 1. 加一個 remote MCP server 到目錄

`JackyAIApp.Server/Services/Connectors/BuiltInMcpConnectors.cs`：

```csharp
new("my-server", "顯示名稱",
    "一兩句話：它能讓 AI 做什麼。",
    "dev",               // data / dev / productivity / finance
    "發行者", "https://example.com/mcp", "none", "https://example.com/docs"),
```

- 必須是 **Streamable HTTP**、**https**、**公開位址**（所有連線都經 `SafeHttp`，內網與保留位址會被擋）。
- `authKind` 是 `"none"`（免登入）或 `"oauth"`（照 MCP 授權規格自動探索、成為 client、授權，見 server CLAUDE.md 的 Connectors）。OAuth 的伺服器要支援 PKCE S256 與 CIMD 或 dynamic client registration，加入前先用「新增 MCP server」貼網址試一次。需要 API key 的請使用者用「新增 MCP server」自己加。
- `BuiltInMcpConnectorsTests` 會自動檢查 key 唯一、格式、網址、欄位齊全——不用另寫測試。
- 工具清單是加入時向伺服器 `tools/list` 讀的；每個工具的效果照 MCP annotations：`readOnlyHint: true` → 讀取（預設自動執行），`destructiveHint: true` → 無法復原（一定先問、不能設自動），其他 → 寫入（預設先問）。

## 2. 加一個本站工具（Studio AI 會用）

在功能自己的模組裡（例如 English 的 `Services/Dictionary/Assistant/`）：

```csharp
public sealed class MyTool(IMyService service) : ISiteTool
{
    public const string Name = "do_something";   // 英數、底線、連字號，≤ 64

    public static readonly AiToolDescriptor Definition = new(
        Name,
        "顯示給使用者看的名稱",
        "給模型看的說明：做什麼、什麼時候用。",
        JsonSerializer.SerializeToElement(new { type = "object", properties = new { /* … */ }, required = new[] { "…" } }),
        ToolEffect.Read);                         // Read / Write / Destructive：決定要不要先問使用者

    public AiToolDescriptor Descriptor => Definition;

    public async Task<AiToolResult> RunAsync(JsonElement input, AiToolContext context, CancellationToken ct)
    {
        // input 是模型寫的：一律當不可信輸入檢查。
        // 給模型的文字放 Content；要給使用者看的放 Card（table / link，或 app 自己註冊的 kind）。
        // 可預期的失敗回 AiToolResult.Error("模型能轉告使用者的人話")，不要丟例外。
        return new AiToolResult("結果", Card: new { kind = "link", title = "…", href = "/some/page" });
    }
}
```

註冊（`Extensions/ServiceCollectionExtensions.cs` 或該模組的 `*ServiceExtensions`）：

```csharp
services.AddScoped<ISiteTool, MyTool>();
services.AddSingleton(new SiteToolSet("功能名稱", [MyTool.Definition]));   // /connectors 列出它、讓使用者調權限
```

- 模組要能用 Connectors（`ModuleBoundariesTests` 的 `MayUse`、server CLAUDE.md 的模組表）。
- 工具在**目前使用者**的 request scope 裡執行（`ICurrentUser` 就是提問的人）。
- 寫入型工具不用自己做確認：`ClientToolRunner` 會先送確認卡（`approval_required`），使用者允許才呼叫 `RunAsync`。
- 卡片會經 `ToolCardGuard`（後端）與 `parseToolCard`（前端）過濾：純文字、table 最多 12 欄 200 列、連結只能是本站路徑或 https。

### 原生 connector（以 Google 日曆為例）

`Services/Connectors/Google/GoogleCalendarTool.cs` 是範本：

- token 用 `IConnectorService.GetAccessTokenAsync(context.UserId, ConnectorProviders.Google)` 拿（既有帳號連線，過期會自己 refresh）；scope 是 `Connectors:Providers:<Provider>:Scopes`，需要新 scope 時寫進 FOR_JACKY（已連線的人要重新連線）。
- **沒連線不是錯誤**：回一句請使用者到 Connectors 連線的話，加一張 `href = "/connectors"` 的連結卡。對方回 401／403 也一樣（多半是 scope 不夠或過期）。其他失敗回 `AiToolResult.Error`，不帶對方的原文。
- 打固定網址的 GET 用具名 client `ExternalReadOnly`（有重試）；**使用者給的網址**才一定要 `SafeHttp`。
- 結果裡有**別人能寫的字**（行事曆邀請的標題、email 主旨、Jira 留言）就在 descriptor 設 `OutsideText: true`：這輪之後的寫入一律先問使用者，跟外部 MCP connector 一樣（`AiToolDescriptor.ReadsOutsideText`）。外部文字也要壓成一行、截長度。
- 時間一律台北時間（`time.GetTaipeiToday()`），測試用 `TestClock` 跨台北午夜。

## 3. 測試樣板

- 本站工具：mock 服務，直接呼叫 `RunAsync`，斷言 `Content`、`Card`、`IsError`，並測不合法的 input（參考 `Services/Dictionary/WordToolsTests.cs`）。
- 跟 chat 整合（確認、權限、不可信包裝）已由 `AnthropicChatStreamTests` 涵蓋，不必重測。
- 原生 connector：`GoogleCalendarToolTests`——mock `IConnectorService` 的 token、`StubHttpMessageHandler` 扮對方 API，測沒連線、請求內容（網址、Bearer、時間範圍）、解析、401／403、其他失敗。
- `JackyAIApp.Server.Tests/TestHelpers` 的假網路（都實作 `IMcpHttpHandlerSource`，直接交給 `McpToolClient`、`McpOAuthDiscovery`、`McpOAuthEndpoints`）：
  - `FakeMcpServer`：最小的 Streamable HTTP MCP server（真的 SDK 跟它講協定），工具清單、要求的 header／token、401 的 `WWW-Authenticate` 都可設定；記下每個方法與 `Authorization`。範例：`McpToolClientTests`。
  - `FakeAuthorizationServer`：一整套 MCP 授權——MCP server 回 401＋`resource_metadata`、PRM、AS metadata、DCR、token 端點（驗 PKCE、redirect URI、resource；refresh token 用一次就作廢）；`Approve(url)` 扮使用者在授權頁按允許。範例：`McpOAuthSignInTests`（probe → 新增 → 授權 → 回呼 → 列工具 → chat 呼叫工具 → 到期 refresh → 撤銷後重新授權）。新的登入方式照它寫一個完整流程的測試。
  - `FakeWeb`：幾個網址各回固定答案，測單一端點（`McpOAuthDiscoveryTests`）。

## 4. 安全清單（PR 前自己檢查）

- [ ] 工具的 `ToolEffect` 誠實：會改任何東西就不是 `Read`。
- [ ] 不把例外訊息、連線字串、token 放進 `Content` 或 `Card`。
- [ ] 對外連線一律走 `SafeHttp`（不要自己 `new HttpClient()` 打使用者給的網址）。
- [ ] 回傳內容會當不可信資料包起來給模型；不要在工具裡「信任」外部回來的指示。
- [ ] 需要花 AI 額度的工具照 usage limit 規則收費（參考 `AskDataTool` 收 `UsageFeature.NlSummarize`）。
