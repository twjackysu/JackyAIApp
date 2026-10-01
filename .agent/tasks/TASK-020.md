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

- New `WordMetaInjectionMiddleware` (`Common/`), registered in `Program.cs` immediately after
  `BotPreviewMiddleware` and before `UseDefaultFiles`/`UseStaticFiles`/`MapFallbackToFile`.
  `BotPreviewMiddleware` is unmodified — for known link-preview bots it still terminates the
  request first, so this middleware only ever sees everyone else (real users, Googlebot, any
  other client) requesting `SitemapController.DictionaryPathPrefix/{word}`.
- Matches only `GET`/`HEAD` on that exact word-detail path shape (reuses
  `BotPreviewMiddleware.TryExtractWord` verbatim, per the task's Technical Notes — no
  second path-parsing implementation). Everything else falls through unchanged.
- Reads `index.html` once via `IWebHostEnvironment.WebRootFileProvider` (the same provider
  `UseStaticFiles`/`MapFallbackToFile` already read from, so no new assumption about physical
  file layout) and caches the string on the middleware instance (constructed once per app by
  `UseMiddleware<T>`).
- On a GET/HEAD hit: calls `IDictionaryService.GetWordAsync(word)` (same lookup
  `BotPreviewMiddleware` already uses) with a 3s timeout via `Task.WhenAny` — a never-looked-up
  word falls through to an OpenAI generation call inside that service, which must not hang a
  real user's page load. Timed-out, failed, or exceptional lookups all fall through to `next()`,
  i.e. the unmodified `index.html` via the existing static-file/fallback pipeline — page load is
  never broken.
- `WordMetaInjectionMiddleware.InjectMetadata` (pure, static, internal — testable without an
  `HttpContext`) replaces `<title>`, replaces `<meta name="description">`'s `content` (only when
  `SeoDescription` is non-empty — `WordSeoTextFormatter.FormatDescription` can return `""` when a
  word has no definition yet, and an empty meta description is worse than the template's
  default), and inserts `<link rel="canonical">` before `</head>`. All three use
  `word.SeoTitle`/`SeoDescription` as-is — no re-deriving the text, consistent with TASK-001's
  "single source of truth" decision. All injected values are `HtmlEncode`d, including the
  canonical URL.
  - Used `Regex.Replace(string, MatchEvaluator)` rather than `Regex.Replace(string, string)` —
    the string-replacement overload treats `$` in the replacement as a backreference token, which
    would have silently corrupted any title/description containing a literal `$`.
- Response gets `Cache-Control: no-store` — this handler serves different content per path on
  what looks like a single static file to any intermediary cache, and the Technical Notes
  specifically flagged the stale-template-after-deploy risk.
- SPA script tags untouched — only the three meta elements change, so client-side JS still boots
  and renders normally for real users.

**Tests** (`WordMetaInjectionMiddlewareTests.cs`, mirrors `BotPreviewMiddlewareTests`' structure):
`InjectMetadata` pure-function tests (title/description/canonical replacement, HTML-encoding of
hostile data, the `$`-backreference edge case, empty-description fallback) plus `InvokeAsync`
tests against a mocked `IWebHostEnvironment`/`IFileProvider` and `IDictionaryService`: non-word
paths untouched, unknown-word fallback, exception fallback, lookup-timeout fallback (via an
injectable `LookupTimeout` property, exercised with a 20ms timeout so the test stays fast), a
non-GET/HEAD method falls through without calling the dictionary service, and the full
happy-path response (status/content-type/cache header/injected body). All offline — no
`LiveContract`, no new AI calls, no new credit cost (reuses the existing anonymous lookup path).

**Tests actually executed this session** (unlike TASK-001, a `dotnet` SDK was available —
installed `dotnet-sdk-10.0` via `apt-get` in this sandbox):
- `dotnet build JackyAIApp.Server/JackyAIApp.Server.csproj -c Release` — succeeds, 0 errors.
- `dotnet test JackyAIApp.Server.Tests -c Release --filter "Category!=LiveContract"` — **663/663
  pass**, including the 10 new `WordMetaInjectionMiddlewareTests`.

## PR
https://github.com/twjackysu/JackyAIApp/pull/392

## Review
