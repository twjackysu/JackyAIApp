# TASK-014

## Title
Frontend: Multi-stock peer comparison tab (Gap Audit item 3 — 多股比較)

## Owner
Frontend

## Status
PR_OPENED

## Priority
MEDIUM (per PM's approved Gap Audit order — item 3, cheapest remaining item,
unblocked, no further Boss sign-off needed)

## Objective
Add a new Finance tab that lets a user compare 2-3 stocks' comprehensive
quantitative scores side by side, reusing the existing free
`comprehensive-analysis` endpoint and the existing `CategoryScoreCard`
component — no backend change.

## Context
See `.agent/architecture/ARCHITECTURE.md` → "Decision: Multi-stock/sector
peer comparison — Gap Audit item 3" for the full investigation and the
reasoning behind every scope decision below. Read it before implementing —
in particular the category-row-not-stock-column layout choice and the
"user-picked stocks, not auto sector-discovery" scope cut.

## Requirements

1. **New tab in `Finance/index.tsx`**: add a 4th tab, "多股比較"
   (`TAB_COMPARISON`, e.g. `AssessmentIcon`/`InsightsIcon`-family icon —
   engineer's call, keep it distinct from the existing `AssessmentIcon` on
   "綜合量化分析"), positioned after "今日市場AI摘要". Free, unauthenticated —
   do not gate it behind `isAuthenticated` like the AI-trend tab.

2. **New state/actions in `Finance/hooks/useFinanceAnalysis.ts`**:
   - A small list of up to 3 stock-code entries (add/remove chips or
     equivalent), each resolved to a market via the existing `detectMarket`
     heuristic (reuse it — do not write a second market-detection helper).
   - A "比較" action that calls `getComprehensiveAnalysis` once per entered
     code, **concurrently** (`Promise.all`), using the existing
     `DEFAULT_CONFIG` (no per-comparison weight customization — see
     Architecture doc's Trade-offs). Reuse `useGetComprehensiveAnalysisMutation`
     the same way `runComprehensiveAnalysis` already does; you may factor a
     shared single-stock-fetch helper out of `runComprehensiveAnalysis` if
     that avoids duplicating the request-building logic, but do not change
     `runComprehensiveAnalysis`'s existing external behavior (`TASK-012`'s
     cross-link and its own tests depend on it).
   - One stock's fetch failing must not fail the others: catch per-call,
     keep a per-slot result/error state so 1-2 successful comparisons still
     render even if a 3rd code is invalid.
   - Clearing/switching away from the tab clears comparison state, same
     convention `handleTabChange`/`clearSearchResults` already follow for
     the other tabs.

