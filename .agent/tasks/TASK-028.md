# TASK-028：Portal 首頁「今日行動」區塊 + 訪客引導 + 卡片分群

## Owner
Frontend Engineer

## Status
PR_OPENED

## Priority
HIGH（Requirement：Portal 首頁「今日行動」化，Issue #409）

## Objective
Portal（`/`）已登入顯示「今日」區塊（streak、今日挑戰、待複習數、投資組合變化），訪客顯示價值說明＋登入入口，卡片分群。純前端組合既有 API。

## Requirements
1. 已登入「今日」區塊，每項可點擊直達：
   - streak：`statsApis` / `dailyChallengeApis.getStats`（與 `/stats` 顯示一致，用同一個來源）。
   - 今日每日挑戰：`dailyChallengeApis` 已完成→顯示完成；未完成→進入 `/daily`。
   - 待複習數：`reviewApis.getDueCount`（>0 才顯示，點擊進 `/review`）。
   - 投資組合總資產較上次變化：僅在有持倉時顯示，點擊進 `/portfolio`。
2. 無資料的項目整項不顯示（不顯示 0／空白）。
3. 未登入：一行價值說明 + 登入入口；English Coach（字典免登入）排最前。
4. 卡片分群：主力（English Coach、Wealth Portfolio、Stock Intelligence）與其他。**主力名單 Boss 已部分同意，但表示其餘六個頁面同樣重要** → 「其他」區不得隱藏或弱化到難以發現（例如摺疊預設展開，或只做視覺次要），由 Frontend 選最簡單的作法並在 PR 說明。
5. 各項請求獨立載入（skeleton／各自失敗不影響其他項），任何一個失敗只隱藏該項。
6. 不新增 AI 呼叫、不呼叫任何 `[UsageLimit]` 端點。

## Acceptance Criteria
- 見 REQUIREMENTS.md「Portal 首頁『今日行動』化」全部 7 條。
- 新增元件測試：已登入有待複習／無待複習／無持倉／訪客 四種情境。
- `npm run lint`、`npm run test`、`npm run build` 全過；≤400px 無橫向捲動。

## Technical Notes
- 投資組合變化：先確認是否有輕量端點可取「總資產 + 上次變化」。`getPortfolioSummary` 會觸發上游報價抓取（成本較高，見 TASK-025/026）；若無輕量資料，**不要**在 Portal 載入時呼叫它——改只顯示「持倉入口」或先略過該項，並回報 Tech Lead 評估是否需要新增輕量後端端點（另開 task）。
- 用 RTK Query `skip` 於未登入時跳過所有查詢。
- 先確認 `Portal/index.tsx` 現有版面與 `constants/apps.tsx`，不做無關重構。

## Dependencies
無。實作前先在 master 推 `Status: IN_PROGRESS`。

## Result
Frontend Engineer（2026-10-04）：
- 新增 `Portal/TodaySection.tsx`；`Portal/index.tsx` 加入訪客引導（價值說明 + Google 登入，`ReturnUrl=/`）、卡片分群。
- 資料來源（皆既有、輕量、非 `[UsageLimit]`）：`getStats`（streak + 今日挑戰，與 `/daily` StatsBar 同源）、`getDueCount`、`getUpdateHistory`（僅判斷是否有持倉）。三個查詢各自 skeleton／失敗只隱藏該項；0 streak／0 待複習／無持倉整項不顯示。
- **投資組合變化未做**：`getPortfolioSummary`／`net-worth-history` 都會抓上游報價（`FetchStocksAsync` deep fetch），依 Technical Notes 不在 Portal 載入時呼叫。目前只在有持倉時顯示「查看投資組合」入口。**請 Tech Lead 評估是否另開輕量後端端點**（回傳最新總資產 + 上次變化）。
- 分群：主力（English Coach、Wealth Portfolio、Stock Intelligence）在前；其餘六個以同樣卡片樣式、預設展開，置於「更多應用」標題下，未隱藏或弱化。
- 訪客：English Coach 排最前；字典路由本來就免登入。
- 測試：`Portal/Portal.test.tsx` 4 情境（有待複習／無待複習／無持倉＋失敗／訪客）。`npm run lint`、`npm test`（258 passed）、`npm run build` 皆通過。≤400px 僅以 CSS 檢視（單欄 grid、`minWidth: 0`），未實機驗證。

## Review
