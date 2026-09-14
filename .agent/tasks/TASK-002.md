# TASK-002

## Title
Frontend: canonical word-path routing + per-word meta tags + fix anonymous 401

## Owner
Frontend

## Status
PR_OPENED

## Priority
HIGH

## Objective
Make the Dictionary word page have a clean, crawlable, shareable canonical
URL per word, with correct per-page metadata, and stop firing a guaranteed-
to-fail authenticated request for logged-out visitors.

## Context
See `.agent/architecture/ARCHITECTURE.md` → "Decision: Public, indexable
Dictionary word pages" for full background. The route is already reachable
without login (`/english/dictionary` is not wrapped in `RequireAuth`); this
task is about making it actually SEO/share-ready, plus a correctness fix.

## Requirements
1. **Path-param routing**: change the word lookup route from query string
   (`/english/dictionary?word=apple`) to a path segment
   (`/english/dictionary/:word`, e.g. `/english/dictionary/apple`).
   - Update `EnglishLearning/index.tsx` route definition.
   - Update `Dictionary/index.tsx` to read the word via `useParams()`
     instead of `useSearchParams()`.
   - Keep the existing search-box/lookup UX working (navigate to the new
     path on submit instead of setting a query param).
   - Decide with Backend (TASK-001) on exact casing/encoding rules for the
     path segment (e.g. lowercase, URL-encode multi-word phrases) so the
     sitemap and the route agree.
2. **Fix the anonymous-401 bug**: `Dictionary/index.tsx` currently calls
   `useGetRepositoryWordsByWordIdQuery` unconditionally. Skip this query
   when there is no authenticated user (reuse whatever "is logged in" signal
   `RequireAuth`/`useGetUserInfoQuery` already exposes elsewhere — don't
   invent a second auth check). The favorite/bookmark button in
   `components/WordCard.tsx` should be hidden, disabled, or redirect to
   login when clicked by an anonymous visitor — not silently fail.
