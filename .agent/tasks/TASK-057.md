# TASK-057：綜合分析的評分／權重／風險改在瀏覽器計算，調整設定即時生效（Issue #464 IMP-2 第 2 步）

## Owner
Frontend Engineer

## Status
CANCELLED（2026-10-16 Boss 選 (1)：全部留後端、取消 IMP-2——評分只留後端一份，不做前端移植。未開工。）
（原狀態：BLOCKED（等 TASK-056 合併，需要它產生的黃金資料 `stockScoringGolden.json`））

## Priority
MEDIUM

## Objective
Boss 決定（Issue #464，2026-10-16）：評分邏輯只留一份，放前端；C# 版刪除（TASK-058）。目前 `/finance`「量化分析」：

- 前端把旗標與三個權重送給 `POST /api/finance/comprehensive-analysis`，後端算出面向分數、總分、方向、建議文字、面向摘要、風險評估；設定被併進快取 key，每種組合各佔一份；拖權重滑桿**不會**更新畫面，要重新查詢。
- 目標：後端固定回傳**全部指標**（快取 key 只剩 代號＋日期＋市場，全站共用）；前端依旗標過濾、正規化權重、計算所有分數與文字。**拖滑桿／勾選面向立刻更新，不再打 API。**

## Requirements
1. **請求形狀**：`getComprehensiveAnalysis` 一律只送 `{ stockCode, market }`（不送旗標與權重）。這個形狀對**現有後端**（旗標預設全開、權重為空）與 TASK-058 之後的後端都成立，所以本任務**不依賴後端改動**、可獨立部署。量化分頁與比較分頁（`DEFAULT_CONFIG`）都是。
2. **新增純函式模組** `Finance/utils/stockScoring.ts`（名稱可自訂），輸入 `indicators` 與 `AnalysisConfig`，輸出 `{ indicators（過濾後）, scoring | null, risk | null }`。必須**逐項重現**現有 C# 的行為（來源：`JackyAIApp.Server/Services/Finance/Scoring/StockScoreService.cs` 的 `ComputeCategoryScore`／`DetermineOverallDirection`／`GenerateRecommendation`／`AssessRisk`／`GenerateCategorySummary`、`CategoryWeightConfig.GetNormalizedWeights`，與 `Builder/StockAnalysisBuilder.cs` 的 `BuildAsync` 過濾段與 `BuildScoring`）：
   - 旗標過濾：`includeTechnical/Chip/Fundamental` 為 false 時該面向的指標全部移除（指標明細表也不顯示它們——與現況一致）。
   - 只有「過濾後仍有指標」的面向才參與權重；權重在**這些面向之間**正規化；總和為 0 時**不正規化**（加權分全為 0）。
   - `includeScoring=false` → `scoring` 為 `null`；`includeRisk=false` → `risk` 為 `null`；過濾後無指標 → 兩者皆 `null`。
   - **要原樣保留的怪癖（不要順手修）**：風險評估永遠用「`scoring` 的 `categoryScores`（`scoring` 為 `null` 時為空）」計算，所以 `includeScoring=false` 時風險因子「各面向分數差距大，訊號不一致」不會出現。這是現行行為；改它是產品決定，不在本任務。
   - `categoryScores` 的順序＝各面向在（過濾後）指標清單中**第一次出現**的順序（卡片就是照這個順序畫）。
   - 全部中文文字（建議、面向摘要、風險因子）**逐字**照搬。
3. **數值地雷（務必處理，黃金資料有專門案例）**：C# 用 `decimal` 與 `Math.Round(x, 1)`（**half-even／銀行家進位**），JS 是 double 與四捨五入。
   - 指標 `score` 是整數；面向平均＝`總分 / 個數` 是有理數。請用**整數運算**判斷 `>= 80/60/40/20`（例如 `總分 >= 80 * 個數`），不要先除成浮點再比。
   - 進位到 0.1 請用整數（以「十分位」為單位）的 half-even 輔助函式，不要用 `Math.round`／`toFixed`；權重請先轉成**整數百分比**（滑桿是 5% 一格，`Math.round(w * 100)`）再做正規化與相乘，避免 `0.35` 這類二進位誤差。
   - **總分是「各面向已進位的加權分」的總和**（再拿這個總和判方向）：請用十分位整數相加，不要累加浮點小數（`0.1 + 0.2` 會讓 60.0 變成 59.99999999999999 而判錯方向）。
   - 風險的分歧分數＝`min(多, 空) / 總數 × 200`：閾值比較（`> 30`、`> 50`、`> 60`）用整數交叉相乘；顯示值才進位。
   - `RSI`／`DirectorPledge`／`VolumeRatio` 的 `value` 是 JSON 數字（來自 decimal），閾值比較（`> 80`、`< 20`、`> 30`、`> 3`）直接用；因子文字裡的 `F1` 格式，C# 的 decimal 是「**遠離零**」進位——請在數字的十進位字串上處理（`String(x)`），不要用 `toFixed(1)`（它依二進位值，`80.05` 會變 `80.0`）。
   - 若某個黃金案例在「重複小數的近似平手」情形下仍與 C# 差 0.1（C# 的 28 位十進位中間值造成），**不要硬湊**：在 PR 說明列出案例 `id` 與兩邊的值，由 Tech Lead 決定是否接受。預期不會發生。
