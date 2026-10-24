# PROGRESS（新的在最上面）

## 2026-10-24 11:42 – 第 5 輪（確認流程、connector 後端、自己的 MCP server、Studio AI）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠。
- **同請求確認（human-in-the-loop）**：`ChatApprovalRegistry`、`approval_required`／`approval_resolved`、`: ping`、`POST conversations/{id}/approvals/{pendingId}`；拒絕／逾時回報模型「沒有執行」，按停止整輪結束（D-026）。後端 `ToolCardGuard` 守卡片上限。CI 40059904526 綠。
- **Connector 後端**：migration `AddConnectorInstallations`（新表，手寫 Designer＋snapshot，CI 的 `ModelMatchesMigrationsTests` 過）、`IAiTool` 搬到 Connectors（D-027）、`ToolPermissions`、`SiteToolSet`、`ConnectorCatalogService`＋`AiConnectorsController`（catalog、tool-permissions、enabled、delete）；chat 每輪照使用者設定（停用不送、需確認才問）。CI 40060529705 綠。
- **自己的 MCP server**：`McpToolClient`（官方 SDK、Streamable HTTP、一律 SafeHttp，D-029）、`ConnectorInstallService`（probe、新增：免認證／API key／Bearer，憑證加密；test）、`ConnectorToolSource`（工具名 `m1__…`）接進 Data Analysis 助理。用 HttpMessageHandler 扮 MCP server 跑真 SDK 的測試在 CI 通過（40061092086）。之後加 taint（讀過外部結果後寫入一律先問，D-031）、目錄的內建 MCP（tw-stock、DeepWiki，D-033）。
- **Studio AI**：後端 `studio` profile（頁面清單、目前頁面、`open_page`）、English 的 `look_up_word`／`add_word_to_repository`（寫入 → 確認卡，`ISiteTool`）；前端 `components/StudioAssistant`（右下角按鈕＋⌘K／Ctrl+K、右側抽屜／手機全螢幕、`AiAssistantPanel variant="sheet"`）。Connectors 回到首頁 portal。
- playwright（`dist/`＋假 API＋模擬 SSE）截圖：Studio 回答與連結卡（桌機、手機）、確認卡、/connectors 有自訂 MCP 時，存到 `screenshots/`。
- `CONTRIBUTING-CONNECTORS.md` 完成（三種貢獻方式、程式樣板、測試樣板、安全清單）；三份 CLAUDE.md 更新。
- 驗證：每個後端 commit 先跑 cscheck（只剩已知雜訊；MCP SDK 原始碼已加入 cscheck），再看 CI；前端 lint／test（1260+）／build 全過。CI：40059904526、40060529705、40061092086、40061855784、40062208900、40062408658 綠。
- 問題：`7d5a813` 的 CI 前端測試紅一次——/data-analysis 頁面測試在登入狀態下 launcher 預先載入面板 chunk，測試結束後 import 才完成（EnvironmentTeardownError）；照 /finance 測試 mock 面板修好（`5702005`）。

## 2026-10-24 09:41 – 第 4 輪（離線型別檢查、agent loop、對話 profile、Data Analysis 助理）

