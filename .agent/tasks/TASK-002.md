# TASK-002

## Title
Frontend: canonical word-path routing + per-word meta tags + fix anonymous 401

## Owner
Frontend

## Status
PR_OPENED (partial — Requirement 3 blocked, see Result)

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
- [x] Visiting `/english/dictionary/apple` (no login) renders the word page
      directly from the path, no query string involved.
- [x] Old query-string links (`?word=apple`), if any exist externally,
      either still work via a redirect or are acceptably deprecated —
      confirm with Tech Lead which approach before removing query-string
      support entirely, in case existing bookmarks/shares rely on it.
      → Implemented as a client-side redirect (see Result).
- [x] Logged-out visitor triggers **no** request to
      `GET /api/repository/word/{wordId}` (check network tab / test).
- [x] Favorite button is hidden or clearly gated (not silently broken) for
      anonymous users.
- [ ] Page `<title>` and meta description change per word and are visible in
      rendered DOM (view-source or React DevTools — confirm it's not only
      set post-hydration in a way a headless check would miss).
      **BLOCKED** — depends on TASK-001 shipping `seoTitle`/`seoDescription`
      on `GET api/dictionary/{word}`, which hasn't landed yet (no PR, no
      code in the backend as of this run). Not implemented — see Result.

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

**Implemented (Requirements 1 & 2):**
- Path-param routing: added `dictionary/:word` alongside the existing bare
  `dictionary` route in `EnglishLearning/index.tsx`. `Dictionary/index.tsx`
  now reads the word via `useParams()`; the search box, recently-searched
  clicks, and spell-suggestion clicks all `navigate()` to the canonical
  path instead of setting local state.
- **Casing/encoding convention** (had to decide unilaterally since TASK-001
  hasn't started — no Backend Engineer has claimed it yet): path segments
  are lowercased and `encodeURIComponent`-ed. Verified against
  `DictionaryService.GetWordAsync` (`JackyAIApp.Server`), which already
  does `word.Trim().ToLower()` before matching `WordText` — so this is not
  a new behavior, just making the existing server-side normalization the
  public URL convention too. Extracted as a single helper,
  `Dictionary/utils/wordPath.ts` (unit-tested), and reused it at the one
  other internal link that pointed at the dictionary
  (`Exam/ClozeTestCard/index.tsx`'s "查看單字" link, previously
  `?word=` query-string) so there's exactly one place that formats this
  path — same reasoning Tech Lead applied to the seoTitle/seoDescription
  single-source-of-truth fix.
- Old `?word=apple` links: handled with a client-side redirect
  (`navigate(wordPath(legacyWord), { replace: true })`) rather than
  dropping support. Chose this over "acceptably deprecated" because the
  fix is one `useEffect`, there's no backend/infra cost, and it protects
  the one pre-existing internal link and any external bookmark equally.
  Flagging the choice here per the acceptance criterion's "confirm with
  Tech Lead" — did not block on a synchronous answer since both options
  were acceptable per the task and this is the lower-risk one; happy to
  change if Tech Lead prefers otherwise.
- Anonymous-401 fix: `useGetRepositoryWordsByWordIdQuery` now skips when
  `!isLoggedIn` (same `!!useGetUserInfoQuery().data` signal
  `EnglishLearning/Layout.tsx` already uses — not a new auth check).
  `WordCard` already had an unused `isHideFavoriteButton` prop; wired it to
  `!isLoggedIn` instead of adding new UI.
- Side effect of path-param routing: `/english/dictionary/apple` no longer
  string-equals the Dictionary tab's `value` in `EnglishLearningLayout`, so
  the tab strip would show nothing selected while viewing a word. Fixed
  `currentTab` there to also fold any `/english/dictionary/*` path onto
  the Dictionary tab (mirrors the existing bare-`/english` handling
  already in that file). Small, directly caused by this task's routing
  change, not scope creep.

**Not implemented — BLOCKED (Requirement 3, per-word metadata):**
Checked before starting: no `seoTitle`/`seoDescription` field exists
anywhere in the backend or frontend as of this run
(`grep -rn "seoTitle|seoDescription"` → no matches), and there is no open
PR for TASK-001 (checked via GitHub). TASK-001 is still unclaimed/READY.
The task instructions are explicit that this frontend code must render
`seoTitle`/`seoDescription` verbatim and must **not** compute its own
title/description text in TypeScript (to avoid the exact drift risk Boss
raised for TASK-003). Since that backend contract doesn't exist yet,
building the `react-helmet-async` wiring now would mean either inventing
an unofficial shape or writing throwaway formatting logic that violates
the task's own reasoning — so left undone rather than either. `Dependencies`
also explicitly calls for confirming field names/shape with Backend before
hard-coding.
Did not add `react-helmet-async` as a dependency yet either, since it
would sit unused until Requirement 3 can be wired up.

**Files changed:**
- `jackyaiapp.client/src/EnglishLearning/index.tsx` — added `dictionary/:word` route.
- `jackyaiapp.client/src/EnglishLearning/Layout.tsx` — tab-highlight fix for word paths.
- `jackyaiapp.client/src/Dictionary/index.tsx` — path-param word, legacy-query redirect, anonymous-401 fix, favorite-button gating.
- `jackyaiapp.client/src/Dictionary/utils/wordPath.ts` (new) + `wordPath.test.ts` (new) — shared canonical-path helper.
- `jackyaiapp.client/src/Exam/ClozeTestCard/index.tsx` — reused `wordPath()` instead of a second `?word=` link.

**Tests performed:**
- `npm run lint` — clean (0 errors/warnings).
- `npx vitest run` — 134/134 passing, including the 3 new `wordPath` tests.
- `npm run build` — TypeScript compiles, production build succeeds.
- Did not manually smoke-test in a browser (no `dotnet run` backend available
  in this session to exercise the full request/response cycle) — flagging
  this per the "don't claim tests passed if they weren't executed" rule.
  Recommend a manual pass (search a word logged-out, confirm no 401 in the
  network tab, confirm the favorite button is gone, click the old
  `?word=` link from a bookmarked URL and confirm it lands on the path
  URL) before this is called fully done.

**Next step for this task:** once TASK-001 ships `seoTitle`/`seoDescription`
on `GET api/dictionary/{word}`, add `react-helmet-async`
(`HelmetProvider` at the app root, `<Helmet>` in `Dictionary/index.tsx`
rendering those two fields verbatim + a canonical `<link>` built from
`wordPath()`) to close out Requirement 3 and the last acceptance
criterion, on the same branch/PR.

## Review
(Tech Lead fills in after PR review)
