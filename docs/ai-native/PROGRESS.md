# PROGRESS（新的在最上面）

## 2026-10-24 07:41 – 第 3 輪（/connectors v2 前端）

- 開工：master 沒有新 commit；branch CI 仍因帳單停擺（rerun 40033649446 一樣「recent account payments have failed」，D-016），所以本輪只做本機可完整驗證的前端。
- **/connectors v2**（設計 05 §3）：先補 `Connectors/index.test.tsx` 鎖住現有頁面行為（帳號卡片、連線導向、中斷、OAuth 回呼訊息），再演進：
  - `AiConnectorsSection`：「AI 可以使用的」（`InstalledSection`，健康狀態 chip、工具權限計數）、「目錄」（`CatalogSection`，分類 chip、一鍵連線／填 key／前往授權）、「新增 MCP server」。目錄載入失敗只顯示一行說明，下方帳號連線照常（後端還沒做時、或合併後端點壞掉時都不會拖垮頁面）。
  - `ConnectorDetailDrawer`：網址、登入方式、scope、最後測試、測試連線／重新授權／停用／移除（二次確認），工具表用 `ToolPermissionToggle`；本站工具（alwaysOn）只能調權限。
  - `AddMcpServerDialog`：網址 → 伺服器偵測（probe）→ 認證（none／bearer／API key／OAuth，含手填 client）→ 列出工具與權限可直接調。目錄中要 key 的項目直接從認證步驟開始。
  - API 契約：`apis/connectorsApis` 新增 8 個 endpoint 與型別，定稿寫進設計 03 §7（新增 `installations/probe`、`tool-permissions`；`installationId: null` 代表沒有列的內建 connector）。
  - `ToolEffect`／`ToolPermission` 改由 API 型別提供（`toolPermission.ts` 轉出、`useChatStreaming` 的 `ToolEvent.effect` 用同一型別）。
- 頁面標題由「Connected Services」改為「Connectors — 讓 AI 使用你的工具與服務」（設計 05 §3），既有卡片區加上小標題。

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
