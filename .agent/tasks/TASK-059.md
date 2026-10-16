# TASK-059：綜合分析查無資料時回明確的 NotFound，且不快取沒有價格的結果

## Owner
Backend Engineer

## Status
READY

## Priority
MEDIUM（優先序可由 PM 調整。會讓使用者一整天看到某檔股票的空白分析：上游暫時故障的結果被快取 12 小時；頻率未量測）

## Objective
`FinanceQueryService.GetComprehensiveAnalysisAsync` 不檢查 `BuildAsync` 的結果有沒有資料，一律以 `ComprehensiveAnalysis_{code}_{date}_{configHash}` 快取 12 小時（`CACHE_HOURS`，見該檔）。來源：TASK-055 的 Engineer 在 PR #467 的讀碼查證，Tech Lead 已自行核對 `GetComprehensiveAnalysisAsync`（快取寫入前沒有任何空結果檢查）；價格鏈上游的吞例外行為依 Engineer 的說明（`TWStockLibHistoricalProvider.FetchRangeAsync` 的 `catch` 會回空 `MarketData`，沙箱連不到上游，**未實測**）。

兩個後果：

1. **不存在的代號**（`9999`、`99999`、拼錯的美股代號）→ HTTP **成功**回應，內容幾乎全空（`indicators: []`、`latestClose: null`、`companyName: ""`、`dataRange: "N/A"`、`scoring/risk: null`），使用者看到空白畫面而不是「找不到」。PM 在需求 6 要求「查不到時回明確訊息」。
2. **真實代號遇上游暫時故障**（TWSE／Yahoo 逾時、被 Polly 的 circuit breaker 擋下）→ 同樣的空結果被快取 12 小時，這檔股票當天的分析一直是空的，重啟或隔天才恢復。`CachedMarketDataProvider` 本身有「無資料不快取」的保護，但分析層沒有。

## Requirements
1. `GetComprehensiveAnalysisAsync` 在 `BuildAsync` 之後：
   - 若 `result.LatestClose` 為 `null`（沒有任何價格）**且** `result.Indicators.Count == 0` → 回 `ServiceResult.Fail(ErrorCodes.NotFound, ...)`，**不寫入快取**。訊息比照既有風格（例如 `No market data found for '{resolvedStockCode}'.`）；前端已經會把失敗訊息顯示在 Alert 與比較分頁的每檔欄位（`找不到 {code}：{message}`），不需改前端。文案若 PM 想改成中文，是另一個小改動。
   - 若 `result.LatestClose` 為 `null` 但仍有指標（例如價格抓不到、但基本面有資料）→ 照常回傳結果，但**不寫入快取**（價格缺席多半是暫時的，不要讓它凍結 12 小時）。
   - 其餘情況（有價格）→ 行為完全不變，照常快取。
2. 不改 `StockAnalysisBuilder`、provider、controller 簽章、DTO、前端。`AnalyzeStockAsync`（AI 趨勢分析）不走這個方法，不在範圍。
3. `GetPriceBarsAsync` 已經對不足 30 根回 `NotFound`，不用動。

## Acceptance Criteria
- 新增測試（沿用 `FinanceQueryService*Tests` 的建構方式，例如 `FinanceQueryServicePriceBarsTests`）：
  1. 價格為空的 provider → 回 `NotFound`，且**連續呼叫兩次 provider 被呼叫兩次**（沒有快取空結果）。
  2. 第一次價格為空、第二次有價格 → 第二次回完整結果（證明暫時故障不會凍結）。
  3. 有價格 → 第二次呼叫不再呼叫 provider（快取行為不變）。
  4. 價格為空但有基本面指標 → 回結果、不快取。
- `dotnet test --filter "Category!=LiveContract"` 全過；既有測試**不改斷言**。
- PR 說明回報：`Indicators.Count == 0 && LatestClose == null` 這個判斷在 TW／US 兩個區域是否都成立（讀碼確認 US 區域的 `BuildAsync` 在查無代號時的輸出）；以及比較分頁、Portfolio 深連結（`?symbol=`）對 404 的處理沒有被破壞（讀碼確認，前端 `comprehensiveError` 路徑）。

## Technical Notes
- 與 TASK-055（PR #467）**不衝突**：它不碰 `FinanceQueryService.cs`。與已取消的 TASK-058 也不再有關。
- 這是既有行為，不是 TASK-055 引入的；TASK-055 只讓 5～6 碼的不存在字串也能走到這條路徑（成本由 OpenAI 轉為 TWSE 呼叫）。任意 US 字串本來就是如此，且 `AddMemoryCache()` 沒有大小上限（`Program.cs:131`）——不快取空結果同時消除「用大量不存在的代號撐大記憶體快取」這個理論上的路徑；它**不**限制呼叫次數（沒有 rate limiter，是另一個技術債）。
- 流程：開工前先把本檔 `Status` 改 `IN_PROGRESS` 並直接 push 到 master（若 session 只能推指定分支，在 Result 註明）；完成後開 PR，`Status` 改 `PR_OPENED`、填 Result。
- 只改後端；不要動前端、`.github/workflows/`、`CLAUDE.md`。

## Dependencies
無。

## Result
（Engineer 填寫）

## Review
（Tech Lead 填寫）
