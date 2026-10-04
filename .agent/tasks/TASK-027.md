# TASK-027

## Title
升級 MUI 至 9.4.0 並確認所有功能正常（Issue #411）

## Owner
Frontend

## Status
MERGED

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
- `@mui/material`、`@mui/icons-material` 由 ^9.0.1 升至 ^9.4.0（npm latest = 9.4.0）；`@mui/x-date-pickers` 維持 9.14.0（latest，peer 相容）。`@mui/system`/`private-theming` 同步 9.4.0，`@mui/utils` 已是 9.4.0。
- 只動 `package.json` / `package-lock.json`，無程式碼調整。
- 驗證：`npm run build`、`npm run lint`（0 warning）、`npm test`（254 passed）皆通過；測試輸出無 deprecation/MUI warning。
- Changelog 9.0.1→9.4.0 檢視：無 breaking change；唯一行為變動為 checkbox/radio 會遵守全域 `MuiButtonBase.defaultProps.disableRipple`（專案未使用 disableRipple，不受影響）。
- 未做：瀏覽器截圖/手機實機視覺驗證（雲端排程環境無法），需 Boss/QA 於 preview 或部署後確認 Portfolio date picker 與 dark theme。

## Review
