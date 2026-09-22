# TASK-012

## Title
Frontend: Finance ↔ Portfolio cross-link for a held stock (Gap Audit item 1,
DECISION-005)

## Owner
Frontend

## Status
PR_OPENED

## Priority
HIGH (per PM's 2026-09-20 gap-audit proposal: cheapest, highest-leverage
item in the Finance/Portfolio proposal list — pure UI discoverability fix,
no new AI calls or infra)

## Objective
A Portfolio holding's detail view (`HoldingDetailDrawer`) currently has no
link to Finance's fuller, **free**, per-stock analysis (technical + chip +
comprehensive score) for that same symbol — a holder gets less insight on a
stock they own than a non-holder gets by searching Finance directly. Add a
link from the drawer to Finance, pre-filled and pre-run for that symbol.

## Requirements

1. **`Finance/hooks/useFinanceAnalysis.ts`**: read `symbol` and `market`
   from the URL query string (`useSearchParams` from `react-router-dom`) on
   mount. When both are present:
   - Set `stockSearchTerm` to `symbol`.
   - Force `analysisTab = TAB_QUANTITATIVE` regardless of any other default
     — this tab is free/unauthenticated; never auto-land on the credit-gated
     AI-trend tab.
   - Auto-fire the same fetch `handleStockSearch` already performs for the
     quantitative tab (reuse the existing function/logic, don't duplicate
     the `getComprehensiveAnalysis` call), exactly once (guard with a ref or
     an effect dependency array that won't re-fire on every render/tab
     change).
   - If `market` is missing/invalid, fall back to the existing
     `detectMarket(symbol)` heuristic already used elsewhere in this hook —
     don't add a second market-detection implementation.
   - Do not change behavior when the URL has no `symbol` param (normal
     `/finance` visits, e.g. from the nav menu, must be unaffected).

2. **`Portfolio/HoldingDetailDrawer.tsx`**: add a link/button, rendered only
   when `position.assetType === 'STOCK'` (not `FUND` — Finance has no
   fund-analysis surface at all, confirmed during scoping), pointing to
   `/finance?symbol=${position.symbol}&market=${position.market}`
   (`position.market` is already `'TW'`/`'US'` — the exact values Finance's
   `detectMarket`/`MarketRegion` already expect, no translation needed).
   - Open in a new tab: `target="_blank" rel="noopener noreferrer"`, same
     pattern already used by `JobSearch/components/JobCard.tsx`'s external
     link. Do not navigate away from the open Portfolio drawer/page state.
   - Place it near `AiTechSummary` (e.g. below it, or as a second action
     next to the "AI 技術面總結" button) so both the narrow
     `PortfolioAiSummary` and the fuller free Finance breakdown are visible
     together, not to replace the existing AI summary — both stay.
   - Label plainly, e.g. "在 Finance 查看完整分析" — don't imply the link
     itself costs credits (the landing tab is free).

## Acceptance Criteria
- [ ] Opening a **STOCK** holding's detail drawer shows a "查看完整分析"-style
      link/button that was not there before; opening a **FUND** holding's
      drawer does not show it.
- [ ] Clicking the link opens `/finance` in a new tab with the quantitative
      (free) tab active and the same symbol's comprehensive analysis already
      loaded/loading — not just the search box pre-filled with the user
      still having to click search themselves.
- [ ] Visiting `/finance` directly (no query params, e.g. from the nav menu)
      behaves exactly as before — no unsolicited auto-search, no tab change.
- [ ] The link never lands the user on, or auto-triggers, the credit-gated
      AI-trend tab.
- [ ] `npm run lint` (0 warnings), `npm run build`, `npx vitest run` all
      pass with no regressions.

## Technical Notes
- This is a **frontend-only** task. No backend change, no new endpoint, no
  DTO change — confirmed during scoping that Finance's
  `chip-analysis`/`score`/`comprehensive-analysis` endpoints are already
  `[AllowAnonymous]` and already accept a bare stock code + market. See
  `ARCHITECTURE.md` → "Decision: Finance ↔ Portfolio cross-link" for the
  full investigation.
- Don't build a second market-detection or search-triggering code path —
  reuse `useFinanceAnalysis`'s existing `detectMarket`/`handleStockSearch`
  logic; the only new thing is reading the initial value from the URL
  instead of only from user typing.
- Keep the auto-trigger effect narrowly scoped (only fires once per mount
  when the URL actually carries `symbol`) — a general "re-run search on any
  URL change" would risk re-firing after the tab/search state has already
  moved on.

## Dependencies
None — Finance's endpoints already support this; no backend task required.

## Result
- **`Finance/hooks/useFinanceAnalysis.ts`**: extracted the existing
  `getComprehensiveAnalysis` call path out of `handleStockSearch` into a
  shared `runComprehensiveAnalysis(code, market)` (wrapped in `useCallback`)
  so both the manual search button and the new URL-driven auto-search call
  the exact same, single fetch path — no duplicated
  `getComprehensiveAnalysis` call. Added a mount `useEffect` (guarded by
  `autoSearchFiredRef`, same one-shot-ref pattern `Credits/index.tsx` already
  uses for its own query-param handling) that reads `symbol`/`market` from
  `useSearchParams()`: when `symbol` is present it sets `stockSearchTerm`,
  forces `analysisTab = TAB_QUANTITATIVE`, and fires
  `runComprehensiveAnalysis` once; `market` is used directly when it's
  exactly `'TW'`/`'US'`, otherwise falls back to the existing `detectMarket`
  heuristic (no second market-detection implementation). A plain `/finance`
  visit has no `symbol` param, so the effect returns immediately and nothing
  changes.
