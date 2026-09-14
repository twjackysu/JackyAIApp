# TASK-003

## Title
Backend: social-preview middleware for known link-preview bot user agents

## Owner
Backend

## Status
MERGED

## Priority
MEDIUM

## Objective
Make links to a word page render a correct title/description card when
pasted into LINE, Facebook, and similar chat/social apps, without touching
the experience for real users or for Googlebot (Googlebot is already
covered by TASK-002's client-rendered meta tags — see ARCHITECTURE.md).

## Context
See `.agent/architecture/ARCHITECTURE.md` → "Decision: Public, indexable
Dictionary word pages" (Phase 2) and `.agent/boss/TECH_LEAD.md`
(2026-09-13 exchange) for the full reasoning. Summary: LINE/Facebook/etc.
link-preview crawlers do not execute JavaScript, so they only ever see the
SPA's near-empty initial HTML. Their User-Agent strings are official,
publicly documented, and rarely change (unlike anti-abuse bot detection) —
this is a well-established, low-maintenance technique ("dynamic
rendering"), not a search-ranking risk, as long as bots and real users are
shown the same underlying content.

This is independent of and lower priority than TASK-001/TASK-002 — those
already satisfy the "findable via Google search" goal on their own.

## Requirements
- Add middleware in `JackyAIApp.Server`, early in the request pipeline
  (before the SPA's static file/index.html fallback), that matches the
  incoming `User-Agent` header against a small, centrally-defined list of
  known link-preview bot UAs: `facebookexternalhit`/`Facebot` (Facebook),
  `Line/` (LINE), `Twitterbot` (X/Twitter), `Slackbot`, `Discordbot`,
  `WhatsApp`. Keep the list in one constant/config so adding a platform
  later is a one-line change; comment each entry with where that platform
  documents its crawler UA.
- For a matched request to a word-detail path (`/english/dictionary/{word}`),
  respond with a minimal server-rendered HTML document containing:
  `<title>`, `<meta name="description">`, `og:title`, `og:description`,
  `og:url`, and `og:image` (a generic site image if no per-word image
  exists — do not build per-word image generation for this task).
- The title/description text shown to bots must be **byte-for-byte identical**
  to what real users get for the same word. Do this by calling the same
  `seoTitle`/`seoDescription` C# helper that TASK-001 adds to
  `DictionaryService` directly (in-process method call, not an HTTP call to
  your own API) — do not re-implement or hand-tune the formatting in the
  middleware. This is a hard requirement, not a style preference: since no
  human ever looks at the bot-facing HTML, any second copy of this logic
  will silently drift out of sync over time and no one will notice until a
  shared link looks wrong. There is exactly one place in the codebase
  allowed to format this text (see TASK-001, ARCHITECTURE.md).
- Every other request (real browsers, Googlebot, anything not on the list,
  any other route) must fall through unchanged to the existing SPA —
  no behavior change for the current, already-working paths.
- No new external service or paid dependency.

## Acceptance Criteria
- [ ] `curl -A "facebookexternalhit/1.1" https://.../english/dictionary/apple`
      (and the same with LINE's UA) returns HTML whose raw body already
      contains the correct per-word title and OG tags — no JS execution
      required to see them.
- [ ] The same URL with a normal browser UA still returns the standard SPA
      shell, unchanged.
- [ ] Googlebot's UA is deliberately NOT special-cased here — verify it
      still gets the normal SPA path (it already gets correct metadata via
      TASK-002's client-rendered tags plus its own JS execution).
- [ ] Bot-facing title/description text is sourced from the same place as
      the real page's metadata, not duplicated/hand-written separately —
      PR review will explicitly check the middleware calls TASK-001's
      shared helper rather than building its own HTML string with the text
      inlined by hand.
- [ ] No new NuGet package for a hosted prerendering/scraping service.

## Technical Notes
- Scope this task to the word-detail route only. Extending it to Daily
  Challenge/streak/exam share links (also named in the Growth & Retention
  requirement) is a natural follow-up once those pages exist as public,
  shareable URLs — not in scope here.
- Land after or alongside TASK-002 so both paths read title/description
  text from one shared source.

## Dependencies
- Should share metadata logic with TASK-002 (Frontend) — coordinate on
  where that text is computed so both surfaces read the same source.

## Result

Implemented as scoped:

- `LinkPreviewBotUserAgents` (`Common/`) — centralized, documented list of
  bot UA markers (facebookexternalhit/Facebot, Line/, Twitterbot,
  Slackbot, Discordbot, WhatsApp), each commented with its platform's UA
  documentation link. `IsMatch` does an `OrdinalIgnoreCase` substring
  check.
- `BotPreviewMiddleware` (`Common/`) — matches a request's path against
  `SitemapController.DictionaryPathPrefix` (`/english/dictionary/{word}`,
  single segment only) and its User-Agent against the list above. On a
  match, calls `IDictionaryService.GetWordAsync` in-process (method-
  parameter injection, since the service is Scoped and the middleware
  instance is a singleton) and renders `DTO.Word.SeoTitle`/
  `SeoDescription` verbatim (HTML-encoded) into `<title>`, meta
  description, and `og:title`/`og:description`/`og:url`/`og:image` (generic
  image: `/JackyAIIcon.png`, copied into wwwroot from the client's
  `public/` at publish time — no per-word image generation). Every other
  request (real browsers, Googlebot, unmatched/invalid word, any other
  route) falls through unchanged via `next()`.
- Registered in `Program.cs` right after `ExceptionMiddleware`, before
  `UseDefaultFiles()`/`UseStaticFiles()` — ahead of the SPA static-file/
  fallback pipeline.
- Confirmed no re-implementation of the SEO text formatting anywhere:
  the middleware only reads the `SeoTitle`/`SeoDescription` fields
  `WordMapper.ToDto` already populates via TASK-001's
  `WordSeoTextFormatter`.

Started this without waiting for TASK-002 (PR #331) to merge first: TASK-003
is backend-only (middleware + in-process service call), doesn't touch or
depend on any frontend code, and its only real technical dependency
(`WordSeoTextFormatter`/`seoTitle`/`seoDescription`, plus the
`/english/dictionary/{word}` path shape) was already settled and merged
via TASK-001. The "land after or alongside TASK-002" note in this task's
Technical Notes reads as a deployment-sequencing preference, not a build
blocker — flagging this judgment call explicitly in case Tech Lead intended
a harder dependency.

**Tests**: added `BotPreviewMiddlewareTests` — `TryExtractWord` path
parsing (trailing slash, `%20`-encoded multi-word phrases, rejecting
non-word-detail paths), `BuildHtml` verbatim rendering + HTML-encoding,
and `InvokeAsync` end-to-end for: normal browser UA (falls through),
Googlebot UA (falls through, deliberately not special-cased), non-word-
detail path (falls through), failed word lookup (falls through), and
every listed bot UA (serves the preview HTML, skips `next()`).

**Known limitations / things Tech Lead should be aware of before merging:**
1. **No `dotnet` SDK in this sandbox** — could not run `dotnet build`/
   `dotnet test`. Compensated with a careful manual read-through (namespace
   resolution incl. the enclosing-namespace `DTO.Word` pattern already used
   in `RepositoryService.cs`, DI lifetimes, implicit usings per project SDK
   type, `PathString.StartsWithSegments` traced against every test case)
   plus an independent review-agent pass; both found no compile errors or
   logic bugs. Still recommend confirming CI is green before merging.
2. `og:url` reflects the visitor's URL-decoded word casing as typed in the
   shared link, not necessarily the DB's canonical stored casing — same
   convention `SitemapController`'s `loc` URLs already use, not a new
   inconsistency.

PR: https://github.com/twjackysu/JackyAIApp/pull/332

## Review

**2026-09-14, Tech Lead.** Merged into `master` via #332. Read the merged
code directly, with particular attention to the hard requirement from
this task and the 2026-09-13 Boss exchange (drift risk): `BotPreviewMiddleware.InvokeAsync`
calls `IDictionaryService.GetWordAsync` in-process and renders
`word.SeoTitle`/`word.SeoDescription` verbatim (HTML-encoded) — confirmed
it does **not** re-implement or hand-format any title/description text;
there is exactly one formatting implementation in the codebase
(`WordSeoTextFormatter`), as required. `LinkPreviewBotUserAgents.IsMatch`
correctly falls through (returns `false`) for a normal browser UA and for
Googlebot (deliberately not special-cased, per the task's own acceptance
criterion). Registration order in `Program.cs` (after
`ExceptionMiddleware`, before `UseDefaultFiles`/`UseStaticFiles`) is
correct — bots get intercepted before the SPA fallback, everything else
passes through unchanged. `TryExtractWord` correctly rejects multi-segment
paths and handles trailing slashes / percent-encoded segments. No
correctness issues found. Formally closing status to `MERGED` per SOP.

Endorsing the Engineer's judgment call to start TASK-003 before TASK-002's
PR had merged: TASK-003 is backend-only and its actual dependency
(TASK-001's shared helper + the `/english/dictionary/{word}` path shape)
was already on `master`; the "land after/alongside TASK-002" note was
correctly read as a sequencing preference, not a hard build dependency —
no rework resulted from doing it this way.
