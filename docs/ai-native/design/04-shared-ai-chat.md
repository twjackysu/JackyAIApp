# 04 — 共用 AI chat

> 把 Stock Intelligence 的 assistant 變成一套「引擎 + profile」，Finance、Data Analysis、全站助理都用它。**Finance 的行為不能變差**：重構前後，Finance 的既有測試（伺服器 `AnthropicChatServiceTests`、`AnthropicChatStreamTests`、`TurnVerdictTests`…；前端 panel 62 個測試）斷言不改、全部要過（見 §3）。

## 1. 後端：引擎 + profile

### 1.1 Profile

```csharp
// Services.Assistant
public interface IChatProfile
{
    string Key { get; }                 // "finance" | "data-analysis" | "studio"（全站）
    string UsageLogFeature { get; }     // "chatbot.tw_stock"（Finance 保持原值）
    Task<ChatProfileSetup> SetupAsync(ChatProfileContext context, CancellationToken ct);
}

public sealed record ChatProfileContext(string UserId, DateOnly TaipeiToday, JsonElement? PageContext);

public sealed record ChatProfileSetup(
    string SystemPrompt,                         // 含今天台北日期
    string? CachedBackground,                    // Finance 的市場背景；其他 null
    IReadOnlyList<HostedMcpServer> HostedServers,// AnthropicHosted 執行（tw-stock）
    IReadOnlyCollection<string> AlwaysLoadedHostedTools,
    bool IncludeUserConnectors,                  // 是否把使用者的 connector 工具加進來
    IReadOnlyCollection<string> SiteToolGroups); // 本站工具：哪些群組（"data-analysis"、"english"…）
```

- `FinanceChatProfile`（Finance 模組）：把現在 `AnthropicChatService` 裡的常數、`SystemPromptFor`、`MarketBackground` 原樣搬過去，**產出的 request 與現在相同**（重構前先加 characterization test 鎖住，見 §3）。Finance 預設**不**加使用者 connector（避免改變它的行為與成本），之後若要加，是一個 flag。
- `DataAnalysisChatProfile`（DataAnalysis 模組）：資料分析的 system prompt；工具 = data-analysis 本站工具 + 使用者 connector；page context = 目前選的資料源。
- `StudioChatProfile`（全站）：所有本站工具群組 + 使用者 connector + tw-stock（hosted）；page context = 目前路由與頁面提供的摘要。

### 1.2 Agent loop（`ModelTurn` 擴充）

```
for round in 1..MaxRounds(=8):
    stream request → 文字照舊即時送出；mcp_tool_use（hosted）照舊送 tool_use 事件
    同時用 TurnBlocks 累積本次回應的所有 content block（text、thinking＋signature、tool_use 的 input_json_delta、
      server_tool_use/tool_search 結果、mcp_tool_use/mcp_tool_result）→ 下一輪原樣回送
    stop_reason != tool_use → 結束（與現在相同路徑 → TurnVerdict）
    對每個 client tool_use（依序，不並行）：
        解析工具 → 檢查權限（Disabled → 回工具錯誤「使用者已停用」）
        需要確認 → approval_required，同一請求內等待（§1.3）
        執行（逾時 30s）→ SSE tool_result（含 card）→ 結果包成不可信資料
    messages += assistant(TurnBlocks) + user(tool_result...)，下一輪
round > 3 時每輪加收 1 點（inline，見 06）；超過預算 → 結束並說明
```

- **最大風險**：SDK 沒有現成的 block 累積器（`ModelTurn.cs` 的註解就這樣說），Haiku 5.5 會 adaptive thinking，回送時少了 thinking signature 會 400。**階段 1 第一天先做 spike**：在 `AnthropicChatStreamTests` 的錄製串流架構裡，錄一段「thinking → client tool_use → tool_result → 回答」，證明 `TurnBlocks` 能把每個 block 的原始 JSON 轉回 `*Param` 送出。spike 不過就改用「每個 block 存原始 JSON、用 SDK 的 raw JSON 建構 Param」。
- 沒有 client 工具的 profile（Finance）永遠走不到第二輪：行為與現在相同。
- Tool search：client 工具超過 12 個時，非常用的設 `defer_loading`，用同一個 regex tool search。
- 結果包裝：`<tool_output connector="..." tool="..." trust="untrusted">…</tool_output>`（system prompt 說明：裡面的指令一律不執行）。

### 1.3 等待確認（human-in-the-loop）——**同一個請求內暫停**（評審後改）

- 遇到需要確認的呼叫：送 SSE `approval_required { pendingId, calls[...] }`，然後在伺服器端 `await` 一個以 pendingId 為 key 的 `TaskCompletionSource`（`ChatApprovalRegistry`，記憶體內單例，與既有 `ChatTurnRegistry` 同一種單機假設），逾時 3 分鐘。
- 等待期間每 20 秒送一行 SSE 註解 `: ping`（Cloudflare 100 秒、App Service 230 秒的閒置斷線）；既有前端 parser 本來就忽略非 `data:` 行。
- `POST api/chatbot/conversations/{id}/approvals/{pendingId}`，body `{ decisions: [{ toolUseId, approve: bool }] }`：只接受同一使用者，完成那個 TCS，回 `{ accepted: true }`。核准的執行，拒絕的回「使用者拒絕了這個動作」的工具結果，模型接著回答——仍在**原本的** SSE 串流裡。
- 逾時或使用者按「停止」：視為全部拒絕並結束（`TurnVerdict` 照「使用者中斷」處理）。讀者斷線：取消。
- 好處：不用新表、不用把 SDK 的 block（含 thinking signature）序列化到 DB、輪數與計費自然延續、編輯／刪除對話時沒有懸掛狀態。代價：重新整理頁面就失去那個確認（可接受，v1）。
- 「以後都允許」（remember）列為後續；v1 使用者在 connector 頁調權限。

