# TASK-013

## Title
Backend: Finance prediction-accuracy tracking, Phase 1 — persist + lazily
evaluate AI stock-trend calls (Gap Audit item 2, DECISION-005)

## Owner
Backend

## Status
IN_PROGRESS

## Priority
MEDIUM (per PM's 2026-09-20 gap-audit proposal order, approved by Boss
2026-09-21: item 2, next in queue after the Finance↔Portfolio cross-link)

## Objective
`FinanceAnalysisService.AnalyzeStockWithAIAsync` produces a bullish/bearish/
neutral call for three time horizons (short 1-3mo, medium 3-12mo, long
1-3yr) but nothing ever checks whether any of those calls turned out to be
right — the app has no way to answer "are this app's stock predictions any
good?" Persist every prediction, and evaluate it against actual price
movement once its horizon has elapsed, entirely server-side with no new
background-job infrastructure.

## Requirements

1. **New EF entity `StockPrediction`** (`Data/Models/`): `Id` (Guid),
   `StockCode`, `Market` (`TW`/`US`), `UserId` (who triggered the
   originating `analyze-stock` call — traceability only, not used for
   access control), `PredictedAtUtc`, `PriceAtPrediction` (decimal,
   nullable — mirrors `StockTrendAnalysis.CurrentPrice`, which is itself
   nullable), `HorizonMonthsMin`/`HorizonMonthsMax` (int — e.g. 1/3 for
   short-term), `PredictedTrend` (string: `bullish`/`bearish`/`neutral`,
   matching the AI DTO's existing casing), `EvaluatedAtUtc` (nullable
   DateTime — null means not yet evaluated), `ActualTrend` (nullable
   string, same 3 values), `ActualPriceAtEvaluation` (nullable decimal),
   `WasCorrect` (nullable bool). One row **per horizon** — a single
   `analyze-stock` call writes 3 rows (short/medium/long), not one row
   with 3 embedded fields. Add the EF migration.

2. **Write path**: after `AnalyzeStockWithAIAsync` returns a successful
   `StockTrendAnalysis`, persist 3 `StockPrediction` rows (one per
   horizon) using that response's `ShortTermTrend`/`MediumTermTrend`/
   `LongTermTrend` and `CurrentPrice`. This should not change
   `analyze-stock`'s response shape, latency in the success path beyond an
   EF `SaveChangesAsync`, or its existing credit-cost gating — pure
   additive side effect. If `CurrentPrice` is null (per the DTO's own
   nullability), still write the rows with `PriceAtPrediction = null`; such
   rows simply can't be evaluated later (guard the evaluation query to skip
   nulls) rather than blocking persistence of the prediction itself.

