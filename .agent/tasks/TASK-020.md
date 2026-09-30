# TASK-020

## Title
SEO Phase B: server-side inject word metadata into index.html

## Owner
Backend

## Status
PR_OPENED（PR #390）

## Priority
MEDIUM

## Objective
Raw HTML for `/english/dictionary/{word}` contains word-specific title, meta description and
canonical, for all user agents.

## Requirements
- For requests matching `SitemapController.DictionaryPathPrefix/{word}` (reuse
  `BotPreviewMiddleware.TryExtractWord`), read the built `index.html`, replace/insert
  `<title>`, meta description, `<link rel="canonical">` using `Word.SeoTitle/SeoDescription`,
  and return it (SPA scripts unchanged). Cache the template in memory.
- HTML-encode all injected values.
- Unknown word, lookup error, timeout, or any exception -> fall through to the unchanged
  `index.html` (never break page load). Only GET/HEAD.
- Keep `BotPreviewMiddleware` behavior for link-preview bots as-is.

## Acceptance Criteria
- Unit tests: injection, encoding of hostile word data, unknown word fallback, non-word paths
  untouched, exception fallback.
- No change for any other route; no new AI calls or credit cost.
- Offline tests only (no LiveContract).

## Technical Notes
Fallback is `MapFallbackToFile("/index.html")` in Program.cs. Prefer a middleware/endpoint
scoped to the word path over changing the global fallback. Watch response caching headers
so a stale template is not served after deploy.

## Dependencies
TASK-019 merged（PR #388，已滿足；可開工）.

## Result
- Added `JackyAIApp.Server/Common/WordMetadataMiddleware.cs`: for GET/HEAD requests matching
  `SitemapController.DictionaryPathPrefix/{word}` (reusing `BotPreviewMiddleware.TryExtractWord`),
  reads and in-memory-caches `wwwroot/index.html`, looks up the word via `IDictionaryService`
  (same cached call path the API/frontend already uses — no new AI cost), and injects
  `<title>`/meta description/`<link rel="canonical">` from `Word.SeoTitle`/`SeoDescription` using
  `MatchEvaluator`-based regex replace (avoids `$`-backreference corruption from AI-generated SEO
  text) + HTML-encoding via `WebUtility.HtmlEncode`. Response sent with `Cache-Control: no-store`
  so a CDN/browser never serves one word's markup for another or outlives a deploy.
  Any failure path (unknown word, lookup exception/timeout, missing/unreadable template file) logs
  (exceptions only) and falls through to `_next(context)`, so the unmodified SPA `index.html`
  always still loads via the existing static-file/fallback pipeline.
- Registered in `Program.cs` right after `app.UseMiddleware<BotPreviewMiddleware>()` and before
  `UseDefaultFiles()/UseStaticFiles()` — known link-preview bots are unaffected (still
  short-circuited by `BotPreviewMiddleware` first); every other requester to a word-detail path
  now gets the real SPA with correct per-word `<head>` metadata instead of the generic static file.

### Files Changed
- `JackyAIApp.Server/Common/WordMetadataMiddleware.cs` (new)
- `JackyAIApp.Server.Tests/Common/WordMetadataMiddlewareTests.cs` (new)
- `JackyAIApp.Server/Program.cs`

### PR
https://github.com/twjackysu/JackyAIApp/pull/390

### Tests
- `dotnet test --filter "FullyQualifiedName~WordMetadataMiddlewareTests"` — 14/14 passed
  (injection, HTML-encoding of hostile word data incl. a `$`-backreference regression case,
  known-word injection end-to-end, unknown-word fallback, lookup-exception fallback, non-word
  paths untouched, non-GET/HEAD methods untouched, missing-template-file fallback).
- `dotnet test --filter "Category!=LiveContract"` (full offline suite) — 667/667 passed, no
  regressions. LiveContract tests not run (out of scope; no external API touched by this change).
- `dotnet build` (Release) — succeeds, no new warnings.

### Known Limitations
- Regex-based `<title>`/meta-description replace assumes the built `index.html`'s existing
  single-line format (matches current `jackyaiapp.client/index.html`); a future edit to that
  template that removes the description meta tag entirely still works (falls back to inserting
  before `</head>`), but a structural change other than that isn't specifically tested.
- No explicit lookup timeout is imposed independent of the underlying `HttpClient`/OpenAI call's
  own timeout — imposing one would require canceling `IDictionaryService.GetWordAsync` mid-flight,
  which risks using the per-request scoped `DbContext` after the response has already fallen
  through and the request scope is disposed. Relying on the existing outbound-call timeout avoids
  that hazard; any timeout still surfaces as a caught exception and falls through correctly.

## Review
