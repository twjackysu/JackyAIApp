# TASK-020

## Title
SEO Phase B: server-side inject word metadata into index.html

## Owner
Backend

## Status
PR_OPENED

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

Implemented as scoped:

- New `WordMetadataInjectionMiddleware` (`Common/`), registered in `Program.cs` right after
  `BotPreviewMiddleware` and before `UseDefaultFiles()`/`UseStaticFiles()`/`MapFallbackToFile`.
  Only acts on GET/HEAD requests whose path matches
  `SitemapController.DictionaryPathPrefix/{word}` (reuses `BotPreviewMiddleware.TryExtractWord`
  verbatim — no new path-parsing logic). Every other route, and link-preview bots (already
  intercepted earlier by `BotPreviewMiddleware`, which is unchanged), fall through exactly as
  before.
- On a match: reads the built `wwwroot/index.html` (cached in `IMemoryCache`, no expiration —
  the process restarts on every deploy, which naturally busts the cache, same assumption the
  startup EF migration code already relies on), calls `IDictionaryService.GetWordAsync(word)`
  (same call/cache path the anonymous `GET api/dictionary/{word}` endpoint and
  `BotPreviewMiddleware` already use — no additional AI-call surface), and replaces/inserts
  `<title>`, `<meta name="description">`, and a `<link rel="canonical">` using
  `Word.SeoTitle`/`SeoDescription` (HTML-encoded via `WebUtility.HtmlEncode`). SPA
  scripts/body are untouched — only tags inside `<head>` change.
- Canonical URL is built the same way as `BotPreviewMiddleware`/`SitemapController`
  (`{scheme}://{host}{DictionaryPathPrefix}/{Uri.EscapeDataString(word)}`), matching the shape
  `Dictionary/index.tsx`'s own `<Helmet>` canonical tag renders client-side.
- Fallback to the unmodified SPA shell (i.e. call `_next`, do nothing else) on: non-word path,
  non-GET/HEAD method, missing/unreadable template file, unknown word / lookup failure
  (`!result.IsSuccess`), a lookup that doesn't finish within a 3s soft timeout
  (`Task.WhenAny` against `Task.Delay`, so a hung dependency can never stall page load), or any
  other exception — everything funnels through one `try/catch` plus the existing early-return
  checks, so nothing here can turn into a broken page load.
- Response sets `Cache-Control: no-store` on an injected response, since it's per-word dynamic
  content — avoids a browser/CDN pinning one word's (or a pre-deploy template's) metadata onto
  another word's URL.
- HEAD requests get the same status/headers but no body written (`HttpMethods.IsHead` check).

**Tests**: new `WordMetadataInjectionMiddlewareTests.cs` — pure `Inject()` tests (title/description
replacement + canonical insertion, HTML-encoding of hostile word data e.g. `<script>`/`&`/quotes,
inserting a description `<meta>` when the template has none) and `InvokeAsync` tests (non-word
path untouched, non-GET/HEAD method untouched, unknown-word fallback, dictionary-service-throws
fallback, missing-template-file fallback, lookup-timeout fallback via a test-only mutable
`internal static LookupTimeout`, happy-path injection incl. `Cache-Control`/content-type
assertions, HEAD omits body). All offline (mocked `IDictionaryService`/`IWebHostEnvironment`,
real temp-dir `index.html` fixture, in-memory `IMemoryCache`) — no `LiveContract` tests involved,
no DB.

**Known limitations / things Tech Lead should be aware of before merging:**
1. **No `dotnet` SDK in this sandbox** — could not run `dotnet build`/`dotnet test`. A
   general-purpose review agent independently re-read every changed/new file line-by-line
   against the current repo state (real type signatures: `BotPreviewMiddleware.TryExtractWord`,
   `SitemapController.DictionaryPathPrefix`, `DTO.Word`/`WordBase` required members,
   `IDictionaryService.GetWordAsync`, `ServiceResult`, `Microsoft.NET.Sdk.Web` implicit usings,
   `AddMemoryCache()` registration, `InternalsVisibleTo` for the test project, C# 12
   primary-constructor + `partial class` + `[GeneratedRegex]` on `net10.0`, Moq API usage,
   xUnit static-field test-isolation safety) and reported no compile errors or logic bugs — but
   this is not a substitute for an actual build/test run. Recommend `dotnet build` +
   `dotnet test --filter "Category!=LiveContract"` before merging.
2. The 3-second lookup timeout is a soft timeout only (`Task.WhenAny`, not a real
   `CancellationToken` passed into `GetWordAsync`, since that method doesn't accept one) — the
   in-flight lookup keeps running in the background after the response falls through; it isn't
   cancelled, just no longer awaited. This matches the existing codebase's `IDictionaryService`
   surface rather than changing its signature (would be a larger, unrequested change).
3. This runs for **every** GET/HEAD request under `/english/dictionary/{word}` from **any**
   caller, not just crawlers — including the SPA's own initial document request for real users.
   That's intentional per the task ("for all user agents"), and reuses the same cached/DB-backed
   `GetWordAsync` path the frontend's own `GET api/dictionary/{word}` call already hits a moment
   later on hydration, so it does not add a second AI-generation call for words that still need
   generating (the first caller to reach `DictionaryService.GetWordAsync` generates + caches it;
   the second is a cache/DB hit) — but flagging the "runs on every real-user page load, not just
   crawlers" scope choice explicitly in case Tech Lead expects it scoped to bots only.
4. Did not verify against a real built `wwwroot/index.html` from an actual `npm run build` +
   `dotnet publish` — verified only against the current `jackyaiapp.client/index.html` source
   content (same file in the current repo). Should be spot-checked once after a real deploy that
   `<title>`/description/canonical actually show up correctly in "View Source" for a known word.

PR: https://github.com/twjackysu/JackyAIApp/pull/391

## Review
