# TASK-060：Portfolio 回撤圖／基準圖接上 `backfill` 狀態（輪詢、進度說明、更新過時的提示）

## Owner
Frontend Engineer

## Status
PR_OPENED

## Priority
MEDIUM（後端已部署並開始補資料，但畫面上現在寫的說明是錯的；純前端、可逆）

## Objective
Boss 直接合併的 #488（`Build and deploy` run 576 成功，正式環境＝`f0e5433`）讓 `GET /api/portfolio/drawdown?days=` 與 `GET /api/portfolio/benchmark-comparison` 在範圍超過約 6 個月時，把更早的台股股價排進**背景逐月補齊**，回應多一個欄位 `backfill { state, coveredFrom, targetFrom }`（`PortfolioDto.cs` 的 `HistoryBackfillDto`）。前端還沒跟上，現況有三個落差：

1. `apis/portfolioApis/types.ts` 的 `DrawdownAnalysis`、`BenchmarkComparison` 沒有 `backfill` 欄位（`grep -i backfill` 只命中一行不相干的註解）。
2. 沒有任何輪詢。選 1Y／3Y 時第一次回應只含「已存好的月份」，補齊在背景進行；RTK Query 把第一次回應快取起來，使用者不換範圍、不重新整理就永遠看不到補好的結果。
3. `HistoryLimitNote.tsx`（#485）對 `days > 183` 一律寫「目前只保留約 6 個月的股價歷史…圖會從約 6 個月前開始」。#488 之後這句對**台股**不再成立。

結果：使用者選 1Y，看到補到一半的圖，旁邊的話說資料只有 6 個月，而且不會自己更新。

## Requirements
1. **型別**：新增 `HistoryBackfill { state: 'Complete' | 'Running' | 'Stalled'; coveredFrom: string; targetFrom: string }`（日期為 `yyyy-MM-dd`），並在 `DrawdownAnalysis`、`BenchmarkComparison` 加 `backfill?: HistoryBackfill | null`。後端有 `JsonStringEnumConverter`（`Program.cs:162`），所以 `state` 是字串；欄位名稱以後端實際序列化為準（先對照 `HistoryBackfillDto`）。
2. **輪詢**：`state === 'Running'` 時每 **15 秒**重抓一次（RTK Query `pollingInterval`，加 `skipPollingIfUnfocused: true`），不是 `Running`（含換到 ≤183 天的範圍、回應沒有 `backfill`）就停。**`Stalled` 不輪詢**——Stalled 代表「缺資料但目前沒人在補」（某個月連續失敗，或佇列滿了），後端 10 分鐘內不會重排，輪詢只會白白打 DB；使用者重新整理即可再試。
   - 注意循環：`pollingInterval` 要由「同一個 hook 回來的資料」決定，不能在宣告 hook 之前讀它。常見做法是一個 `useState` 存間隔，再用 `useEffect` 依 `backfill?.state` 更新；實作方式由你選。
   - 間隔**不要小於 10 秒**：每次請求都會讀一次已存的股價（3Y × 持股數 筆，`AsNoTracking`），資料庫是 Basic 5 DTU。
3. **說明文字**（取代 `HistoryLimitNote` 現在那句靜態文字；兩張圖共用同一個元件）：
   - `Running`：說明正在補齊更早的股價，帶出 `coveredFrom`（已補到）與 `targetFrom`（目標），並說明圖與數字會隨補齊更新（補齊期間最大回撤、報酬等數字是暫定的，這一點要寫出來）。
   - `Stalled`：說明部分股票更早的股價暫時抓不到、稍後重新整理會再試。
   - `Complete` 或 `backfill` 為 null：**不得再出現「只保留約 6 個月」**。若 `days > PRICE_HISTORY_DAYS` 且圖的第一個點比「今天 − days」晚超過 14 天，補一行中性說明「圖從 {第一個點的日期} 開始」，原因寫成「更早沒有持倉紀錄或沒有價格」；美股目前**不**補更早的歷史（`CLAUDE.md` Portfolio 一節），這種情況就是它。其餘情況不顯示任何說明。
   - 文案由你擬，PR 說明逐字列出，Boss 可改。只描述事實，不下「資料不準」之類的判斷，也不承諾補齊時間（後端是每秒約一個請求，沒有 ETA）。
4. 兩張圖（`DrawdownChart.tsx`、`BenchmarkChart.tsx`）都要接；`isFetching` 的 `LinearProgress` 照舊，不要拿它當補齊進度。
5. 手機寬度（≤400px）下文字要自動換行，不造成橫向捲動。
6. **只改前端**。不動 `JackyAIApp.Server`、`.github/workflows/`、`CLAUDE.md`。若發現後端契約不夠用（例如要拿第一個有資料的日期），在 PR 說明提出，不要自己改後端。

