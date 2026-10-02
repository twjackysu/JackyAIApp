# TASK-026

## Title
Portfolio 前端效能：分批載入圖表、避免首屏同時打滿 API（Issue #401）

## Owner
Frontend

## Status
MERGED

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
- 新增 `Portfolio/LazyMount.tsx`：用 `IntersectionObserver` 延後掛載 children（`rootMargin: '200px 0px'`，
  提前一點點開始載入），一旦進入視窗就永久掛載（不會因為捲出畫面又卸載，避免重複發 query）；偵測不到
  `IntersectionObserver`（極舊瀏覽器/測試環境）時直接掛載，不讓功能整個消失。未掛載期間顯示固定
  `minHeight` 的 `CenteredSpinner` placeholder，避免捲動時版面跳動。沒有新依賴。
- `Portfolio/index.tsx`：把會自己發 query 的 5 個圖表（`NetWorthTrendChart`、`NetWorthChart`、
  `FinancialChangesChart`、`BenchmarkChart`、`DrawdownChart`）包進 `LazyMount`；`ContributionWaterfallChart`
  / `ConcentrationCard` / `RebalancingCard` / `FireCard` 吃的是已載入的 `summary.positions`/`summary`，
  本身不發 query，維持原樣不包。
- `data-tour="portfolio-tour-chart"` 掛在 `ChartSectionCard` 本身（不在 `LazyMount` 內），所以導覽目標
  一直存在於 DOM；driver.js 換到該步驟時會 `scrollIntoView`，捲動本身就會觸發 `IntersectionObserver`
  把真正的 `NetWorthChart` 掛載上來，不需要額外處理。
- Requirement 2（`FinancialChangesChart` 預設 180→90 共用快取）**未採用**：這是顯示預設值變更，照任務
  說明先回報不自行決定 — 現狀 180 天視窗是否為刻意的產品決定不確定，留給 Tech Lead/PM 判斷是否要開新
  task。

### Files Changed
- 新增：`Portfolio/LazyMount.tsx`、`Portfolio/LazyMount.test.tsx`
- 修改：`Portfolio/index.tsx`（import + 5 處包 `LazyMount`，含各自估算的 `minHeight`）

### UI / UX
- 首屏只看到 `summary`（`NetWorthCard`/`AllocationCard`）+ 固定高度 placeholder spinner；捲動到對應圖表
  時才觸發該圖表的 query，無額外版面跳動。
- 既有 range/groupBy 切換、`isFetching` 提示等行為不變（`LazyMount` 只控制「要不要掛載」，掛載後的元件
  完全沒變）。

### Tests
- 新增 `Portfolio/LazyMount.test.tsx`（4 tests）：未進視窗前不掛載 children（query 不會發）、
  placeholder 保留設定的 `minHeight`、`isIntersecting=false` 不會誤觸掛載、沒有 `IntersectionObserver`
  時直接掛載降級。
- `npm run test`：31 files / 226 tests passed（含新增 4 個）。
- `npm run lint`：0 errors/warnings。
- `npm run build`：tsc + vite build 成功。

### 首屏請求數對照（Network 面板，有持倉時）
- **改動前**：`summary` + `NetWorthHistory(days=90,groupBy=assetType)`（`NetWorthTrendChart`/
  `NetWorthChart` 共用）+ `NetWorthHistory(days=180,groupBy=assetType)`（`FinancialChangesChart`）+
  `benchmark-comparison(365)` + `drawdown(365)` = mount 時 **5 個請求**同時發出。
- **改動後**：mount 時只有 `summary` **1 個請求**；其餘 4 個請求延後到使用者捲動到對應圖表（或導覽
  `scrollIntoView` 時）才各自發出。

### Known Issues
- `ContributionWaterfallChart`/`ConcentrationCard`/`RebalancingCard`/`FireCard` 不發 query，故未套用
  `LazyMount`；若之後想連這幾個一起延後掛載以降低首屏 ECharts render 成本，屬額外優化，建議另開 task。
- 各 `minHeight`（320/400/320/360/300）是依現有各圖表大致渲染高度估算，非逐像素對齊；若某些視窗寬度下
  仍有輕微跳動，可再微調。

### Blockers
無。

## PR
https://github.com/twjackysu/JackyAIApp/pull/403 (branch `claude/tender-edison-art6fg`)

## Review
Tech Lead 2026-10-02：PR #403 已由 Boss 合併。LazyMount 設計合理（無新依賴、降級處理、tour 不受影響）。FinancialChangesChart 180→90 未採用，列為 Optional/Future Work，待 Boss 決定。
