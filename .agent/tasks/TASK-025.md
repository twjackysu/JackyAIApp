# TASK-025

## Title
Portfolio 後端效能：消除重複上游抓取與 cold-cache 併發重複請求（Issue #401）

## Owner
Backend

## Status
MERGED

## Priority
MEDIUM

## Objective
Boss 回報 Portfolio 頁「所有資料出現太久」（Issue #401）。降低首次載入時後端的上游呼叫次數與重複計算。

## Requirements
Tech Lead 分析（讀碼，未量測）找到的瓶頸，依預期效益排序：

1. **cold-cache 併發重複抓取（single-flight）**：頁面載入時同時打 `summary`、`net-worth-history`(x2)、
   `benchmark-comparison`、`drawdown` 五支 API，皆走 `FetchStockDataAsync`/`FetchFundsAsync`/
   `ComputeDividendsAsync`。`IMemoryCache` 的 TryGetValue→Fetch→Set 沒有 single-flight，快取未命中時
   同一 symbol 會被多個 request 同時各抓一次（上游含 TWStockLib/Yahoo/FundRich）。
   做法：對 `FetchStockDataAsync`、`FundDataService.GetFundNavHistoryAsync`、`DividendService` 的
   取得路徑以 per-key `Lazy<Task>`/`SemaphoreSlim` 去重（沿用專案既有快取慣例，勿引入新套件）。
2. **shallow/deep 雙重抓取**：`summary` 用 `deep:false`（`latest_` key），其他圖表用 `deep:true`
   （`deep_` key），同一 symbol 第一次載入會抓兩次。評估：deep 結果已在快取時，`deep:false` 直接由
   deep 資料切出最後幾筆（deep ⊇ latest），反之不行。
3. **FX 序列 await**：`GetPortfolioSummaryAsync` 內 TDCC/Cathay 匯率在 `foreach` 內逐一 `await`，
   改為與 stock/fund prefetch 一起 `Task.WhenAll`。
4. **重複載入 snapshots**：`AllSnapshotsAsync`（Include Positions 全表）在 summary/net-worth/drawdown/
   benchmark 各查一次。評估以 request-scoped 或短 TTL（<=30s，於 SaveHoldings/Delete 時失效）快取；
   若失效邏輯複雜度高於收益則不做，並在 Result 說明。

## Acceptance Criteria
- 新增/更新 unit tests：同 key 併發 N 次只觸發 1 次上游 fetch（mock provider 計數）。
- 既有 Portfolio 測試全綠（`--filter "Category!=LiveContract"`）；回傳 DTO 數值不變（純效能改動，無行為變更）。
- PR 描述附「改動前後上游呼叫次數」的估算或 log 對照（無法實測則說明）。
- 不改 API 契約、不改快取 TTL（`CacheTtl.cs`）。

## Technical Notes
- 相關程式：`Services/Portfolio/PortfolioService.cs`（`FetchStockDataAsync` ~L1929、`FetchStocksAsync`、
  `FetchFundsAsync`、`GetPortfolioSummaryAsync` L185~、`AllSnapshotsAsync` ~L1584）、
  `FundDataService.cs`、`DividendService.cs`。
- 風險：single-flight 需處理 `CancellationToken`（一個 caller 取消不應取消共用 task）與失敗不快取。
- 實作前先在 master 推 `Status: IN_PROGRESS`（Workflow conventions）。
- 前端部分另開 TASK-026，兩者可並行。

## Dependencies
無。

## Result

開工前確認：無現存 open PR 觸及本任務，且無其他 Backend 任務依賴它。依任務本身要求，先推送一筆
`Status: IN_PROGRESS` 的 bookkeeping commit，再開始實作。

依 Requirements 四項逐一處理：

