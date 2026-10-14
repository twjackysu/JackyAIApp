# TASK-051：`StockCodeResolver` 在 OpenAI 回應失敗時記錄錯誤訊息（#455 風險最高路徑的可觀測性）

## Owner
Backend Engineer

## Status
MERGED（PR #459，Boss 於 2026-10-14 07:37 +0800 合併；push 觸發的 Build and deploy run 548 成功。Tech Lead 事後審閱見 Review）

## Priority
LOW（只加日誌，不改行為。但它是 PM 的 Tech Lead Action「確認 #455 部署後各 AI 路徑正常」唯一缺的觀測點）

## Objective
#455 把全站 chat 模型改成 `gpt-6-luna`，PR 自述「尚未對真的 OpenAI API 實測」，並點名風險最高的是 `StockCodeResolver`（它自己組 `ChatCompletionCreateRequest`：`ReasoningEffort.Low`、`MaxCompletionTokens = 1024`、拿掉 `Temperature`／`MaxTokens`，「依推理模型的一般限制推斷」）。

現況（Tech Lead 讀碼 `JackyAIApp.Server/Services/Finance/StockCodeResolver.cs`）：兩處 `CreateCompletion`（`FindExactCompanyMatchAsync`、`ResolveWithAIAsync`）在 `!response.Successful` 時**什麼都不記**，直接回空字串，上層只記一行 `Could not resolve user input '…'`（Warning）。若 OpenAI 因任何參數問題拒絕請求，後果是：使用者輸入股票名稱（例如「台積電」）一律「找不到」，而**日誌裡看不到原因**；且 `LogUsageAsync` 只在回應帶 `Usage` 時才寫 `AiUsageLogs`，被直接拒絕（HTTP 400）的請求通常沒有 `Usage`，所以 `AiUsageLogs.Success = 0` 也抓不到。對照：共用的 `OpenAIPromptService` 在失敗時會 `LogError("OpenAI completion failed: {Error}", …)`，其他 AI 路徑已有這條線索。

## Requirements
只改 `StockCodeResolver.cs`（與其測試，若有）：
1. 兩處 `CreateCompletion` 之後，當 `!response.Successful` 時 `_logger.LogError` 一行，訊息格式與共用 wrapper 一致，例如 `"OpenAI stock-code resolution failed: {Error}"`，錯誤取 `response.Error?.Message ?? "OpenAI request failed"`；不要把 `userInput` 以外的使用者資料寫進日誌。
2. 行為不變：失敗時仍回空字串、不丟例外、不重試。
3. 不改 prompt、模型、`ReasoningEffort`、`MaxCompletionTokens`。

## Acceptance Criteria
- `dotnet test --filter "Category!=LiveContract"` 全過。
- 新增（或擴充既有）單元測試：以 mock 的 `IOpenAIService` 回傳 `Successful = false` 的回應時，(a) 回傳空字串、(b) 記錄了一筆 Error 等級日誌且含錯誤訊息。若該類別目前難以注入 mock，PR 說明並改以最小可行的方式驗證，不要為此重構整個類別。
- 只動 `StockCodeResolver.cs` 與測試；無 migration、API、prompt 變更。

## Technical Notes
- 這是**觀測性**修補，不是對 #455 的行為修正。Tech Lead 無法判斷 `gpt-6-luna` 在這兩個請求上是否真的失敗（沙箱無法呼叫 OpenAI）；若 Boss 已手動確認「台積電」查得到代號，這個任務仍值得做，因為下次換模型時會用到。
- 沙箱可能沒有 .NET：先前 Engineer 以 `apt` 安裝 `dotnet-sdk-10.0` 成功。

## Dependencies
無。

## PR
https://github.com/twjackysu/JackyAIApp/pull/459（branch `claude/exciting-cray-1q2n9v`）

## Result
（Engineer 未填；以 commit `77da069` 與 PR 為準，見 Review。）

## Review
**Tech Lead 事後審閱（2026-10-14，排程執行；#459 在審閱前已被 Boss 合併）**
- 範圍：`StockCodeResolver.cs`（+21／−2）與新增 `StockCodeResolverTests.cs`（109 行，3 個測試）；無 prompt、模型、`ReasoningEffort`、`MaxCompletionTokens`、migration、API 變更——符合需求 3。
- 正確性：兩處 `CreateCompletion` 之後（`LogUsageAsync` 之後）在 `!response.Successful` 時呼叫私有 `LogCompletionFailure`，記 `LogError("OpenAI stock-code resolution failed: {Error}", response.Error?.Message ?? "OpenAI request failed")`，格式與共用 wrapper 一致。`else` 分支保留原本的解析邏輯，失敗時仍回空字串、不丟例外、不重試（需求 2）。日誌只含 OpenAI 的錯誤訊息，**不含 `userInput`**（需求 1）。
- 測試品質：mock `IOpenAIService.ChatCompletion`，以「部分名稱」同時走到兩個呼叫點——(a) 失敗時回空字串、Error 日誌恰好 2 筆且含 `Unsupported parameter: 'max_completion_tokens'`，且 `CreateCompletion` 恰好呼叫 2 次（證明沒有重試）；(b) `Error` 無訊息時記 `OpenAI request failed`；(c) 成功時回 `2330` 且**沒有** Error 日誌（防止誤報）。PR CI（run 678／679）全綠，合併後 master CI 亦綠。
- 這個任務的效果：**#455 部署後，`StockCodeResolver` 被 OpenAI 拒絕時，Log Analytics 會有一行可查的 Error**（`ARCHITECTURE.md` 2026-10-13 那節的 KQL，`AppTraces | where Message startswith "OpenAI completion failed"` 現在要再加 `or Message startswith "OpenAI stock-code resolution failed"`）。**它只是觀測點，沒有對真的 OpenAI 驗證任何事**——gpt-6-luna 在這兩個請求上是否可用，仍未知。
- 結論：**無須追加修正**，結案。