- 開工：master 沒有新 commit；CI 仍因帳單停擺（rerun 40045469489 同樣「recent account payments have failed」）。
- **`.loop/cscheck`（D-021）**：NuGet 與 CI 都不能用，改用 apt 的 dotnet-sdk-10.0 內附的 Roslyn 直接編譯 server／tests，Anthropic SDK、xunit assert、Moq、EF Core 用 GitHub 上的原始碼當參考編譯，與 master 比較只列新診斷（約 1 分鐘）。驗證過會抓到故意放的型別錯誤；上一輪未經 CI 的 `6a75d3a` 檢查結果 0 個新診斷。
- **agent loop（M2 核心，`d1c413e`）**：`IAiTool`／`AiToolDescriptor`／`ToolEffect`、`ChatProfileSetup.Tools`、`ChatTurnRequest` 加 client tools 與「工具結果是不可信資料」提示、`ModelTurn` 用 `BetaMessageContentAggregator` 多輪（最多 8 次請求）、`ClientToolRunner`（逐一執行、30 秒逾時、SSE `tool_result`、`<tool_output trust="untrusted">` 包裝且不能被提前關閉）。寫入型工具在確認流程完成前不執行（D-023）。Finance 沒有工具，路徑不變。
- **對話屬於 profile（`7e8948d`）**：migration `AddChatProfileAndCards`（只新增 `ChatConversations.Profile`、`ChatMessages.CardsJson`，手寫 Designer＝新 snapshot），`ChatRequest.Profile`（未知 → 收費前 400）、列表 `?profile=`、不同 profile 的對話不續聊不 rewind；前端列表帶 profile。D-022：connector 表另一個 migration。
- **page context ＋ Data Analysis 助理**：`ChatProfileContext`（UserId、台北日期、Page）、`ChatRequest.Context`（最多 8 個短項目）、`DataAnalysisChatProfile`（key `data-analysis`）與 `AskDataTool`（包既有 問 AI 管線，照 NlSummarize 收費，回 chart 卡片，plan 帶頁面的資料源）。前端 `AiChatConfig.pageContext`、`/data-analysis` 最上方掛上 `AiAssistantLauncher`，歡迎建議改成 ask_data 答得出來的問題（D-024）。
- **`describe_data` 工具**（欄位、型別、日期範圍，不收費）、**卡片存進 `CardsJson`**（重新打開對話時畫出來，前端 `Message.cards` 經 `parseToolCard` 過濾）、AI chat 底部免責說明改由 app 設定（Data Analysis 不再顯示「不構成投資建議」）。
- playwright（`dist/` + 假 API + 模擬 SSE）截圖確認 /data-analysis 的對話、圖表卡片、手機版面，存到 `docs/ai-native/screenshots/`。
- server CLAUDE.md 的「AI assistant (chat)」與「Data analysis」補上 agent loop、profile、page context、ask_data、describe_data、卡片儲存。
- 驗證：每個後端 commit cscheck 0 個新診斷（只剩已知雜訊）；前端 lint、test（1249）、build 全過。CI 無法驗證（帳單），所有後端測試都還沒實際跑過（NEXT 第 1 節列了清單與最可能出錯的地方）。
- **CI 在 10:14 左右恢復**（不知道誰處理的，FOR_JACKY 0 已劃掉）：`7e8948d`（含 agent loop、migration、profile）CI 綠；`f643aae`／`0a26878` 的 run 40055177229 只有一個測試紅——`SessionUploadLimitsTests` 單獨註冊 DataAnalysis 服務做 DI 驗證，少了 `IUsageLimitService`（`c595fe4` 補 mock）。cscheck 抓不到這類執行期 DI 問題。
- 收尾合併 master（`5dc1160` README 精簡），merge `9c450c4`：**CI run 40055896595 綠**（server build＋全部測試、client）——本輪與上一輪所有後端變更都已由 CI 驗證。
- 問題：停擺期間無法驗證是主要風險，已解除。

## 2026-10-24 07:41 – 第 3 輪（/connectors v2 前端）

- 開工：master 沒有新 commit；branch CI 仍因帳單停擺（rerun 40033649446 一樣「recent account payments have failed」，D-016），所以本輪只做本機可完整驗證的前端。
- **/connectors v2**（設計 05 §3）：先補 `Connectors/index.test.tsx` 鎖住現有頁面行為（帳號卡片、連線導向、中斷、OAuth 回呼訊息），再演進：
  - `AiConnectorsSection`：「AI 可以使用的」（`InstalledSection`，健康狀態 chip、工具權限計數）、「目錄」（`CatalogSection`，分類 chip、一鍵連線／填 key／前往授權）、「新增 MCP server」。目錄載入失敗只顯示一行說明，下方帳號連線照常（後端還沒做時、或合併後端點壞掉時都不會拖垮頁面）。
  - `ConnectorDetailDrawer`：網址、登入方式、scope、最後測試、測試連線／重新授權／停用／移除（二次確認），工具表用 `ToolPermissionToggle`；本站工具（alwaysOn）只能調權限。
  - `AddMcpServerDialog`：網址 → 伺服器偵測（probe）→ 認證（none／bearer／API key／OAuth，含手填 client）→ 列出工具與權限可直接調。目錄中要 key 的項目直接從認證步驟開始。
  - API 契約：`apis/connectorsApis` 新增 8 個 endpoint 與型別，定稿寫進設計 03 §7（新增 `installations/probe`、`tool-permissions`；`installationId: null` 代表沒有列的內建 connector）。
  - `ToolEffect`／`ToolPermission` 改由 API 型別提供（`toolPermission.ts` 轉出、`useChatStreaming` 的 `ToolEvent.effect` 用同一型別）。