4. **黃金資料測試**：`stockScoring.golden.test.ts` 載入 `__fixtures__/stockScoringGolden.json`（TASK-056 產生），逐案例比對 `indicators` 順序、`scoring`、`risk`。分數、方向、文字、`factors` 必須**完全相等**；正規化後的 `weight` 以 `toBeCloseTo(…, 12)` 比對。另補幾個你自己的小測試（進位 helper、整數判斷）。
5. **Hook 與畫面**：
   - `useFinanceAnalysis`：`analysisResults` 保存**原始**伺服器結果；新增 `useMemo` 衍生出「已計分」的結果（依 `analysisConfig`）；`analysisConfig` 變動不得觸發任何請求。比較分頁的每個 slot 以 `DEFAULT_CONFIG` 同樣方式衍生。
   - `ComprehensiveAnalysisResult`、`OverallScoreSection`、`RiskSection`、`ComparisonResult`、`StockIdentitySummary` 等讀 `scoring`／`risk` 的地方改吃衍生結果；畫面外觀與文案**不變**。指標明細表顯示**過濾後**的指標。
   - `AnalysisConfigPanel`：行為改為即時生效，沿用現有文案（「權重會自動正規化」）；若有任何文字暗示「下次查詢才生效」請移除。
   - `apis/financeApis/types.ts`：`StockAnalysisRequest` 縮成 `{ stockCode, market? }`；`StockAnalysisResultData` 移除 `scoring`／`risk`／`configuration`（TASK-058 之前伺服器仍會多送，多餘欄位被忽略即可），新增前端計分結果的型別（可重用 `CategoryScore`／`RiskAssessment`）。
6. 既有測試（`useFinanceAnalysis.test.tsx`、`ComparisonResult.test.tsx` 等）依新形狀調整**輸入資料**，但**不得放寬斷言**；新增：改權重／勾選面向後 `getComprehensiveAnalysis` **沒有**再被呼叫、畫面分數改變。

## Acceptance Criteria
- `npm run lint`（0 warning）、`npm run build`、`npm test` 全過；黃金資料測試全部案例通過。
- 手動／測試驗證：同一檔股票查一次後，拖動權重滑桿、勾選／取消三個面向、切換「評分」「風險」顯示，畫面立即更新且 Network 沒有新請求（測試以 mutation 呼叫次數斷言）。
- 與現有行為一致的情形（PR 說明逐項勾）：關掉一個面向 → 權重在剩下的面向間重分配；三個權重都拉到 0 → 總分 0；`includeScoring` 關閉 → 沒有綜合評分儀表與面向卡片，但風險區塊仍在（且沒有「面向差距」因子）。
- 未在手機／真實瀏覽器驗證的部分（≤400px 畫面）在 PR 說明誠實寫明。

## Technical Notes
- **部署順序安全**：本任務單獨部署時，伺服器仍會計算並回傳 `scoring`／`risk`（被忽略），只是浪費；TASK-058 再移除。反過來，若有人還開著舊版 SPA 分頁，TASK-058 部署後它拿到 `scoring: null`、畫面不顯示分數，重新整理即恢復——可接受，不需處理。
- 比較分頁（多檔）以前每檔一次 POST、各自佔一份快取（因為 `DEFAULT_CONFIG` 的權重在 key 裡）；改後與量化分頁共用同一份快取（key 只剩代號＋日期＋市場）。
- 不引入新的相依套件（不要為了十進位而加 decimal.js；整數運算足夠，見需求 3）。
- 這是「投資建議文字與分數」的移植：Tech Lead 審閱時會逐行對照 `StockScoreService.cs`，並抽查黃金資料之外的案例。
- 流程：本檔 `Status` 在 TASK-056 合併後由 Tech Lead 改為 `READY`；開工前請先把 `Status` 改 `IN_PROGRESS` 並直接 push 到 master（若 session 只能推指定分支，在 Result 註明）；完成後開 PR，`Status` 改 `PR_OPENED`、填 Result。
- 只改前端；不要動後端、`.github/workflows/`、`CLAUDE.md`。

## Dependencies
TASK-056（黃金資料）。

## Result
（Engineer 填寫）

## Review
（Tech Lead 填寫）
