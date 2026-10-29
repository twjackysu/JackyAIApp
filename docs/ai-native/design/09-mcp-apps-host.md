# 09 — 本站當 MCP Apps 的 host：使用者加的 MCP server 的介面畫在對話裡

> 第 53 輪（2026-10-29）新增，D-201。design/07 把「MCP Apps：支援 `ui://` 資源，在 sandboxed iframe 顯示」列為延伸；
> 本站對外早已是 MCP App（第 9 輪，`SiteMcpApp`），但自己的 chat 畫不出別人的 app——**只做了一半**。

## 1. 為什麼

- MCP Apps（SEP-1865，`io.modelcontextprotocol/ui`）是 2026 年 MCP 最受注目的擴充：Claude、ChatGPT、VS Code 都會在對話裡
  畫出 MCP server 自己的互動介面（圖表、表單、地圖），而不是一段文字。
- Jacky 的要求是「別人做好的東西直接拿來用」。今天使用者在 /connectors 加了一個有 app 的 MCP server，本站 chat 只拿到
  它的文字；在 Claude 裡卻看得到它的介面。**做完之後，本站同時是 MCP App 的 server 與 host**，這是成果發表的一句話。

## 2. 範圍（凍結前做得完的部分）

| 做 | 不做（記在 §8） |
|---|---|
| 使用者連線的 remote MCP server 的工具宣告 `_meta.ui.resourceUri` 時，回答裡畫出它的 app | 原生 connector、本站工具（它們已有宣告式卡片，D-019） |
| `ui/initialize`、`tool-input`、`tool-result`、`size-changed`、`ui/open-link`（先問）、`ping` | `fullscreen`／`pip` 顯示模式、`tool-input-partial` |
| 主題：app 的文件帶跟聊天一樣的 `color-scheme`，換亮暗時重畫（不送 `host-context-changed`） | `ui/message`（第 53 輪未做，回 -32601；要做就放進輸入框、當外部文字）、`ui/update-model-context`（回 `{}`，不送給模型） |
| app 呼叫同一個 server 的**唯讀**工具（`tools/call`，依權限、visibility 有 `app`）| app 呼叫寫入工具（回錯誤，請使用者在對話裡要求） |
| `_meta.ui.visibility` 沒有 `model` 的工具不給模型 | `permissions`（相機、麥克風）一律不給 |

## 3. 資料流

1. **列工具**（`McpToolClient.ListToolsAsync`）：`CachedTool` 多 `UiResourceUri`（`ui://` 開頭、≤ 512 字）與
   `AppOnly`（visibility 有 `app` 沒有 `model`）。存在既有的 `ToolsCacheJson`，`CachedTools` 寬鬆讀，**不需要 migration**。
   `AppOnly` 的工具不進 `ConnectorToolSource` 給模型的清單。
2. **呼叫工具**（`CallToolAsync`）：`McpToolOutcome` 多 `AppResult`：`CallToolResult` 的 `content`（只留 text block）與
   `structuredContent`，序列化後 ≤ 64 KB（超過就不給 app，只有文字）。
3. **卡片**：該工具有 `UiResourceUri` 時，`tool_result.card` 是 `{ kind: "mcp-app", installationId, toolName, title, input, result }`
   ——**不含 HTML**。卡片照舊存進 `ChatMessages.CardsJson`（小），重開對話也能重畫。`ToolCardGuard` 認得這個 kind（input／result 各 ≤ 64 KB）。
4. **HTML**：前端畫卡片時才 `GET api/connectors/installations/{id}/app?uri=ui://…`：伺服器以使用者的認證對**同一個 server**
   `resources/read`（經 `SafeHttp`），要求 mimeType `text/html;profile=mcp-app`、文字（不收 blob）、≤ 512 KB、connector 開著、uri 是
   該 connector 的工具宣告過的**前 5 個不同的 app** 之一（`McpAppService.AppUris`；其餘工具的結果只顯示文字）；回
   `{ html, connectDomains, resourceDomains, prefersBorder, connector, host }`。**伺服器不快取**（D-202：一個 app 可達 512 KB，
   快取會被一個帳號塞滿記憶體），前端的 RTK Query 留 10 分鐘。別人的 installation 是 NotFound。
5. **app 呼叫工具**：`POST api/connectors/installations/{id}/app-call { tool, arguments }`：只限開著的、有 app 的 connector 的工具、
   visibility 有 `app`（`CachedTool.NotForApp` 沒設）、`ToolEffect.Read`、使用者的權限是「自動」（停用或改成先問都不行）；其他一律
   Forbidden（app 收到 JSON-RPC error -32000）。結果只給 app。不扣用量（沒有 AI）。body ≤ 64 KB。
   兩個端點共用 `RateLimitPolicies.McpApp`（每人每分鐘 60 次）。

## 4. 前端：`McpAppCard`

