# TASK-017

## Title
Frontend: Portfolio rebalancing suggestions against a target allocation (Gap Audit item 3)

## Owner
Frontend

## Status
PR_OPENED

## Priority
MEDIUM (per PM's approved Gap Audit order — Portfolio proposal 3, next
default item, unblocked, no further Boss sign-off needed)

## Objective
Let a user set a target allocation (% by asset type) and see, next to their
current allocation, how much TWD to buy or sell in each bucket to reach it —
reusing data the Portfolio summary already returns, no new AI calls, no
backend change.

## Context
See `.agent/architecture/ARCHITECTURE.md` → "Decision: Portfolio rebalancing
suggestions against a target allocation — Gap Audit item 3" for the full
investigation and reasoning. Read it before implementing — in particular:
- Buckets are the 5 asset-type fields `GET api/portfolio/summary`
  (`PortfolioSummaryDto`) already returns — TW Stock, US Stock, Fund, Cash,
  Real Estate — **not** the 16-value sector `PortfolioCategory` enum and
  **not** the `groupBy=category` time-series endpoint. Liability is
  deliberately excluded from the buckets (debt isn't something you allocate
  into).
- The target percentages are a **per-browser preference stored in
  `localStorage`**, modeled directly on the existing
  `Portfolio/feeDiscountRate.ts` pattern (`utils/storage.ts`'s
  `readJson`/`writeJson` helpers) — there is no user-settings table in this
  app and this feature does not need one.

## Requirements

