# NEXT — 下一輪從最上面開始

> 設計已定稿（PLAN.md）。M1 進行中（目標 10/27）。

## 0. 每輪固定動作

- **開工時與收尾前都同步 master**：`git fetch origin master` → 有新 commit 就 `git merge origin/master`（**不用 rebase**，D-014）。
- **先看 CI 是否恢復**：`gh run list -R twjackysu/JackyAIApp --branch feature/ai-native --limit 3`。若最新一次仍是 failure，用 check-runs annotation 看原因；若還是「recent account payments have failed…」就是帳單問題（FOR_JACKY 第 0 條），不是程式碼問題（D-016）。可以 `gh run rerun <id>` 試一次。

## 1. CI 恢復後第一件事：驗證尚未經 CI 的後端 commit

- `6a75d3a refactor(chat): 共用 chat 引擎的 profile 抽象（M1-5）`——唯一的後端變更。最可能出錯的地方：`IAnthropicChatService.cs` 的 `<see cref="Assistant.IChatProfile.Key"/>`（若 CS1574 就改成全名）、`ChatTurnRequest.Build` 回傳的 `MessageCreateParams` 初始化、`ModuleBoundariesTests`（新 module Assistant、TableOwners）、`AnthropicChatStreamTests` 的 `ChatProfiles([...])` 建構。紅了就照 annotation 修；修不好用 `git revert`。
- 只要 CI 還沒恢復，就**不要**再疊大型後端變更（D-016）。

## 2. 前端（本機可完整驗證，CI 停擺時優先）

1. **/connectors v2 介面**（設計 05 §3）：在現有 `Connectors/index.tsx` 上加「已連線／目錄／進階」三區、詳情抽屜（工具表用 `ToolPermissionToggle`）、「新增 MCP server」三步對話框。API 還沒有：先定義 `apis/connectorsApis` 的新型別與 endpoint（`GET api/connectors/catalog`、`GET/POST/DELETE api/connectors/installations`、`POST .../probe`、`POST .../test`、`PUT .../tools/{name}`，照設計 03），UI 對假資料寫測試；後端好了再接。現有 Google/Microsoft/Atlassian 卡片與「複製 token」行為不能變（`ConnectorCard`）。
2. **Data Analysis 的 AI 對話前端**：`DataAnalysis/aiChatConfig.ts`（profile `data-analysis`、問候語、範例：「上個月各地區營收？」「畫出近 12 個月趨勢」「把這張圖存成 dashboard widget」），以及在 `/data-analysis` 最上方放 `AiAssistantLauncher`。**等後端 profile（M1-6 + data-analysis profile）完成才掛上頁面**，否則會用 Finance 的 prompt 回答。
3. `useChatStreaming` 送 `profile`（`config.profile`）：要同時改 `index.test.tsx` 斷言 chat body 的那幾個測試（那是 M1-6 的 API 變更，不是退化，PROGRESS 註明）。
4. 小整理：`ToolEffect` 在 `components/aiChat/utils/approval`（經 `ApprovalCallEvent`）與 `Connectors/toolPermission` 各有一份，可以合成一個共用型別（例如 `apis/connectorsApis/types.ts`）。
5. 工具卡片（`tool_result.card`）的 renderer 註冊表（table／chart／link），設計 04 §2.1；chart 用 `DataAnalysis/viz`。

## 3. 後端（CI 恢復後）

1. **M1-6 Migration（一個）**：`ChatConversations.Profile`（nvarchar(40)，not null，default `'finance'`）、`ChatMessages.CardsJson`（nvarchar(max) null）、`ConnectorInstallations`（欄位見 design/03 §3；filtered unique `(UserId, DefinitionKey) WHERE DefinitionKey <> 'custom'`）。參考 `Data/Migrations/20261022111134_SplitStockClassification*` 手寫 migration + Designer（= 新 snapshot 全文）+ 更新 `AzureSQLDBContextModelSnapshot.cs`。對話端點（list/get/delete/resolve）加 profile 檢查（不符 = NotFound），`DTO.ChatRequest` 加 `Profile`（預設 finance，controller 用 `IChatProfiles.Find` 驗證，找不到 400）。
2. **M2 後端**：工具框架（`IAiTool`、`AiToolDescriptor`、`ToolEffect`）、`ModelTurn` 的 client tool loop（`BetaMessageContentAggregator`，spike 已證明可行）、SSE `tool_result`／`approval_required`／`approval_resolved`（前端已就緒，事件欄位見 `hooks/useChatStreaming.ts` 的 `ToolEvent`／`ApprovalEvent`）、`ChatApprovalRegistry` + `POST api/chatbot/conversations/{id}/approvals/{pendingId}`（body `{ decisions: [{ toolUseId, approve }] }`，前端 `useChatConversation.decide` 已在送）、Data Analysis profile 與 `ask_data` 工具（D-007）、connector 框架（None/ApiKey/Bearer）走 `SafeHttp`。

## 4. 已經先做好的 M2 零件（直接用）

- 後端：`Services/Connectors/SafeHttp.cs`（具名 HttpClient，逾時 15 秒，不掛重試）、`OutboundAddressPolicy`、`McpServerUrl.Normalize`、`ToolNames.ForModel`。
- 前端：`ToolActivity`、`ApprovalCard`、`useChatConversation.decide`、`ToolPermissionToggle`、`AiChatConfig`。

## 5. 之後

依 `design/07-implementation-plan.md`：M2 垂直切片目標 10/29（CI 停擺可能讓後端延後，前端先行）。
