# PROGRESS（新的在最上面）

## 2026-10-24 03:42 – 第 1 輪（續：提早開工 M1）

- 設計定稿（D-012），開始 M1：
  - `FinanceChatRequestTests`：逐欄位鎖住 Finance request（system prompt 原文、model、max_tokens、effort、MCP server、工具、beta、body 欄位集合）。CI run 40016078956 綠。
  - NuGet spike：`ModelContextProtocol.Core` 1.3.0 在 CI restore/build 成功（同一 run）。
  - `splitSseLines`：修好前端 SSE 跨 chunk 事件遺失的 bug（8 個測試）。
  - `ClientToolRoundTripTests`：用真的 SDK 證明 client 工具往返（`BetaMessageContentAggregator` + JSON 轉 param，thinking signature 保留）。查 SDK 原始碼發現它有現成的累積器與 `Anthropic.Mcp` 轉換範例，設計 04 的最大風險大幅降低。
  - 新 module Connectors + `TwStockMcp`（tw-stock 設定從 AnthropicChatService 搬出）。
  - 前端 `Finance/components/aiAssistant` → `components/aiChat`（純搬家）。
  - 合併 master（#567、#568）。CI run 40018092469 綠。
  - M2 先做的獨立小塊：`SafeHttp` + `OutboundAddressPolicy`（SSRF，含 DNS rebinding 防護與 40+ 個測試案例）、`McpServerUrl`（https、canonical URI）。
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
