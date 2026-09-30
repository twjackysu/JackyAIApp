# TASK-019

## Title
SEO: Fold "JackyAI" brand into page titles/descriptions

## Owner
Frontend (backend string change is trivial; same owner)

## Status
READY

## Priority
HIGH (SEO, follows Boss decision 2026-09-30: the site is named JackyAI)

## Objective
Visible title/description text contains "JackyAI" so brand queries match.

## Requirements
- `jackyaiapp.client/index.html`: `<title>` and add meta description with JackyAI brand.
- `WordSeoTextFormatter.cs` (SeoTitle/SeoDescription): replace "EngAI Learn" with JackyAI
  wording, e.g. `{word} - JackyAI 英文字典`. Used by both client Helmet and BotPreviewMiddleware.
- Update affected existing tests.

## Acceptance Criteria
- No user-visible "EngAI Learn" remains in title/meta/OG output.
- Existing tests updated and passing; no other behavior changes.

## Technical Notes
grep for "EngAI Learn" across client and server. Do not touch the verification meta from PR #386.

## Dependencies
PR #386 merged (same file, `index.html`) to avoid conflict.

## Result

## Review
