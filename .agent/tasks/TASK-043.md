# TASK-043：回落煞車卡片 — 資料時間與「重新整理資料」按鈕（需求 8，#431）

## Owner
Frontend Engineer

## Status
MERGED（PR #442，Boss 於 2026-10-11 合併；Build and deploy run 531 成功）

## Priority
MEDIUM（PM 需求 8；第二階段必備）

## Objective
卡片顯示「資料有多新」，並讓使用者手動重新取資料（重新整理訊號與 0050／00631L 價格），來源失敗時誠實標示「資料未更新」。

## Requirements
1. **資料時間**（卡片內固定一行，所有非載入狀態都顯示）：
   - 「訊號資料日期：{asOfDate}」（證交所收盤後公布的最新交易日）。
   - 「伺服器最近檢查：{lastCheckedAt 當地時間}」；`lastCheckedAt` 為 null 時省略此句，不顯示 1970 或 Invalid Date。
   - 價格時間：若 `summary.positions` 已有價格時間欄位就顯示；沒有就不顯示並在 PR 說明（**不要**為此改後端契約）。
2. **「重新整理資料」按鈕**：呼叫 `POST /api/portfolio/drawdown-brake/refresh`（RTK Query mutation），成功後用回應的 `signal` 更新 `getDrawdownBrake` 快取。依 `result` 顯示：
   | result | 顯示 |
   |---|---|
   | `Updated` | 「已更新到 {asOfDate}」 |
   | `NoNewData` | 「目前沒有更新的資料」 |
   | `SourceUnavailable` | 「資料未更新：證交所暫時取不到，稍後再試」（保留原資料，不得顯示成功） |
   | `Throttled` | 「剛剛已有人更新過，請 {retryAfterSeconds} 秒後再試」；按鈕停用並倒數 |
   - 請求進行中按鈕停用；`DrawdownBrakeNotEnabled`／網路錯誤顯示通用錯誤，不卡死。
3. **價格重新整理**：按鈕同時對卡片實際使用的標的（0050、00631L 中使用者持有者）呼叫既有 `POST /api/portfolio/holdings/{symbol}/refresh-price`，再刷新 portfolio summary 快取。**客戶端冷卻 30 秒**，避免連按放大對報價來源的請求；沒持有就不呼叫。
4. 固定警語與模擬值註記在所有狀態仍常駐（既有測試維持）；不改任何 `advise`／`composition` 邏輯；第二階段的 `estimate`／`estimateCorrected` 渲染不動。
5. 手機寬度（360／400px）無橫向捲動（沿用 #432 的量測方式）。

## Acceptance Criteria
- `npm run lint`（0 warnings）、`npm run test`、`npm run build` 全過。
- 測試：四種 `result` 各一；`lastCheckedAt` 為 null；冷卻倒數與按鈕停用；沒有持股時不呼叫 refresh-price；refresh 失敗時舊資料仍在、且不顯示「已更新」。
- 功能關閉（`drawdownBrakeEnabled` 不為 true）時仍不渲染、零請求（既有行為）。

## Technical Notes
- 為什麼不是每個使用者冷卻 60 秒：全站冷卻由後端負責（TASK-041），前端只負責顯示；價格的 30 秒冷卻是前端對既有端點的保護。
- 不要輪詢；只在使用者按下按鈕時請求。

## Dependencies
TASK-041（端點與 DTO 欄位）、TASK-042（已合併）、**TASK-046（需求 12：同一張卡片大改——分桶表、對帳行、移除策略現金輸入；043 應排在 046 之後開工，或至少從 046 合併後的 master 開分支，避免卡片衝突）**。

## PR
https://github.com/twjackysu/JackyAIApp/pull/442（`claude/nice-allen-onfrhi` → `master`）

## Result
- 實作：`refreshDrawdownBrake` mutation（回應的 `signal` 以 upsert 寫回 `getDrawdownBrake` 快取，失敗時快取不動）；`Portfolio/DrawdownBrakeRefresh.tsx`（資料時間＋按鈕＋回饋）、`drawdownBrakeRefresh.ts`（時間格式、倒數純函式）、文案進 `drawdownBrakeCopy.ts`。`DrawdownBrakeDto.lastCheckedAt`／`generatedAt` 改為必填。
- 驗證：`npm run lint`（0 warnings）、`prettier --check`、`npm run test`（55 檔 596 項）、`npm run build` 全過；15 處變異測試皆被抓到；Chromium 實測 6 情境 × 1280／400／360px 無橫向捲動、無 console error，請求次數與快取更新符合預期。截圖（示意資料）：`TASK-043/desktop-1280-throttled.png`、`mobile-360-updated.png`、`mobile-360-error.png`。
- 與任務書字面不同／自行選擇（詳見 PR 描述）：錯誤且無訊號資料時不顯示資料時間與按鈕；不論 `result` 都以回應 `signal` 更新快取；按下後一律客戶端冷卻 30 秒；價格時間不顯示（`PositionDto` 沒有該欄位）；`SignalSummary` 的「資料截止日」與新「訊號資料日期」重複未動。
- 已知限制：重新整理只涵蓋 0050／00631L 的報價（範圍內其他持股報價失敗不會被重抓，需 Tech Lead 決定是否另開任務）；`refresh-price` 每次回整份 summary；客戶端冷卻只活在該次頁面載入。
- 流程：本 session 僅限推 feature branch，未能先把 `IN_PROGRESS` 推到 master；未動 `PROJECT_STATUS.md`。

## Review
**Tech Lead 事後審閱（2026-10-11，排程執行第 3 次；PR 由 Boss 先行合併並已部署，當時未走審閱）**
- 依任務書 Result、PR CI（run 648／649 成功）與部署結果核對，未逐行讀 diff；Engineer 的自選項（錯誤且無訊號時不顯示按鈕、一律客戶端冷卻 30 秒、價格時間不顯示）皆**可接受**。
- **Engineer 提出的「已知限制」＝重新整理只涵蓋 0050／00631L 的報價——Tech Lead 決定**：需求 12 之後，範圍內任一持股價格失敗會暫停整張卡片的建議，使用者唯一的手段是重新載入頁面。已列入 **TASK-047 的 Optional 項**（重新整理同時重抓 `scope.priceFailed` 的持股，上限 5 檔），不另開任務。
- 結論：無須追加修正。
