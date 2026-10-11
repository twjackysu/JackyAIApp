# TASK-045：Stock Intelligence「過去 5 年同期間走勢」圖（前端）（#431 延伸）

## Owner
Frontend Engineer

## Status
PR_OPENED（2026-10-11 Frontend Engineer；PR #439，待 Tech Lead 審查）

## Priority
MEDIUM（PM：A 圖；Boss 2026-10-11 02:38 UTC 新增，DECISION-016）

## Objective
在 `/finance` 的 Stock Intelligence 顯示一張線圖：同一段月/日區間，今年與前 4 年共 **5 條線**，每年一色、有圖例，供使用者參考（不是預測）。標的＝加權指數（價格指數，不含息）。資料來自 TASK-044。

## Requirements
1. **位置**：`Finance/index.tsx` 的 `!hasStockResults` 區塊內、`<IndicatorExplorer />` 之後（大盤層級內容；下方四個分頁是個股分析，不放那裡）。新元件 `Finance/components/MarketSeasonalChart.tsx`，純計算放 `Finance/utils/seasonal.ts`（可單測）。圖用既有 `charts/ReactECharts.tsx`（需要的 ECharts 元件若尚未註冊，依該檔註解註冊到 feature-local 模組，不要拖大其他路由的 chunk）。
2. **資料**：RTK Query `useGetMarketIndexHistoryQuery`（`apis/financeApis`，型別放 `types.ts`），`keepUnusedDataFor: 3600`。契約見 TASK-044。**不需登入**。
3. **期間**：預設 **今年 1/1 ~ `asOf` 的月/日**（年初至今）；使用者可改起訖「月＋日」（兩個選單；起 > 迄、不存在的日期如 2/30 要擋並提示）。修改後 5 條線同步更新（純前端計算，不再打 API）。
4. **對齊與標準化**：
   - 每一年取該年「起始月/日當日或之後第一個交易日」的收盤為基準＝100（該年區間內無任何交易日則該線省略並註明）。
   - X 軸為月/日（以閏年參考年排列，2/29 可存在；非閏年沒有該點即可）。
   - 今年的線只畫到最後一個有資料的日期，**不延伸、不預測**；其他年畫滿區間。
   - Tooltip（滑過／點按）：每條線顯示該年「不晚於滑到的月/日」的最近一個交易日的**實際收盤價與實際日期**（遇休市日沿用前一交易日並標出其日期，不報錯、不留空白）。
5. **顏色**：5 條線顏色明顯不同且在深色／淺色主題皆可辨識（沿用主題 palette，今年的線加粗）；圖例標示年份，可點圖例顯示／隱藏單條線。
6. **常駐文字**（所有狀態都顯示，包含載入中與錯誤）：標的「加權指數（價格指數，不含息）」；警語「過去走勢僅供參考，不代表未來表現，不構成投資建議。」；資料日期「資料至 {asOf}」。**畫面上不得出現任何預測性文字**或由過去年份推算的數字（例如「預期」「看漲」「平均上漲」）。
7. **狀態**：載入中（骨架）；錯誤或後端回 `MarketHistoryUnavailable` → 「資料暫時無法取得」（不顯示空圖或部分資料）。
8. 手機寬度（360／400px）無橫向捲動、圖例可讀（可換行／捲動圖例）。

## Acceptance Criteria
- `npm run lint`（0 warnings）、`npm run test`、`npm run build` 全過。
- `seasonal.ts` 單元測試：閏年 2/29、起始日為假日（取之後第一個交易日）、起迄跨月、起 > 迄、該年區間無交易日、今年資料只到中途、休市日沿用前一交易日、基準＝100。
- 元件測試：警語／標的／資料日期在載入中、錯誤、正常三種狀態都在；錯誤顯示「資料暫時無法取得」；5 個年份出現在圖例；改期間後重新計算且**不重新打 API**。
- 手機寬度量測方式同 PR #432（Playwright harness，不提交 harness）。
- 無 AI、無 `[UsageLimit]`、無登入要求。

## Technical Notes
- 價位差很大（2022 年約 1.5 萬、現在約 5 萬點），所以必須標準化為起點＝100；Tooltip 才顯示實際價。
- 回落煞車的任何程式不要動；本圖與持股／Portfolio 無關。
- 相似區段圖（B）**不在本任務**，等 Boss 看過 Tech Lead 的演算法提議與歷史實例（`ARCHITECTURE.md`）再決定。

## Dependencies
TASK-044 的回應契約（可平行開發，用 mock；合併順序不拘，整合驗證需 044）。

## PR
https://github.com/twjackysu/JackyAIApp/pull/439 （分支 `claude/nice-allen-n9jgz8`）

## Result
**實作摘要（2026-10-11 Frontend Engineer）**
- `Finance/utils/seasonal.ts`（純計算）＋ `seasonalChartOption.ts`（ECharts option／tooltip 文字）＋ `seasonalCopy.ts`（文案集中）＋ `Finance/components/MarketSeasonalChart.tsx`；`apis/financeApis` 新增 `getMarketIndexHistory`（`keepUnusedDataFor: 3600`）與型別；元件放在 `Finance/index.tsx` 的 `!hasStockResults` 區塊、`IndicatorExplorer` 之後。ECharts 需要的元件都已在共用 `charts/echarts.ts` 註冊，沒有新增註冊。
- 測試：`seasonal.test.ts`（30）、`seasonalChartOption.test.ts`（9）、`MarketSeasonalChart.test.tsx`（14）；`npm run lint`（0 warnings）、`npx prettier --check`、`npm run build`、`npm run test`（52 檔／476 項）皆於本機實際執行並通過。
- 版面：Playwright + Chromium 對 production build（API 全 stub，harness 不提交）量測 1280／400／360px × 預設、tooltip 開啟、跨農曆年區間、2/30 警示、還原，`scrollWidth == innerWidth`、無 console error。截圖（亂數合成資料，**非真實加權指數**）：`.agent/tasks/TASK-045/`（`desktop-1280-tooltip.png`、`mobile-360-default.png`、`mobile-360-tooltip.png`）。
- 實測中發現並修正：360px 下 `scroll` 圖例要翻頁（改 `plain` 會換行）、tooltip 超出圖表被裁掉（兩行排版＋`max-width: 90%`）。

**與任務書的差異／自行決定（請 Tech Lead 確認；PR 描述有同樣清單）**
1. 載入中／錯誤時「資料至 —」（尚無 `asOf`），使三種狀態都顯示該行。
2. 今年超過 `asOf` 的 tooltip 顯示「尚無資料」，不沿用 `asOf` 當天收盤（避免讀成今年線延伸）。
3. 起訖日期選單每月都列 1–31 日，不存在的日期（2/30）擋下並說明；起始日當天該年未開市時 tooltip 顯示「尚未開盤」。
4. 圖例用 `plain`（換行）而非 `scroll`；tooltip 另顯示「相對 X」（該年自己相對起點 100 的值，無跨年推算）。
5. 加了「年初至今」還原鈕（僅在使用者改過期間時出現）。
6. 改期間後圖例顯示／隱藏狀態會重置（`notMerge` 重畫），已知小限制。

**未驗證／依賴**
- 與真實後端的整合（`market-index-history` 回應）需 TASK-044 合併後在正式環境確認；欄位依任務書契約。
- 「改期間不重打 API」以 mock hook 驗證（range 不進 query、hook 只有無參數呼叫）；未另做網路層測試。

## Review
（待填）