1. **`Portfolio/rebalancingTarget.ts`** (new, pure functions + localStorage,
   unit tested):
   - Type: `RebalancingTarget = { twStock: number; usStock: number; fund:
     number; cash: number; realEstate: number }` (percentages, 0-100,
     independent numbers — not required to sum to 100, see Requirement 3).
   - `loadRebalancingTarget(): RebalancingTarget | null` /
     `saveRebalancingTarget(target: RebalancingTarget): void` — localStorage
     key `portfolio.rebalancingTarget`, via `utils/storage.ts`'s
     `readJson`/`writeJson` (do not hand-roll `JSON.parse`/`localStorage`
     calls directly — reuse the existing wrapper for its never-throw
     behavior).
   - `computeCurrentAllocation(summary: PortfolioSummaryDto): { bucket:
     keyof RebalancingTarget; currentTWD: number; currentPct: number
     }[]` — `totalAssetsTWD = summary.liquidGrossAssetsTWD +
     summary.realEstateTWD`; each bucket's `currentPct = currentTWD /
     totalAssetsTWD * 100` (0 when `totalAssetsTWD` is 0 — do not divide by
     zero).
   - `computeRebalancingDeltas(current: ReturnType<typeof
     computeCurrentAllocation>, target: RebalancingTarget, totalAssetsTWD:
     number): { bucket: ...; currentPct: number; targetPct: number;
     deltaTWD: number }[]` — `deltaTWD = (targetPct / 100 * totalAssetsTWD) -
     currentTWD`. Positive = buy more of this bucket, negative = sell/reduce.

2. **`Portfolio/RebalancingCard.tsx`** (new component):
   - 5 editable target-% number inputs (TW Stock / US Stock / Fund / Cash /
     Real Estate), labeled to match the existing Chinese labels used
     elsewhere in Portfolio (reuse `portfolioExport.ts`'s `TYPE_LABEL`-style
     naming convention where it overlaps — TW Stock/US Stock don't have an
     exact `AssetType` equivalent there since STOCK doesn't split by market
     in that map, so pick clear standalone labels, e.g. "台股"/"美股").
   - Live sum of the 5 target inputs shown next to them; a soft visual
     warning (not a blocking error, not a disabled save button) when the sum
     isn't within a small tolerance of 100% (e.g. outside 95-105%) —  a
     user deliberately targeting less than 100% (e.g., "raise cash, haven't
     decided the rest yet") is valid.
   - One row per bucket: current % · target % · delta (formatted as "buy
     $X" / "sell $X" / "±持平" when the delta is within a small rounding
     tolerance, e.g. under NT$100).
   - A "重設為目前配置" (reset to current) button that overwrites the 5
     inputs with the current live allocation's percentages.
   - **First-run behavior (no saved target in localStorage yet)**: seed the
     5 inputs with the *current* allocation's percentages, not zeros/blank
     — a first-time viewer should see "already balanced" (all deltas ~0)
     before they've expressed any preference, not a wall of "sell
     everything" values.
   - Persist to localStorage on every edit (or on blur — either is fine;
     just don't require a separate explicit "save" click for this
     lightweight, low-stakes preference).

3. **Wiring**: add `RebalancingCard` to the Portfolio summary page, near
   `ConcentrationCard` (both are "how is my money spread out" cards). Reads
   `PortfolioSummaryDto` from whatever query/hook already supplies
   `ConcentrationCard`'s data — do not add a second fetch of the same
   summary endpoint.

## Acceptance Criteria
- [ ] `rebalancingTarget.ts`'s pure functions are unit tested: allocation
      math against a sample `PortfolioSummaryDto`, delta math against a
      sample target, the zero-total-assets edge case (no divide-by-zero/NaN
      leaking into the UI).
- [ ] `RebalancingCard` renders 5 buckets with current %, target %, and a
      buy/sell delta that updates live as target inputs change (no page
      reload/re-fetch needed to see the recompute).
- [ ] First view (no prior localStorage value) shows the current allocation
      as the seeded target, not zeros.
- [ ] Editing a target input and reloading the page shows the edited value
      persisted (localStorage round-trip works).
- [ ] Liability is not shown as a rebalancing bucket and is not part of the
      allocation denominator.
- [ ] Sum-not-100% is a soft visual warning only — inputs remain editable,
      nothing is blocked.
- [ ] `npm run lint` (0 warnings), `npm run build`, `npx vitest run` all
      pass with no regressions.

## Technical Notes
- No backend change of any kind — do not touch `JackyAIApp.Server`. Do not
  add a new endpoint or DTO field; everything needed is already in
  `PortfolioSummaryDto`.
- Do not model buckets on `PortfolioCategory`/`groupBy=category` — that's a
  different (sector-level, time-series) mechanism scoped out in the
  Architecture doc's Alternatives. If you find yourself needing to call
  `net-worth-history`, you've picked the wrong data source for this task.
- Follow `feeDiscountRate.ts`'s existing comment convention explaining *why*
  this is localStorage and not a backend field, for the next person who
  wonders the same thing.
- Unit-test the pure compute functions directly, same convention as
  `Portfolio/concentration.ts`/`exposureGauges.ts`'s existing `*.test.ts`
  siblings. Use `renderToStaticMarkup` for any component-level test, per
  `TASK-014`/`TASK-015`'s precedent (no `@testing-library/react` in this
  project).

## Dependencies
None — pure frontend, no other open task's code needed.

## Result
Implemented per spec, frontend-only:
- `Portfolio/rebalancingTarget.ts` (new) — `RebalancingTarget` type, `loadRebalancingTarget`/
  `saveRebalancingTarget` (localStorage key `portfolio.rebalancingTarget` via
  `utils/storage.ts`'s `readJson`/`writeJson`, same pattern as `feeDiscountRate.ts`),
  `computeCurrentAllocation` (5 buckets as % of `liquidGrossAssetsTWD + realEstateTWD`, 0% when
  total is 0 — no divide-by-zero), `computeRebalancingDeltas` (positive = buy, negative = sell).
- `Portfolio/RebalancingCard.tsx` (new) — 5 editable target-% `TextField`s (台股/美股/基金/現金/
  不動產), live sum with a soft `WARN_COLOR` nudge outside 95–105% (never blocking), one row per
  bucket (current% → target%, buy/sell delta in TWD or "±持平" under NT$100), "重設為目前配置"
  button, first-run seeds inputs from the current allocation, persists to localStorage on every
  edit. Reuses `usePrivacy()`'s `value()`/`showRatio` the same way `AllocationCard`/
  `ConcentrationCard` do.
- `Portfolio/index.tsx` — added a new "再平衡建議" `ChartSectionCard` right after "持股集中度",
  passing the same `summary` already fetched for the page (no second `/summary` request).

## Tests
- `Portfolio/rebalancingTarget.test.ts` (new): bucket/% math against a sample
  `PortfolioSummaryDto`, liability excluded from both buckets and the denominator, the
  zero-total-assets edge case, delta math (buy vs. sell direction), zero-total delta edge case,
  and a localStorage round-trip.
- `Portfolio/RebalancingCard.test.tsx` (new, `renderToStaticMarkup` per `TASK-014`/`TASK-015`
  precedent — no `@testing-library/react` in this project): first-run seeding (all buckets
  "±持平"), a saved target that differs from current shows buy/sell deltas, exactly 5 target
  inputs rendered (liability is not a 6th bucket).
- `npm run lint` — 0 warnings. `npm run build` — passes. `npx vitest run` — 194/194 passed (25
  files), no regressions.
- Manual browser verification not performed in this automated session (flagged as a known
  limitation, consistent with `TASK-015`/`TASK-016`'s same note).

## Review
(Pending — PR opened, awaiting Tech Lead review.)
