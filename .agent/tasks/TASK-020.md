# TASK-020

## Title
SEO Phase B: server-side inject word metadata into index.html

## Owner
Backend

## Status
IN_PROGRESS

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

## Review