- `AiChatConfig.cardRenderers` 的共用 renderer（三個助理都有，因為 connector 工具三個助理都能用）。`React.lazy`。
- **一層 iframe，`sandbox="allow-scripts"`、不給 `allow-same-origin`**：app 的 origin 是 opaque，讀不到本站的 cookie、
  localStorage，也不能 `fetch` 本站 API（沒有 cookie）。`srcdoc` = 我們在 `<head>` 最前面插入的 CSP `<meta>` + app 的 HTML。
  多個 CSP 取交集，所以 app 自己的 `<meta>` 只能更嚴、不能放寬。
  - 規格要求 web host 用「不同 origin 的 sandbox proxy」兩層 iframe；那需要另一個網域（DNS、憑證），只有 Jacky 能設。
    opaque origin 的單層 iframe 在隔離上至少一樣嚴（D-201），代價是 app 不能用 localStorage——規格本來就要求 app 自己偵測。
- **CSP**（`mcpAppCsp(declared)`）：規格的預設——`default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline';
  img-src data:; media-src data:; connect-src 'none'; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'`，
  再加上 server 宣告的 `connectDomains`／`resourceDomains`，**只收 `https://` 的主機**（不收 `*`、`http:`、`data:` 以外的 scheme）。
  `frameDomains` 不給。
- **橋**（`useMcpAppBridge`，純函式 `handleAppMessage` 有測試）：只收 `event.source === iframe.contentWindow` 的 JSON-RPC 2.0；
  - `ui/initialize` → `{ protocolVersion, hostInfo: { name: "Jacky Studio" }, hostCapabilities: { openLinks: {}, serverTools: {} },
    hostContext: { theme, displayMode: "inline", locale: "zh-TW", timeZone: "Asia/Taipei", platform: "web", containerDimensions: { maxHeight: 640 } } }`
  - 收到 `ui/notifications/initialized` 後送 `tool-input`、`tool-result`（之前什麼都不送）
  - `size-changed` → iframe 高度，夾在 80–640
  - `ui/open-link` → 只收 https、不含帳密；卡片下方出現「介面想開啟 example.com　開啟／不用」，讀者按了才 `window.open(…, 'noopener')`（app 不能自己開）
  - `tools/call` → `app-call`（伺服器決定能不能）；`ui/request-display-mode` → `inline`；`ui/update-model-context` → `{}`；`ping` → `{}`；其他 → `-32601`
- **外框**：iframe 外面永遠有一行「「{connector}」的介面 · 來自 example.com · {工具標題}」，app 畫不到；名稱先、server 給的標題放最後並用
  `<bdi>` 隔開，名稱與標題都去掉控制與格式字元（`\p{Cc}\p{Cf}`：RLO 之類不能把說明列重新排序，D-202）。
- **app 不能離開**：srcdoc 只會載入一次；iframe 第二次 `load`（app 用 `location` 或 `<meta refresh>` 換成別的網頁，CSP 隨之消失）就移除
  iframe、改顯示文字結果與「這個介面想換成別的網頁，已停止顯示」（D-202）。
- CSP 的主機只收網路上的名稱：不收 IP、`localhost`、`.local`、沒有點的名稱（讀者瀏覽器會連到他家裡的設備，D-202）。

## 5. 安全（補 design/06）

- **隔離**：opaque origin、無 `allow-same-origin`／`allow-popups`／`allow-forms`／`allow-top-navigation`／`allow-modals`。
- **外洩**：app 拿到的只是同一個 server 自己的工具結果；CSP 只放行它宣告的 https 主機。它想送出的資料 server 本來就有。
- **注入**：app 的文字不經模型；`ui/message` 只進輸入框，送出時當外部文字（taint）。
- **寫入**：app 不能呼叫寫入工具；寫入仍只能經模型 → 確認卡。
- **SSRF**：`resources/read` 只對 installation 本身的網址，經 `SafeHttp`。
- **大小**：HTML 512 KB、input／result 64 KB、高度 640。

## 6. 測試

- 後端：`FakeMcpServer` 加一個有 `_meta.ui` 的工具與 `ui://` 資源；`McpToolClient` 讀到 `UiResourceUri`／`AppOnly`；
  `ConnectorToolSource` 不給模型 `AppOnly`、卡片是 `mcp-app`；`app` 端點：別人的 NotFound、未宣告的 uri 拒絕、mimeType 不對拒絕、太大拒絕；
  `app-call`：寫入、未給 app、停用的工具拒絕。
- 前端：`mcpAppCsp`、`handleAppMessage`（每個 method、不認得的、source 不對）、卡片在沒有 HTML（失敗）時顯示文字結果。
- 截圖：`.loop/shots/mcp-host.cjs`（假的 server app：一個會跟主題變色、會 `size-changed` 的小圖表）。

## 7. 里程碑

| 輪 | 內容 |
|---|---|
| 53 | **全部完成**：後端 §3、前端 §4、`app-call`、截圖（`.loop/shots/mcp-host.cjs`）、審查（D-202） |
| 54 | CLAUDE.md、SHOWCASE、CONTRIBUTING-CONNECTORS；找一個真的有 app 的公開 MCP server 試（若有）；`ui/message` 評估 |

## 8. 不做、之後

- 兩層 iframe＋獨立 sandbox 網域（需要 DNS，FOR_JACKY）。
- 寫入工具從 app 呼叫（要接確認卡）。
- `ui/update-model-context`、全螢幕、`permissions`。
