# NEXT — 下一輪從最上面開始

> 設計已定稿（PLAN.md）。現在在 **M1 引擎與共用元件**（目標 10/27）。

## 0. 每輪固定動作（Jacky 2026-10-24 要求）

- **開工時與收尾前都同步 master**：`git fetch origin master` → 有新 commit 就 `git merge origin/master`（**不用 rebase**：branch 已 push，禁止 force push 與改寫歷史；見 D-014）。有其他 agent 同時在 master 開發，所以要常同步，衝突在這裡解決。合併後要等 CI 綠。

## 1. M1-5 共用 chat 引擎：profile 抽象 + 模組重整（server，最大的一塊）

已完成的前置：`FinanceChatRequestTests`（逐欄位鎖住 Finance request，CI 綠）、`ClientToolRoundTripTests`（SDK 往返可行）、`Services.Connectors` 模組與 `TwStockMcp`。

依序做，**每一步都要讓 `FinanceChatRequestTests`、`AnthropicChatServiceTests`、`AnthropicChatStreamTests`、`MarketBackgroundTests` 保持綠**（斷言不改，只允許機械式改指向，D-006）：

1. `ModuleBoundariesTests`：新增 module `Assistant` = `Services.Chatbot` + `Services.Assistant`，MayUse `Connectors, Platform`；Finance MayUse 加 `Assistant`；DataAnalysis MayUse 加 `Assistant, Connectors`；English/Portfolio 加 `Connectors`。`TableOwners` 的 `ChatConversations`/`ChatMessages`/`ChatAttachments` 改歸 `Assistant`。server `CLAUDE.md` 模組表同步。
2. `MarketBackground.cs`（用到 `Services.Finance`、`DTO.Finance`，所以不能留在 Assistant）搬到 `Services/Finance/Assistant/MarketBackground.cs`，namespace `Services.Finance.Assistant`。`MarketBackgroundTests` 改 using。
3. 新 `Services/Assistant/IChatProfile.cs`：
   ```csharp
   public interface IChatProfile { string Key { get; } string UsageLogFeature { get; }
       Task<ChatProfileSetup> SetupAsync(DateOnly taipeiToday, CancellationToken ct); }
   public sealed record ChatProfileSetup(string SystemPrompt, string? CachedBackground, bool AttachTwStock);
   ```
   （先只放 Finance 需要的；工具相關欄位 M2 再加。）`IChatProfiles`（DI 拿 `IEnumerable<IChatProfile>`，用 key 查）。
4. `Services/Assistant/ChatRequest.cs`：`static MessageCreateParams Build(ChatProfileSetup setup, List<BetaMessageParam> messages)`——把 `AnthropicChatService.BuildRequest` 的本體搬過來，model/effort/max_tokens 不變；`AttachTwStock` 為真時加 `TwStockMcp` 的 server、beta、regex tool search、toolset。
5. `Services/Finance/Assistant/FinanceChatProfile.cs`：`Key = "finance"`、`UsageLogFeature = "chatbot.tw_stock"`、`SystemPromptFor(DateOnly)`（從 AnthropicChatService 原文搬來）、`SetupAsync` 讀 `IMarketBackground`（失敗回 null 並 log，原 `MarketBackgroundAsync` 的行為）。
6. `AnthropicChatService`：建構子把 `IMarketBackground` 換成 `IChatProfiles`；`StreamChatAsync` 多一個 `string profileKey` 參數（controller 先傳 `"finance"`）；`ReportTurnAsync` 用 `profile.UsageLogFeature`。為了讓測試只改指向，保留 `internal static MessageCreateParams BuildRequest(DateOnly, List<BetaMessageParam>, string? background = null)` 作為 **Finance 的 facade**？——不行，Assistant 不能引用 Finance。改法：測試改呼叫 `ChatRequest.Build(new ChatProfileSetup(FinanceChatProfile.SystemPromptFor(d), background, AttachTwStock: true), messages)`；`AnthropicChatService.AlwaysLoadedTools` 改 `TwStockMcp.AlwaysLoadedTools`；`SystemPromptFor` 改 `FinanceChatProfile.SystemPromptFor`。`AnthropicChatStreamTests` 的 `Service()` 改傳 profiles（`new FinanceChatProfile(_background.Object, ...)`）。
7. DI：`services.AddScoped<IChatProfile, FinanceChatProfile>()`（Finance 的註冊處）、`IChatProfiles`。
8. push、等 CI；紅了看 annotation 修。

## 2. M1-6 Migration（一個）

`ChatConversations.Profile`（nvarchar(40)，not null，default `'finance'`）、`ChatMessages.CardsJson`（nvarchar(max) null）、`ConnectorInstallations`（欄位見 design/03 §3；filtered unique `(UserId, DefinitionKey) WHERE DefinitionKey <> 'custom'`）。參考 `Data/Migrations/20260913120000_AddUniqueIndexOnWordText*` 手寫 migration + Designer + 更新 `AzureSQLDBContextModelSnapshot.cs`。對話端點（list/get/delete/resolve）加 profile 檢查（不符 = NotFound）。

## 3. M1-7 前端 config 化（client，本機可完整驗證）

`components/aiChat` 已從 `Finance/components/aiAssistant` 搬好（本輪，純搬家）。接著：
- `AiChatConfig`（design/04 §2.1），Finance 的設定放 `Finance/aiChatConfig.ts`，Launcher/Panel 透過 props 或 context 拿 config。
- 寫死的台股文案參數化：`CompactChatBar.tsx`、`MessageArea.tsx`（`GREETING_TITLE`）、`ChatHeader.tsx`（「台股資料已連線」）、`ChatInput.tsx`、`examples.ts`、`chatErrors.ts` 的 `refused`。
- `useChatStreaming` 的 `/api/chatbot/chat` body 加 `profile`（後端 M1-5 完成後才送）。
- 既有測試斷言不改。

## 4. 之後

依 `design/07-implementation-plan.md`：M2 垂直切片（工具框架、同請求確認、Data Analysis 工具與對話、connector 框架、/connectors v2），目標 10/29。
