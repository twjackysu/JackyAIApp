# TASK-001

## Title
Backend: sitemap/robots endpoint + unique index on Word.WordText

## Owner
Backend

## Status
READY

## Priority
HIGH

## Objective
Support public/indexable Dictionary word pages (Growth & Retention
requirement, DECISION-003) by adding crawl-discovery endpoints and locking
down the word-text uniqueness that the public URL scheme will now depend on.

## Context
See `.agent/architecture/ARCHITECTURE.md` → "Decision: Public, indexable
Dictionary word pages" for full background. The read endpoint
(`GET api/dictionary/{word}`) is already anonymous and needs no change.
This task only adds new, small backend surface area.

## Requirements
- Add `GET /sitemap.xml` (or a reasonable existing-route location — Backend
  Engineer's call) that lists canonical URLs for every valid, non-deleted
  `Word.WordText` known to the system, in standard sitemap XML format.
  - Keep it cheap: a single indexed query over `Word`, no per-word AI calls.
  - If the word count is large enough that a single sitemap file is
    unwieldy, a sitemap index + chunked sub-sitemaps is acceptable, but
    don't over-build this — check actual row count first.
- Add/confirm a `robots.txt` (static or generated) that allows crawling of
  `/english/dictionary/*` and points to the sitemap.
- Add a single C# helper (e.g. a static method on `DictionaryService` or a
  small dedicated `WordSeoTextFormatter`) that formats a word's SEO title
  and short meta-description text from its existing `Meanings`/`Definitions`
  data. Expose the result as two new fields on the `Word` DTO returned by
  `GET api/dictionary/{word}` — e.g. `seoTitle` / `seoDescription`.
  - This is the **single source of truth** for this text. TASK-002
    (frontend `<title>`/meta tags) must render these fields as-is, and
    TASK-003 (bot middleware) must call this same helper directly rather
    than re-deriving or hand-writing the text a second time. Do not
    duplicate the formatting logic anywhere else — see
    `.agent/architecture/ARCHITECTURE.md` for why (drift risk raised by
    Boss, 2026-09-13).
- Add an EF Core migration adding a **unique index** on `Word.WordText`
  (case-insensitive per whatever collation the DB already uses for text
  columns — check existing convention, don't introduce a new one).
  - This is a data-integrity fix independent of SEO, but becomes load-bearing
    once `WordText` is used as a public routing key (see TASK-002). If a
    duplicate already exists in production data, surface that to Tech Lead
    before merging the migration — do not silently dedupe/delete rows.

## Acceptance Criteria
- [ ] `GET /sitemap.xml` returns valid XML listing word detail URLs matching
      the new frontend path shape (`/english/dictionary/{word}`) — coordinate
      the exact path with TASK-002's Frontend Engineer so they match.
- [ ] `GET api/dictionary/{word}` response includes `seoTitle`/`seoDescription`
      fields produced by one shared C# helper, callable directly by
      TASK-003's middleware (no HTTP round-trip needed between them).
- [ ] `robots.txt` is served and does not block `/english/dictionary/`.
- [ ] Migration adds a unique index on `Word.WordText` and applies cleanly
      against current data (or the duplicate conflict is reported back
      before merging).
- [ ] No change to `DictionaryController` auth attributes — confirm existing
      anonymous access still works after the migration.

## Technical Notes
- `DictionaryController.Get` (`GET api/dictionary/{word}`) is the existing
  anonymous read path — no changes needed there.
- `SQLWord.cs` — `Word.Id` is a GUID, `Word.WordText` is the human-readable
  field; migration should target `WordText`.
- Follow the resilience/HTTP-client and general backend patterns already in
  `JackyAIApp.Server/CLAUDE.md`.

## Dependencies
- None blocking — can start immediately. Coordinate URL path string with
  TASK-002 (Frontend) so the sitemap URLs match the actual route.

## Result
(fill in on completion)

## Review
(Tech Lead fills in after PR review)