3. **New `Finance/components/comparison/` folder**:
   - A multi-code input (up to 3 chips, add/remove, `Enter`-to-add pattern
     consistent with the existing single-stock search `TextField`).
   - A comparison layout component that, for each category present across
     the fetched results (`Technical`/`Chip`/`Fundamental` — only render a
     category row if at least one compared stock actually has that
     category, same `active` filter pattern `ComprehensiveAnalysisResult.tsx`
     already uses), renders one row of up to 3 `CategoryScoreCard`s
     side-by-side (`Grid`, one column per stock) — reuse `CategoryScoreCard`
     unmodified, do not fork it.
   - A small per-stock header above the grid (company name + code + overall
     score, e.g. reusing the name/code/score fragment pattern from
     `OverallScoreSection.tsx` — extract a shared sub-piece if that avoids
     copy-pasting the JSX, but do not pull in the *entire* section, which
     includes the score gauge / recommendation text this comparison view
     doesn't need at 3-wide).
   - A slot whose fetch failed shows an inline error in its own
     column/position (e.g. "找不到 XXXX" or the mapped error message) rather
     than omitting that stock silently or blocking the other columns.

4. **Loading/error states**: mirror the existing `isAnyAnalyzing`/
   `LinearProgress` pattern while any comparison fetch is in flight; an
   overall network/unexpected error (not a per-stock lookup failure) uses
   the same `Alert severity="error"` convention the other tabs use.

## Acceptance Criteria
- [ ] "多股比較" appears as a new Finance tab, usable without logging in.
- [ ] Entering 2-3 valid stock codes and comparing renders one row per
      present category (Technical/Chip/Fundamental), each row showing that
      many `CategoryScoreCard`s side by side, one per stock.
- [ ] Entering one invalid code alongside 1-2 valid ones still renders the
      valid stocks' comparison, with an inline error for the invalid one.
- [ ] Switching away from the tab and back clears prior comparison results
      (consistent with the other tabs' existing behavior).
- [ ] No new backend call shape — confirm via network inspection (or by
      reading `useFinanceAnalysis.ts`) that comparison uses the same
      `POST api/finance/comprehensive-analysis` request shape as the
      existing single-stock quantitative tab, just called multiple times.
- [ ] `npm run lint` (0 warnings), `npm run build`, `npx vitest run` all
      pass with no regressions.

## Technical Notes
- No backend change of any kind — do not touch `JackyAIApp.Server`.
- Default `AnalysisConfig` only (no custom weights UI for this tab) —
  deliberate scope cut, see Architecture doc.
- Do not auto-run the credit-gated AI-trend analysis for any compared
  stock — this tab must stay entirely free, same reasoning as `TASK-012`'s
  cross-link (a comparison view must never look free and silently charge
  credits per stock).
- `CategoryScoreCard` takes a single `cs: CategoryScore` prop today and has
  no dependency on its parent's layout — confirm this still holds before
  reusing it verbatim.
- Cap at 3 compared stocks — both because 4+ side-by-side cards stop being
  legible on a normal screen and to bound the concurrent-fetch count.

## Dependencies
None — `TASK-012`/`TASK-013` are both merged; this task doesn't depend on
either's code, only reuses the same free endpoint pattern `TASK-012`
established for `runComprehensiveAnalysis`.

## Result
Implemented frontend-only, no backend change.

- `Finance/hooks/useFinanceAnalysis.ts`: added `TAB_COMPARISON`, comparison
  chip-code state (up to 3, `addComparisonCode`/`removeComparisonCode`,
  reusing `detectMarket`), and `runComparison` which fires one
  `getComprehensiveAnalysis` call per code concurrently via `Promise.all`.
  Factored `buildAnalysisRequest(code, market)` out of
  `runComprehensiveAnalysis` so both the single-stock tab and the
  comparison tab build the same request shape from `analysisConfig`;
  `runComprehensiveAnalysis`'s own behavior (reset/set calls, catch
  logging) is unchanged. Comparison uses a **separate**
  `useGetComprehensiveAnalysisMutation()` hook instance so N concurrent
  comparison calls never touch `isComprehensiveLoading`/`comprehensiveError`
  (the single-stock tab's existing state) — each RTK Query mutation hook
  call tracks only requests triggered through its own trigger function.
  Each per-code fetch catches its own failure
  (`getCaughtErrorMessage`) into a per-slot `{ code, market, data, error }`
  result, so one invalid code never blocks the others.
  `clearSearchResults`/`handleTabChange` now also clear comparison state,
  matching the existing per-tab-switch convention.
- `Finance/utils/financeHelpers.ts`: added `MAX_COMPARISON_CODES` (3) and
  the pure, testable `addComparisonCode(codes, newCode)` helper (trim,
  cap, case-insensitive de-dup) used by the hook.
- `Finance/components/comprehensiveAnalysis/StockIdentityHeader.tsx` (new):
  extracted the market-chip/name+code/direction-chip fragment out of
  `OverallScoreSection.tsx` (which now composes it — rendered output
  unchanged) so the comparison header can reuse it via a `dense` prop
  instead of duplicating the JSX.
- `Finance/components/comparison/` (new): `ComparisonCodeInput.tsx` (up to
  3 chips, Enter-to-add, same pattern as the single-stock search box) and
  `ComparisonResult.tsx` (category-row grid — one row per
  Technical/Chip/Fundamental category present across the fetched results,
  one `CategoryScoreCard` per stock per row, reused unmodified; a failed
  slot renders an inline `Alert` in its own column instead of being
  dropped or blocking the others).
- `Finance/index.tsx`: added the "多股比較" tab (`CompareArrowsIcon`,
  positioned after "今日市場AI摘要"), free/unauthenticated like the
  quantitative tab; wired the new input/button/`LinearProgress` (mirroring
  `isAnyAnalyzing`) and the `comparisonError`/`comparisonSlots` rendering.

### Tests
- `Finance/utils/financeHelpers.test.ts` (new): `addComparisonCode` —
  trims, appends, caps at 3, case-insensitive de-dup, ignores blank input.
- `Finance/components/comparison/ComparisonResult.test.tsx` (new): only
  renders category rows for categories present across compared stocks;
  a failed slot's inline error renders alongside the successful stocks'
  cards without dropping them.
- `npm run lint` (0 warnings), `npm run build`, `npx vitest run` (143/143,
  including the 5 new tests) all pass.

### Known Limitations
- No manual browser verification was performed in this session (no
  running dev server / backend in this environment) — verified via
  `tsc`/`vitest`/component-level tests instead, per the Acceptance
  Criteria's own test list.

## Review
(Fill in after PR review.)

## Review
(Fill in after PR review.)
