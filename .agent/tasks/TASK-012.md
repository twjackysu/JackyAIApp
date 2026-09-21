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
  `TAB_QUANTITATIVE` fetch logic out of `handleStockSearch` into a shared
  `runQuantitativeSearch(code, marketOverride?)` helper (no new/duplicate
  `getComprehensiveAnalysis` call site — `handleStockSearch` now just calls
  the helper). Added a mount-only `useEffect` (`useSearchParams` from
  `react-router-dom`, empty dep array, `eslint-disable-next-line
  react-hooks/exhaustive-deps` — same established pattern used elsewhere in
  this codebase, e.g. `Exam/ClozeTestCard/index.tsx`) that reads `symbol`/
  `market` from the URL: when `symbol` is present, sets `stockSearchTerm`,
  forces `analysisTab = TAB_QUANTITATIVE`, and calls
  `runQuantitativeSearch(symbol, market)` directly (passing the resolved
  values in, rather than going through `handleStockSearch`'s state-closure
  read, since `setState` before it wouldn't be visible synchronously). Uses
  the URL's `market` when it's exactly `'TW'`/`'US'`, otherwise falls back
  to the existing `detectMarket(symbol)` heuristic — no second
  market-detection implementation added. No-`symbol` visits (e.g. `/finance`
  from the nav menu) hit the `if (!symbol) return;` guard and are completely
  unaffected — verified no other state changes before that guard.
- **`Portfolio/HoldingDetailDrawer.tsx`**: added a text `Button` (MUI
  `href`+`target="_blank"`+`rel="noopener noreferrer"`, same pattern as
  `JobSearch/components/JobCard.tsx`'s external link) below `AiTechSummary`,
  rendered only when `position.assetType === 'STOCK'` (new `isStock` const,
  parallel to the existing `isFund` const — `FUND`/`CASH`/`LIABILITY`/
  `REAL_ESTATE` all excluded). Links to
  `/finance?symbol=${symbol}&market=${market}` (both URL-encoded;
  `position.market` is already `'TW'`/`'US'`, no translation). Label "在
  Finance 查看完整分析" — doesn't imply a credit charge, matching that the
  landing tab is free.
- `npm ci` was needed first (fresh sandbox had no `node_modules`).
  `npm run lint` (0 warnings/errors), `npm run build` (TS compiles clean),
  `npx vitest run` (136 tests, all existing suites, no regressions) all
  pass.
- No manual end-to-end run against a live backend (no backend/DB available
  in this sandbox, same limitation noted in `TASK-011`) — the acceptance
  criteria's network-payload/URL-behavior checks should get a manual or QA
  pass before merge.
- Committed and pushed to `claude/amazing-cori-lj0owf`.

### PR
https://github.com/twjackysu/JackyAIApp/pull/360

## Known Issues
- Manual end-to-end verification (opening a STOCK holding's drawer,
  clicking through to `/finance` with the quantitative tab pre-loaded and
  the analysis already loading, confirming a FUND holding shows no link,
  confirming `/finance` with no query params behaves exactly as before) was
  **not** performed in this session — no backend/DB available to run the
  app against. Recommend a manual pass before merge, or QA picking this up
  per the normal task lifecycle.

## Review
(To be filled in by Tech Lead after a PR is opened.)
