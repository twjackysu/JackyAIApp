# TASK-002

## Title
Frontend: canonical word-path routing + per-word meta tags + fix anonymous 401

## Owner
Frontend

## Status
READY

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

## Result
(fill in on completion)

## Review
(Tech Lead fills in after PR review)
