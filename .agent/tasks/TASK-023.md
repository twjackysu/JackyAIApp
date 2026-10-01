# TASK-023

## Title
後端刪除 Credit/Payment/Subscription 程式碼

## Owner
Backend

## Status
BLOCKED

## Priority
HIGH

## Objective
移除 PayPal/ECPay/PayUni、`CreditService`、`CreditController`、訂閱與所有點數獎勵（程式碼層級；DB schema 不動）。

## Requirements
- 刪除 `PayPal|ECPay|PayUni` 的 Controller/Service/Options/DI/設定與測試、`CreditPack`、`SubscriptionPlan`、`CreditController`、`ICreditService/CreditService(+Tests)`、`ConsumeCreditsAttribute`、`CreditCosts`。
- `AccountController`：不再建立初始 50 點與 `CreditTransaction`；response 不含 credit 欄位。
- `DailyChallengeService`：移除點數獎勵與 `ClaimDailyBonusAsync`/endpoint；`DailyChallengeSubmitResponse` 移除 `CreditsAwarded`（XP/streak 不變）。
- `User.CreditBalance/TotalCreditsUsed/CreditLastUpdated` 改可空或預設值並停止讀寫；`CreditTransaction`/`Subscription` 實體與 DbSet 移除但**資料表先保留**（避免不可逆動作，見 TASK-024）；確認 EF migration 不產生 DROP（必要時以 model 保留 shadow 設定，或本任務僅標註 entity 未使用，由 TASK-024 一次移除）。
- 清除 Key Vault/appsettings 中金流相關設定的文件說明；更新根/Server `CLAUDE.md`、`REQUIREMENTS` 不動（PM 持有）。

## Acceptance Criteria
- `grep -rniE "credit|paypal|ecpay|payuni|subscription" JackyAIApp.Server`（排除 Migrations）無殘留（`UsageLimit` 相關除外）。
- CI 綠燈；API 無 `/api/credit*`、`/api/paypal*`、`/api/ecpay*`、`/api/payuni*`；無外部金流呼叫。

## Technical Notes
- 必須在 TASK-021、TASK-022 都已合併且部署後才開工（前端不再呼叫被刪 endpoint）。
- 注意 Polly resilience 註解（`Program.cs` 提及 PayPal/ECPay）一併更新。

## Dependencies
TASK-021、TASK-022 MERGED。

## Result
## PR
## Review