## Acceptance Criteria
- 新增前端測試（Vitest + Testing Library，沿用 `HistoryLimitNote.test.tsx` 與 Portfolio 其他圖表測試的寫法，mock RTK hook）：
  - `Running`：說明含 `coveredFrom`／`targetFrom` 的日期；輪詢間隔被設為正值。
  - `Stalled`：有對應說明；**不**輪詢。
  - `Complete`：畫面上沒有「6 個月」字樣；不輪詢。
  - `backfill` 為 null 且 `days ≤ 183`：不顯示任何說明。
  - 圖第一個點明顯晚於範圍起點、`backfill` 為 null：顯示「圖從 … 開始」那行。
  - 既有的 `HistoryLimitNote.test.tsx` 要跟著改（它測的是舊行為），不是刪掉。
- 回撤圖與基準圖兩邊都有上述行為（可共用元件，各一個 smoke 測試確認有接上）。
- `npm run lint`（0 warnings）、`npm run build`、`npm test` 全過。
- 手機寬度的畫面在 PR 說明寫明你怎麼確認；沒辦法實機就寫「僅依 CSS 判斷、未實機」，不要宣稱已驗證。

## Technical Notes
- 相關檔案：`Portfolio/HistoryLimitNote.tsx`（+ `.test.tsx`）、`Portfolio/DrawdownChart.tsx`、`Portfolio/BenchmarkChart.tsx`、`Portfolio/dayRanges.ts`（`PRICE_HISTORY_DAYS = 183`、`WIDE_RANGES`、`DEFAULT_WIDE_RANGE_DAYS = 90`）、`apis/portfolioApis/index.ts`（`getDrawdown`、`getBenchmarkComparison`）、`apis/portfolioApis/types.ts`。
- 後端行為（讀 `PortfolioService.ExtendHistoryAsync`、`DeepHistoryService`、`TwHistoryBackfillService`）：範圍起點 `>= 今天 − 5 個月` 時完全不補，回應 `backfill` 為 null。需要補時，**第一次請求**就把缺的月份排進背景佇列並立刻回傳，內容是「已存好的月份」；`coveredFrom` 是所有被問到的台股都有價格的最早日子（不是單一股票）。補齊是月份優先、新到舊，所以圖會往左一段一段變長。佇列在記憶體裡：App 重啟會清掉，下一次請求會依資料庫裡的進度重排，不會從頭來。
- `state` 的含義：`Complete`＝要的都已存好；`Running`＝背景還在抓；`Stalled`＝缺資料且沒人在做。依讀碼，`Running` 會在有限步數內變成另外兩者之一（每個月份不是存好就是連續失敗 3 次後放棄 10 分鐘），所以輪詢不需要自己的逾時上限；這是讀碼推論，沒實測。
- 沙箱沒有 .NET，也連不到正式環境；我**沒有**在正式環境看過補齊實際在跑，也不知道它補完一個典型持倉要多久（閘道預設每秒約一個請求，只是設定值、未量測）。Result 區請寫你實際驗證了什麼、沒驗證什麼。
- 流程：開工前先把本檔 `Status` 改 `IN_PROGRESS` 並直接 push 到 master（`ARCHITECTURE.md`「Workflow conventions」；若 session 只能推指定分支，照 TASK-052／053 的做法在 Result 註明）；完成後開 PR，`Status` 改 `PR_OPENED`、填 Result。
- **重複工作提醒**：這個缺口出自 Boss 直接合併的 #488，Boss 可能自己接著補。開工前先看 `master` 的 `HistoryLimitNote.tsx` 與 `types.ts` 是否已經有 `backfill`；已有就把本任務標 `CANCELLED` 並在 Result 註明，不要重做。

## Dependencies
無（#488 已部署）。

