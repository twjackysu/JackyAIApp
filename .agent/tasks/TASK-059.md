# TASK-059：綜合分析查無資料時回明確的 NotFound，且不快取沒有價格的結果

## Owner
Backend Engineer

## Status
MERGED

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
**PR：** https://github.com/twjackysu/JackyAIApp/pull/469（分支 `claude/exciting-cray-jra6uf`）

**實作**（只改 `FinanceQueryService.GetComprehensiveAnalysisAsync`，約 +14 行）
- `BuildAsync` 之後：`LatestClose == null && Indicators.Count == 0` → `ServiceResult.Fail(ErrorCodes.NotFound, "No market data found for '{code}'.")`（HTTP 404），不寫快取。
- `LatestClose == null` 但有指標 → 照常回傳、**不寫快取**。
- 有價格 → 行為不變，照常快取 12 小時。
- 未動 `StockAnalysisBuilder`、provider、controller、DTO、前端。

**測試**（本機實際執行）
- 新增 `FinanceQueryServiceComprehensiveAnalysisTests`（4 個，真的 `StockAnalysisBuilder` ＋ 真的 `ExtendedMemoryCache`）：空價格 → NotFound 且 provider 兩次呼叫都打到；先空後有價格 → 第二次回完整結果；有價格 → 第二次不呼叫 provider；無價格但有基本面指標 → 回結果、不快取。
- 還原修改後測試 1、2、4 失敗，套用後 4/4 通過；`dotnet test --filter "Category!=LiveContract"` → 1362 通過／0 失敗，既有斷言未改。
- 沙箱連不到 TWSE／Yahoo，**上游行為未實測**，下列結論皆為讀碼。

**驗收要求的讀碼回報**（完整說明在 PR 描述）
1. **TW：** `Prices` 空 → `LatestClose == null`，計算器資料不足被略過 → `Indicators` 空，判斷成立。**US：不成立，也到不了這條路徑**——`YahooFinanceMarketDataProvider.FetchUrlAsync` 對 404 `EnsureSuccessStatusCode()` 丟例外，`BuildAsync` 的 `priceTask` 沒有 `Safe` 包裝，被 catch 轉成 `InternalServerError`（HTTP 500），所以拼錯的美股代號現在是 **500 而非「成功但全空」**，也不會被快取。本任務沒改它。
2. **前端：** 錯誤不分 HTTP 狀態碼，404 與 500／502 同一條路徑，沒被破壞。比較分頁顯示 `找不到 {code}：{message}`（含後端訊息）；量化分頁與 Portfolio 深連結（`?symbol=`）顯示的是**固定文案**「綜合分析失敗，請檢查股票代碼是否正確」（`Finance/index.tsx:271`），不顯示後端訊息——與本檔 Requirements 第 1 點「前端已經會把失敗訊息顯示在 Alert」略有出入，但使用者看到的是錯誤而非空白。

**Known issues / 給 Tech Lead**
- **美股不存在的代號回 500（非 404）**：要改需動 `YahooFinanceMarketDataProvider`／`StockAnalysisBuilder`，超出本任務範圍；請決定是否另開任務。
- `YahooFinanceMarketDataProvider.FetchAsync` 自己有 1 小時快取且**不論有沒有價格都寫入**（`CachedMarketDataProvider` 才有「無資料不快取」）。Yahoo 回成功但價格為空時會在 provider 層凍結 1 小時，分析層的修正擋不到這一層。
- 「技術面」「基本面」都關閉（純籌碼）的請求不抓價格，`LatestClose` 恆為 `null`：無籌碼指標回 404；有籌碼指標回結果但不快取。前端預設不會送這種組合，依規格未特別排除。
- 沙箱沒有 .NET SDK，本次用 `apt-get install dotnet-sdk-10.0` 安裝後測試。
- 本 session 只能推指定分支，認領（`IN_PROGRESS`）未能直接推 master，隨本 PR 帶入。

## Review
（Tech Lead 事後審閱，2026-10-17 第 14 次排程；Boss 於 2026-10-16 21:46 +0800 在 PR 開出約 6 分鐘後直接合併為 #469，`e933ee1`，`Build and deploy` run 557 成功；合併的 head 是 `55d4724`，CI run 692～694 皆綠）

**結論：無須追加修正。** 逐行讀了 `FinanceQueryService.GetComprehensiveAnalysisAsync` 的差異與 4 個新測試：

- **三條分支與任務書一致**：`LatestClose == null && Indicators.Count == 0` → `NotFound`（不快取、記一行 warning）；`LatestClose == null` 但有指標 → 回傳但不快取；有價格 → 照舊快取 12 小時。快取 key 仍含 `configHash`，未動。`GetComprehensiveAnalysisAsync` 只有 `FinanceController` 一個呼叫端，沒有其他程式依賴「成功但全空」。
- **測試用真的 `StockAnalysisBuilder` ＋真的 `ExtendedMemoryCache`**，只 mock provider 與指標引擎；以 `Times.Exactly(2)`／`Assert.Same`／`Assert.NotSame` 直接證明「有沒有被快取」，不是間接推論。Engineer 回報還原修改後 1、2、4 失敗（我沒有重跑，沙箱無 .NET）。
- **Engineer 的讀碼回報我認可，並決定如下（皆不排任務）**：
  1. **美股不存在的代號回 500 而非 404**（`YahooFinanceMarketDataProvider.FetchUrlAsync` 對 404 `EnsureSuccessStatusCode()` 丟例外，`BuildAsync` 的 `priceTask` 沒有 `Safe` 包裝）。使用者仍看到錯誤訊息而不是空白，前端不分狀態碼同一條路徑，所以產品面沒有缺口；代價是這類輸入會在後端記成例外（Application Insights 可能出現噪音，**未量**）。列**技術債**，異常再升級。
  2. **`YahooFinanceMarketDataProvider.FetchAsync` 自己有 1 小時快取，不論有沒有價格都寫入**：Yahoo「回成功但價格為空」會在 provider 層凍結 1 小時，分析層的修正擋不到。罕見（Yahoo 無資料通常是 404），影響上限 1 小時、僅單一代號。列**技術債**，不排。
  3. **「技術面」「基本面」都關閉（純籌碼）的請求不抓價格**，`LatestClose` 恆為 `null`：無籌碼指標回 404、有則回結果但不快取。前端預設不會送這種組合，維持現狀。
  4. 量化分頁與 Portfolio 深連結對錯誤顯示**固定文案**「綜合分析失敗，請檢查股票代碼是否正確」，只有比較分頁顯示後端訊息——與我寫任務書時的假設略有出入（我寫「前端已經會顯示失敗訊息」），但使用者看到的是明確錯誤而非空白，**不改**。若 PM 要在量化分頁顯示「找不到 {code}」，是另一個前端小改動，由 PM 決定。
- **未驗證**：未對正式環境量測；上游吞例外的行為（`TWStockLibHistoricalProvider.FetchRangeAsync`）仍是讀碼結論。
