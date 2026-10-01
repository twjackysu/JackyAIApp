# TASK-021

## Title
Usage Limit：以每日用量預算取代扣點（後端新機制，與舊機制切換）

## Owner
Backend

## Status
PR_OPENED

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
Implemented as scoped in ARCHITECTURE.md "Credits & Payments 移除設計":

- `Common/UsageCosts.cs` — `CreditCosts` weights carried over 1:1, minus the three gamification
  bonuses (DailyLoginBonus/DailyChallengeBonus/StreakMilestone7Day), which don't apply to a
  consumption-only budget.
- `Configuration/UsageLimitOptions.cs` (`UsageLimits:DailyBudget`, default 60) + `appsettings.json`
  entry. New `User.UsageLimitMultiplier` (decimal, default 1) for per-user overrides (author edits
  the DB directly — no admin UI, per scope).
- New `DailyUsage` entity/table (composite PK `(UserId, Date)`) + hand-written EF migration
  (`20261001050000_AddUsageLimit`) — no `dotnet` SDK in this sandbox, so `.Designer.cs` and
  `AzureSQLDBContextModelSnapshot.cs` were updated by hand, verified line-by-line against the most
  recent precedent (`20260913120000_AddUniqueIndexOnWordText`) for structural consistency.
- `IUsageLimitService`/`UsageLimitService`: `TryConsumeAsync`/`RefundAsync`, backed by a single
  atomic conditional `UPDATE ... WHERE Used + @cost <= @limit` (insert-if-missing fallback on the
  first charge of a UTC day), per the architecture doc — no read-modify-write round trip.
  **Deviation from the doc's literal "Used ulong" storage note**: the column is `bigint`, not the
  `decimal(20,0)` convention `User.CreditBalance` uses, because the concurrency test needs a real
  relational provider (InMemory doesn't support the raw SQL this relies on) and SQLite stores
  `decimal` as TEXT — comparing it with raw SQL arithmetic would be lexicographic, not numeric.
  `bigint` is numerically identical in both SQL Server and SQLite and usage weights are tiny, so
  nothing is lost. Documented inline in `AzureSQLDBContext.cs` and the test file.
- `Common/UsageLimitAttribute.cs` (`[UsageLimit(cost, feature)]`) replaces `[ConsumeCredits]` on
  every call site; the few inline (non-attribute) gates in `ExamService`, `AudioController`,
  `ChatbotController` (+ its refund path), `DataAnalysisController.TryChargeNlSummarizeAsync`,
  `FinanceQueryService.GetMarketDigestAsync` (once-per-day logic preserved), and
  `PortfolioService.GetAiTechSummaryAsync` were switched the same way. `ErrorCodes.UsageLimitExceeded
  = 1042900` maps to HTTP 429 via the existing `(int)code / 100 % 1000` formula in
  `ResponseFactory.cs` — no factory change needed. All new/changed error messages are neutral
  ("Daily usage limit exceeded.") — grep confirms no `Insufficient credits` string remains outside
  `CreditService.cs`/`ConsumeCreditsAttribute.cs`.
  - One incidental fix needed to actually deliver the 429 contract: `PortfolioController
    .GetAiTechSummary` mapped *every* service failure (not-found position, no price data, usage
    limit, OpenAI failure) to `ErrorCodes.InternalServerError` regardless of cause. Extended
    `GetAiTechSummaryAsync`'s return tuple with an `ErrorCodes?` so the controller can surface the
    right code; this was the smallest change that let this endpoint honor the required contract
    (not a broader refactor — no other behavior changed).
  - The chatbot's `/api/chatbot/chat` SSE endpoint doesn't use the JSON error envelope (writes
    plain status + text to the stream); it now returns a bare 429, consistent with TASK-022's note
    that the streaming path checks the status code directly rather than parsing `error.code`.
- Old Credit/Payment code (`ICreditService`/`CreditService`, `ConsumeCreditsAttribute`,
  `CreditCosts`, PayPal/ECPay/PayUni, daily login/challenge bonuses) is **untouched** — still wired
  up, just no longer called from the switched sites above — per scope (TASK-023 deletes it once
  TASK-022 ships).

### Tests
- `UsageLimitServiceTests.cs` — new, against a real SQLite (in-memory, shared-cache) provider, not
  InMemory: within-budget, exceeds-budget (including a single charge larger than the whole budget,
  and the exact-at-limit boundary), cross-UTC-day reset, multiplier raising the effective limit, an
  unknown-user fallback (defaults to multiplier 1 rather than throwing or defaulting to 0),
  refund reducing `Used`, refund never driving `Used` negative (incl. refunding with no row for
  today yet), and a genuine concurrency test (10 parallel requests against a budget of 5, each its
  own `DbContext`/connection, asserting exactly 5 succeed and the stored `Used` is exactly 5 — this
  is the test that would fail against a naive read-modify-write implementation).
- `UsageLimitAttributeTests.cs` — new: unauthenticated short-circuits `Forbidden` without calling
  `TryConsumeAsync`; within-limit calls `next()` without setting `context.Result`; exceeding limit
  short-circuits 429/`UsageLimitExceeded`; the error message never contains "credit"/"purchase"/"pay".
- `ExamServiceTests.cs` — swapped the `ICreditService` mock for `IUsageLimitService`; the one
  negative-path test (writing evaluation) renamed/reasserts `ErrorCodes.UsageLimitExceeded`
  instead of `Forbidden`.
- 10 `Services/Portfolio/*.cs` test files — mechanical swap of the `ICreditService` constructor
  mock for `IUsageLimitService` (none of them exercise `GetAiTechSummaryAsync`, so no behavioral
  assertions needed changing).
- Added `Microsoft.EntityFrameworkCore.Sqlite` to the test project (test-only dependency) —
  justified by the acceptance criteria itself ("可用 in-memory/SQLite...說明取捨") and necessary
  because `ExecuteSqlInterpolatedAsync` throws immediately against the InMemory provider.

### Known limitations / out of scope
- No `dotnet` SDK/DB connectivity in this sandbox — compensated with a careful manual read-through
  of every changed file plus cross-checking the hand-written migration's Designer/Snapshot bodies
  byte-for-byte against the pre-existing pattern. CI must still confirm an actual build + test run.
- `UsageLimitMultiplier` has no admin UI (explicitly out of scope per the task) — the author edits
  the DB column directly.
- Frontend (TASK-022) still reads the old credit contract; until it ships, a 429 just renders as a
  generic error client-side — flagged as an accepted short window in ARCHITECTURE.md's deployment
  order note.

## PR
## Review
