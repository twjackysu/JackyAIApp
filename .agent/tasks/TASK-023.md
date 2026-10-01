# TASK-023

## Title
後端刪除 Credit/Payment/Subscription 程式碼

## Owner
Backend

## Status
PR_OPENED

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
Implemented exactly as scoped, with one documented deviation on the entity-removal requirement
(see below).

- **Deleted entirely**: `Controllers/{PayPal,ECPay,PayUni,Credit}Controller.cs`;
  `Services/{I,}{PayPal,ECPay,PayUni,Credit}Service.cs`; `Services/CreditPack.cs`,
  `Services/SubscriptionPlan.cs`; `Configuration/{PayPal,ECPay,PayUni}Options.cs`;
  `Common/ConsumeCreditsAttribute.cs`, `Common/CreditCosts.cs`; their `appsettings.json` config
  sections; and the corresponding test files (`CreditServiceTests.cs`, `ECPayServiceTests.cs`,
  `PayUniServiceTests.cs`). `ServiceCollectionExtensions.AddConnectorAndPaymentServices` (mixed
  payment DI with unrelated OAuth-connector DI) renamed to `AddConnectorServices` with only the
  payment lines removed; `Program.cs`'s call site and a nearby resilience-pipeline comment
  mentioning PayPal/ECPay updated to match.
- **`AccountController`**: no longer creates an initial `CreditBalance`/`CreditTransaction` on
  first login; log message no longer mentions credits.
- **`DailyChallengeService`**: removed the credit-bonus block (daily-challenge + streak-milestone)
  from `SubmitChallengeAsync`, and the whole `ClaimDailyBonusAsync` method (+ its interface
  declaration + its controller endpoint). `DailyChallengeSubmitResponse.CreditsAwarded` removed —
  XP/streak logic untouched, confirmed by reading the surrounding code, not just grep.
- **Deviation from the literal requirement wording** (flagged per this role's SOP §13 — technical
  judgment call, not a product/architecture one, but worth Tech Lead's attention): the requirement
  says "`CreditTransaction`/`Subscription` entities and DbSets removed, but tables kept" — I did
  **not** remove the entity classes/DbSets/`User.CreditTransactions` nav property this task.
  Instead I kept them completely as-is (now fully unused/dead code — nothing constructs, reads, or
  writes them anymore) and only added comments marking them unused-pending-TASK-024. This is the
  task's own explicitly sanctioned alternative ("或本任務僅標註 entity 未使用，由 TASK-024 一次移除") —
  chosen over hand-editing the EF model to exclude them from migrations, because that would require
  touching `AzureSQLDBContextModelSnapshot.cs`/`.Designer.cs` by hand with no `dotnet ef` available
  in this sandbox to verify the resulting migration diff is actually inert, which is a real risk
  (an out-of-sync snapshot could make an unrelated future migration generate an unintended `DROP
  TABLE` for these tables). Deferring the entity+DbSet+nav-property removal to TASK-024 — done
  together with the actual table DROP, under Boss's already-given approval and (presumably) CI/SDK
  access to verify the migration — is lower-risk than a half-measure now.
- **`User.CreditBalance`/`TotalCreditsUsed`**: kept as columns (same type, same mapping — zero
  schema/migration impact) but dropped `required`, defaulted to `0`, and added `[JsonIgnore]` so
  they're excluded from every API response (satisfies the "response 不含 credit 欄位" requirement
  without needing a migration). Confirmed via grep that nothing outside the entity definition reads
  or writes them anymore. `LastUpdated` was *not* touched/renamed — it's genuinely shared with
  non-credit code (`RepositoryService`'s word-list add/remove), only its doc comment was corrected
  to stop calling it credit-specific (there was never a separate `CreditLastUpdated` property, that
  was a misnomer in the original task wording).
- **CLAUDE.md**: root `CLAUDE.md`'s Backend Stack resilience bullet, Key Features section 8, and
  the `User` entity one-liner all rewritten to describe the usage-limit model instead of
  Credits & Payments; `JackyAIApp.Server/CLAUDE.md` had no credit/payment mentions to begin with
  (confirmed).
- **Comment sweep**: beyond the acceptance criterion's literal grep, also fixed ~10 files' doc
  comments that used stale "credit"/"credit-gated" wording to describe the *new* usage-limit gate
  (e.g. `IExamService.cs`, `TtsCache.cs`, `IAnthropicChatService.cs`) — these predate this task but
  the grep check surfaced them, and leaving "deliberately not credit-gated" next to a `[UsageLimit]`
  attribute would be actively misleading. Two dangling XML-doc `<see cref="CreditCosts">`/
  `<see cref="ConsumeCreditsAttribute">` references (in `UsageCosts.cs`/`UsageLimitAttribute.cs`,
  now-broken since those types are deleted) were rewritten to plain text.
- **Left alone** (confirmed genuinely unrelated, not residue): `Services/Portfolio/PortfolioService.cs`'s
  "credited"/"Crediting" (plain financial-accounting language), `ServiceCollectionExtensions.cs`/
  `AzureSpeechTextToSpeechService.cs`'s "OpenAI credit balance" (the real external OpenAI account
  balance), `Dictionary/en_US.dic`/`README_en_US.txt` (Hunspell word-list data, unrelated).

### Verification
No `dotnet` SDK in this sandbox — compensated with a careful manual read-through plus an
independent adversarial review-agent pass (fresh context): re-grepped every deleted type name
repo-wide (zero remaining references outside doc-comment plain text), checked brace balance on
every touched file, confirmed the `required`→optional change on `CreditBalance`/`TotalCreditsUsed`
didn't strand any test's object-initializer (fixed 3: `ExamControllerTests.cs`, `ExamServiceTests.cs`,
`UsageLimitServiceTests.cs`), and confirmed the project has no `TreatWarningsAsErrors`/strict
settings that could turn a dangling-cref warning into a build failure (moot anyway — none remain).
Also confirmed via a frontend-side grep that nothing in `jackyaiapp.client/src` still calls
`/api/credit*`, `/api/paypal*`, `/api/ecpay*`, `/api/payuni*` (TASK-022 already removed those
callers and is deployed). CI must still confirm an actual build + test run.

### Known limitations / out of scope
- `CreditTransaction`/`Subscription` entities, their DbSets, and `User.CreditTransactions` remain
  in the codebase as dead code pending TASK-024 (see Deviation above) — acceptance criterion's grep
  will still show matches in those specific files; flagged there as intentional, not residue.
- Azure Key Vault itself (the actual PayPal/ECPay/PayUni secret entries) is outside repo scope —
  no code comment documented their secret names to begin with, so nothing to scrub there; the
  Key Vault entries themselves need manual removal by whoever manages that, separately from this PR.

## PR
(see PR link in the accompanying commit / GitHub)

## Review
