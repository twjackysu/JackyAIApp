# TASK-054：Portfolio 逐日估值的「as-of 價格」查詢改為預排序＋二分搜尋（Issue #464 IMP-5 第一步，範圍擴及 `ValueAsOfDate`）

## Owner
Backend Engineer

## Status
PR_OPENED

## Priority
LOW（沒有量測顯示它是瓶頸；行為不變、有既有測試釘住，所以風險也低。可以排在 TASK-053 之後）

## Objective
`PortfolioService.cs` 裡有兩處「逐日 × 逐持倉」的迴圈，每一格都對**整段價格歷史**做 `Where(日期 <= d).OrderByDescending(日期).FirstOrDefault()`：

- `InvestedValueAsOfDate`（約第 1128 行；`GetBenchmarkComparisonAsync` 用，最長 ~3 年）
- `ValueAsOfDate`（約第 1945 行；`GetDrawdownAnalysisAsync` 最長 1095 天，以及日漲跌 day-change 用）——**Issue #464 只點到前者；後者是同一個寫法、同樣的成本**，一起修。

複雜度約為「天數 × 持倉數 × 歷史長度 × log」，且每格都配置新的 LINQ buffer。同檔案的 `GetNetWorthHistoryCoreAsync` 早已改用往前推的 cursor；這兩處漏了。

做法：每個 request 把價格序列**排序一次**，之後每個日期用二分搜尋，語意不變。

## Requirements
1. 抽一個小型 helper（名稱自訂，例如 `AsOfPriceSeries`，放在 `Services/Portfolio/`）：
   - 建構時對一檔股票的 `HistoricalPrices`（依 `Date` 遞增）、一檔基金的 `Prices`（依 `Date` 字串 ordinal 遞增）**各排序一次**，**不得就地改動**傳入的集合——它們是被快取共用的實例（見 `GetNetWorthHistoryCoreAsync` 對基金的註解）。
   - 提供「取得 `date` 當日或之前最後一個收盤價」的方法，二分搜尋。沒有任何 `<= date` 的價格 → 回「沒有」（呼叫端現在是 `?? 0`，行為要一致）。
2. `ValueAsOfDate` 與 `InvestedValueAsOfDate` 改用它。helper 在各自的 request 內建好、傳入（`ValueAsOfDate` 是 static 且簽章被多處使用，請以最小改動的方式傳入，例如把 `stockMap`／`fundMap` 的參數換成預先建好的序列字典；不要因此動到不相干的呼叫端）。
3. **同日重複資料的語意要先弄清楚**：現行股票版是 `Where(x.Date.Date <= dt)` 之後 `OrderByDescending(x.Date)`——排序用的是**含時間成分**的 `Date`，取最大者；`Date` 完全相同的多筆則取**原序列中最先出現的那筆**（LINQ 排序是穩定的）。`GetNetWorthHistoryCoreAsync` 的 cursor 版則是取「`Date.Date <= dt` 的**最後一筆**」。兩者在資料有同日重複（或同日但時間不同）時本來就可能不一致。請先查各 provider（`TWStockLibHistoricalProvider`、`PersistentTwHistoricalProvider`、Yahoo、`FundDataService`）的輸出是否可能同日重複：
   - 不會 → 兩種語意沒差別，helper 任選一種並在 PR 說明寫明查證結果。
   - 會 → 沿用**現行這兩處原本的語意**（含時間成分的 `Date` 取最大；完全相同取原序列最先出現者），並在 PR 說明記下「net-worth 版與這兩處在同日重複時不一致」，**不要順手統一**（那是行為變更）。
4. 不改任何 API、DTO、輸出數字；不碰 `GetNetWorthHistoryCoreAsync`。

## Acceptance Criteria
- 既有 Portfolio 測試（`PortfolioDrawdownTests`、`PortfolioBenchmarkComparisonTests`、`PortfolioDayChangeTests`、`PortfolioDayChangeFxTests`、`PortfolioNetWorthHistoryTests` 等）**不改一行**就全過。
- 新增 helper 單元測試：早於第一筆、剛好等於某一筆、介於兩筆之間（含週末）、晚於最後一筆、輸入無序、空序列、基金的字串日期、同日重複（依 Requirement 3 的結論）。另加一個「用新舊兩種實作對一組隨機日期比對結果相同」的測試更好（舊實作可寫在測試內當 oracle）。
- PR 說明附一個粗略的前後量級（例如測試內用 Stopwatch 跑 3 年 × 30 檔的合成資料）。**只當說明，不要寫成會 flaky 的效能斷言。**
- `dotnet test --filter "Category!=LiveContract"` 全過。