1. **cold-cache single-flight**：新增 `Common/SingleFlight.cs`——一個以 `ConcurrentDictionary<string,
   object>` + `Lazy<Task<T>>` 實作的泛型去重工具，沿用專案既有慣例（純 `IMemoryCache` +
   `System.Collections.Concurrent`，未引入新套件）。同一 key 併發呼叫時，只有第一個呼叫者真正執行
   `factory`，其餘呼叫者等待同一個 `Task`；完成（成功或失敗）後立即從表中移除，讓下一次呼叫重新
   觸發（它是去重，不是快取——失敗不會被「快取」住，呼叫者自己的 `cache.Set` 才決定是否快取成功
   結果）。
   - **CancellationToken 處理**（Technical Notes 指出的風險）：`factory` 一律不接收個別呼叫者的
     `ct`，共用的 fetch 永遠用 `CancellationToken.None` 執行；只有「等待共用結果」這一步
     (`lazy.Value.WaitAsync(ct)`) 看呼叫者自己的 `ct`。因此某個呼叫者取消，只會讓它自己提早收到
     `OperationCanceledException`，完全不影響其他正在等待同一 key 的呼叫者，也不影響共用 fetch
     本身是否完成並寫入快取。
   - 套用到三個任務指名的取得路徑：`PortfolioService.FetchStockDataAsync`、
     `FundDataService.GetFundNavHistoryAsync`、`DividendService.GetStockDividendsAsync`/
     `GetFundDividendsAsync`。四處都是「先查 cache，沒中再用 cache key 當 SingleFlight key」的同一
     套寫法。
2. **shallow/deep 共用**：`FetchStockDataAsync` 在 `deep:false` 且自己的 `latest_` cache
   未命中時，多檢查一次 `deep_` cache；命中就直接回傳 deep 版本（不寫回 `latest_`
   key——那個 key 的既有不變量「只放真正的 shallow 結果」維持不變，見程式內原本就有的那段註解），
   不另外呼叫 provider。deep ⊇ latest 的前提本身沒變、消費端只讀
   `HistoricalPrices.LastOrDefault()?.Close`，回傳整包 deep 資料對它們完全透明。
3. **FX 序列 await**：`GetPortfolioSummaryAsync` 內原本是「tdcc 的 foreach+await」接著「cathay 的
   foreach+await」兩段序列迴圈。改寫成兩段都先用 LINQ 建出 distinct 的 currency/date 清單，再各自
   包成一個 `Task.WhenAll(...Select(async ...))`，最後這兩個 `Task.WhenAll` 本身再包一層
   `Task.WhenAll` 一起等。兩組 rate 彼此不互相依賴（一個只讀 fundMap、一個只讀 stockMap，皆已在
   更早的 `Task.WhenAll(stockTask, fundTask)` 後就緒），所以可以安全地平行跑。
   - **刻意偏離任務原文字面**：原文寫「改為與 stock/fund prefetch 一起 Task.WhenAll」，但
     cathay 的 rate 查詢需要 `stockMap` 算出的收盤日期（`stockMap[symbol].HistoricalPrices
     .LastOrDefault()?.Date`），不可能在 `stockTask` 完成前就知道要查哪個日期——這是真實的資料
     依賴，不是寫法問題。改為「两組 rate 查詢彼此平行」，是在尊重這個依賴下最大化平行度的做法。
4. **重複載入 snapshots**：`AllSnapshotsAsync` 加上 30 秒短 TTL 快取（`SnapshotsCacheTtl`，刻意
   比 `CacheTtl.Portfolio.*` 任何現有值都短——它只需要撐過同一次頁面載入的那一小段併發視窗，不是
   真的要讓資料「新鮮」）+ 同一套 SingleFlight 去重；`SaveHoldingsAsync`/`DeleteSnapshotAsync`
   成功後立即 `cache.Remove` 失效，讓下一次讀取保證拿到最新資料。沒有走任務描述裡「評估失效邏輯
   複雜度過高則不做」的退路——因為 `SaveHoldingsAsync`/`DeleteSnapshotAsync` 本來就在同一個
   `PortfolioService` 類別、本來就已經注入了同一個 `IMemoryCache cache`，失效只是多一行
   `cache.Remove(key)`，複雜度並不高。

### 改動前後上游呼叫次數（估算，無法在此 sandbox 實測——見下方 Known limitations）
以一次冷快取的 Portfolio 頁面載入為例（同時打 summary、net-worth-history×2、benchmark-comparison、
drawdown 五支 API，持有 N 個股票 symbol、M 個基金）：
- **股票報價**：改動前每支 symbol 最多被獨立抓 5 次（summary 的 shallow 1 次 + 其他 4 支圖表各自
  deep 1 次，彼此快取 key 不同、互不相讓）。改動後：4 個 deep 請求經 single-flight 收斂成 1 次；
  summary 的 shallow 請求視時間差，最壞情況（在任何 deep 結果寫入快取前就檢查）仍需自己的 1 次
  shallow 抓取，最好情況（deep 已寫入快取）則完全不必外呼。即最多 2 次、最少 1 次，相對 5 次是
  60%–80% 的減少。