- 頁面標題由「Connected Services」改為「Connectors — 讓 AI 使用你的工具與服務」（設計 05 §3，app 名稱同步），既有卡片區加上小標題。Connectors 暫不放回 portal（D-020）。
- 用 playwright 截圖（`dist/` + 假 API，桌機 1280、手機 390）檢查：手機上詳情的工具表被擠成直排，改成每個工具一列。
- **工具結果卡片（generative UI，D-019）**：`components/aiChat/cards`（`parseToolCard` 過濾不可信內容、`TableCardView`、`LinkCardView`、`ToolCards`），`AiChatConfig.cardRenderers` 讓 app 註冊自己的 kind；`ToolStep.card` 由 `tool_result.card` 而來。截圖確認 Finance 對話裡的表格與連結卡片。
- **Data Analysis 助理的設定**（`DataAnalysis/aiChatConfig.ts`，未掛上頁面）與 `chart` 卡片（`assistant/ChartCardView`，用頁面的 `renderViz`，有 plan 就能「加到 dashboard」）。
- **chat 每一輪送 `profile`**（`ChatStreamingOptions.profile`）：後端還沒有這個欄位時 System.Text.Json 預設略過，不影響 Finance；`index.test.tsx` 斷言 chat body 的測試加上 `profile: 'finance'`（API 變更，非退化）。
- Connectors 頁的重新整理（與 OAuth 成功回來）同時重讀 AI connector 目錄（`refreshSignal`）。
- 決策 D-018～D-020。
- 驗證：每次 push 前本機 lint、test（最後 111 檔 1244 個）、build 全過。CI 無法驗證（帳單），本輪沒有後端變更。
- 問題：中途 auto mode classifier 連續 6 次沒有回應（Bash 暫時不能用），改用 Write/Edit 先寫文件，之後恢復。有一個 commit（`feat(client): AI chat 的每一輪送出 profile`）push 時帶了一個 lint 警告（useCallback 依賴），下一個 commit 立刻修好。

## 2026-10-24 05:42 – 第 2 輪（M1-5、M1-7、M2 前端零件）

- 合併 master（#569 Data Analysis 頁拆檔，純前端）：本機 lint／test（1130）／build 全過。
- **M1-5 共用 chat 引擎 profile 抽象（後端）**：新 module Assistant（`Services.Chatbot` + `Services.Assistant`）、`IChatProfile`／`ChatProfileSetup`／`IChatProfiles`、`ChatTurnRequest.Build`、`FinanceChatProfile` 與 `MarketBackground` 搬到 `Services.Finance.Assistant`、controller 傳 `"finance"`。Finance 的 request 測試只改指向（`TestHelpers/FinanceChatRequest`），斷言不變；新增 `ChatProfileTests` 5 個。server CLAUDE.md 模組表與「AI assistant (chat)」更新。**尚未經 CI 驗證**（見問題）。
- **M1-7 前端 config 化**：`AiChatConfig` + context，Finance 文案搬到 `Finance/aiChatConfig.ts`；refused 文案 app 可覆寫。既有測試斷言不改，只在 render 外包 config。
- **M2 前端零件**：`ToolActivity`（回答上方顯示用了哪些工具，Finance 現有的 tw-stock 工具立刻受惠）、`ApprovalCard` + `decide()`（human-in-the-loop 確認卡，接設計 04 §1.3 的 SSE 事件與端點）、`ToolPermissionToggle`（自動／需確認／停用）。
- 驗證：前端每次 push 前本機 lint、test（最後 102 檔 1161 個）、build 全過。
- **問題（阻礙）**：GitHub Actions 從本輪起不執行任何 job——annotation 是「The job was not started because recent account payments have failed or your spending limit needs to be increased」（run 40029336575，rerun 一次同樣結果；之後 40030813265 等也是）。後端無法 build／測試。已寫進 FOR_JACKY 第 0 條，決策 D-016。

