# TASK-018

## Title
Frontend: Portfolio FIRE/goal tracking (Gap Audit item 4, last item)

## Owner
Frontend

## Status
READY

## Priority
MEDIUM (per PM's approved Gap Audit order — Portfolio proposal 4, the last
item, unblocked, no further Boss sign-off needed)

## Objective
Let a user set a target net worth (and, optionally, an assumed growth rate
and a planned monthly contribution) and see a projected "years to goal" —
reusing data the Portfolio summary already returns, no new AI calls, no
backend change.

## Context
See `.agent/architecture/ARCHITECTURE.md` → "Decision: Portfolio FIRE/goal
tracking — Gap Audit item 4 (last item)" for the full investigation and
reasoning. Read it before implementing — in particular:
- Current net worth (`summary.totalNetWorthTWD`) and a real, all-history
  money-weighted annualized return (`summary.annualizedReturnPct`, XIRR
  including dividends) are **already** returned by `GET api/portfolio/summary`
  — the same response `RebalancingCard`/`ConcentrationCard` already consume.
  Zero backend change needed to read them.
- `annualizedReturnPct` is **nullable** — it is `null` until a user has at
  least 30 days of snapshot history (a deliberate anti-misleading guard in
  `PortfolioService.ComputeAnnualizedReturn`, not a bug/missing-data case to
  paper over). **Never treat `null` as `0`** — that would show an
  impossibly pessimistic "will never reach goal" result for a brand-new
  user. Show a clearly-labeled placeholder growth rate instead (e.g. `6`)
  with a caption explaining why, and let the user override it either way.
- Do **not** derive a growth-rate default from `GET
  api/portfolio/net-worth-history` — that endpoint clamps to a 180-day
  window server-side (`Math.Clamp(days, 7, 180)` in
  `PortfolioService.GetNetWorthHistoryAsync`), a poor basis for a
  multi-year compounding assumption. `annualizedReturnPct` (all-history XIRR)
  is the correct number to seed from.
- The target net worth / growth-rate override / monthly contribution are a
  **per-browser preference stored in `localStorage`**, modeled directly on
  the existing `Portfolio/feeDiscountRate.ts` and `Portfolio/
  rebalancingTarget.ts` pattern (`utils/storage.ts`'s `readJson`/`writeJson`
  helpers) — there is no user-settings table in this app and this feature
  does not need one.

## Requirements

1. **`Portfolio/fireTarget.ts`** (new, pure functions + localStorage, unit
   tested):
   - Type: `FireTarget = { targetNetWorthTWD: number; annualGrowthRatePct:
     number; monthlyContributionTWD: number }`.
   - `loadFireTarget(): FireTarget | null` / `saveFireTarget(target:
     FireTarget): void` — localStorage key `portfolio.fireTarget`, via
     `utils/storage.ts`'s `readJson`/`writeJson` (do not hand-roll
     `JSON.parse`/`localStorage` calls directly).
   - `defaultAnnualGrowthRatePct(annualizedReturnPct: number | null |
     undefined): number` — returns `annualizedReturnPct` when it's a finite
     number, otherwise a fixed placeholder (pick a clearly-documented
     constant, e.g. `6`, with a comment explaining it's a generic assumption
     used only when the user has no computable historical rate yet).
   - `computeYearsToGoal(currentNetWorthTWD: number, target: FireTarget):
     number | null` — year-by-year simulation (not a closed-form formula —
     see Architecture doc's Alternatives for why): each iteration, grow the
     running balance by `target.annualGrowthRatePct`, then add
     `target.monthlyContributionTWD * 12`; stop and return the elapsed whole
     years once the balance reaches `target.targetNetWorthTWD`. Return `0`
     immediately if `currentNetWorthTWD >= target.targetNetWorthTWD` already.
     Cap the loop at 100 years and return `null` if the target still isn't
     reached by then (never show a wrong-looking precise number past a
     sane horizon — `null` means "not reachable at this assumption").

2. **`Portfolio/FireCard.tsx`** (new component):
   - Target net worth input (TWD), a growth-rate input (%, seeded from
     `defaultAnnualGrowthRatePct(summary.annualizedReturnPct)` on first view
     when no saved target exists yet, always editable regardless of whether
     a real historical rate was available), and a monthly-contribution input
     (TWD, defaults to `0` — a pure lump-sum projection if left blank).
   - When `summary.annualizedReturnPct` is `null` and the growth-rate input
     still equals the placeholder default, show a caption explaining why
     (not enough snapshot history yet to compute a real historical rate) —
     don't let it look like a measured number when it isn't.
   - Renders the result of `computeYearsToGoal` as one of three states:
     already reached (`currentNetWorthTWD >= targetNetWorthTWD`), a
     projected year count, or "at this assumption, not reachable within 100
     years" (the `null` case) — recomputed live as any input changes, no
     page reload/re-fetch needed.
   - Persists to `localStorage` on every edit (or on blur — either is fine,
     same latitude `RebalancingCard` had), same lightweight-preference
     pattern as `RebalancingCard`/`feeDiscountRate.ts`.

3. **Wiring**: add `FireCard` to the Portfolio summary page, near
   `RebalancingCard` (both are forward-looking planning cards). Reads
   `PortfolioSummaryDto` from whatever query/hook already supplies
   `RebalancingCard`'s data — do not add a second fetch of the same summary
   endpoint.

## Acceptance Criteria
- [ ] `fireTarget.ts`'s pure functions are unit tested: the already-reached
      (`current >= target`) case, a normal multi-year projection against a
      hand-computed expectation, the "monthly contribution = 0" pure-lump-sum
      case, the 100-year cap returning `null` (e.g. a target far above what a
      near-zero/negative growth rate could ever reach), and
      `defaultAnnualGrowthRatePct`'s null-vs-number behavior.
- [ ] `FireCard` renders the three result states correctly and recomputes
      live as any of the three inputs change.
- [ ] First view (no prior localStorage value) seeds the growth-rate input
      from `summary.annualizedReturnPct` when it's a real number, or the
      documented placeholder (with an explanatory caption) when it's `null`.
- [ ] Editing an input and reloading the page shows the edited values
      persisted (localStorage round-trip works).
- [ ] `npm run lint` (0 warnings), `npm run build`, `npx vitest run` all
      pass with no regressions.

## Technical Notes
- No backend change of any kind — do not touch `JackyAIApp.Server`. Do not
  add a new endpoint or DTO field; `TotalNetWorthTWD`/`AnnualizedReturnPct`
  already exist on `PortfolioSummaryDto`.
- Do not fetch or reference `net-worth-history` for this feature — see
  Context above for why it's the wrong data source here.
- Follow `feeDiscountRate.ts`/`rebalancingTarget.ts`'s existing comment
  convention explaining *why* this is localStorage and not a backend field.
- Unit-test the pure compute functions directly, same convention as
  `Portfolio/rebalancingTarget.ts`/`concentration.ts`'s existing `*.test.ts`
  siblings. Use `renderToStaticMarkup` for any component-level test, per
  `TASK-014`/`TASK-015`/`TASK-017`'s precedent (no `@testing-library/react`
  in this project).

## Dependencies
None — pure frontend, no other open task's code needed.

## Result
(Fill in once implemented.)

## Review
(Fill in after PR review.)