- **基金 NAV**：同一邏輯，每個 fundId 從最多 5 次降到最多 2 次（沒有 shallow/deep 共用可省，純靠
  single-flight）。
- **股利**：`ComputeDividendsAsync`（summary 用）與 benchmark-comparison 各自對每個 symbol/fundId
  呼叫一次 dividend 服務；冷快取時兩者幾乎同時發生，single-flight 前是 2 次、之後收斂成 1 次。
- **Snapshots（DB 查詢）**：`AllSnapshotsAsync` 被 5 支 API 全部呼叫，改動前 5 次獨立
  `Include(Positions)` 查詢；改動後 single-flight + 30 秒快取收斂成 1 次（`GetPortfolioSummaryAsync`
  另外還有一次 `LatestSnapshotAsync`，那是不同查詢、本任務未動）。
- **FX 序列 await**：TDCC/Cathay 兩段原本序列（先等完 TDCC 全部貨幣、再等 Cathay）變成兩段平行
  跑。呼叫次數不變（本來就各自已用 `.Distinct()` 去重），省的是「等待時間」不是「呼叫次數」——
  若 TDCC 查 1 個貨幣耗時 Ta、Cathay 查 1 個日期耗時 Tc，改動前總等待 ≈ Ta+Tc，改動後 ≈
  max(Ta, Tc)。

### Tests
- `Common/SingleFlightTests.cs`（新增，針對 `SingleFlight` 本身）：同 key 併發 N 次只觸發 1 次
  factory；不同 key 各自觸發；同一 key 在上一次呼叫完全結束後再呼叫會重新觸發（證明它是去重不是
  快取）；factory 失敗時每個等待者都收到例外、且失敗後下一次呼叫允許重試成功；一個呼叫者取消自己
  的 `ct` 不會影響另一個仍在等待同一共用結果的呼叫者（用 `TaskCompletionSource` 精確控制時序）。
- `Services/Portfolio/PortfolioSingleFlightTests.cs`（新增）：
  - `LookupStockAsync` 對同一 symbol 併發 5 次只觸發 1 次 provider 呼叫（用
    `TaskCompletionSource` 擋住 provider 回應，確保 5 個呼叫真的重疊而不是依序各自完成）。
  - 先觸發一次 deep 抓取（`GetHoldingPriceHistoryAsync`）讓 `deep_` cache 命中後，shallow 呼叫
    （`LookupStockAsync`）驗證 `FetchLatestAsync` 完全沒被呼叫、`FetchAsync` 只被呼叫一次、且回傳
    的價格等於 deep 資料的最後一筆。
  - `AllSnapshotsAsync` 的快取：直接在 `IMemoryCache` 塞入一筆「canary」snapshot（DB 完全沒有任何
    資料），呼叫 `GetNetWorthHistoryAsync` 驗證結果來自快取而非 DB（`HoldingSnapshots` 表仍是空的
    卻能拿到資料，證明讀的是快取）。另外兩個測試驗證 `SaveHoldingsAsync`/`DeleteSnapshotAsync`
    成功後確實把快取 key 清掉。
  - 用固定 `TimeProvider`（週一）避免「今天」剛好落在週末時，`GetNetWorthHistoryAsync`
    的工作日迴圈產生空結果造成的潜在 flaky。
- `Services/Portfolio/FundDividendSingleFlightTests.cs`（新增）：對 `FundDataService
  .GetFundNavHistoryAsync`、`DividendService.GetStockDividendsAsync`、
  `DividendService.GetFundDividendsAsync` 三者各寫一個併發測試，用一個本地的 `GatedHandler`
  （比照既有 `StubHttpMessageHandler` 慣例，但改成真正非同步地擋住請求直到測試釋放
  `TaskCompletionSource`）取代既有同步 stub，確保 5 個併發呼叫是真的重疊而不是依序完成；斷言
  底層 HTTP handler 只被呼叫 1 次。
