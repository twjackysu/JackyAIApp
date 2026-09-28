# TASK-018

## Title
Frontend: Portfolio FIRE/goal tracking (Gap Audit item 4, last item)

## Owner
Frontend

## Status
PR_OPENED

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

**PR**: https://github.com/twjackysu/JackyAIApp/pull/378

Implemented as scoped in `ARCHITECTURE.md` — frontend-only, no backend/DB change.

- **`Portfolio/fireTarget.ts`** (new): `FireTarget = { targetNetWorthTWD, annualGrowthRatePct,
  monthlyContributionTWD }`. `loadFireTarget`/`saveFireTarget` via `utils/storage.ts`'s
  `readJson`/`writeJson`, localStorage key `portfolio.fireTarget` — same pattern as
  `rebalancingTarget.ts`/`feeDiscountRate.ts`. `defaultAnnualGrowthRatePct(annualizedReturnPct)`
  returns the real historical rate when it's a finite number, otherwise the documented
  `PLACEHOLDER_ANNUAL_GROWTH_RATE_PCT = 6` — never coerces `null`/`undefined` to `0`.
  `computeYearsToGoal(currentNetWorthTWD, target)` is a year-by-year simulation (not a closed-form
  formula, per the Architecture doc's reasoning): returns `0` immediately if the target is already
  met, otherwise applies `annualGrowthRatePct` then adds `monthlyContributionTWD * 12` each year,
  capped at 100 years, returning `null` if still short after the cap.
- **`Portfolio/FireCard.tsx`** (new): three inputs (target net worth, growth-rate assumption,
  monthly contribution), seeded on first view (no saved target) from
  `defaultAnnualGrowthRatePct(summary.annualizedReturnPct)` for the growth rate and `0` for the
  other two. Persists every edit to `localStorage` immediately (same latitude as
  `RebalancingCard`). When `annualizedReturnPct` is `null` *and* the growth-rate input still
  equals the placeholder, shows an explanatory caption ("尚無足夠歷史資料計算實際報酬率…") so the
  assumption never looks like a measured number. Renders one of: an empty-state prompt when no
  target has been entered yet (`targetNetWorthTWD <= 0`, a case not called out by name in the
  task's three outcome states but needed to avoid a false "已達成目標" default at `target = 0`),
  "已達成目標", "預估 N 年後達成目標", or "此假設下無法在 100 年內達成" — recomputed live from
  `target` state on every input change, no re-fetch.
- **Wiring**: added a new `ChartSectionCard` ("財務目標 (FIRE)") right after the existing
  "再平衡建議" (`RebalancingCard`) section in `Portfolio/index.tsx`, reusing the same
  already-fetched `summary` prop — no second `/summary` request.

### Files Changed
- `jackyaiapp.client/src/Portfolio/fireTarget.ts` (new)
- `jackyaiapp.client/src/Portfolio/fireTarget.test.ts` (new)
- `jackyaiapp.client/src/Portfolio/FireCard.tsx` (new)
- `jackyaiapp.client/src/Portfolio/FireCard.test.tsx` (new)
- `jackyaiapp.client/src/Portfolio/index.tsx` (new `FireCard` section, import)

### Tests
- `npm run lint` — 0 warnings/errors.
- `npm run build` (tsc + vite build) — passes.
- `npx vitest run` — 207/207 passed (27 files), no regressions; includes new `fireTarget.test.ts`
  (already-reached case, a hand-computed multi-year growth+contribution projection, the
  monthly-contribution-only pure-lump-sum case, the 100-year cap returning `null`,
  `defaultAnnualGrowthRatePct`'s null-vs-number behavior, and a localStorage round-trip) and
  `FireCard.test.tsx` (`renderToStaticMarkup` pattern, per `TASK-014`–`017`'s precedent — no
  `@testing-library/react` in this project). Also manually rendered the component to a static HTML
  file in this sandbox to visually confirm no stray whitespace/line-wrap artifacts around the
  Chinese copy (Prettier line-wraps the JSX text across multiple source lines; confirmed the
  rendered output has no unintended extra spaces).

### Known Issues
- Not manually verified in a running browser this session (no dev server / browser check
  performed) — same limitation flagged on `TASK-014`/`TASK-015`/`TASK-017`. Editing an input and
  reloading the page (the acceptance criterion's localStorage-persistence check) is covered by the
  `fireTarget.ts` round-trip unit test and `FireCard`'s "seeds from a saved target" test, not by an
  actual browser reload.
- The `targetNetWorthTWD <= 0` empty-state ("請先輸入目標淨值") is a judgment call not explicitly
  specified by the task (which names three outcome states, not four) — done to avoid a confusing
  false "已達成目標" on first view before the user has entered a target at all. Flagged here in
  case Tech Lead prefers defaulting to one of the three named states instead.

### Blockers
None.

### QA Fix (2026-09-27, Frontend Engineer, scheduled run)
QA reported a MEDIUM bug on this PR (comment
https://github.com/twjackysu/JackyAIApp/pull/378#issuecomment-5857538744):
預估年化成長率's `type="number"` input silently dropped a typed minus sign
(the HTML value-sanitization algorithm collapses an in-progress `"-"` to
`""` before `onChange` sees it, and the old handler coerced that `NaN`
straight to `0`). Fixed and pushed (`e30c7d3`) — switched only that field
to `type="text"` + `inputMode="decimal"`, reworked all three inputs to
track raw string state that commits to `FireTarget` only once it parses as
finite, resyncing on blur. Added a mount-based regression test
(`createRoot`/`act`) typing `"-5"` one keystroke at a time; confirmed it
fails without the fix before restoring it. `npm run lint` (0 warnings),
`npm run build`, `npx vitest run` (208/208, up from 207) all green.

## Review
(Fill in after PR review.)
