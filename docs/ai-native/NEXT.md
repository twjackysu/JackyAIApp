# NEXT — 下一輪從最上面開始

## 1. 設計複核並定稿（下一輪，約 30 分鐘內）

- 重讀 `design/03`、`04`、`06`、`07`，確認評審意見（見 `DECISIONS.md` D-004 ～ D-010）都已反映、彼此沒有矛盾。
- 把 `PLAN.md` 的狀態改成「**設計定稿**」，並在 `PROGRESS.md` 記錄。之後就可以開始寫功能程式碼。

## 2. M1-1 Finance golden test（server，單獨 commit）

- 在 `JackyAIApp.Server.Tests/Services/Chatbot/` 加一個測試：用固定日期與固定市場背景呼叫 `AnthropicChatService.BuildRequest`，把 request 序列化成 JSON（SDK 的 JSON 序列化），與檔案 `Fixtures/finance-request.golden.json` 比對。第一次 CI 沒有檔案時無法得知內容——作法：測試先把實際 JSON 與「我們手寫預期的關鍵欄位」逐項斷言（model、max_tokens、betas、mcp_servers、tools 種類與 configs、system 兩段、cache_control），不要依賴無法本機產生的整檔快照。
- 讀 `AnthropicChatServiceTests.cs`、`MarketBackgroundTests.cs`，避免重複既有斷言，只補缺的。

## 3. M1-2 `parseSseChunk`（client）

- 新檔 `jackyaiapp.client/src/hooks/sseParser.ts`：`parseSseChunk(rest: string, chunk: string) → { events: unknown[], rest: string }`，忽略非 `data:` 行（含 `: ping`）、空行；JSON 解析失敗的事件丟掉並 `console.warn`。
- 測試：一個事件被切成兩半、一個 chunk 多個事件、`: ping`、`\r\n`。
- `useChatStreaming.ts:148` 改用它；跑既有 aiAssistant 測試（不改斷言）。

## 4. M1-3 NuGet spike

- `JackyAIApp.Server.csproj` 加 `ModelContextProtocol.Core`（先試 1.3.0），push，看 CI restore/build（警告即失敗）。失敗就 revert 並記錄，改走自寫 client。

## 5. M1-4 之後

依 `design/07-implementation-plan.md` 的 M1 清單往下做（agent loop spike → 模組重整 → migration → 前端搬家）。
