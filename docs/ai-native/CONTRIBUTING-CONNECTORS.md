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

### 你的 MCP server 帶介面（MCP Apps）

本站的 chat 是 MCP Apps 的 host（design/09）：工具的 `_meta.ui.resourceUri` 指向一個 `ui://` 資源時，呼叫結果會在回答裡畫成那個 app。

- 資源用 `resources/read` 讀，必須是 `text`（不收 `blob`）、mimeType `text/html;profile=mcp-app`、512 KB 以內；一個 connector 只畫**前 5 個不同的 app**，其他工具的結果顯示成文字。
- 要連外的主機寫在資源的 `_meta.ui.csp`（`connectDomains`、`resourceDomains`），只收 **https 的網路主機名稱**（不收 `*`、IP、`localhost`、路徑）；沒寫就什麼都連不出去，圖片只能用 `data:`。
- app 在 **opaque origin** 的 iframe 裡（`sandbox="allow-scripts"`）：沒有 cookie、localStorage、彈出視窗、表單送出；不要換掉自己的頁面（`location = …`），本站會當成離開、改顯示文字結果。
- 支援的訊息：`ui/initialize`（`hostContext.theme` 是 `light`／`dark`，換主題會重畫 app）、`ui/notifications/initialized` 之後收到 `tool-input`、`tool-result`（`content` 只有 text block，加上 `structuredContent`）、`ui/notifications/size-changed`（高度 80–640）、`ui/open-link`（只收 https，讀者按了才開）、`tools/call`（只限同一個 server、**唯讀**、visibility 含 `app`、使用者設成自動的工具）。`ui/message` 尚未支援（-32601），`ui/update-model-context` 不會送給模型。
- `_meta.ui.visibility: ["app"]` 的工具只給 app 用，模型看不到；`["model"]` 的工具 app 不能呼叫。
- 本地驗證：照 `.loop/shots/mcp-host.cjs` 裡的假 app 寫法（迴圈的截圖腳本，合併前會刪，看它的訊息順序即可）。

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
- 同一張卡片不用多寫任何東西就會出現在**別家的 AI** 裡：本站 MCP server 把 table／link 卡片放進 `structuredContent.card`，Claude、ChatGPT 這類支援 MCP Apps 的 client 用 `SiteMcpApp` 畫出來（design/03 §9.2）；link 的本站路徑會自動補成完整網址。所以卡片的欄位名稱與標題要讓沒看過本站的人也看得懂。

### 原生 connector（以 Google 日曆為例）

`Services/Connectors/Google/GoogleCalendarTool.cs` 是範本（第二個例子 `Services/Connectors/Atlassian/JiraIssuesTool.cs`：先問 accessible resources 找到使用者的 Jira 網站、再用 JQL 搜尋，模型寫的查詢字串壓成一行、截長度，對方說查詢寫錯（400）時把對方的錯誤訊息截短交給模型修正；第三個 `Services/Connectors/Outlook/OutlookCalendarTool.cs`：同一種資料換一家服務時，共用資料型別與卡片，只換請求與解析）：

- token 用 `IConnectorService.GetAccessTokenAsync(context.UserId, ConnectorProviders.Google)` 拿（既有帳號連線，過期會自己 refresh）；scope 是 `Connectors:Providers:<Provider>:Scopes`，需要新 scope 時寫進 FOR_JACKY（已連線的人要重新連線）。
- **沒連線不是錯誤**：回一句請使用者到 Connectors 連線的話，加一張 `href = "/connectors"` 的連結卡。對方回 401／403 也一樣（多半是 scope 不夠或過期）。其他失敗回 `AiToolResult.Error`，不帶對方的原文。
- 打固定網址的 GET 用具名 client `ExternalReadOnly`（有重試）；**使用者給的網址**才一定要 `SafeHttp`。
- 結果裡有**別人能寫的字**（行事曆邀請的標題、email 主旨、Jira 留言）就在 descriptor 設 `OutsideText: true`：這輪之後的寫入一律先問使用者，跟外部 MCP connector 一樣（`AiToolDescriptor.ReadsOutsideText`）。外部文字也要壓成一行、截長度。
- 時間一律台北時間（`time.GetTaipeiToday()`），測試用 `TestClock` 跨台北午夜。
- **寫入型的原生工具**（範例 `Services/Connectors/Atlassian/JiraAddCommentTool.cs`，在 Jira 留言，D-132）：
  - `ToolEffect.Write`，並實作 `IDescribesCall`，確認卡用一句話寫出**完整**的內容（哪張票、留言全文）。
  - 送出的內容就是使用者確認的內容：太長就回錯誤請模型縮短，**不要自己截斷**。
  - POST 用預設 client（`Options.DefaultName`，有逾時、沒有重試），**不要用 `ExternalReadOnly`**——重試可能寫兩次。
  - 對方回 5xx 或逾時時，不要說「沒寫入」（對方可能已經寫了）：說「可能沒有成功」並附連結讓使用者自己去看。
  - 測試要斷言用的是哪個 client、送出的 body，以及 401／403／404／5xx 的說法（`JiraAddCommentToolTests`）。

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

## 附：你的工具也會出現在本站的 MCP server

寫好的 `ISiteTool` 不只 Studio AI 用得到：本站對外的 MCP server（`POST /api/mcp`，design/03 §9）會把它提供給使用者接上的 Claude Desktop、Cursor 等。讀取型工具一律提供；`ToolEffect.Write` 只給允許寫入的 token；`Destructive` 永遠不提供。所以 **`Effect` 要老實標**，說明（`Description`）也要寫得讓別家的模型看得懂。不需要另外註冊。

想讓使用者在 Claude Code 打一個斜線指令就叫你的工具做一件完整的事，再加一個 **現成指令（MCP prompt）**，放在工具旁邊（design/03 §9.3，範例 `Services/Dictionary/Assistant/EnglishSitePrompts.cs`）：

```csharp
public static readonly SitePrompt ExplainWord = new(
    "explain_word", "解釋單字", "從 Jacky Studio 的字典查一個英文單字，用中文解釋並附例句。",
    [LookUpWordTool.Name],                                        // 這些工具都提供給 token 時才出現
    [new SitePromptArgument("word", "要查的英文單字", Required: true)],
    args => $"用 jacky-studio 的 {LookUpWordTool.Name} 查「{args["word"]}」，……");
public static IReadOnlyList<SitePrompt> All { get; } = [ExplainWord];
// 模組的註冊：foreach (var prompt in EnglishSitePrompts.All) services.AddSingleton(prompt);
```

`SitePromptDefinitionsTests` 會自動檢查它（名稱、工具存在、參數進到文字），不用另外寫測試。

