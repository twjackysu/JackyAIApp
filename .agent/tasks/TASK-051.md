# TASK-051：`StockCodeResolver` 在 OpenAI 回應失敗時記錄錯誤訊息（#455 風險最高路徑的可觀測性）

## Owner
Backend Engineer

## Status
READY

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
（尚無）

## Result

## Review
