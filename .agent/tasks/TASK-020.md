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
- Added `WordPageMetadataMiddleware` (`Common/`): for GET/HEAD requests to
  `/english/dictionary/{word}`, reads `wwwroot/index.html` (cached in memory after first read),
  looks up the word via `IDictionaryService.GetWordAsync`, and replaces/inserts `<title>`,
  `<meta name="description">`, and `<link rel="canonical">` using `Word.SeoTitle`/`SeoDescription`
  — the same single formatting source `WordSeoTextFormatter`/`BotPreviewMiddleware` already use, so
  nothing is reformatted a second time. Registered in `Program.cs` right after
  `BotPreviewMiddleware` (which fully handles the link-preview-bot case already) and before the
  static-file pipeline, so it only ever sees requests BotPreviewMiddleware didn't already answer.
- Every failure path (non-GET/HEAD method, non-word path, missing/unreadable template, unknown
  word, lookup exception, a >3s lookup) falls through to `_next(context)` before touching the
  response — verified no path writes a status/header/body and then still falls through.
- All injected values are `HtmlEncode`d. Regex replacements use `MatchEvaluator` lambdas (not
  replacement strings) so a literal `$` in word/definition text can't be misread by
  `Regex.Replace` as a backreference token (`$1`, `$&`, ...) — caught this in an independent
  review pass and added a dedicated test for it.
- HEAD requests get the same headers (including `Content-Length`) but no body, per HTTP
  semantics — also caught by the independent review pass and fixed before opening the PR.
- Response sent with `Cache-Control: no-store` so a stale injected page can't outlive a deploy.
- No `dotnet` SDK/build available in this sandbox — verified via a careful manual read-through
  plus a separate adversarial review-agent pass (fresh context, read every referenced file
  including the real `WordMapper.cs`/`DictionaryService.cs`/`jackyaiapp.client/index.html`) that
  found no compile errors and no bug that could break page load. CI still needs to confirm.
- **Known, deliberately out-of-scope items flagged by the review** (pre-existing patterns this
  task doesn't introduce, or accepted trade-offs):
  - `pageUrl`'s scheme comes from `context.Request.Scheme`, read before `UseHttpsRedirection()`
    runs — same construction `BotPreviewMiddleware` already uses for `og:url`. If the app is ever
    fronted by a proxy that doesn't mark the connection HTTPS at the Kestrel layer (no
    `UseForwardedHeaders` is configured), the canonical link could read `http://`. Not a
    regression from this PR, but this PR does expose the same construction to every user agent
    instead of just preview bots — worth a sanity check against how Azure App Service actually
    terminates TLS for this app.
  - If `<title>`/`<meta name="description">` are ever removed entirely from
    `jackyaiapp.client/index.html`, injection silently no-ops for that field (no insert-if-missing
    fallback, unlike the canonical link). Both tags exist today; flagged as a latent gap only.
  - The 3s lookup timeout can't cancel the underlying `GetWordAsync` call (interface has no
    `CancellationToken` parameter) — the abandoned task keeps running server-side; a later fault
    on it is an unobserved task exception (no-op in modern .NET, not a crash).

## Review