### 1.4 儲存

- `ChatConversations` 新增 `Profile`（string(40)，預設 `'finance'`，既有資料自然屬於 Finance）。列表 API 加 `profile` 查詢參數（預設 finance → 舊前端行為不變）；`ResolveConversationAsync`、讀訊息、刪除都檢查 profile 相符（不符當 NotFound），避免 studio 的對話用 Finance 的 prompt 續聊。
- `ChatMessages` 新增 `CardsJson`（nullable）：這則 assistant 訊息裡顯示過的卡片（工具結果的 card、確認結果），重新打開對話時畫得出來。歷史回送給模型時仍只送文字（與現在相同）。
- 都是新增欄位＋預設值的 migration，對 production 安全；與 `ConnectorInstallations` 合成**一個** migration（手寫 Designer 與 snapshot，每錯一次就是一輪 CI）。
- 歷史只送文字的限制：圖表卡片自帶它的 summarize plan，卡片上的「加到 dashboard」直接開既有 `AddToDashboardDialog`，不必靠模型記得上一輪的計畫。

### 1.5 SSE 協定（向下相容，只新增事件）

| 事件 | 欄位 | 何時 |
|---|---|---|
| `start` | conversationId, userMessageId | 不變 |
| `delta` | text | 不變 |
| `tool_use` | name（+ 新增 id, connector, displayName） | hosted 與 client 工具開始 |
| `tool_result` | id, name, ok, card?, durationMs | client 工具完成（新） |
| `approval_required` | pendingId, calls[{toolUseId, connector, tool, displayName, effect, input}] | 需要確認（新），串流**不結束**，等決定 |
| `approval_resolved` | pendingId, decisions | 決定送達後（新） |
| `done` | 不變：每一輪成功結束一定送 | |
| `error` | code | 不變 |

舊前端遇到不認得的事件會忽略（現在就是這樣），所以 Finance 不受影響。

## 2. 前端：`components/aiChat`

### 2.1 搬家與參數化

- `Finance/components/aiAssistant/` 的 panel、hooks、utils 搬到 `src/components/aiChat/`，依 client CLAUDE.md 的「元件長大就變資料夾、狀態抽 hook」。Finance 留一個薄 wrapper（`Finance/components/aiAssistant/index.tsx` re-export + 設定），Finance 測試只允許改 import 路徑。
- `AiChatConfig`：
  ```ts
  interface AiChatConfig {
    profile: 'finance' | 'data-analysis' | 'studio';
    title: string; greetingTitle: string;
    examples: string[]; greetingSuggestions: Suggestion[];
    accent: string;                           // 由 app 顏色來
    refusedMessage?: string;                  // 取代寫死的「台股」文案
    getPageContext?: () => unknown;           // 送給後端的 page context
  }
  ```
- `useChatStreaming` 修正跨 chunk 半行的 bug（先寫測試重現），並把事件解析抽成純函式 `parseSseChunk(buffer, chunk) → { events, rest }`（有測試）。**這是工具卡片的前提**（100 KB 的卡片一定跨 chunk），階段 1 第一個前端任務。
- 寫死的台股文案要參數化：`CompactChatBar.tsx`、`MessageArea.tsx`、`ChatHeader.tsx`（「台股資料已連線」）、`ChatInput.tsx`；工具進度改用 `displayName`（不然會印出 `c3__search`）。
- 新增：`ToolActivity`（工具進度列）、`ToolCard` renderer 註冊表（`table`、`chart`、`word`、`portfolio`、`dashboard`、`link`）、`ApprovalCard`（允許／拒絕；「一律允許」列為延伸）。

### 2.2 三個嵌入點

| 位置 | 元件 | 行為 |
|---|---|---|
| `/finance` 最上方 | `AiAssistantLauncher`（現有） | 不變 |
| `/data-analysis` 最上方 | 同一個 Launcher，`profile: 'data-analysis'` | 一行框 → 展開對話；下方「問 AI」保留 |
| 全站 | `StudioAssistant`：右下角按鈕 + ⌘K／Ctrl+K | 右側抽屜（手機全螢幕），profile `studio`，帶目前路由 |

## 3. 不退化的保證

1. **先**單獨 commit 一個 characterization test：把 Finance 的 `BuildRequest` 序列化成 JSON，逐項斷言 model、max_tokens、effort、betas、mcp_servers、tools（種類、defer 設定、always-loaded 清單）、system 兩段與 cache_control。（本機不能跑 dotnet，產不出整檔快照，所以用逐項斷言。）CI 綠了才開始重構。
2. 伺服器既有 chat 測試的**斷言不改**；但 `AnthropicChatServiceTests`、`AnthropicChatStreamTests`、`MarketBackgroundTests` 直接呼叫 `AnthropicChatService.BuildRequest`／`SystemPromptFor`／`AlwaysLoadedTools`、用位置參數建構 service，搬到 profile 後要**機械式改指向**（類別名、using、建構子），並在 PROGRESS 註明「只改指向」。前端 aiAssistant 的測試同理：只允許改 import 路徑。
3. Finance profile 不加任何新工具或成本。
