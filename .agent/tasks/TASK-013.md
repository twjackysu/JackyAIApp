# TASK-013

## Title
Backend: Finance prediction-accuracy tracking, Phase 1 — persist + lazily
evaluate AI stock-trend calls (Gap Audit item 2, DECISION-005)

## Owner
Backend

## Status
READY

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
(To be filled in by the implementing Backend Engineer.)

## Review
(To be filled in by Tech Lead after a PR is opened.)