## 2026-10-24 03:42 – 第 1 輪（續：提早開工 M1）

- 設計定稿（D-012），開始 M1：
  - `FinanceChatRequestTests`：逐欄位鎖住 Finance request（system prompt 原文、model、max_tokens、effort、MCP server、工具、beta、body 欄位集合）。CI run 40016078956 綠。
  - NuGet spike：`ModelContextProtocol.Core` 1.3.0 在 CI restore/build 成功（同一 run）。
  - `splitSseLines`：修好前端 SSE 跨 chunk 事件遺失的 bug（8 個測試）。
  - `ClientToolRoundTripTests`：用真的 SDK 證明 client 工具往返（`BetaMessageContentAggregator` + JSON 轉 param，thinking signature 保留）。查 SDK 原始碼發現它有現成的累積器與 `Anthropic.Mcp` 轉換範例，設計 04 的最大風險大幅降低。
  - 新 module Connectors + `TwStockMcp`（tw-stock 設定從 AnthropicChatService 搬出）。
  - 前端 `Finance/components/aiAssistant` → `components/aiChat`（純搬家）。
  - 合併 master（#567、#568）。CI run 40018092469 綠。
  - M2 先做的獨立小塊：`SafeHttp` + `OutboundAddressPolicy`（SSRF，含 DNS rebinding 防護與 40+ 個測試案例）、`McpServerUrl`（https、canonical URI）、`ToolNames`（前綴、64 字上限）。最後一次含程式碼的 push：CI run 40019395272 綠。
  - branch CI 的前端測試改用 github-actions reporter（失敗測試變 annotation）。
- Jacky 中途留言兩則：KB connector 建議（記為 D-013，M4 延伸）、要求定期同步 master（D-014，用 merge）。
- 問題：第一次 push spike 測試時少了 `AnthropicModel` using 別名，CI 編譯失敗，下一個 commit 修好。run 40017652281 前端 Test 步驟失敗一次但看不到是哪個測試（當時還沒有 reporter），下一個 run 同樣的程式碼就綠了，推測是 flaky；之後若再發生，annotation 會指出測試名稱。

## 2026-10-24 03:42 – 第 1 輪（階段 0）

- 建立 `docs/ai-native/`（PLAN、NEXT、PROGRESS、DECISIONS、FOR_JACKY、SHOWCASE、design/）與 `.loop/` 鎖。
- 讀懂現有架構（Finance chat、Connected Services、Data Analysis、usage limit、架構測試），寫進 `design/01-research.md`。
- 趨勢研究：MCP 2026-07-28 stateless 規格、MCP 授權（PRM、CIMD、DCR deprecated、PKCE、RFC 8707、RFC 9207、step-up）、MCP Apps（SEP-1865 Final）、Anthropic MCP connector（工具在 Anthropic 端執行、無確認機制）。
- 設計草稿 02–07；請一個 subagent 做對抗式評審，修正：模組環、Finance 測試無法「一行不改」、SSE 必須送 done、跨 chunk bug 變成必修、對話 profile 外洩、filtered unique index、agent loop 的 block 累積風險、改成同請求內等待確認、`ask_data` 包既有 NL 管線、單一 migration。決策記在 D-001～D-011。
- 驗證：本輪只有文件，未改程式碼；branch CI 最新一次（run 40009250817）為綠。
- 問題：無。
