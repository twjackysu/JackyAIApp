# NEXT — 下一輪從最上面開始

> 設計已定稿（PLAN.md）。M1 大致完成；M2 垂直切片（Data Analysis 對話）後端與前端都已寫好、已掛上頁面，**但所有後端 commit 都還沒經過 CI**（GitHub Actions 帳單停擺，FOR_JACKY 0）。

## 0. 每輪固定動作

- **開工時與收尾前都同步 master**：`git fetch origin master` → 有新 commit 就 `git merge origin/master`（**不用 rebase**，D-014）。
- **先看 CI 是否恢復**：`gh run list -R twjackysu/JackyAIApp --branch feature/ai-native --limit 3`。仍是「recent account payments have failed…」就是帳單問題（D-016），不是程式碼問題。
- **後端改動一律先跑離線型別檢查**（D-021）：`sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0`（約 1 分鐘），然後 `CSCHECK_OUT=/tmp/cscheck .loop/cscheck/run.sh`（約 1 分鐘；第一次會 clone Anthropic SDK、xunit、Moq、EF Core 原始碼）。輸出只列「比 master 新增的診斷」；`*.Designer.cs` 的 `CS0616/CS0653/CS1503` 與 EF 擴充方法（`AsNoTracking` 等）的 `CS1061` 是已知雜訊；「找不到的名稱」若只有第三方型別（DataAnalysisLibrary、AggregationType、Betalgo…）也是雜訊。其他任何一行都要修好才 push。
- 畫面驗證：`npm run build` 後用 playwright `page.route` 從 `dist/` 回檔案、`/api/*` 回假資料（`/api/account/info` 要回 `{ data: { id, name, isAdmin, drawdownBrakeEnabled } }`），chat 的 SSE 用 `route.fulfill({ contentType: 'text/event-stream', body })` 模擬。

## 1. CI 恢復後第一件事：讓這些尚未經 CI 的後端 commit 變綠

依序（全部 cscheck 0 新診斷，但測試從沒跑過）：
1. `6a75d3a` 共用 chat 引擎的 profile 抽象（M1-5）。
2. `d1c413e` agent loop（`IAiTool`、`ClientToolRunner`、`ModelTurn` 多輪）。最可能出錯：`AnthropicChatStreamTests` 新的 6 個 agent loop 案例（SSE 事件順序、第二個請求的 messages 形狀、`is_error` 欄位名、token 加總 300/70）、`ChatProfileTests` 的 tools 序列化（client tool 沒有 `type` 欄位的假設）。
3. `7e8948d` migration `AddChatProfileAndCards`（`ModelMatchesMigrationsTests` 會驗證手寫的 Designer／snapshot）＋ profile 檢查。
4. `f643aae` page context（`ChatProfileContext`、`ChatRequest.Context`、`ChatbotController.PageContext`）＋ `DataAnalysisChatProfile`／`AskDataTool`（`DataAnalysisAssistantTests`、`ChatbotPageContextTests`）。也要看 `ModuleBoundariesTests`（`Services.DataAnalysis.Assistant` 用 `Services.Assistant`，表上允許）。
5. `0a26878` `DescribeDataTool`（`IDataAnalysisQueryService`／`ISessionDataStore`；`FieldListResult.Fields` 的 `CanonicalType.ToString()` 照 NlSummarizeService 的寫法）。
6. `d02b0ba` 卡片存進 `ChatMessages.CardsJson`、`ChatMessageDto.Cards`（`GetMessagesAsync` 改成先投影成匿名型別再補 Cards；`ChatConversationServiceTests.Messages_BringBackTheCardsAnAnswerShowed`）。
- 紅了照 annotation 修；真的修不好用 `git revert`（不要 reset）。

## 2. 後端（依序；CI 停擺也可以做，但每步都要 cscheck）

1. **確認流程（human-in-the-loop，design/04 §1.3，D-004、D-023）**：`ChatApprovalRegistry`（單例，pendingId → TCS，3 分鐘逾時，20 秒 `: ping`）、`ClientToolRunner` 遇到非 Read 工具送 `approval_required { pendingId, calls: [{ toolUseId, connector, tool, displayName, effect, input }] }` 並等待、`POST api/chatbot/conversations/{id}/approvals/{pendingId}`（body `{ decisions: [{ toolUseId, approve }] }`，只接受同一使用者）、`approval_resolved`。前端 `ApprovalCard`／`useChatConversation.decide` 已就緒（看 `components/aiChat/utils/approval.ts` 的事件型別，欄位要一致）。
2. **後端守卡片上限**（D-019：table 12 欄／200 列、連結只允許本站路徑或 https；chart 20 欄／500 列）：目前只有前端 `parseToolCard` 過濾，`AskDataTool` 自己限 500 列；MCP 工具接進來前要在 `ClientToolRunner` 統一檢查。
3. **Connector 端點**（前端已完成，契約見 design/03 §7、`apis/connectorsApis/types.ts`，D-018）＋ `ConnectorInstallations` migration（D-022，欄位見 design/03 §3；filtered unique `(UserId, DefinitionKey) WHERE DefinitionKey <> 'custom'`）。先 None／ApiKey／Bearer（`SafeHttp`、`McpServerUrl.Normalize`、`OutboundAddressPolicy`），OAuth 端點先回「尚未支援」(M3)。MCP 工具經 `IAiTool` 介面接進 `ChatProfileSetup.Tools`（名稱用 `ToolNames.ForModel`）。`ModelContextProtocol.Core` 沒有原始碼在 cscheck 裡：要的話把 `https://github.com/modelcontextprotocol/csharp-sdk`（tag v1.3.0 的 `src/ModelContextProtocol.Core`）加進 `run.sh` 的 CSCHECK_REFS。

## 3. 前端

1. **用 playwright 看 /data-analysis 的 AI 對話**（本輪已掛上：`DataAnalysis/index.tsx` 的 `AiAssistantLauncher`，`pageContext` 由 `utils/aiPageContext.ts`）：模擬 SSE `tool_use`（id、displayName）→ `tool_result`（card kind chart）→ delta → done，確認圖表卡片、「加到 dashboard」、手機版面。
2. **Studio AI（全站助理）前端外殼**（design/05 §1）：右下角按鈕 + ⌘K、桌機右側抽屜／手機全螢幕，內容用 `AiAssistantPanel`。profile `studio`，等後端 profile 再掛到 `App.tsx`。
3. 後端 catalog 端點完成後：把 `CONNECTORS` 放回 `apps.tsx` 的 `portalDestinations`（D-011、D-020）。

## 4. 已經做好的零件

- 後端：`SafeHttp`、`OutboundAddressPolicy`、`McpServerUrl.Normalize`、`ToolNames.ForModel`、`IChatProfile`／`ChatProfileContext`／`ChatTurnRequest`、`IAiTool`／`ClientToolRunner`／agent loop、`DataAnalysisChatProfile`／`AskDataTool`、對話 profile 欄位。
- 前端：`ToolActivity`、`ApprovalCard`、`useChatConversation.decide`、`ToolPermissionToggle`、`AiChatConfig`（`profile`、`pageContext`、`cardRenderers`）、工具卡片（table、link、Data Analysis 的 chart）、/connectors v2 全部 UI、/data-analysis 的 AI 對話。

## 5. 之後

依 `design/07-implementation-plan.md`：M2 垂直切片目標 10/29（後端程式碼已大致到位，差確認流程、卡片儲存、connector）。M3：⌘K Studio AI、English／Portfolio 工具、MCP OAuth。
