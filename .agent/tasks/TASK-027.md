# TASK-027

## Title
升級 MUI 至 9.4.0 並確認所有功能正常（Issue #411）

## Owner
Frontend

## Status
IN_PROGRESS

## Priority
MEDIUM

## Objective
`@mui/material`、`@mui/icons-material`、`@mui/x-date-pickers` 升到最新 9.x 穩定版（Boss 指定 9.4.0），
詳讀 migration guide / changelog，修正 deprecation 與行為變動，所有頁面功能與樣式不退化。

## Requirements
- 目前版本：`@mui/material ^9.0.1`、`@mui/icons-material ^9.0.1`、`@mui/x-date-pickers ^9.14.0`
  （已是 v9 大版本，屬 minor 升級，非 v8→v9 breaking migration）。
- 先 `npm view @mui/material versions` 確認 9.4.0 是否存在；若 npm 上最新非 9.4.0，升到最新 9.x 並在 PR 說明。
- 三個 @mui 套件版本需相容（x-date-pickers 的 peerDependency）。
- 逐版閱讀 9.0.1→9.4.0 release notes，列出 deprecated API、行為變動，並於 PR 說明處理結果。
- 清除 console 中新增的 deprecation warning。

## Acceptance Criteria
- `npm run build`、`npm run lint`、前端現有測試全過。
- 手動/截圖驗證主要頁面（Dictionary、Exam、Portfolio 含 date picker、Finance、PDF、dark theme、手機版）無視覺或功能退化。
- PR 說明含：升級版本、Release notes 重點、所做調整、驗證方式。

## Technical Notes
注意 Portfolio 淨值走勢 date picker（#410 剛調整手機版）與 dark theme 客製。不要順手重構。

## Dependencies
無。實作前先在 master 推 `Status: IN_PROGRESS`。

## Result

## Review
