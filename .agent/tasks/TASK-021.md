# TASK-021

## Title
Usage Limit：以每日用量預算取代扣點（後端新機制，與舊機制切換）

## Owner
Backend

## Status
READY

## Priority
HIGH

## Objective
所有原本 `CreditCosts` 把關的 AI 功能改由 `IUsageLimitService` 每日預算把關；超量回 429 + `UsageLimitExceeded`。設計見 ARCHITECTURE.md「Credits & Payments 移除設計」。

## Requirements
- 新增 `Common/UsageCosts.cs`（沿用 `CreditCosts` 的 AI 功能權重；**不含** DailyLoginBonus/DailyChallengeBonus/StreakMilestone）、`UsageLimitOptions`（`UsageLimits:DailyBudget`，預設 60）。
- 新表 `DailyUsage`（PK UserId+Date）+ `User.UsageLimitMultiplier`（預設 1）+ EF migration。
- `IUsageLimitService.TryConsumeAsync/RefundAsync`：單一原子條件式更新（見 ARCHITECTURE），有效上限 = `DailyBudget * Multiplier`。
- `[UsageLimit(cost, feature)]` filter 取代所有 `[ConsumeCredits]`；內聯呼叫點（ExamService、AudioController、ChatbotController、DataAnalysisController、FinanceQueryService.MarketDigest、PortfolioService）一併替換；chatbot 退款改 `RefundAsync`。MarketDigest「每人每天只扣一次」邏輯保留。
- `ErrorCodes.UsageLimitExceeded = 1042900`；`IMyResponseFactory` 需能回 429（若現有 factory 以 code 推 HTTP status，補對應）。錯誤 message 不得含 credit/purchase/pay 字樣。
- 本任務**不刪**舊 Credit/Payment 程式碼，也不動前端。

## Acceptance Criteria
- 單元測試：預算內通過、超量被擋、跨日重置、Multiplier 放大上限、併發兩請求不會超賣（可用 in-memory/SQLite 或對 SQL 產生器的合理替代，說明取捨）、Refund 不使 Used 低於 0、未登入被擋。
- 既有 Exam/Chatbot/Portfolio/Finance 測試更新後通過；CI 綠燈。
- grep `Insufficient credits` 於 `JackyAIApp.Server`（非 Payment/Credit 檔）無殘留。

## Technical Notes
- 呼叫點清單見 `grep -rn "ConsumeCredits\|ConsumeCreditsAsync" JackyAIApp.Server`（約 25 處）。
- 沒有 .NET SDK 的 session 需特別 grep 受影響測試斷言（見 TASK-019 教訓）。
- 預設 60 是 Tech Lead 的成本估計，可由設定調整，不需改程式。

## Dependencies
無。

## Result
## PR
## Review