## Technical Notes
- Tech Lead 的粗估（**未量測**）：歷史 750 筆、30 檔持倉、1095 天的 drawdown，約 3 萬次查詢 × 每次掃描並排序數百筆；B1 單核上約秒級 CPU。若實際只有 6 個月歷史（`deep` 抓取的註解寫約 6 個月），則小一個量級。所以優先度 LOW；這是「寫法比其他迴圈差」的修正，不是已證實的瓶頸。
- 要不要做更大的重構（Issue #464 的 IMP-4：把四張圖合併成一份時間軸）見 `ARCHITECTURE.md`「Issue #464 效能提案評估」——**本任務不含**，也不依賴它。
- 流程：開工前先把本檔 `Status` 改 `IN_PROGRESS` 並直接 push 到 master；完成後開 PR，並把本檔 `Status` 改 `PR_OPENED`、填 Result。
- 只改後端；不要動前端、`.github/workflows/`、`CLAUDE.md`。

## Dependencies
無（與 TASK-053 互不相干，可並行）。

## Result
- **PR**：https://github.com/twjackysu/JackyAIApp/pull/466（分支 `claude/exciting-cray-lm9zkb`）。後端：新增 `Services/Portfolio/AsOfPriceSeries.cs`（`AsOfPriceSeries<TKey>`、`ForStock`／`ForFund`、`AsOfPriceBook`），`PortfolioService.cs` 的 `ValueAsOfDate`（drawdown、summary 日漲跌）與 `GetBenchmarkComparisonAsync` 的 `InvestedValueAsOfDate` 改用它；測試新增 `AsOfPriceSeriesTests`（17）與 `PortfolioAsOfValuationTests`（19）。
- **實作**：每個 request 把每檔價格序列排序一次，之後 `CloseOnOrBefore(day)` 二分搜尋，沒有任何 `<= day` 的價格回 `null`（兩個呼叫端對「沒有價格」與「價格為 0」處理不同，所以不折成 0）。來源集合只列舉、不就地改動。`ValueAsOfDate` 的 `stockMap`／`fundMap` 兩個參數換成一個 `AsOfPriceBook`。API、DTO、輸出數字、`GetNetWorthHistoryCoreAsync` 都沒動。
- **Requirement 3（同日重複）**：TW 持久層有 `(Symbol, TradeDate)` unique index 且 `Date` 一律午夜，今日疊加是取代最後一筆 → 不會重複；TW 直通、Yahoo（`Date` 含時間成分）、`FundDataService`（`List.Sort` 非穩定）都不去重，**無法排除**同日重複（讀碼結論，沙箱連不到上游，未實測）。因此沿用兩處原本的語意（含時間成分的 `Date` 取最大；完全相同取原序列最先出現者），**未統一**；已在 PR 說明記下 net-worth cursor 版（取最後一筆）在同日重複時與這兩處不一致。
- **測試**：新增 36 個、既有測試未改任何一行。舊 LINQ 當 oracle 的隨機比對（60 個種子）、endpoint 層的基金／缺價／零價／同日重複測試——**這 19 個在舊實作上同樣全過**（暫時還原 `PortfolioService.cs` 跑過）。變異檢查（反轉同值順序）有 3 個測試轉紅，已還原。`dotnet test --filter "Category!=LiveContract"`：**1344 通過、0 失敗**（原 1308 ＋ 36）。.NET 10.0.112 是我在沙箱用 `apt` 裝的。
- **前後量級**（合成資料、沙箱單次、僅供說明）：30 檔 × 750 筆 × 1095 天 = 32,850 次查詢，舊 ≈ 431 ms → 新（含排序）≈ 9 ms。比任務書估的「秒級」小；沒有對正式環境量測。
- **已知限制**：基金 `dayKey` 沿用原本的 `date.ToString("yyyy-MM-dd")`（未改 culture）；benchmark 路徑的 `stockMap` 含 benchmark symbol，會多排序幾檔，未特別過濾。
- **流程偏離**：本次 session 只能推到指定分支，`IN_PROGRESS` 與 `PR_OPENED` 都在 `claude/exciting-cray-lm9zkb` 上，沒有照 Workflow conventions 推到 master（只動 `.agent/**`，不會觸發部署）。

## Review
（Tech Lead 填寫）
