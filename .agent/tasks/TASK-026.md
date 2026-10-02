# TASK-026

## Title
Portfolio 前端效能：分批載入圖表、避免首屏同時打滿 API（Issue #401）

## Owner
Frontend

## Status
READY

## Priority
MEDIUM

## Objective
Portfolio 頁首屏目前在 mount 時同時發出 `summary` + 3 個 `NetWorthHistory`（`NetWorthTrendChart` days=90、
`NetWorthChart` days=90、`FinancialChangesChart` days=180）+ `benchmark-comparison`(365) + `drawdown`(365)，
其中多數圖表在首屏下方。目標：讓 `summary`（主要內容）先完成，下方圖表延後/按需載入。

## Requirements
1. 圖表區塊（`Portfolio/index.tsx` 的 `ChartSectionCard` 內元件）改為「進入視窗才掛載/發 query」
   （IntersectionObserver 或 RTK Query `skip` 狀態；優先用最簡單且無新依賴的做法）。
2. `NetWorthTrendChart` 與 `NetWorthChart` 預設同為 `days=90, groupBy=assetType` 已由 RTK Query 去重；
   `FinancialChangesChart` 預設 180 另成一支請求 → 評估把預設改為 90 以共用快取
   **（屬顯示預設值變更：先確認不影響其既有 UX，若有疑慮回報 Tech Lead/PM，不要自行決定）**。
3. `summary` 載入中維持現有 skeleton/spinner；圖表區塊在未進入視窗時顯示固定高度 placeholder，避免捲動時版面跳動。
4. 不改 API 契約與後端。

## Acceptance Criteria
- 首屏 Network 面板只看到 `summary`（及使用者可見區塊所需 query）；捲動到各圖表才發對應請求。
- 既有 frontend 測試與 `lint-and-build-client` 綠燈；新增或更新 placeholder/懶載入的單元測試。
- 切換 range/groupBy 的既有行為（含 isFetching 提示）不變。
- PR 描述附改動前後首屏請求數對照。

## Technical Notes
- 相關檔案：`jackyaiapp.client/src/Portfolio/index.tsx`（~L600-630）、各 chart 元件的 `useGet*Query`。
- 若 `data-tour="portfolio-tour-chart"` 導覽（`NetWorthChart`）依賴元件已掛載，需確保 tour 開始時該區塊會被載入。
- 實作前先在 master 推 `Status: IN_PROGRESS`。

## Dependencies
無（與 TASK-025 可並行；兩者合併後效果疊加）。

## Result
（Engineer 填寫）

## Review
（Tech Lead 填寫）
