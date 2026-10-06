# TASK-030：Exam／Conversation 結束後的「下一步」（答錯單字 + 加入單字庫 + 前往 Review）

## Owner
Frontend Engineer

## Status
PR_OPENED

## Priority
HIGH（Requirement：逐模組 UX／留存改進 第 1 項，DECISION-004/009）

## Objective
一輪測驗結束、對話摘要出現時，使用者能立刻做「下一步」：看到答錯單字、一鍵加入單字庫、前往 `/review`。

## Requirements
1. `Exam/components/RoundResultScreen.tsx`：列出本輪答錯的單字（沿用 `WordRecap` 的精簡樣式或單字 Chip 列即可）；有答錯時顯示「前往複習 `/review`」按鈕與「全部加入單字庫」（單字已在庫者不得重複加入或報錯）；全對時整個區塊不顯示（不留空標題）。
2. Conversation 摘要的 `suggestedWords` 改為可點擊，單字加入單字庫並有成功／失敗 Snackbar；已在庫者顯示已加入狀態。
3. 先確認現有「加入單字」API（Repository／Dictionary 既有 mutation）；**優先重用，不新增後端端點**。若 Exam 結果頁拿不到「本輪答錯單字清單」，在前端的 round state 累積即可（SRS 排程已在逐題背景記錄，不要重複送）。
4. 不新增 AI 呼叫、不呼叫任何 `[UsageLimit]` 端點。

## Acceptance Criteria
- 見 REQUIREMENTS.md「逐模組 UX／留存改進」前兩條 AC。
- 元件測試：有答錯／全對／已在庫／加入失敗 四情境；Conversation suggestedWords 點擊成功與失敗。
- `npm run lint`、`npm run test`、`npm run build` 全過。

## Technical Notes
- 加入單字後 invalidate 對應 tag（單字庫列表、DueCount），讓 Portal 的待複習數同步。
- 不做無關重構；Cloze 與 Translation 兩種 Round 都要涵蓋（其他測驗若共用 RoundResultScreen 則一併受益）。

## Dependencies
無。開工前先在 master 推 `Status: IN_PROGRESS`。

## Result
- `useExamRound` 累積本輪答錯單字（wordId 去重），Cloze／Translation／Sentence 三卡皆傳入；`RoundResultScreen` 顯示答錯單字、「全部加入單字庫」、「前往複習」（全對時整區不顯示）。
- 新增 `useSaveWords`：重用既有 `PUT /api/repository/word/{id}`；後端回 400（已在庫）視為已加入。無新增後端端點、無 AI／UsageLimit 呼叫。
- Conversation 摘要 `suggestedWords` 可點擊（`useLazyGetWordQuery` 取 id → 加入），含成功／失敗 Snackbar 與已加入狀態。
- 測試：RoundResultScreen（答錯／全對／已在庫／失敗）、ConversationSummary（成功／失敗）。lint／test（274）／build 全過。
- Known limitation：「已在庫」以後端 400 判斷，未預先查詢單字庫（避免逐字多打 API）。

## Review
