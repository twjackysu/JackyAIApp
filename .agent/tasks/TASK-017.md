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
Implemented per spec, frontend-only, no backend change:
- `Portfolio/rebalancingTarget.ts` (new): `RebalancingTarget` type, `loadRebalancingTarget`/
  `saveRebalancingTarget` (localStorage key `portfolio.rebalancingTarget` via `utils/storage.ts`'s
  `readJson`/`writeJson`), `computeCurrentAllocation(summary)` (bucket → current TWD/%, 0% not
  NaN at zero total assets), `computeRebalancingDeltas(current, target, totalAssetsTWD)`.
- `Portfolio/RebalancingCard.tsx` (new): 5 target-% inputs (台股/美股/基金/現金/不動產), live sum
  with a soft warning outside 95-105%, per-bucket current%/target%/delta row (買入/賣出/±持平 at
  <NT$100), "重設為目前配置" button, first-run seeding from current allocation, persists on every
  edit.
- `Portfolio/index.tsx`: wired into a new "再平衡建議" `ChartSectionCard` next to `ConcentrationCard`,
  reusing the page's existing `summary` — no second fetch.
- Tests: `rebalancingTarget.test.ts` (allocation/delta math, zero-total-assets edge case,
  localStorage round-trip) + `RebalancingCard.test.tsx` (`renderToStaticMarkup`, no liability
  bucket, first-run `±持平` seeding, soft sum warning, non-flat buy/sell deltas).
- `npm run lint` (0 warnings), `npm run build`, `npx vitest run` (194/194) all pass.
- Known limitation: no manual browser verification performed in this automated session.

**PR**: https://github.com/twjackysu/JackyAIApp/pull/377

## Review
(Pending.)
