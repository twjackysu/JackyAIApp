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

Implemented as scoped in `ARCHITECTURE.md` — frontend-only, no backend/DB change.

- **New tab**: "多股比較" (`TAB_COMPARISON = 3`) added to `Finance/index.tsx` after
  "今日市場AI摘要", free/unauthenticated (no `isAuthenticated` gate).
- **`Finance/utils/comparisonCodes.ts`**: pure `addComparisonCode`/`removeComparisonCode`
  helpers (trim, case-insensitive dedupe, capped at `MAX_COMPARISON_CODES = 3`) — unit
  tested directly, no React/store needed.
- **`Finance/hooks/useFinanceAnalysis.ts`**:
  - Extracted `fetchComprehensiveAnalysis(code, market, config)` out of
    `runComprehensiveAnalysis` as the shared request-building call site; the single-stock
    tabs still pass the user-adjustable `analysisConfig`, comparison always passes the
    fixed `DEFAULT_CONFIG` (per Architecture doc — `AnalysisConfigPanel` stays scoped to
    the single-stock tabs). `runComprehensiveAnalysis`'s external behavior is unchanged.
  - New state: `comparisonInput`, `comparisonCodes`, `comparisonSlots`
    (`ComparisonSlot[]`, one per compared code with `status: loading|success|error`),
    `isComparing`, `comparisonError` (unexpected/overall failure only — per-stock failures
    live in each slot).
  - `runComparison()` fires `fetchComprehensiveAnalysis` concurrently via `Promise.all`,
    each call individually try/caught so one bad code doesn't fail the others; per-slot
    errors use `getCaughtErrorMessage` (the standard RTK Query error-extraction util).
  - `clearSearchResults` (already the single clear-everything call site used by
    `handleTabChange` and the search box's refresh button) now also clears comparison
    state, so switching tabs clears comparison results the same way it already clears the
    other tabs' results.
- **`Finance/components/comprehensiveAnalysis/StockIdentitySummary.tsx`** (new): extracted
  the market-chip + company-name/code + direction-chip fragment out of
  `OverallScoreSection.tsx` (behavior-preserving refactor, verified via `npx vitest run`)
  so the comparison header can reuse it without pulling in the score gauge/recommendation
  text.
- **`Finance/components/comparison/`** (new folder):
  - `ComparisonCodeInput.tsx` — up to 3 chips, `Enter`-to-add, matches the existing
    single-stock search box's styling/placeholder conventions.
  - `ComparisonResult.tsx` — category-row layout: for each of
    Technical/Chip/Fundamental present across the fetched results, one row of up to 3
    unmodified `CategoryScoreCard`s side by side; a per-stock header (via
    `StockIdentitySummary` + a plain "綜合評分: N" line, deliberately not the score gauge);
    a failed slot renders `找不到 {code}：{errorMessage}` inline in its own column instead
    of being dropped or blocking the others.
- Loading/error UX mirrors the existing `isAnyAnalyzing`/`LinearProgress` and
  `Alert severity="error"` conventions.

### Files Changed
- `jackyaiapp.client/src/Finance/index.tsx`
- `jackyaiapp.client/src/Finance/hooks/useFinanceAnalysis.ts`
- `jackyaiapp.client/src/Finance/components/comprehensiveAnalysis/OverallScoreSection.tsx`
- `jackyaiapp.client/src/Finance/components/comprehensiveAnalysis/StockIdentitySummary.tsx` (new)
- `jackyaiapp.client/src/Finance/components/comparison/ComparisonCodeInput.tsx` (new)
- `jackyaiapp.client/src/Finance/components/comparison/ComparisonResult.tsx` (new)
- `jackyaiapp.client/src/Finance/components/comparison/ComparisonResult.test.tsx` (new)
- `jackyaiapp.client/src/Finance/components/comparison/ComparisonCodeInput.test.tsx` (new)
- `jackyaiapp.client/src/Finance/utils/comparisonCodes.ts` (new)
- `jackyaiapp.client/src/Finance/utils/comparisonCodes.test.ts` (new)

### Tests
- `npm run lint` — 0 warnings/errors.
- `npm run build` (tsc + vite build) — passes.
- `npx vitest run` — 147/147 passed (19 test files), including the 5 new tests above; no
  regressions in existing suites (Portfolio, ConversationTestCard, apis/utils, etc.).
- No `@testing-library/react` in the project, so interactive DOM tests (click/keydown)
  weren't available; new component tests use the existing `renderToStaticMarkup` pattern
  (see `DeliveryFeedbackAlert.test.tsx`) to assert rendered output for both components, and
  the add/remove/cap/dedupe logic is covered directly as pure functions instead.
- Manual UI verification (running the dev server, clicking through the new tab) was not
  performed in this session — see Known Issues.

### Known Issues
- Not manually verified in a running browser this session (no dev server / browser check
  performed) — only build/lint/unit-test verified. Worth a quick manual pass (add 2-3
  codes incl. one invalid, confirm the category-row layout and inline error) before/at QA.
- No per-comparison weight customization (`AnalysisConfigPanel` stays single-stock-only) —
  deliberate scope cut per Architecture doc, not a gap.

### Blockers
None.

## Review
(Fill in after PR review.)
