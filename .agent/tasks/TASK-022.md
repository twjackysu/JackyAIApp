# TASK-022

## Title
前端移除付費/點數 UI，改用 UsageLimitExceeded 提示

## Owner
Frontend

## Status
READY

## Priority
HIGH

## Objective
使用者介面完全沒有 credit/點數/付費/購買/儲值概念（DECISION-007）；超量時顯示聯絡作者提示。

## Requirements
- 刪除：`Credits/`、`Pricing/` 頁與路由（`App.tsx`）、`InsufficientCreditsModal`、`apis/creditApis|paypalApis|ecpayApis`、`constants/creditCosts.ts(+test)`、`uiSlice` credit modal state。
- `UserProfileMenu`：移除餘額、Buy Credits、tooltip；`accountApis/type.ts` 移除 `creditBalance/totalCreditsUsed`。
- 各 Exam/Finance/Portfolio/DataAnalysis/Chatbot/DailyChallenge 元件中的點數成本標籤、扣點提示、`creditsAwarded` 顯示、每日登入獎勵 UI 全部移除。
- `errorMiddleware`：改依 `error.code === 1042900`（HTTP 429）開啟「用量超量」對話框/提示，文案：「您已超出目前的使用量，如果您是重度使用者，請聯絡作者 jacky19918@gmail.com 幫您調高上限」；不 parse message 字串；錯誤文字抽取走 `apis/utils/errorMessage.ts`。Chatbot 串流（`useChatStreaming`）路徑也要處理 429。
- 舊 `/credits`、`/pricing` URL 導向首頁（避免 404）。

## Acceptance Criteria
- `grep -rniE "credit|點數|付費|購買|儲值|pricing" jackyaiapp.client/src` 無使用者可見字串殘留（內部識別字若殘留需於 PR 說明）。
- `npm run lint` / `npm run build` / `npm run test` 通過；超量提示有測試。

## Technical Notes
- 後端 `DailyChallengeSubmitResponse.CreditsAwarded` 與 login-bonus endpoint 由 TASK-023 移除；前端先停止使用即可。
- 契約見 ARCHITECTURE「超量回應契約」，TASK-021 未合併前可先以 mock 開發。

## Dependencies
契約依 TASK-021（可平行開發，合併順序建議 021 先）。

## Result
## PR
## Review
