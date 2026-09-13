# TASK-001

## Title
Backend: sitemap/robots endpoint + unique index on Word.WordText

## Owner
Backend

## Status
PR_OPENED

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

Implemented as scoped:

- `SitemapController` adds `GET /sitemap.xml` (single projection over
  `Word.WordText`, excludes `DataInvalid` rows, absolute route template —
  no class-level `[Route]`) and `GET /robots.txt` (`Allow: /` +
  `Sitemap:` pointer). Both anonymous, matching `DictionaryController.Get`.
- `WordSeoTextFormatter` (`Services/Dictionary/`) is the single place that
  formats `seoTitle`/`seoDescription` text (`"{word} - EngAI Learn
  Dictionary"` / `"{word} ({partOfSpeech}): {first definition, ≤160
  chars}"`). `WordMapper.ToDto` calls it in-process and sets the two new
  `required` fields on `DTO.Word`, so `GET api/dictionary/{word}` now
  returns `seoTitle`/`seoDescription` (camelCase — confirmed System.Text.Json
  Web-default serialization applies, no `PropertyNamingPolicy` override
  exists). TASK-002/TASK-003 should read these fields rather than
  reformatting.
- `DictionaryController.Get` unchanged — anonymous access confirmed intact.
- EF migration `20260913120000_AddUniqueIndexOnWordText` bounds
  `Word.WordText` from `nvarchar(max)` to `nvarchar(450)` (required — SQL
  Server can't index a MAX column) and adds a unique index. No custom
  collation introduced; relies on the DB's existing default collation for
  case-insensitivity, consistent with every other unique index in
  `AzureSQLDBContext`.
- Chose `/english/dictionary/{word}` (lowercase, `Uri.EscapeDataString`-encoded)
  as the sitemap URL shape, matching the path already decided in
  `ARCHITECTURE.md` for TASK-002's frontend route.

**Tests**: added `WordSeoTextFormatterTests` (title/description formatting,
including the multi-meaning-skip and truncation cases) and
`SitemapControllerTests` (XML/robots content, `DataInvalid` filtering via
an in-memory `AzureSQLDBContext`, following `ExamControllerTests`'s
pattern); extended `DictionaryServiceTests` to assert the new fields are
populated on `GetWordAsync`.

**Known limitations / things Tech Lead should be aware of before merging:**
1. **No `dotnet` SDK or DB connectivity in this sandbox.** Could not run
   `dotnet build`/`dotnet test`/`dotnet ef migrations add`. The migration
   was hand-written to mirror the exact `AlterColumn`+`CreateIndex` pattern
   used by existing migrations (e.g.
   `20260827112814_AddFxRateAndWidenUsdTwdRate.cs`), and the
   `.Designer.cs` was generated by diffing against
   `AzureSQLDBContextModelSnapshot.cs` to guarantee they match — but none
   of this has been compiled or run against a real SQL Server instance. A
   general-purpose review agent independently re-read every changed file
   line-by-line against the current repo state (types/signatures/required
   members/namespace resolution) and found no compile errors, and confirmed
   the Designer.cs body is byte-identical to the updated ModelSnapshot
   except for the expected header differences — but this is not a
   substitute for an actual build.
2. **Could not check production data for pre-existing `WordText` duplicates
   or values >450 chars** (no DB access here). Per the task instructions I
   have *not* added any dedupe/delete logic — if either condition exists,
   the migration will fail to apply rather than silently corrupt data.
   Recommend running it against a staging/prod snapshot (or at minimum
   `SELECT WordText, COUNT(*) FROM Words GROUP BY WordText HAVING
   COUNT(*) > 1` / `SELECT WordText FROM Words WHERE LEN(WordText) > 450`)
   before merging.
3. `robots.txt` uses a broad `Allow: /` rather than narrowly restricting to
   `/english/dictionary/*`, since most other routes are auth-gated anyway
   and Google won't index behind-login pages — flagging this choice in
   case Tech Lead prefers a narrower policy.

PR: https://github.com/twjackysu/JackyAIApp/pull/328

## Review
(Tech Lead fills in after PR review)
