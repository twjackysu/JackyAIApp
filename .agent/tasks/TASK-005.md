# TASK-005

## Title
Frontend: display delivery/fluency feedback on Conversation Test voice turns

## Owner
Frontend

## Status
MERGED

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

Implemented as scoped:

- `apis/examApis/types.ts`: added `DeliveryFeedback` (`paceWpm`, `pauseCount`,
  `fillerWordCount`, `comments: string[]`), matching TASK-004's merged C#
  DTO field-for-field (ASP.NET Core's default camelCase JSON policy —
  confirmed via `Program.cs` — turns `PaceWpm`/`PauseCount`/
  `FillerWordCount`/`Comments` into exactly these names, same pattern
  already used for `ConversationCorrection`). Added
  `deliveryFeedback?: DeliveryFeedback` to both `WhisperTranscriptionResponse`
  and `ConversationTurn`.
- `hooks/useVoiceRecording.ts` (the shared hook — confirmed via grep it's
  also used by `TranslationTestCard`, so kept backward compatible):
  `onTranscriptionComplete` now takes an optional second
  `deliveryFeedback?: DeliveryFeedback` arg, passed through from
  `response.data.deliveryFeedback`. `TranslationTestCard`'s existing
  `(text) => setInput(text)` callback still type-checks unchanged (extra
  arg is simply ignored) and needed no edits.
- New `ConversationTestCard/components/DeliveryFeedbackAlert.tsx` — sibling
  to `CorrectionAlert.tsx`, same `Alert`-based pattern. Renders pace/pause/
  filler-word `Chip`s plus the backend's rule-based comment strings, headed
  "表達流暢度回饋" — never "發音"/pronunciation wording, per the task's
  explicit labeling requirement.
- `ConversationTestCard/index.tsx`: added `pendingDeliveryFeedback` state,
  set on `onTranscriptionComplete` alongside the transcribed text. Attached
  to the user turn's `deliveryFeedback` field at the same point
  `handleSendMessage` builds that turn (mirrors how `correction` is later
  attached to the same turn once the AI responds), then cleared — so a
  stale value can't leak onto a later, unrelated turn (e.g. user records
  once, then sends two manually-typed messages). Also cleared in
  `handleNewConversation`.
- `ConversationTestCard/components/ChatMessages.tsx`: renders
  `DeliveryFeedbackAlert` next to (never replacing) `CorrectionAlert` for
  the user's own turns, only when `turn.deliveryFeedback` is present — no
  empty/placeholder card on the short/silent-clip case where the backend
  omits the field (TASK-004's documented behavior).
- Added `DeliveryFeedbackAlert.test.tsx` following the existing
  `MarkdownMessage.test.tsx` pattern (`renderToStaticMarkup`, no RTL in this
  repo) — asserts the metrics/comments render and that the copy never
  contains "發音".

**Tests run**: `npm run lint` (0 warnings), `npm run build` (clean), and
`npx vitest run` — 133/133 passing (131 pre-existing + 2 new), no
regressions. `node_modules` had to be reinstalled (`npm ci`) first — the
sandbox's checkout didn't have it staged; not a code issue.

PR: https://github.com/twjackysu/JackyAIApp/pull/339

## Known limitations
- `pendingDeliveryFeedback` is attached to whatever the *next* sent message
  is, not strictly re-validated against the transcribed text — if a user
  records, then manually edits the transcribed text before sending, the
  delivery metrics (which describe the audio, not the final text) still
  get attached. Acceptable per the task's scope; flagged for awareness.

## Review
Merged into `master` via PR #339 (2026-09-15), no review comments before
merge. Verified 2026-09-16 by reading the actual merged code directly:
- `DeliveryFeedbackAlert.tsx` renders pace/pause/filler `Chip`s + comments,
  headed "表達流暢度回饋"; its own test file
  (`DeliveryFeedbackAlert.test.tsx`) asserts the rendered HTML never
  contains "發音" — a real regression guard, not just a claim in the task
  doc.
- `ChatMessages.tsx` gates the alert on `turn.deliveryFeedback` being
  present, so the short/silent-clip case renders nothing rather than an
  empty card, matching TASK-004's documented `null` behavior.
- `useVoiceRecording.ts`'s extra optional callback arg is backward
  compatible — `TranslationTestCard`'s existing single-arg callback still
  type-checks, confirmed by reading the current call site.
No correctness issues found. Status flipped to `MERGED` here per SOP
§10/§18 (bookkeeping only).
