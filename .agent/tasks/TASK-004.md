# TASK-004

## Title
Backend: fluency/delivery feedback on Conversation Test voice turns (pace, pauses, filler words)

## Owner
Backend

## Status
PR_OPENED

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

Implemented as scoped (Phase 1 only — fluency/delivery, not pronunciation
accuracy):

- `OpenAIPromptService.TranscribeAudioAsync` now requests
  `ResponseFormat = AudioResponseFormat.VerboseJson` with
  `TimestampGranularities = [TimestampGranularity.Word]` (confirmed via the
  Betalgo.Ranul.OpenAI v9.2.6/master source that `AudioResponseFormat`/
  `TimestampGranularity` are string-backed structs with implicit
  conversions — same OpenAI per-minute Whisper billing regardless of
  response format, per OpenAI's public pricing page, which doesn't list
  response format as a separate cost dimension). Word-level granularity
  was available (no need for the segment-level fallback the task
  anticipated), giving finer-grained pause detection than segment
  boundaries would.
- Return type changed from `(string? Transcription, string? Error)` to
  `(AudioTranscriptionResult? Result, string? Error)` — a small new record
  (`Services/OpenAI/AudioTranscriptionResult.cs`) carrying `Text`,
  `DurationSeconds`, and `Words` (list of `TranscribedWord(Word, Start,
  End)`), so the metrics helper doesn't need to depend on the Betalgo
  package's own response types directly.
- New `SpeechFluencyAnalyzer` (`Services/Exam/`, pure static class, no DI) —
  computes:
  - **Pace**: transcript word count / duration in minutes.
  - **Pause count**: gaps between consecutive word timestamps exceeding a
    named `LongPauseThresholdSeconds` constant (1.5s).
  - **Filler-word count**: whole-word/phrase regex match (`\bword\b`,
    case-insensitive) against "um", "uh", "uhh", "like", "you know" — whole-
    word matching specifically avoids false positives like "unlike"
    containing "like" as a substring.
  - Rule-based (non-AI) Chinese comments per metric, phrased close to the
    task's own examples. Every threshold/wording is a named constant.
  - Returns `null` (no feedback block) when duration is below
    `MinDurationSecondsForFeedback` (1.5s) or the transcript has zero
    words — avoids divide-by-zero/nonsensical WPM on near-silent clips, per
    the acceptance criteria.
  - Never uses the word "pronunciation" anywhere in code, comments, or
    returned strings — checked explicitly against the task's own
    requirement.
- `WhisperTranscriptionResponse` (`DTO/ConversationModels.cs`) gains an
  optional `DeliveryFeedback` field (`PaceWpm`, `PauseCount`,
  `FillerWordCount`, `Comments`); `Text` is unchanged, so this is additive
  and backward-compatible with the existing frontend contract (TASK-005
  will consume the new field; today's frontend simply ignores it).
- `ExamService.TranscribeAudioAsync` calls `SpeechFluencyAnalyzer.Analyze`
  in-process after the existing `IOpenAIPromptService.TranscribeAudioAsync`
  call — no second `ConsumeCreditsAsync` call added; the feature rides on
  the existing `CreditCosts.AudioTranscription` charge, confirmed by a new
  regression test asserting `ConsumeCreditsAsync` is still called exactly
  once.

**API type verification without a local SDK**: no `dotnet` SDK or cached
NuGet packages exist in this sandbox (same limitation as TASK-001/003).
Fetched the actual Betalgo.Ranul.OpenAI source from GitHub (matching the
installed `9.2.6` package — same tag list, no newer release exists as of
today) to confirm exact types before writing code, rather than guessing:
`AudioCreateTranscriptionRequest.ResponseFormat`/`TimestampGranularities`
property types, the `AudioCreateTranscriptionResponse.Words`/`Segments`/
`Duration` shape and nested `WordSegment`/`Segment` classes, `IAudioService
.CreateTranscription`'s exact signature, and `BaseResponse.Error`/
`Successful` (the latter is a computed `Error == null` property with no
setter — verified before writing the failure-path unit test, which sets
only `Error` rather than trying to assign `Successful` directly).

**Tests**: 
- `SpeechFluencyAnalyzerTests` — pure-function coverage: too-short/empty
  clip → null, pace calculation, slow/fast pace comments, pause detection
  (including a boundary case that should *not* count), whole-word-only
  filler matching (the "unlike" false-positive case), and the multi-word
  "you know" phrase.
- `OpenAIPromptServiceTests` — two new tests: request shape (verifies
  `ResponseFormat`/`TimestampGranularities` on the captured request, and
  that `Duration`/`Words` flow through into `AudioTranscriptionResult`) and
  the failure path (`Error` set → `null` result + error message).
- `ExamServiceTests` — updated the existing transcription test for the new
  tuple shape, plus three new tests: delivery feedback present on a normal
  clip, omitted on a too-short clip, and no second credit charge on
  failure.

**Known limitations / things Tech Lead should be aware of before merging:**
1. No `dotnet build`/`dotnet test` run (no SDK in this sandbox) — every
   type/signature claim above was cross-checked against the actual
   upstream library source rather than assumed, but this is not a
   substitute for CI actually compiling it. Recommend confirming CI is
   green before merging.
2. Pace is computed from the transcript's whitespace-split word count, not
   from the `Words` timestamp list's count — simpler, and decoupled from
   whatever Whisper's word-tokenization happens to look like, but means
   pace and pause-detection technically use two different notions of "a
   word" internally. Not expected to matter in practice (both come from
   the same transcript), flagging as a design note.
3. Filler-word list and pace/pause thresholds are English-only /
   conversational-English-tuned constants (matches the task's own
   examples) — no attempt made to localize for non-English conversation
   scenarios if the app ever supports those.

PR: (to be filled in after opening)

## Review
(Tech Lead fills in after PR.)
