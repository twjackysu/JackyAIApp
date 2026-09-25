# TASK-016

## Title
Frontend: Finance comparison tab's per-stock errors leak into the single-stock tab's shared Alert

## Owner
Frontend

## Status
PR_OPENED

## Priority
MEDIUM (correctness bug on already-shipped `master` code, not a regression
risk to anything in flight — no other open task touches this file)

## Objective
Fix a bug QA found on the merged `TASK-014` PR (#364,
https://github.com/twjackysu/JackyAIApp/pull/364#issuecomment-5798485642):
entering a stock code that fails on Finance's "多股比較" (comparison) tab
also pops the single-stock tab's generic "綜合分析失敗，請檢查股票代碼是否
正確" `Alert` at the top of the page, on top of the comparison tab's own
correct per-slot inline errors. Confusing/duplicated error messaging, no
crash or data loss.

## Context
Read `jackyaiapp.client/src/Finance/hooks/useFinanceAnalysis.ts` and
`jackyaiapp.client/src/Finance/index.tsx` before implementing — confirmed
by Tech Lead 2026-09-25 by reading the actual code (not just QA's report):

- `fetchComprehensiveAnalysis` (`useFinanceAnalysis.ts`) is a single call
  site sharing one `useGetComprehensiveAnalysisMutation()` trigger between
  the single-stock quantitative tab (`runComprehensiveAnalysis`) and the
  comparison tab's `runComparison()` (`Promise.all` over multiple codes).
  RTK Query's mutation hook only reflects the *last* triggered call's
  `error`/`isLoading` — it isn't per-call.
- `Finance/index.tsx:268` renders `{comprehensiveError && !isComprehensiveLoading && (...)}`
  unconditionally — it doesn't check which tab (`analysisTab`) is active.
  So when `runComparison()`'s concurrent calls include a failure, the
  shared `comprehensiveError` becomes truthy and this Alert renders even
  while the user is on the comparison tab (`TAB_COMPARISON`), stacking on
  top of `ComparisonResult.tsx`'s own correct per-slot error display.
- `comparisonError` itself (the field meant to guard unexpected/whole-batch
  failures in `runComparison`) is working correctly and stays `null` here —
  the bug is the *other* shared state leaking across tabs, not a bug in the
  comparison logic itself.

## Requirements
1. Gate the `comprehensiveError` Alert (`Finance/index.tsx:268`) so it only
   renders on the tabs it actually belongs to — i.e. add
   `analysisTab === TAB_QUANTITATIVE &&` to its existing condition. Do not
   render it while `analysisTab === TAB_COMPARISON`.
2. Confirm this doesn't reintroduce a stale-error flash when switching
   *into* the quantitative tab after a failed comparison run: verify
   `handleTabChange`'s existing `clearSearchResults()` → `resetComprehensive()`
   call already clears `comprehensiveError` on every tab switch (it does,
   per the current code) — no additional reset needed, just confirm/test it.
3. Add a regression test exercising the actual interaction (not
   `renderToStaticMarkup`, which QA's report notes can't catch this class of
   bug) — e.g. a `useFinanceAnalysis`-level test that runs a comparison with
   a failing mock call and asserts `comprehensiveError` doesn't cause
   `Finance/index.tsx`'s quantitative-tab Alert condition to read true while
   `analysisTab === TAB_COMPARISON`. Follow whatever this project's existing
   hook-testing convention is (check for other `use*.test.ts(x)` files under
   `Finance/hooks/` or similar first).

## Acceptance Criteria
- [ ] On the comparison tab, a failing code shows only `ComparisonResult`'s
      own per-slot inline error — the top-level "綜合分析失敗..." Alert does
      not appear.
- [ ] On the quantitative tab, a failed single-stock search still shows the
      top-level Alert exactly as before (no regression).
- [ ] Switching from a failed quantitative search into the comparison tab
      (or vice versa) never shows a stale Alert from the other tab.
- [ ] A new automated test exercises this interaction directly (not just
      static markup) and fails without the fix.
- [ ] `npm run lint` (0 warnings), `npm run build`, `npx vitest run` all pass
      with no regressions.

## Technical Notes
- Minimal, targeted fix — do not split `getComprehensiveAnalysis` into two
  separate mutation triggers (one per tab) unless the tab-gate approach
  above turns out to be insufficient once you're in the code; QA's report
  suggested that as *a* possible fix, not the only one, and the tab-gate is
  smaller and lower-risk for what's actually a rendering-condition bug, not
  a data-fetching one.
- No backend change — this is a frontend rendering-condition bug only.

## Dependencies
None — self-contained fix in already-merged code, no other open task
touches these files.

## Result
Implemented by another Frontend Engineer session before this run started:
[PR #371](https://github.com/twjackysu/JackyAIApp/pull/371)
(`claude/amazing-cori-0k55ra`), opened 2026-09-25T02:23:56Z. Adds a derived
`showComprehensiveErrorAlert` flag in `useFinanceAnalysis.ts`
(`analysisTab === TAB_QUANTITATIVE && !!comprehensiveError &&
!isComprehensiveLoading`), gates `Finance/index.tsx`'s top-level Alert on
it, and adds `useFinanceAnalysis.test.tsx` — a new hook-level test
convention (real `createRoot`/`act` mount, mocked `fetch`/`Request`) that
fails without the fix per the PR description. Matches all of this task's
Requirements and Technical Notes (tab-gate only, no mutation-trigger
split). This run verified via the GitHub API rather than re-implementing:
CI green (`lint-and-build-client` + `build-and-test-server` both
`success`), `mergeable_state: clean`, no review comments yet. Not
re-implemented to avoid adding a 5th duplicate PR to this task (see
`TASK-015`'s 4-way pile-up on 2026-09-25) — awaiting Tech Lead/Boss
review.

## Review
(Fill in after PR review.)