3. **Per-word metadata**: add `react-helmet-async` (new dependency — confirm
   it's acceptable per frontend conventions in `jackyaiapp.client/CLAUDE.md`)
   and set, per word:
   - `<title>` — word + short product suffix (match existing title style).
   - `<meta name="description">` — short definition/summary text, truncate
     sensibly.
   - `<link rel="canonical">` pointing at the path-param URL.
   - This is JS-rendered metadata (helps Google, not link-preview scrapers —
     that gap is tracked separately as Phase 2 in ARCHITECTURE.md, not part
     of this task).
   - **Do not compute the title/description text in TypeScript.** Coordinate
     with TASK-001 (Backend) so `GET api/dictionary/{word}` returns two new
     response fields — e.g. `seoTitle` / `seoDescription` — computed by a
     single C# helper on the backend. This frontend code must render those
     fields verbatim (string interpolation into `<title>`/`<meta>` only, no
     truncation/formatting logic of its own). Reason: TASK-003's bot
     middleware needs to show bots the exact same text real users get: if
     both the frontend (TS) and the backend middleware (C#) each format
     their own copy of this text, the two will drift apart over time since
     no one interacts with the bot-facing copy to notice it going stale.
     Making the backend the single place that formats this string and
     having the frontend just display it removes that failure mode
     entirely instead of relying on developers remembering to keep two
     implementations in sync.

## Acceptance Criteria
- [ ] Visiting `/english/dictionary/apple` (no login) renders the word page
      directly from the path, no query string involved.
- [ ] Old query-string links (`?word=apple`), if any exist externally,
      either still work via a redirect or are acceptably deprecated —
      confirm with Tech Lead which approach before removing query-string
      support entirely, in case existing bookmarks/shares rely on it.
- [ ] Logged-out visitor triggers **no** request to
      `GET /api/repository/word/{wordId}` (check network tab / test).
- [ ] Favorite button is hidden or clearly gated (not silently broken) for
      anonymous users.
- [ ] Page `<title>` and meta description change per word and are visible in
      rendered DOM (view-source or React DevTools — confirm it's not only
      set post-hydration in a way a headless check would miss).

## Technical Notes
- `RequireAuth.tsx` does a hard redirect and is not reusable here — do not
  wrap this route in it.
- `App.tsx` currently sets `document.title` per top-level app via `useEffect`
  — the new per-word title should take precedence on the dictionary route;
  check for conflicts with that existing effect.

## Dependencies
- Coordinate path/casing convention with TASK-001 (Backend sitemap).
- Depends on TASK-001 adding `seoTitle`/`seoDescription` to the word response
  (see Requirement 3) — confirm the exact field names/shape together before
  either side hard-codes them.

## Result

Implemented as scoped:

- **Path-param routing**: added `dictionary/:word` alongside the existing
  `dictionary` route in `EnglishLearning/index.tsx` (React Router has no
  optional-param syntax, so kept as two route entries rather than one).
  `Dictionary/index.tsx` now reads the word via `useParams()`; the search
  box and "recently searched"/spell-suggestion clicks all `navigate()` to
  `/english/dictionary/{encodeURIComponent(word)}` instead of setting a
  query param. Word casing/encoding matches TASK-001's sitemap exactly:
  `encodeURIComponent` (JS) ↔ `Uri.EscapeDataString` (C#) both being
  RFC 3986 percent-encoders, no forced lowercasing on either side (the
  lookup is already case-insensitive server-side).
- **Old `?word=` links**: rather than dropping support or blocking on a
  round-trip to Tech Lead for a call on a minor UX detail, chose the
  reversible option — `Dictionary/index.tsx` redirects
  `?word=apple` → `/english/dictionary/apple` via `navigate(..., {replace:
  true})` the first time it renders with a legacy query param and no path
  param. Flagging this choice here per the task's own instruction; happy to
  change to "no redirect" if Tech Lead prefers, but this seemed like the
  safer default given unknown existing external links.
- **Anonymous-401 fix**: `useGetRepositoryWordsByWordIdQuery` is now
  skipped when `useGetUserInfoQuery()` (the same auth signal
  `RequireAuth`/`UserProfileMenu` already use) has no user — no second auth
  check invented. `WordCard` already had an `isHideFavoriteButton` prop
  (used today by `WordOfTheDay`), reused here rather than adding a new one:
  passed `isHideFavoriteButton={!isLoggedIn}` so the favorite button is
  hidden (not silently broken) for anonymous visitors.
- **Per-word metadata**: added `react-helmet-async@^3.0.0` (new dependency,
  matches the "small, no server component" framing in
  `ARCHITECTURE.md`/task notes) + `HelmetProvider` in `main.tsx`.
  `Dictionary/index.tsx` renders `<title>`/`<meta name="description">`/
  `<link rel="canonical">` straight from `data.data.seoTitle`/
  `seoDescription` (TASK-001's fields) with **no** TypeScript-side
  formatting — string interpolation only, per the single-source-of-truth
  requirement.
  - `App.tsx`'s existing `document.title` `useEffect` was the flagged
    conflict risk: it runs on every route change and would stomp
    Dictionary's per-word title. Replaced it with `<Helmet><title>...`
    at the `App` level instead — `react-helmet-async` lets the most
    deeply-rendered `<Helmet>` win on singleton tags like `<title>`, so
    Dictionary's per-word `<Helmet>` (rendered further down the tree)
    takes precedence automatically without any route-name coupling
    between `App.tsx` and `Dictionary`.
  - `apis/dictionaryApis/types.ts`: added `seoTitle`/`seoDescription` to
    the `Word` interface (both `getWord` and `getWordOfTheDay` return this
    same DTO shape, confirmed against `WordMapper.ToDto`/
    `WordOfTheDayService` on the backend).

**Verification performed**:
- `npm run build` (tsc + vite build) — clean.
- `npm run lint` — 0 warnings.
- `npm test` (vitest) — all 131 existing tests still pass, no regressions.
- Manual reasoning through the redirect/no-loop case: after a legacy-URL
  redirect, `word` becomes non-null so the redirect condition can't re-fire.

**Known limitations**:
1. **No new automated test for `Dictionary/index.tsx` itself.** This repo
   has no existing pattern/infra for testing a page component that's wired
   into Redux (RTK Query) *and* React Router at once — every current
   `*.test.tsx` renders a plain presentational component via
   `renderToStaticMarkup`, no `@testing-library/react`, no mock
   store/router helpers exist anywhere. Building that scaffolding to cover
   one component felt like exactly the "new dependency/over-engineering"
   the task's own principles ask me to avoid, so I verified behavior by
   reading the code paths (redirect-once, skip-when-logged-out,
   navigate-on-search) instead and left this as a flagged limitation rather
   than silently skipping it. Recommend a follow-up task if Tech Lead wants
   real component-level coverage (would need to introduce
   `@testing-library/react` + a small test-store/router helper as
   shared infra, not just for this one file).
2. Did not verify in a live browser (no `dotnet`-backed API running in this
   sandbox to fetch real word data against) — confirmed via build/lint/unit
   tests only, per the acceptance criteria's "view-source or React
   DevTools" check I could not execute here.
3. Left the exact wording of the old-query-string handling (redirect vs.
   deprecate) as my own call per the task's escalation guidance — see
   above; easy to flip if Tech Lead disagrees.

PR: https://github.com/twjackysu/JackyAIApp/pull/333

## Review
(Tech Lead fills in after PR review)