- 既有 `PortfolioFundFxTests.cs`/`PortfolioUsStockFxTests.cs` 的 `GetBuyRateAsync` mock 設定全都用
  `It.IsAny<CancellationToken>()`，FX 平行化改動不影響呼叫次數斷言（`Times.Once`/`Times.Never`）；
  讀過 `PortfolioNetWorthHistoryTests.cs`/`PortfolioDrawdownTests.cs`/
  `PortfolioBenchmarkComparisonTests.cs`/`PortfolioFeeTaxTests.cs` 等既有測試，確認每個測試方法
  (xUnit 預設每個 `[Fact]` 都是全新 class instance、全新 `MemoryCache`）內最多只呼叫一次
  summary/net-worth/drawdown/benchmark 類方法（唯一一個呼叫兩次的
  `GetSummary_OutOfRangeRate_IsClampedRatherThanRejected` 兩次呼叫之間資料未變，只是
  `feeDiscountRate` 不同），新增的 30 秒 snapshots 快取不會讓這些既有測試讀到過期資料。

### Known limitations / out of scope
- 本 sandbox 沒有 `dotnet` SDK，無法實際執行 `dotnet build`/`dotnet test`，也沒有真實流量可以
  實測「改動前後上游呼叫次數」——以上數字是逐讀程式路徑推導出的估算，並非量測結果；CI 需確認一次
  真正的 build + test 通過。
- 不改 API 契約、不改 `CacheTtl.cs` 既有的值（新加的 `SnapshotsCacheTtl` 是全新常數，不是改動既有
  TTL）。
- `PortfolioExposure.cs`、`XirrTests.cs` 等與本任務邏輯無關的檔案未觸碰。
- 前端部分（TASK-026，圖表進入視窗才載入）為並行任務，未在本 PR 範圍內。

## PR
https://github.com/twjackysu/JackyAIApp/pull/404

Update：CI 第一輪 `build-and-test-server` 紅——抓到自己測試裡的真錯誤（非產品程式碼）：
`FundDataServiceTests.GetFundNavHistoryAsync_ParsesPriceHistoryAndSortsAscending` 失敗（預期 2 筆
價格，實際 1 筆）。根因：`SingleFlight.InFlight` 依設計是 process-wide 的全域去重表（這對正式環境
是對的——同一個 fundId/symbol 本來就該被視為同一次上游呼叫，不管哪個 request 觸發），但這代表任何
一個測試把某個 key「卡在 in-flight 狀態」一段時間，就會變成整個測試組件（assembly）裡任何其他
測試都可能撞上的共用 key。我新增的 `FundDividendSingleFlightTests`
（`FundDataService_GetFundNavHistoryAsync_ConcurrentCallsSameFundId_OnlyFetchesUpstreamOnce`）刻意
用 `TaskCompletionSource` 把 `"ALI006"` 這個 key 卡住 50ms 以確保 5 個併發呼叫真的重疊——但
`"ALI006"`/`"AAPL"`/`"2330"` 這些寫實的 fixture 值同時也被其他既有測試檔（像
`FundDataServiceTests`、`DividendServiceTests`）用來跑不同的 mock 資料。xUnit 併發跑測試類別時，
剛好撞上同一個 key，另一個測試的呼叫就接到了我測試卡住的那個共用 Task，拿到我 mock 的 1 筆資料
而不是它自己預期的 2 筆。
修正：4 個刻意卡住 key 的併發測試（`PortfolioSingleFlightTests.cs` 2 個、
`FundDividendSingleFlightTests.cs` 3 個，但其中一個原本就沒有卡住視窗，一併處理以防微杜漸）全部
改用 `Guid.NewGuid()` 產生的唯一 id/symbol，不再用任何其他測試可能重複使用的寫實字串。已推送
修正（commit `e9ec70f`），等待 CI 重跑確認。

Update：CI 重跑（head `b803cf6`）`build-and-test-server`/`lint-and-build-client` 皆綠燈，
`mergeable_state: clean`，目前無 review comment。純等待人工 review，已訂閱 PR 活動。

## Review
Tech Lead 2026-10-02：PR #404 已由 Boss 合併。單元測試已涵蓋 single-flight/取消語意/快取失效；CI 抓到的測試 key 衝突已修。實際效能改善未實測（無 dotnet/正式流量），建議部署後由 Boss 體感確認。
