# TASK-003

## Title
Backend: social-preview middleware for known link-preview bot user agents

## Owner
Backend

## Status
READY

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
(fill in on completion)

## Review
(Tech Lead fills in after PR review)