## Result
- **PR**：https://github.com/twjackysu/JackyAIApp/pull/490（分支 `claude/nice-allen-szv1lq`）。純前端，無後端、`.github/workflows/`、`CLAUDE.md` 改動。開工前 `master`（`38d1134`）的 `HistoryLimitNote.tsx`／`types.ts` 都還沒有 `backfill`，Open PR 為 0，故未取消。
- **實作**
  - `apis/portfolioApis/types.ts`：新增 `HistoryBackfill { state: 'Complete' | 'Running' | 'Stalled'; coveredFrom; targetFrom }`，`DrawdownAnalysis`、`BenchmarkComparison` 加 `backfill?: HistoryBackfill | null`。欄位名稱與字串列舉已對照 `HistoryBackfillDto`、`BackfillState`、`Program.cs` 的 `JsonStringEnumConverter` 與預設 camelCase。
  - 新增 `Portfolio/useBackfillPolling.ts`：`backfillPollingInterval(backfill)`（只有 `Running` 回 15000，其餘 0）與 `useBackfillPolling(backfill, setPollingInterval)`（effect）。圖表先宣告 `useState(0)` 當 `pollingInterval`，傳給 `useGet…Query(arg, { pollingInterval, skipPollingIfUnfocused: true })`，再由同一個 hook 回來的 `backfill` 更新，解掉「間隔要先於 hook 宣告」的循環。
  - `HistoryLimitNote.tsx` 改為依 `backfill`／`firstPointDate` 顯示；`DrawdownChart.tsx`、`BenchmarkChart.tsx` 各接上。`backfill`、第一個點、輪詢都取自 `currentData`（目前這個範圍的回應），所以換範圍、新範圍還沒回來時不會拿上一個範圍的狀態去輪詢或說明；圖本身仍用 `data`（換範圍時保留舊圖），`isFetching` 的 `LinearProgress` 照舊。
  - 說明文字**不再依賴 `days > 183` 才出現**：後端 `backfill` 非 null 就有說明。這是刻意的——6M＝180 天，起點早於「今天 − 5 個月」，後端也會排補齊，所以 6M 也可能是 `Running`。只有 `Complete`／null 才回到 `days > 183` 的「圖從…開始」判斷。
- **文案（逐字，Boss 可改；`{…}` 為 `yyyy-MM-dd`）**
  - `Running`：「正在補齊更早的股價（已補到 {coveredFrom}，目標 {targetFrom}）。圖與數字會隨補齊自動更新；補齊期間最大回撤、報酬等數字是暫定的，補齊後可能改變。」
  - `Stalled`：「部分股票更早的股價暫時抓不到（已補到 {coveredFrom}，目標 {targetFrom}），目前沒有在補。稍後重新整理頁面會再試。」
  - `Complete`／null，且 `days > 183`、第一個點比「今天 − days」晚超過 14 天：「圖從 {第一個點的日期} 開始——更早沒有持倉紀錄，或沒有價格。」
  - 其餘情況不顯示。
- **測試**：`HistoryLimitNote.test.tsx` 改寫（舊的「約 6 個月」斷言換成新行為，並加上「任何狀態都不得出現『6 個月』」）；新增 `useBackfillPolling.test.ts`、`DrawdownChart.test.tsx`、`BenchmarkChart.test.tsx`（mock RTK hook 與 `ReactECharts`，斷言 hook 第二個參數的 `pollingInterval`／`skipPollingIfUnfocused`；涵蓋 Running→Complete 輪詢停止、Stalled 不輪詢、Complete 無「6 個月」、null＋late start 顯示「圖從…開始」）。變異驗證：把「只有 `Running` 才輪詢」改成「任何有 `backfill` 的都輪詢」，7 個測試失敗，還原後通過。本機實際執行：`npm run lint`（0 warnings）、`npm run build`、`npm test`（73 檔／775 測試）全過。
- **手機寬度（≤400px）**：僅依 CSS 判斷、未實機。說明是 `Typography caption` 區塊（`display: block`），加了 `overflowWrap: 'anywhere'`，中文可任意字元換行、日期不會撐出橫向捲動。
- **沒驗證的**：(1) 沙箱沒有 .NET、連不到正式環境，**沒有**對著真的後端看過 `Running → Complete` 的實際回應與補齊要多久；測試用的是依 `HistoryBackfillDto` 手寫的 fixture。(2) 15 秒間隔與 `skipPollingIfUnfocused` 只驗證了傳給 RTK Query 的參數，沒有在瀏覽器觀察實際請求節奏。(3) 「Running 一定在有限步數內變成另一個狀態」是任務書的讀碼推論，前端沒有加逾時上限，也沒驗證。
- **觀察（不在本任務範圍，未處理）**：`Stalled` 只靠重新整理重試；目前頁面上沒有「重試」按鈕，若 Boss 想要可另開任務。圖表在 `points.length === 0` 時整張換成「資料不足」，此時即使 `Running` 也不會顯示補齊說明——第一個回應若是空的，使用者只會看到「資料不足」，但輪詢仍會進行（hook 在 early return 之前）。
- **流程偏離**：本次 session 只能推指定分支 `claude/nice-allen-szv1lq`，`IN_PROGRESS` 沒有推到 master（與 TASK-052／053 相同）。

## Review
（尚無）