3. **New `IPredictionEvaluationService.EvaluateDuePredictionsAsync(int
   batchSize = 5, CancellationToken ct = default)`**:
   - Query `StockPrediction` where `EvaluatedAtUtc IS NULL AND
     PriceAtPrediction IS NOT NULL AND
     PredictedAtUtc.AddMonths(HorizonMonthsMin) <= DateTime.UtcNow`,
     ordered oldest-due-first, take `batchSize`.
   - For each: resolve the appropriate `IMarketDataProvider` via the
     existing `MarketDataProviderFactory` (same market-detection/selection
     already used elsewhere — do not add a second provider-selection path),
     fetch the historical price nearest
     `PredictedAtUtc.AddMonths(HorizonMonthsMin)` (TW via
     `ITWStockLibHistoricalProvider.FetchRangeAsync`/
     `PersistentTwHistoricalProvider`, US via
     `YahooFinanceMarketDataProvider` — reuse whichever existing method
     already returns a price for/near a specific historical date; if none
     currently exposes "price on date X" directly, add the minimal query
     over an existing range-fetch result rather than a new provider).
   - Classify the actual move: `(actualPrice - priceAtPrediction) /
     priceAtPrediction`, `> +3%` → `bullish`, `< -3%` → `bearish`, else
     `neutral` (3% threshold: starting value, flag as tunable, not
     load-bearing for this task's acceptance criteria).
   - Set `ActualTrend`, `ActualPriceAtEvaluation`, `WasCorrect =
     (ActualTrend == PredictedTrend)`, `EvaluatedAtUtc = DateTime.UtcNow`.
   - If the historical-price lookup fails for a given prediction (provider
     error, delisted symbol, etc.), log and skip that row this pass
     (leave `EvaluatedAtUtc` null so it's retried next time) rather than
     throwing and abandoning the rest of the batch.

4. **Trigger point**: call `EvaluateDuePredictionsAsync()` as a
   fire-and-forget task (`_ = Task.Run(...)` with its own scope/DbContext,
   matching how this codebase already isolates background work from
   request-scoped services — check `Program.cs`/existing DI lifetimes for
   the established pattern before adding a new one) from
   `FinanceController`'s `market-digest` endpoint, **after** the digest
   response has already been computed/returned to the caller — this must
   never add latency to the response the user is waiting on, and a failure
   in evaluation must never fail the digest request itself (wrap in
   try/catch, log only).

## Acceptance Criteria
- [ ] Calling `analyze-stock` for a stock persists exactly 3
      `StockPrediction` rows (short/medium/long), matching the response's
      `CurrentPrice`/trend fields.
- [ ] A `StockPrediction` row whose horizon hasn't elapsed yet is never
      touched by `EvaluateDuePredictionsAsync`.
- [ ] A due, unevaluated row gets `EvaluatedAtUtc`/`ActualTrend`/
      `ActualPriceAtEvaluation`/`WasCorrect` populated correctly for both a
      TW and a US symbol (unit tests should cover both market paths via
      the existing provider test seams).
- [ ] A historical-price lookup failure for one row in a batch doesn't
      prevent the other rows in that batch from being evaluated.
- [ ] `market-digest`'s own response time/shape/credit-gating is
      unaffected by this change (the evaluation call must be observably
      fire-and-forget, not awaited inline).
- [ ] No credit-cost change anywhere; no new endpoint added.
- [ ] EF migration is additive only (new table, no changes to existing
      tables/data).

## Technical Notes
- See `ARCHITECTURE.md` → "Decision: Finance prediction-accuracy tracking
  — Gap Audit item 2" for the full investigation, including why this is
  **not** modeled as a real scheduled/background job (no such
  infrastructure exists anywhere in this codebase today — confirmed via a
  repo-wide search; introducing one would be a bigger cost than PM's
  original estimate assumed).
- This task is backend-only, Phase 1 (data collection) only. There is no
  UI for this yet, deliberately — see the Architecture doc's Phase 2 note
  on why a results page is deferred until there's actual evaluated data to
  show. Do not add a frontend surface as part of this task.
- Reuse `MarketDataProviderFactory`/`IMarketDataProvider` for market
  resolution — do not write a second TW-vs-US detection path (Finance
  already has one, used by `useFinanceAnalysis`'s `detectMarket` on the
  frontend and by an equivalent check server-side).

## Dependencies
None — this is independent of `TASK-012` (item 1). Can proceed in parallel;
sequencing them only reflects PM's proposed priority order, not a hard
build dependency.

## Result

Implemented as scoped:

- **New `StockPrediction` entity** (`Data/Models/StockPrediction.cs`, string GUID `Id` —
  matches this codebase's existing string-key convention, e.g. `Word`/`ReadingPassage`,
  rather than the task text's literal "Guid"): `StockCode`, `Market`, `UserId`
  (`[MaxLength(255)]`, traceability only, matching `AiUsageLog`/`ChatConversation`'s
  precedent for a non-FK UserId column), `PredictedAtUtc`, `PriceAtPrediction` (nullable),
  `HorizonMonthsMin`/`Max`, `PredictedTrend`, `EvaluatedAtUtc`/`ActualTrend`/
  `ActualPriceAtEvaluation`/`WasCorrect` (all nullable). EF migration
  `20260922050000_AddStockPrediction` is additive-only (new table + one index on
  `(EvaluatedAtUtc, PredictedAtUtc)`), decimal columns `(18,4)` matching
  `DailyStockPrice`'s existing precision.
- **Write path**: `StockPredictionRecorder` (`Services/Finance/Prediction/`) — a small,
  directly unit-testable service (`AzureSQLDBContext` only) that writes the 3 per-horizon
  rows (`StockPredictionHorizons`: 1-3/3-12/12-36 months) from a successful
  `StockTrendAnalysis`. `FinanceQueryService.AnalyzeStockAsync` calls it after a successful
  AI analysis, before caching — wrapped in try/catch internally so a persistence failure can
  never turn a successful `analyze-stock` call into a failed response. No response-shape
  change; `Market` is resolved via the existing `IMarketDataProviderFactory.DetectRegion`
  (no second region-detection path).
- **Evaluation path**: `PredictionEvaluationService.EvaluateDuePredictionsAsync(batchSize=5)`
  queries due, unevaluated, priced rows oldest-first, and for each resolves TW vs US from the
  row's own `Market` field (falling back to `DetectRegion` only if that ever fails to parse).
  Fetches a small date range **centered on the horizon's actual target date**
  (`targetDate ± 10 days`, clamped so the end never exceeds now) via
  `ITWStockLibHistoricalProvider.FetchRangeAsync` (TW) or a new
  `YahooFinanceMarketDataProvider.FetchRangeAsync` (US, added using Yahoo's
  `period1`/`period2` params — `FetchAsync`'s existing fixed `range=6mo` behavior is
  unchanged, its JSON-parsing logic was just extracted into a shared private helper).
  Classifies the nearest price within that window against a ±3% neutral band; a lookup
  failure *or* no price within the window (defensive max-gap guard) leaves the row
  unevaluated for retry rather than throwing or silently grading against a distant price.
  A single `SaveChangesAsync` commits the batch after each row's own try/catch.
- **Trigger**: `FinanceController.GetMarketDigest` builds/returns the digest response first,
  then starts a fire-and-forget `Task.Run` that creates its **own** `IServiceScope` (via
  `IServiceScopeFactory`, not the request's scope, which may already be disposed by the time
  the task actually runs) and calls `IPredictionEvaluationService.EvaluateDuePredictionsAsync()`,
  wrapped in try/catch/log — never awaited inline, never affects the digest response's
  shape/latency/credit-gating.

**Design note — an earlier draft was wrong and got caught before merge**: the first pass had
`EvaluateOneAsync` calling `IMarketDataProvider.FetchAsync(stockCode)` (the same method
`GetTechnicalIndicatorsAsync` etc. already use). That method always returns a fixed ~6-month
window trailing from *today*, not a range around an arbitrary past date — for a due
long-horizon (12mo) prediction, whose target date is itself ~1yr in the past by the time it's
due, that window never contains the target date at all, so evaluation would have silently
graded against a wrong, distant "nearest" price with no error. Caught by an independent review
pass before this was ever pushed as final; fixed by switching to the two `FetchRangeAsync`
paths described above (exactly what TASK-013's own technical notes had already pointed at) and
adding the max-gap guard in `NearestPrice` as a second line of defense. A regression test
(`EvaluateDuePredictionsAsync_DueLongHorizonTwRow_FetchesAroundTargetDateNotJustRecentHistory`)
locks this in by asserting the real fetch call's date-range arguments actually straddle the
target date.

**Tests**: `StockPredictionRecorderTests` (3-row write, per-horizon field mapping, null
`CurrentPrice` handling) and `PredictionEvaluationServiceTests` (not-due rows never touched,
null-price rows never touched even if due, TW + US correct bullish/bearish/neutral
classification including a long-horizon case, the max-gap guard, one row's lookup failure not
blocking the rest of the batch, batch-size + oldest-due-first ordering).

**Known limitations / things Tech Lead should be aware of before merging:**
1. **No `dotnet` SDK in this sandbox** — nothing here was compiled, built, or run. The
   migration/`.Designer.cs` were hand-written and cross-checked against
   `AzureSQLDBContextModelSnapshot.cs` (both diffed to confirm the `StockPrediction` block is
   byte-identical between the two, following the same approach TASK-001 used), and the whole
   diff went through two independent read-only review passes (one on the initial
   implementation, one re-verifying the fix described above) that read every changed file in
   full against the real production signatures — but this is not a substitute for an actual
   build/test run.
2. **Evaluation cadence is opportunistic**, tied to `market-digest` traffic, per
   ARCHITECTURE.md's own trade-off note — not a concern for this task's acceptance criteria,
   just carried over from the design doc.
3. `PredictionEvaluationService` injects `ITWStockLibHistoricalProvider` (the raw TWStockLib
   source) rather than the DB-caching `PersistentTwHistoricalProvider` for TW range fetches —
   deliberate, since `PersistentTwHistoricalProvider`'s DB cache only covers the same ~6-month
   rolling window that caused the bug described above, so evaluating anything older than that
   (the whole point of the fix) has to go past it to TWStockLib directly. This means
   long-horizon TW evaluations are a live TWStockLib call rather than DB-cached, which is fine
   given the batch cap (5/call) and opportunistic cadence, but flagging the trade-off
   explicitly.

PR: (opening now)

## Review
(To be filled in by Tech Lead after a PR is opened.)
