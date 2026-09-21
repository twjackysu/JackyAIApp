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
- **`Finance/hooks/useFinanceAnalysis.ts`**: extracted the quantitative
  (`getComprehensiveAnalysis`) call out of `handleStockSearch` into a
  `runQuantitativeAnalysis(code, market)` callback so both the manual
  search path and the new URL-driven auto-search share one code path (no
  duplicated `getComprehensiveAnalysis` call, per the task's own
  constraint). Added a `useSearchParams`-backed effect, guarded by a
  `useRef` flag so it fires at most once per mount: reads `symbol`/`market`
  from the URL, falls back to the existing `detectMarket` heuristic when
  `market` is missing or isn't exactly `'TW'`/`'US'`, sets
  `stockSearchTerm`, forces `analysisTab = TAB_QUANTITATIVE` (never the
  credit-gated AI-trend tab), and calls `runQuantitativeAnalysis` directly
  with the URL values (not through `handleStockSearch`/state, which would
  race against React's async state update). A visit to `/finance` with no
  `symbol` param takes the early return and behaves exactly as before.
- **`Portfolio/HoldingDetailDrawer.tsx`**: new `FinanceCrossLink`
  component — a full-width text `Button` with `OpenInNewIcon`, labelled
  "在 Finance 查看完整分析", linking to
  `/finance?symbol=${symbol}&market=${market}` with
  `target="_blank" rel="noopener noreferrer"` (same pattern as
  `JobSearch/components/JobCard.tsx`'s external link). Rendered directly
  below `AiTechSummary` (both stay visible together, per the task), gated
  on `!isFund` — confirmed via `PositionsTable.tsx` that the drawer only
  ever opens for `STOCK` or `FUND` positions, so `!isFund` is equivalent to
  `assetType === 'STOCK'` here and the link never shows for `FUND`.
- No backend/DTO changes — Finance's quantitative endpoints were already
  `[AllowAnonymous]` and already accepted a bare code + market, confirmed
  in `ARCHITECTURE.md`'s own investigation before starting.
- `npm run lint` (0 warnings), `npm run build`, `npx vitest run` (136
  tests) all pass with no regressions. `node_modules` wasn't installed in
  this session's sandbox initially (`npm run lint` failed on a missing
  `@eslint/js`); ran `npm install` first, which resolved it.

## Known Issues
- Manual browser verification (opening a STOCK holding's drawer, clicking
  the link, confirming the new tab lands on the quantitative tab with the
  analysis already loaded, and confirming a plain `/finance` visit is
  unaffected) was **not** performed — no backend/DB available in this
  sandbox to run the app against, consistent with every other
  Skill-Building frontend task's own note on this. Recommend a manual pass
  (or QA) before merge, matching the acceptance criteria's own emphasis on
  verifying actual behavior, not just the code.

### PR
https://github.com/twjackysu/JackyAIApp/pull/361

## Review
(To be filled in by Tech Lead after a PR is opened.)
