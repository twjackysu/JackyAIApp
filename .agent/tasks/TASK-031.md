# TASK-031：不得無聲失敗（PDF／Repository／Exam）＋單字包加入後的下一步

## Owner
Frontend Engineer

## Status
READY

## Priority
MEDIUM（Requirement：逐模組 UX／留存改進 第 2、4 項）

## Objective
所有失敗都要讓使用者看到原因與可做的事；單字包加入成功後給出前往 Review／Exam 的入口。

## Requirements
1. `PDFUnlocker/index.tsx`：失敗（密碼錯誤、伺服器錯誤）顯示明確訊息（錯誤文字一律經 `apis/utils/errorMessage.ts` 取得，不自行解析 `error.data`）；成功顯示完成提示；下載檔名改為「原檔名-unlocked.pdf」。仍保留 console.error 不必。
2. `Repository`：單字查詢失敗顯示錯誤（含重試按鈕），不得再顯示「單字庫是空的」警示。
3. `Repository/StarterPacks.tsx`：加入失敗要提示；加入成功訊息附「前往複習」（`/review`）與「開始測驗」（`/exam`）按鈕。
4. Exam 取題失敗：顯示錯誤與「重試」。
5. 不新增 AI 呼叫。

## Acceptance Criteria
- AC：PDF 錯誤密碼顯示明確錯誤；單字查詢失敗（mock）顯示錯誤而非空庫警示；單字包加入後出現兩個入口按鈕；Exam 取題失敗可重試。
- 各項至少一個元件測試。`npm run lint`、`npm run test`、`npm run build` 全過。

## Technical Notes
- 與 TASK-030 都會動到 Repository／Exam 但檔案大致不重疊；若衝突以先合併者為準。
- PDF 解鎖為後端 API 失敗時的 HTTP 狀態碼對應訊息，先看現有 controller 回傳的 error code，不要改後端契約。

## Dependencies
無（可與 TASK-030 並行）。開工前先在 master 推 `Status: IN_PROGRESS`。

## Result

## Review
