# NEXT — 下一輪從最上面開始

> 設計已定稿（PLAN.md）。M1 進行中（目標 10/27），M2 前端大致就緒，後端被 CI 停擺卡住。

## 0. 每輪固定動作

- **開工時與收尾前都同步 master**：`git fetch origin master` → 有新 commit 就 `git merge origin/master`（**不用 rebase**，D-014）。
- **先看 CI 是否恢復**：`gh run list -R twjackysu/JackyAIApp --branch feature/ai-native --limit 3`。若最新一次仍是 failure，用 check-runs annotation 看原因；若還是「recent account payments have failed…」就是帳單問題（FOR_JACKY 第 0 條），不是程式碼問題（D-016）。可以 `gh run rerun <id>` 試一次。第 3 輪 rerun 仍失敗。
- 畫面驗證：scratchpad 的 playwright 腳本做法——`npm run build` 後用 `page.route` 直接從 `dist/` 回檔案、`/api/*` 回假資料（`/api/account/info` 要回 `{ data: { id, name, isAdmin, drawdownBrakeEnabled } }` 才進得了 RequireAuth 頁面），不用起任何 server。chat 的 SSE 可以用 `route.fulfill({ contentType: 'text/event-stream', body: 'data: {...}\n\n…' })` 模擬。第 3 輪用這招看過 /connectors（桌機、手機）與 chat 卡片。

## 1. CI 恢復後第一件事：驗證尚未經 CI 的後端 commit

- `6a75d3a refactor(chat): 共用 chat 引擎的 profile 抽象（M1-5）`——唯一的後端變更（第 3 輪沒有新的後端 commit）。最可能出錯的地方：`IAnthropicChatService.cs` 的 `<see cref="Assistant.IChatProfile.Key"/>`（若 CS1574 就改成全名）、`ChatTurnRequest.Build` 回傳的 `MessageCreateParams` 初始化、`ModuleBoundariesTests`（新 module Assistant、TableOwners）、`AnthropicChatStreamTests` 的 `ChatProfiles([...])` 建構。紅了就照 annotation 修；修不好用 `git revert`。
- 只要 CI 還沒恢復，就**不要**再疊大型後端變更（D-016）。

## 2. 後端（CI 恢復後，依序）

1. **M1-6 Migration（一個）**：`ChatConversations.Profile`（nvarchar(40)，not null，default `'finance'`）、`ChatMessages.CardsJson`（nvarchar(max) null）、`ConnectorInstallations`（欄位見 design/03 §3；filtered unique `(UserId, DefinitionKey) WHERE DefinitionKey <> 'custom'`）。參考 `Data/Migrations/20261022111134_SplitStockClassification*` 手寫 migration + Designer（= 新 snapshot 全文）+ 更新 `AzureSQLDBContextModelSnapshot.cs`。`DTO.ChatRequest` 加 `Profile`（**前端已在送**，預設 finance；controller 用 `IChatProfiles.Find` 驗證，找不到 400）；對話端點（list/get/delete/resolve）加 profile 檢查（不符 = NotFound）。
2. **Connector 端點**（前端已完成，契約見 design/03 §7、`jackyaiapp.client/src/apis/connectorsApis/types.ts`，D-018）：`GET catalog`、`POST installations/probe`、`POST installations`、`POST installations/{id}/authorize`、`POST installations/{id}/test`、`PUT installations/{id}/enabled`、`DELETE installations/{id}`、`PUT tool-permissions`。回 `ServiceResult` → `{ data }`。先做 None／ApiKey／Bearer（走 `SafeHttp`、`McpServerUrl.Normalize`、`OutboundAddressPolicy`）；OAuth 端點可以先回「尚未支援」(M3)。每個 DTO 欄位名稱要跟前端型別一致（camelCase）。
3. **M2 agent loop**：工具框架（`IAiTool`、`AiToolDescriptor`、`ToolEffect`）、`ModelTurn` 的 client tool loop（`BetaMessageContentAggregator`，spike 已證明可行）、SSE `tool_result`（含 `card`，格式見 design/04 §1.5 表格與 D-019；後端也要守 12 欄／200 列、連結只允許本站路徑或 https）／`approval_required`／`approval_resolved`、`ChatApprovalRegistry` + `POST api/chatbot/conversations/{id}/approvals/{pendingId}`（body `{ decisions: [{ toolUseId, approve }] }`）、Data Analysis profile（key `data-analysis`）與 `ask_data` 工具（D-007；回 `chart` 卡片：`{ kind: 'chart', title, data: { viz, headers, rows, groupHints, temporalHints, plan } }`，見 `DataAnalysis/assistant/chartCard.ts`）。

## 3. 前端（本機可完整驗證，CI 停擺時優先）

1. **掛上 Data Analysis 的 AI 對話**：`DataAnalysis/aiChatConfig.ts`（`DATA_ANALYSIS_AI_CHAT`，含 chart renderer）已就緒；在 `/data-analysis` 最上方放 `<AiAssistantLauncher config={DATA_ANALYSIS_AI_CHAT} …/>`。**等後端 data-analysis profile 完成才掛**，否則會用 Finance 的 prompt 回答。page context（目前選的資料源）怎麼帶給後端還沒定：建議 `ChatStreamingOptions` 加 `context?: Record<string, string>`（例如 `{ sourceId }`），一起寫進 design/04。
2. **Studio AI（全站助理）前端外殼**（設計 05 §1）：右下角按鈕 + ⌘K、桌機右側抽屜／手機全螢幕，內容用 `AiAssistantPanel`（可能需要讓 panel 支援「抽屜」版面，不要複製一份）。profile `studio`，等後端 profile 再掛到 `App.tsx`。
3. 後端 catalog 端點完成後：把 `CONNECTORS` 放回 `apps.tsx` 的 `portalDestinations`（D-011、D-020）。

## 4. 已經先做好的零件（直接用）

- 後端：`Services/Connectors/SafeHttp.cs`（具名 HttpClient，逾時 15 秒，不掛重試）、`OutboundAddressPolicy`、`McpServerUrl.Normalize`、`ToolNames.ForModel`、`IChatProfile`／`ChatTurnRequest`（未經 CI）。
- 前端：`ToolActivity`、`ApprovalCard`、`useChatConversation.decide`、`ToolPermissionToggle`、`AiChatConfig`（含 `profile` 已送出、`cardRenderers`）、工具卡片 `components/aiChat/cards`（table、link）、Data Analysis `chart` 卡片、/connectors v2 全部 UI（`AiConnectorsSection` 等）。

## 5. 之後

依 `design/07-implementation-plan.md`：M2 垂直切片目標 10/29。CI 停擺若持續到 10/27，考慮把 M3（MCP OAuth）的後端順延，先把 M2 的後端一次做紮實（每一步都自我審查），恢復後一次修。
