# TASK-032：Repository／Exam 訪客先看到價值（登入前說明頁）＋Exam 選單手機溢出

## Owner
Frontend Engineer

## Status
MERGED

## Priority
MEDIUM（Requirement：逐模組 UX／留存改進 第 3、7 項）

## Objective
未登入訪客開啟 `/repository`、`/exam` 時，先看到簡短說明與登入按鈕，而不是空白頁後直接被導向 Google。

## Requirements
1. 新增小元件（例如 `auth/LoginGate.tsx`，可帶 title／bullets 參數）：說明「能做什麼、免費、為何需登入（儲存你的單字與進度）」＋ Google 登入按鈕（`/api/account/login/Google?ReturnUrl=<目前路徑>`）。可沿用 Portal 訪客引導（TASK-028）的樣式與文案，避免重複實作。
2. **不要改變 `RequireAuth` 對其他路由的行為**（`/daily`、`/portfolio` 等維持現狀）。作法由 Engineer 選最簡單者，例如給 `RequireAuth` 一個可選 `fallback` prop，只有 Repository／Exam 傳入 LoginGate；PR 說明所選作法。
3. `RequireAuth` 在「正在檢查登入」時仍不得閃現 LoginGate（避免已登入者看到一瞬間的訪客畫面）。
4. Exam 選單（`Exam/index.tsx` 與測驗選擇按鈕群）≤400px 無橫向捲動；並標示一個建議起手測驗（例如 Cloze 標「建議從這裡開始」），不新增邏輯。

## Acceptance Criteria
- 訪客開 `/repository`、`/exam` 看到說明＋登入按鈕；已登入者行為不變；檢查中不閃現。
- 元件測試：訪客／已登入／檢查中 三情境。
- ≤400px Exam 選單無橫向捲動（以 CSS／測試可驗證者為準，PR 註明未實機驗證的部分）。
- `npm run lint`、`npm run test`、`npm run build` 全過。

## Technical Notes
- Requirement 的 Open Question：與既有 `RequireAuth` 無衝突，以 fallback prop 擴充即可，不需回報 Boss。

## Dependencies
無。開工前先在 master 推 `Status: IN_PROGRESS`。

## Result

- 新增 `auth/LoginGate.tsx`；`RequireAuth` 加可選 `fallback` prop（無 fallback 時行為完全不變，仍導向 Google）。僅 `/english/repository`、`/english/exam` 傳入 LoginGate；檢查中（isFetching）不渲染 fallback。
- Exam 按鈕改 `whiteSpace: normal` + `maxWidth: 100%`（移除 max-content/nowrap），Cloze 下方標「建議從這裡開始」。
- 測試：`RequireAuth.test.tsx`（已登入／檢查中／訪客／無 fallback）、`LoginGate.test.tsx`。lint／test(287)／build 皆通過。
- 未實機驗證：≤400px 實際視覺（僅以 CSS 推斷）。

## Review