- **`Portfolio/HoldingDetailDrawer.tsx`**: added `FinanceCrossLink`, a
  small `Button` (text variant, `OpenInNewIcon`, `target="_blank"` +
  `rel="noopener noreferrer"`, same external-link pattern
  `JobSearch/components/JobCard.tsx` uses) linking to
  `/finance?symbol=${symbol}&market=${market}`. Rendered directly below
  `AiTechSummary`, only when `position.assetType === 'STOCK'` (not `FUND`,
  per the task — Finance has no fund-analysis surface). Label: "在 Finance
  查看完整分析" — no credit-cost implication, since the landing tab is free.
- No backend change; no new dependency.
- `npm run lint` (0 warnings), `npm run build`, `npx vitest run` (136 tests,
  unchanged) all pass with no regressions.

### Tests
`npm run lint`, `npm run build`, `npx vitest run` all green. No dedicated
component test added — matches the existing convention for
`HoldingDetailDrawer`/`useFinanceAnalysis` (neither had one before this
change).

### PR
https://github.com/twjackysu/JackyAIApp/pull/359

## Known Issues
- Manual end-to-end verification (opening a STOCK holding's drawer in a
  running app, clicking through to `/finance?symbol=...&market=...`,
  confirming the quantitative tab auto-loads and a plain `/finance` visit is
  unaffected) was not performed in this session — no backend/DB available
  to run the app against. Recommend a manual pass before merge, or QA
  picking this up per the normal task lifecycle.
**Six parallel scheduled sessions independently picked up this task before
any PR landed**, producing six functionally-identical implementations:
PRs #357, #358, #359, #360, #361, #362, all touching the same two files
(`Finance/hooks/useFinanceAnalysis.ts`, `Portfolio/HoldingDetailDrawer.tsx`),
all CI-green. Tech Lead reviewed all six diffs 2026-09-22 and kept **#359**
(https://github.com/twjackysu/JackyAIApp/pull/359), closing the other five
as duplicates with a pointer here. This is the same recurring pattern
flagged in prior status entries (`TASK-002`'s #331/#333, `TASK-008`'s
#346/#347, `TASK-011`'s #353/#354 + #355/#356) but at a new scale — 6
instead of 2-3 — see the escalation in `.agent/boss/TECH_LEAD.md`
(2026-09-22).

**#359's implementation** (kept):
- `Finance/hooks/useFinanceAnalysis.ts`: extracts the existing
  `getComprehensiveAnalysis` call out of `handleStockSearch` into
  `runQuantitativeAnalysis(code, market)`, wrapped in `useCallback` with a
  complete `[analysisConfig, getComprehensiveAnalysis, resetComprehensive]`
  dependency list — no duplicated fetch call. A `useSearchParams`-backed
  `useEffect`, guarded by `useRef` (protects against React 18 StrictMode's
  dev double-invoke, which a hardcoded-`[]`-deps effect without a ref guard
  would not), reads `symbol`/`market` from the URL: when `symbol` is
  present, sets `stockSearchTerm`, force-sets
  `analysisTab = TAB_QUANTITATIVE` (never the credit-gated AI-trend tab),
  and calls `runQuantitativeAnalysis` with the resolved values exactly
  once. Falls back to the existing `detectMarket` heuristic when the URL's
  `market` isn't exactly `TW`/`US` — no second market-detection path.
  `handleStockSearch`'s existing public signature is untouched. A
  `/finance` visit with no `symbol` param hits the early return, zero
  state change.
- `Portfolio/HoldingDetailDrawer.tsx`: new `FinanceCrossLink` — text
  `Button`, `OpenInNewIcon`, "在 Finance 查看完整分析", linking to
  `/finance?symbol=...&market=...`, `target="_blank" rel="noopener
  noreferrer"` (same pattern as `JobCard.tsx`'s external link), rendered
  below `AiTechSummary`, gated explicitly on
  `position.assetType === 'STOCK'`.
- No backend/DTO change, matching the scoping decision.
- `npm run lint` (0 warnings), `npm run build`, `npx vitest run` (136
  tests) all pass, no regressions.

### PR
https://github.com/twjackysu/JackyAIApp/pull/359 (awaiting Boss merge)

## Known Issues
- Manual end-to-end verification (opening a STOCK holding's drawer in a
  running app, clicking through, confirming the quantitative tab auto-loads
  the analysis, confirming a FUND holding shows no link, confirming a plain
  `/finance` visit is unaffected) was not performed in any of the six
  implementing sessions — no backend/DB available in any sandbox, same
  limitation as `TASK-011`. Recommend a manual pass or QA before merge.

## Review
Reviewed #359 in full (diff + all six competing implementations for
comparison) 2026-09-22 — see the PR review comment for the complete
write-up. No correctness issues found; matches every acceptance criterion
in code (the one unchecked box above — manual click-through — is a "needs
a live app" gap, not a code defect). Posted as **READY FOR BOSS REVIEW**.
Closed #357/#358/#360/#361/#362 as duplicates, each with a comment
explaining the specific reason #359 was preferred over it (signature
change requiring a follow-up fix elsewhere, missing `useCallback`/complete
deps array, no `useRef` guard, or a less explicit STOCK gate — see each
PR's closing comment for specifics).
