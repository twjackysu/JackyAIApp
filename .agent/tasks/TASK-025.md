# TASK-025

## Title
Portfolio 後端效能：消除重複上游抓取與 cold-cache 併發重複請求（Issue #401）

## Owner
Backend

## Status
READY

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
（Engineer 填寫）

## Review
（Tech Lead 填寫）
