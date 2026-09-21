# TASK-012

## Title
Frontend: Finance ↔ Portfolio cross-link for a held stock (Gap Audit item 1,
DECISION-005)

## Owner
Frontend

## Status
IN_PROGRESS

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
- **`Finance/hooks/useFinanceAnalysis.ts`**: added `useSearchParams` read on
  mount, guarded by a `useRef` so it fires at most once. When the URL
  carries a `symbol` (and optionally a `market` of exactly `'TW'`/`'US'`):
  sets `stockSearchTerm`, force-sets `analysisTab = TAB_QUANTITATIVE`
  (explicit call, even though it's already the default, per the task's
  "regardless of any other default" wording), then calls `handleStockSearch`
  directly with the URL values — **not** a duplicate `getComprehensiveAnalysis`
  call. `handleStockSearch` gained two optional params
  (`termOverride`, `marketOverride`) purely so the effect can pass the URL
  values without waiting on the next render's `stockSearchTerm`/`detectMarket`
  state (React state setters are async, so reading `stockSearchTerm` in the
  same tick would still see the pre-update empty string); existing call
  sites (button click, Enter key) are unaffected since both params are
  optional and default to the prior `stockSearchTerm`/`detectMarket(code)`
  behavior. When `market` is missing/invalid, falls back to the existing
  `detectMarket` heuristic unchanged — no second market-detection path
  added. Visiting `/finance` with no `symbol` param is a no-op (early
  return), so normal nav-menu visits are unaffected.
- **`Finance/index.tsx`**: one incidental fix required by the above —
  the search button's `onClick={handleStockSearch}` was passing the click
  `MouseEvent` straight through as `handleStockSearch`'s (now real) first
  argument; changed to `onClick={() => handleStockSearch()}` so it still
  calls with no args. Enter-key handler already called `handleStockSearch()`
  with no args, unaffected.
- **`Portfolio/HoldingDetailDrawer.tsx`**: new `FinanceCrossLink` component
  — an outlined, full-width `Button` (not `IconButton`, so the label itself
  is the plain-text CTA: "在 Finance 查看完整分析") linking to
  `/finance?symbol=${symbol}&market=${market}` (both URL-encoded),
  `target="_blank" rel="noopener noreferrer"`, mirroring `JobCard.tsx`'s
  external-link pattern. Rendered directly below `AiTechSummary`, gated on
  `position.assetType === 'STOCK'` only (not shown for `FUND`/`CASH`/
  `LIABILITY`/`REAL_ESTATE`) — `AssetType` checked against the literal
  union in `apis/portfolioApis/types.ts`. `position.market` passed through
  unchanged (`'TW'`/`'US'`, already the exact values Finance's
  `detectMarket`/`MarketRegion` expect — no translation needed, matching
  the Tech Lead scoping note).
- No new component test added (no existing test file for
  `useFinanceAnalysis` or `HoldingDetailDrawer` to extend; manually traced
  the effect's guard logic and the button's gating condition instead).
- `npm run lint` (0 warnings), `npm run build`, `npx vitest run` (136
  tests, all pre-existing — none reference the touched files) all pass with
  no regressions.

### Tests
`npm run lint` → 0 warnings. `npm run build` → succeeds (`Finance` and
`Portfolio` chunks both compile clean). `npx vitest run` → 136/136 passed.

### PR
(pending — see next commit)

## Known Issues
- Manual end-to-end verification (opening a STOCK holding's drawer in a
  running app, clicking the link, confirming `/finance` opens in a new tab
  with the quantitative tab active and the analysis already loading/loaded,
  and confirming a plain `/finance` visit is unaffected) was **not**
  performed in this session — no backend/DB available to run the app
  against, same limitation noted in `TASK-011`'s own Known Issues.
  Recommend a manual pass (or QA) before merge.

## Review
(To be filled in by Tech Lead after a PR is opened.)
