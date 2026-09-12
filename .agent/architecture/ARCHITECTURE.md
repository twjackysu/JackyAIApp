# JackyAIApp — Architecture Notes

> Maintained by Tech Lead Agent. Not a full rewrite of `CLAUDE.md` — this file
> only records decisions and technical designs for active initiatives, plus
> pointers to the patterns already established in the codebase.

## Baseline

Frontend: Vite + React 18 + TS, client-side rendered SPA, RTK Query, React
Router v6. Backend: .NET 10 Web API, EF Core / Azure SQL, ASP.NET Cookie
auth (Google OAuth), `[Authorize]` per-controller with `[AllowAnonymous]`
overrides where needed. See root `CLAUDE.md` and module `CLAUDE.md` files
for the full stack description — not duplicated here.

---

## Decision: Public, indexable Dictionary word pages (Growth & Retention — SEO)

### Context
`REQUIREMENTS.md` → "Growth & Retention (No Paid Ads)" (HIGH priority,
DECISION-003) names publicly-viewable, indexable word pages as the
highest-leverage growth idea: zero incremental AI cost (content is already
cached), no login required to view.

Investigated the current implementation (2026-09-12) before scoping tasks.
Findings:

1. **Backend is already public.** `DictionaryController.Get`
   (`GET api/dictionary/{word}`) has no `[Authorize]` and already carries a
   comment stating anonymous lookup is intentional. `DictionaryService.GetWordAsync`
   touches no per-user data. No backend auth change is needed for the read path.
2. **Frontend route is already outside `RequireAuth`.** `/english/dictionary`
   is not wrapped in `RequireAuth` (`EnglishLearning/index.tsx`). So a
   logged-out visitor can already open the page today.
3. **But it's not actually SEO-ready**, for three separate reasons:
   - `Dictionary/index.tsx` unconditionally calls the `[Authorize]`-gated
     `GET api/repository/word/{wordId}` (favorite/bookmark status) even when
     logged out — guaranteed 401 on every anonymous page view, and the
     favorite button renders (and silently fails) for anonymous users.
   - The word is read from a **query string** (`?word=apple`), not a path
     segment. There is no clean canonical URL per word, which hurts both
     crawling and shareability (the two things this initiative is for).
   - The app is **pure client-side rendered**, with a single static
     `<title>` and no meta description/OG tags anywhere, and no per-word
     `document.title`/meta injection. Search engines that execute JS
     (Googlebot) can index CSR content to a degree, but link-preview
     scrapers (LINE, Facebook, etc. — the actual sharing channel named in
     the requirement) generally do **not** execute JS and will see nothing
     useful. So "public" ≠ "indexable/shareable" here; both existing
     `[AllowAnonymous]`-style access AND page metadata are required, and
     metadata is the part that's actually missing.
4. `Word.Id` is a GUID (not slug-friendly); `Word.WordText` is the natural
   slug but has no DB-level unique constraint today (uniqueness is only
   enforced by application logic via `SingleOrDefaultAsync`).

### Decision
Ship this in two phases, split by cost/complexity, per DECISION-003's "no
increase in cost per user" constraint:

**Phase 1 — low effort, ships now (see TASK-001, TASK-002):**
- Move word routing from query string to a path param:
  `/english/dictionary/:word` (frontend route + component change only,
  backend endpoint shape unchanged since it's already keyed on word text).
- Skip the `GET api/repository/word/{wordId}` call entirely when the viewer
  is not logged in (check auth state before firing the query), and hide/gate
  the favorite button for anonymous users instead of letting it fail silently.
- Add dynamic per-word `<title>` and `<meta name="description">` (and
  canonical `<link>`) via `react-helmet-async` (new, small, no server
  component — helps JS-executing crawlers and is a prerequisite for Phase 2
  regardless of how Phase 2 is implemented).
- Add a `sitemap.xml` endpoint (backend, enumerating known `WordText`
  values) and a `robots.txt` entry pointing to it, so crawl discovery
  doesn't rely on internal links alone.
- Add a unique index on `Word.WordText` (data-integrity fix, independent of
  SEO, but relevant now that the column becomes a public routing key).

**Phase 2 — needs a cost/complexity call, escalated to Boss (see
`.agent/boss/TECH_LEAD.md`):** non-JS-executing scrapers (LINE/Facebook
share previews — the actual sharing mechanic in the requirement) need
server-rendered meta tags, which a pure Vite CSR SPA cannot provide as-is.
Options (not yet decided):
  a. Lightweight bot-detection middleware in `JackyAIApp.Server` that,
     for known crawler/bot user agents only, serves a small server-rendered
     HTML shell with the right `<title>`/OG tags for that word (real users
     still get the normal SPA) — smallest blast radius, no new hosting
     component, some maintenance cost.
  b. A hosted prerendering service (e.g. prerender.io-style) sitting in
     front of the SPA — no app code change, but a new paid/infra dependency,
     working against the "reduce cloud cost" goal.
  c. Accept Phase 1 only for now (JS-executing search engines still index
     it) and defer proper share-preview support — cheapest, weakest on the
     "shareable results" acceptance criterion.

### Reason
Backend already does the hard part (public, cacheable, no per-user side
effects) for free — no architecture change needed there. The actual gap is
frontend metadata/routing (cheap, Phase 1) and crawler-vs-SPA rendering
(a real trade-off, Phase 2) — those shouldn't be bundled into one task or
decided unilaterally given the cost-sensitivity in DECISION-003.

### Alternatives
- Full SSR framework migration (Next.js/Remix): rejected — disproportionate
  cost/complexity for one feature area, against "simplest architecture that
  satisfies requirements" and against the stated cost-reduction goal.
- Static-generate all word pages at build/deploy time: rejected for now —
  word set grows continuously (AI-generated on demand), so static
  generation would need a rebuild/redeploy trigger per new word; revisit
  only if Phase 2 bot-middleware proves insufficient.

### Trade-offs
Phase 1 ships real value (crawlable by Google, correct canonical URLs, no
more silent 401s) without touching hosting/infra. It does not fully satisfy
the "shareable to LINE/social" acceptance criterion on its own, since link
previews need Phase 2.

### Impact
- `jackyaiapp.client`: routing change, new dependency (`react-helmet-async`),
  `Dictionary/index.tsx` conditional-query fix.
- `JackyAIApp.Server`: new `sitemap.xml`/`robots.txt` endpoints, one EF
  migration (unique index on `WordText`).
- No change to `DictionaryController` auth or `DictionaryService` — already
  correct.
