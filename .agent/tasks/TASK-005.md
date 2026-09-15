# TASK-005

## Title
Frontend: display delivery/fluency feedback on Conversation Test voice turns

## Owner
Frontend

## Status
READY

## Priority
HIGH

## Objective
Surface the new delivery-feedback metrics from `TASK-004` (pace, pause
count, filler-word count, rule-based comments) in the Conversation Test UI,
next to the existing grammar-correction alert, so the feature is actually
usable — not just an unused API field.

## Context
See `.agent/architecture/ARCHITECTURE.md` → "Decision: Speaking delivery
feedback (pronunciation/fluency) — Skill-Building item 1" and `TASK-004`
for the backend side. Today, `useVoiceRecording.ts` posts the recorded
audio to `whisper/transcribe`, gets back `{ Text }`, then the transcript is
submitted to `conversation/respond` for grammar correction; the result is
rendered via `CorrectionAlert.tsx` inside `ConversationTestCard/index.tsx`.
After `TASK-004`, `whisper/transcribe`'s response will also include a
`DeliveryFeedback` object (pace/pause/filler metrics + rule-based comment
strings) — this task renders it.

**This is delivery/fluency feedback, not pronunciation-accuracy scoring —
label it accordingly in the UI** (e.g. "表達流暢度回饋"), do not call it
"發音評分"/pronunciation grading, to avoid promising something the feature
doesn't measure (see Architecture doc's Phase 1/Phase 2 distinction).

## Requirements
- Extend the relevant API types (`apis/examApis/types.ts`, wherever the
  `whisper/transcribe` response type is defined) with the new
  `DeliveryFeedback` fields from TASK-004's response shape.
- Add a small display component (or extend `CorrectionAlert.tsx`'s
  pattern with a sibling component, Frontend Engineer's call on which reads
  more naturally) that renders the delivery feedback for a turn: pace,
  pause count, filler count, and the rule-based comment strings — shown
  alongside (not replacing) the existing grammar `CorrectionAlert`.
- Only render the delivery-feedback block when the backend actually
  returned one (handle the "too short/silent clip" case from TASK-004
  where the field may be absent — don't render an empty/broken card).
- Keep this scoped to the Conversation Test's voice-recording turns only;
  do not touch the grammar-correction logic/copy itself.

## Acceptance Criteria
- [ ] After a voice-recorded turn in Conversation Test, the UI shows
      delivery-feedback (pace/pauses/fillers + comments) alongside the
      existing grammar correction, when the backend returns it.
- [ ] No delivery-feedback UI renders (no empty/placeholder card) when the
      backend omits the field (short/silent clip case).
- [ ] UI copy never uses "pronunciation"/"發音評分" wording for this
      feature — "delivery"/"fluency"/"流暢度" only.
- [ ] `npm run lint`, `npm run build`, and existing Vitest suite stay
      green; add tests for the new display component/logic following the
      existing test patterns in the Conversation Test test files.

## Technical Notes
- `ConversationTestCard/index.tsx`, `CorrectionAlert.tsx`,
  `useVoiceRecording.ts` — the three files most likely to need changes;
  confirm exact paths (search hasn't been re-verified against `master`'s
  current state at task-authoring time — do a quick `git grep` before
  starting since other Conversation Test PRs have landed since the last
  Tech Lead file-path check).
- `apis/examApis/types.ts` — where `ConversationCorrection` and related
  turn-result types live; add `DeliveryFeedback` alongside them.

## Dependencies
- Depends on `TASK-004` (backend) landing first for the actual response
  shape — coordinate field names/casing with whichever PR merges first;
  do not guess and diverge if TASK-004 changes shape during its own
  review.

## Result
(Frontend Engineer fills in.)

## Review
(Tech Lead fills in after PR.)
