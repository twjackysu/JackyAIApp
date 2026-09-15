# TASK-004

## Title
Backend: fluency/delivery feedback on Conversation Test voice turns (pace, pauses, filler words)

## Owner
Backend

## Status
READY

## Priority
HIGH

## Objective
Close the "說 speaking" gap named in `REQUIREMENTS.md` → "Skill-Building
Feature Expansion": Conversation Test currently gives grammar correction on
the transcript only, with zero feedback on *how* something was said. Add
objective delivery-feedback metrics (speaking pace, pause count, filler-word
count) computed from data the existing Whisper call already returns, at
zero added AI/API cost.

## Context
See `.agent/architecture/ARCHITECTURE.md` → "Decision: Speaking delivery
feedback (pronunciation/fluency) — Skill-Building item 1" for the full
investigation and the Phase 1/Phase 2 split. This task is Phase 1 only.
**This is fluency/delivery feedback (pace/pauses/fillers), not
phoneme-level pronunciation-accuracy scoring** — the latter needs a new
third-party vendor and is explicitly out of scope here (tracked as an open
question to Boss, not this task).

Current flow: `ExamController.cs:68` (`POST whisper/transcribe`) →
`ExamService.TranscribeAudioAsync` (`ExamService.cs:463`) →
`OpenAIPromptService.TranscribeAudioAsync` (`Services/OpenAI/
OpenAIPromptService.cs:204`), which calls Whisper with `ResponseFormat =
"json"` (line 216) and returns only `response.Text`. Audio is a transient
`byte[]` (read via `audioStream.ReadExactlyAsync`) — not persisted, out of
scope after the call.

## Requirements
- In `OpenAIPromptService.TranscribeAudioAsync`, change the
  `AudioCreateTranscriptionRequest.ResponseFormat` to `"verbose_json"` and
  request word-level timestamps (`TimestampGranularities`, if exposed by
  the installed Betalgo.Ranul.OpenAI version — fall back to segment-level
  `Start`/`End` timestamps if word-level isn't available in this package
  version; note whichever granularity is actually used in the Result
  section). Confirm the OpenAI Whisper per-minute billing is unaffected by
  response format (it is, per OpenAI's public pricing — response format is
  not a separate line item) before relying on that cost claim in review.
- Add a small helper (e.g. `ConversationDeliveryMetrics` /
  `SpeechFluencyAnalyzer`) that computes, from the verbose response:
  - **Pace**: words per minute (word count / `Duration` in minutes).
  - **Pause count**: number of gaps between consecutive segments/words
    exceeding a fixed threshold (start at 1.5s; make it a named constant,
    not a magic number, so it's easy to tune later).
  - **Filler-word count**: case-insensitive match against a small fixed
    English filler-word list ("um", "uh", "uhh", "like", "you know") in
    the transcript text.
  - A short rule-based (non-AI) feedback string per metric, in the same
    tone as existing user-facing copy (e.g. "語速適中" / "說話有點快，可以
    放慢一點" / "有 3 次明顯停頓，可以試著先想好整句再開口" /
    "出現了一些贅字（um/like）"). Keep thresholds/wording as named
    constants so a follow-up tuning pass doesn't need to touch call sites.
  - Do **not** call this "pronunciation feedback" anywhere in code,
    comments, or returned copy — call it delivery/fluency feedback, to
    avoid overselling what's actually measured (see Architecture doc).
- Extend `WhisperTranscriptionResponse` (returned by `POST
  whisper/transcribe`) with the new fields (e.g. `DeliveryFeedback` object
  containing `PaceWpm`, `PauseCount`, `FillerWordCount`, and a `Comments`
  list of the rule-based strings). Keep `Text` unchanged for backward
  compatibility with `conversation/respond`'s existing input contract.
- No new credit gate — this rides on the existing
  `CreditCosts.AudioTranscription` charge already applied in
  `ExamService.TranscribeAudioAsync` (`ExamService.cs:~470`,
  `ConsumeCreditsAsync(..., CreditCosts.AudioTranscription, ...)`). Do not
  add a second `ConsumeCreditsAsync` call for this feature.
- Handle the case where `Duration` is 0 or very short (e.g. near-silent or
  sub-1-second clips) without dividing by zero or producing a nonsensical
  WPM — return no delivery feedback (null/omitted) rather than a garbage
  number in that case.

## Acceptance Criteria
- [ ] `POST whisper/transcribe` response includes delivery-feedback fields
      (pace, pause count, filler-word count, rule-based comments) computed
      from the same Whisper call already being made — no second AI call.
- [ ] No new `ConsumeCreditsAsync` call added; `AudioTranscription`
      remains the only credit cost for this endpoint.
- [ ] Short/silent audio clips do not crash or return nonsensical metrics.
- [ ] Unit tests cover the metrics helper directly (pace calculation, pause
      detection across a synthetic list of segment timestamps, filler-word
      matching) independent of the OpenAI call, following the existing
      `WordSeoTextFormatterTests`-style pattern (pure-function unit tests
      for a formatting/computation helper).
- [ ] No occurrence of "pronunciation" in user-facing copy or code comments
      for this feature — must read as delivery/fluency feedback only.

## Technical Notes
- `Services/OpenAI/OpenAIPromptService.cs:204-235` — the only place the
  Whisper call happens.
- `Common/CreditCosts.cs:45` — `AudioTranscription = 1`, already applied
  in `ExamService.cs`; do not duplicate the charge.
- Frontend counterpart is `TASK-005` — coordinate the exact JSON shape of
  the new fields with that task so the frontend doesn't need a second
  round of changes once it lands.

## Dependencies
- None blocking. Independent of TASK-001/002/003 (SEO work, already
  merged). TASK-005 (frontend) depends on this task's response shape.

## Result
(Backend Engineer fills in.)

## Review
(Tech Lead fills in after PR.)
