# TASK-043：回落煞車卡片 — 資料時間與「重新整理資料」按鈕（需求 8，#431）

## Owner
Frontend Engineer

## Status
BLOCKED（待 TASK-041 的端點契約合併；可先依 `ARCHITECTURE.md` 契約用 mock 平行開發，整合驗證需 041）

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
TASK-041（端點與 DTO 欄位）、TASK-042（同一張卡片，先合併可避免衝突）。

## PR
（待填）

## Result
（待填）

## Review
（待填）
