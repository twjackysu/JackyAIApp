# TASK-022

## Title
前端移除付費/點數 UI，改用 UsageLimitExceeded 提示

## Owner
Frontend

## Status
PR_OPENED

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
契約依 TASK-021（可平行開發，合併順序建議 021 先）。TASK-021 尚未合併，本任務依 ARCHITECTURE「超量回應契約」先行開發（HTTP 429 + `error.code = 1042900`），未對實際後端行為做 mock/stub，純前端改依 code 判斷。

## Result
- 刪除 `Credits/`、`Pricing/`、`InsufficientCreditsModal`、`apis/creditApis|paypalApis|ecpayApis`、`constants/creditCosts.ts(+test)`、`components/Footer.tsx`（唯一用途是連到已刪除的 `/pricing`，故一併移除）。
- **範圍微調（非需求原文列出，屬必然結果）**：一併刪除 `apis/payuniApis`（PayUni 訂閱 API）。需求只列了 creditApis/paypalApis/ecpayApis，但 `Credits/index.tsx` 是 PayUni 唯一呼叫點，刪除 Credits 頁後 `payuniApi` 在 store.ts 裡會變成無人使用的殭屍 slice，所以一併清掉。若 Tech Lead 認為應該獨立成另一個 PR 請告知，我可以拆分。
- `App.tsx`：`/credits`、`/pricing` 改成 `<Navigate to="/" replace />`，不再 404；移除 `InsufficientCreditsModal`，改掛 `UsageLimitModal`。
- `uiSlice`：`isCreditModalOpen/creditModalMessage/requiredCredits` 換成單一 `isUsageLimitModalOpen`（文案固定，不需動態訊息）。
- `errorMiddleware`：改判斷 `status === 429 && error.code === ErrorCodes.UsageLimitExceeded`（新增於 `apis/types.ts`），不再 parse `message` 字串找 `Required: N`。
- 新增 `components/Modals/UsageLimitModal.tsx`：取代 `InsufficientCreditsModal`，顯示固定文案「您已超出目前的使用量，如果您是重度使用者，請聯絡作者 jacky19918@gmail.com 幫您調高上限」。
- `useChatStreaming`：402 → 429 判斷，改用 `apis/utils/errorMessage.ts` 的 `getApiErrorMessage` 統一解析錯誤內文（嘗試 JSON parse body 取 `error.message`，失敗則退回純文字），不再手刻訊息组合。
- 移除所有 Exam/Finance/Portfolio/DataAnalysis/Chatbot 元件中的點數成本標籤（`CREDIT_COSTS.*` 文案與 `ListeningQuizCard` 的 `creditCost` prop）、`DailyChallengeSubmitResponse.creditsAwarded` 顯示、`UserProfileMenu` 餘額/Buy Credits UI。
- 順手清掉 `dailyChallengeApis` 裡沒有任何元件呼叫的 `claimDailyBonus`（每日登入獎勵端點，TASK-023 會砍後端對應 endpoint）。
- `accountApis` 的 `User` 型別移除 `creditBalance/totalCreditsUsed`。

### Files Changed
- 詳見 PR diff；核心：`App.tsx`、`redux/{uiSlice,errorMiddleware,store}.ts`、`hooks/useChatStreaming.ts`、`components/Modals/UsageLimitModal.tsx`（新增）、`apis/types.ts`、`apis/accountApis/type.ts`、`apis/dailyChallengeApis/*`、全部 Exam 子元件、`Finance/components/{MarketSummaryTab,StockSearchSection}.tsx`、`Portfolio/HoldingDetailDrawer.tsx`、`components/FloatingChatbot/components/ChatInput.tsx`、`DataAnalysis/components/SummarizeTab.tsx`、`DailyChallenge/{index,components/ResultScreen}.tsx`。
- 刪除：`Credits/`、`Pricing/`、`apis/{creditApis,paypalApis,ecpayApis,payuniApis}/`、`constants/creditCosts.{ts,test.ts}`、`components/Footer.tsx`、`components/Modals/InsufficientCreditsModal.tsx`。

### UI / UX
- 使用者介面不再出現任何 credit/點數/付費/購買/儲值字樣（`grep -rniE "credit|點數|付費|購買|儲值|pricing" jackyaiapp.client/src` 僅剩程式內部註解與 `/credits`、`/pricing` 兩個導回首頁的 route path 字面值，無使用者可見字串）。
- 舊 `/credits`、`/pricing` 連結導回首頁，不再 404。
- 429 超量時彈出 `UsageLimitModal`，文案為固定聯絡作者訊息；Chatbot 串流路徑同樣處理。

### Tests
- 新增 `redux/errorMiddleware.test.ts`：驗證 429 + `UsageLimitExceeded` code 才會開啟對話框、其他狀態碼或錯誤 code 不會誤觸、且判斷邏輯只看 `error.code`（不 parse message 文字）。
- `npm run lint`：0 errors/warnings。
- `npm run build`：tsc + vite build 成功。
- `npm run test`：222 tests passed（含新增 4 個）。

## PR
https://github.com/twjackysu/JackyAIApp/pull/395 (branch `claude/tender-edison-opot9r`)

## Review
