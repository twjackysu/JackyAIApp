# TASK-010

## Title
Backend: Dictation practice (Skill-Building item 4 — 聽, last item)

## Owner
Backend

## Status
PR_OPENED

## Priority
MEDIUM-LOW

## Objective
Close the "聽 listening" gap named in `REQUIREMENTS.md` → "Skill-Building
Feature Expansion": today listening practice is multiple-choice only
(Listening Test, Listening Response Test) — there is no dictation-style
practice (hear a word, type what you heard). Add a word-anchored dictation
mode that reuses the user's own saved words, the existing TTS pipeline, and
a zero-AI string-comparison grader.

See `.agent/architecture/ARCHITECTURE.md` → "Decision: Dictation practice —
Skill-Building item 4 (last item)" for the full investigation and reasoning.

## Requirements

1. **`GET api/exam/dictation`** → new `GenerateDictationExamAsync()` on
   `ExamService`/`IExamService`:
   - Pick one word via the **existing** `GetRandomUserWordIdAsync(userId)`
     helper (`ExamService.cs:985`) — same helper Cloze/Translation/Sentence
     already use, so the "prefer due words, fall back to a random global
     word if the user's repository is empty" behavior comes for free. Do
     **not** write a new word-picking query for this.
   - Response DTO: `DictationPromptResponse { WordId: string }`. **Do not
     include `WordText`** in this response — unlike Cloze/Translation
     (which already put the answer in their GET response and rely on the
     frontend not to render it), dictation's entire point is "figure out
     the word from sound alone," so the word text must not be
     inspectable in the network response before the user submits. This is
     a deliberate deviation from the Cloze/Translation precedent — flag it
     in the PR so it isn't "fixed" later by someone copying that pattern
     out of habit.
   - Credit-gated via `[ConsumeCredits(CreditCosts.DictationTest, ...)]` on
     the controller action (flat charge, same convention as
     `GetListeningTest`/`GetReadingTest` — covers the one TTS call the user
     is expected to make right after).

2. **`GET api/exam/dictation/audio?wordId=X`** → new
   `GetDictationWordTextAsync(wordId)` service method (looks up
   `Words.SingleOrDefaultAsync(w => w.Id == wordId)`, returns `WordText` via
   `ServiceResult`, `NotFound` if missing) wired into the controller via the
   **existing** `ToAudioResultAsync` private helper (`ExamController.cs:164`)
   — same exact pattern as `GetListeningOptionAudio`/
   `GetListeningResponsePromptAudio`. **Unbilled** (the flat `dictation`
   charge above already covers it) — do not add a second `[ConsumeCredits]`
   here, same "don't double-charge for content the user must consume to
   answer" reasoning already documented on `GetListeningOptionAudio`.

3. **`POST api/exam/dictation/grade`** → new `GradeDictationAsync(request)`:
   - Request DTO `DictationGradeRequest { WordId: string, UserInput: string }`.
   - Looks up the real `Word.WordText` server-side by `WordId` (never trust
     a client-supplied "correct answer").
   - Compare `UserInput` (trimmed, case-insensitive) to `WordText`:
     - `IsCorrect`: exact match after trim + case-fold. No partial credit
       counts as correct — this is a spelling/listening precision drill,
       not a fuzzy-match feature.
     - `Score` (0–100): `100` if exact match; otherwise a normalized
       Levenshtein-distance-based closeness score
       (`100 * (1 - distance / max(correctLength, 1))`, floored at `0`,
       rounded to the nearest int) — purely informational (“you were
       close”), does not affect `IsCorrect`.
   - Response DTO: `DictationGradeResponse { CorrectText: string,
     IsCorrect: bool, Score: int }`. `CorrectText` reveals the answer for
     the post-submission UI.
   - **Not credit-gated** — same "zero underlying cost, don't charge for
     nothing" reasoning as `GenerateWritingPromptAsync`/`GET
     api/exam/writing`: this is pure in-memory string comparison, no AI
     call, no TTS call. Add the same kind of explicit code comment warning
     against adding `[ConsumeCredits]` here later.

4. **`Common/CreditCosts.cs`**: add `DictationTest = 1` — a single TTS clip
   per attempt (one word, not four options like `ListeningTest`), so this
   should be priced at raw `TextToSpeech` cost, not `ListeningTest`'s bundled
   rate. Flag in the PR if you think a different value fits better.

5. **No change to `RecordExamResultAsync`.** Dictation is word-anchored
   (exactly one `WordId` per attempt, like Cloze/Translation/Sentence) —
   it is **not** added to the `isWordless` group. The client calls the
   existing `POST api/exam/record-result` with `ExamType: "Dictation"`,
   `WordId`, `Correct: <IsCorrect from the grade response>`, `Score: <Score
   from the grade response>` — this already works today with zero backend
   changes, since `MapQuality`/`ApplyResult` don't care what string
   `ExamType` is, only whether `WordId` is present. Do not add a
   `"Dictation"` branch anywhere in `RecordExamResultAsync` — if you find
   yourself doing that, stop and re-check against this task; it's a signal
   you've misread the wordless-vs-word-anchored split.

## Acceptance Criteria
- [ ] `GET api/exam/dictation` returns a `WordId` only — verify with a test
      that the raw JSON response contains no word text anywhere (guards
      against a future DTO change accidentally leaking the answer).
- [ ] `GET api/exam/dictation` charges `CreditCosts.DictationTest` exactly
      once per call (same `[ConsumeCredits]` convention as `GetListeningTest`).
- [ ] `GET api/exam/dictation/audio?wordId=X` is unbilled (no
      `ConsumeCreditsAsync` call happens on this path) and returns the
      correct word's synthesized audio — verify it resolves to the same
      text `POST dictation/grade` would score against, not some other
      word.
- [ ] `POST api/exam/dictation/grade` is unbilled, returns `IsCorrect: true`
      only on an exact (trimmed, case-insensitive) match, and returns a
      lower `Score` (not necessarily 0) for a close-but-wrong submission —
      cover at least one exact-match case, one case-difference-only case
      (still correct), and one genuinely-wrong case with a partial `Score`.
- [ ] `RecordExamResultAsync` requires no code change — confirm with a test
      that posting `ExamType: "Dictation"` + a valid `WordId` through the
      **existing, unmodified** endpoint updates that word's SM-2 schedule
      exactly like a Cloze attempt would (same `MapQuality`/`ApplyResult`
      call path).
- [ ] No EF migration is part of this PR — if you find yourself needing
      one, stop and re-check against the Architecture doc; every piece of
      this feature reuses `Word`/`UserWord`/`ExamAttempt` as they already
      exist today.

## Technical Notes
- Read `ExamService.cs:985` (`GetRandomUserWordIdAsync`) and
  `ExamController.cs:104-112` (`GetListeningOptionAudio` +
  `ToAudioResultAsync`) end-to-end before writing new code — this task is
  "wire two existing patterns together," not "invent new word-picking or
  audio-serving logic."
- Levenshtein distance: a small standard DP implementation is fine
  (word-length strings, no performance concern) — do not pull in a new
  NuGet package for this.
- Trim/case-fold using the same culture-invariant comparison style already
  used elsewhere in this file (check how existing string comparisons in
  `ExamService.cs` handle culture, e.g. `StringComparison.OrdinalIgnoreCase`)
  rather than introducing a new convention.
- Deliberately **out of scope** for this task: adjustable playback speed
  (a related but separate gap noted in `REQUIREMENTS.md`'s skill-coverage
  table, not one of the four approved proposals). Don't add a `speed`
  parameter to the audio endpoint — if you think it's worth doing, flag it
  as a follow-up in the PR rather than bundling it in here.

## Dependencies
None — can start immediately. `TASK-011` (Frontend) depends on this task's
response DTO shapes and route names.

## Result

Implemented exactly as scoped — no new word-picking/audio-serving logic, reused the two
existing patterns end-to-end.

### Implementation
- `DTO/ExamModels.cs`: added `DictationPromptResponse { WordId }`, `DictationGradeRequest
  { WordId, UserInput }`, `DictationGradeResponse { CorrectText, IsCorrect, Score }`. Updated
  `RecordExamResultRequest.ExamType`'s doc comment to list `"Dictation"`.
- `Common/CreditCosts.cs`: added `DictationTest = 1` (raw `TextToSpeech` rate — one clip per
  attempt, not `ListeningTest`'s bundled 4-clip rate).
- `Services/Exam/IExamService.cs` + `ExamService.cs`:
  - `GenerateDictationExamAsync()` — reuses the existing `GetRandomUserWordIdAsync`
    (unchanged), returns `WordId` only.
  - `GetDictationWordTextAsync(wordId)` — `Words.SingleOrDefaultAsync`, `NotFound` if missing.
  - `GradeDictationAsync(request)` — looks up `Word.WordText` server-side, trimmed
    `OrdinalIgnoreCase` exact match for `IsCorrect`, a small self-contained Levenshtein-DP
    helper for the informational `Score` (100 on exact match, otherwise
    `100 * (1 - distance/max(len,1))`, floored at 0, rounded). Explicit comment warning against
    adding `[ConsumeCredits]` here later, same as `GenerateWritingPromptAsync`.
- `Controllers/ExamController.cs`: `GET dictation` (`[ConsumeCredits(CreditCosts.DictationTest,
  ...)]`), `GET dictation/audio?wordId=X` (unbilled, wired through the existing
  `ToAudioResultAsync` helper verbatim), `POST dictation/grade` (unbilled).
- **No changes to `RecordExamResultAsync`** — verified with a test that `ExamType: "Dictation"`
  + `WordId` drives the same word-anchored `MapQuality`/`ApplyResult` path Cloze uses.
- **No EF migration** — every piece reuses `Word`/`UserWord`/`ExamAttempt` as-is.

### Files Changed
- `JackyAIApp.Server/DTO/ExamModels.cs`
- `JackyAIApp.Server/Common/CreditCosts.cs`
- `JackyAIApp.Server/Services/Exam/IExamService.cs`
- `JackyAIApp.Server/Services/Exam/ExamService.cs`
- `JackyAIApp.Server/Controllers/ExamController.cs`
- `JackyAIApp.Server.Tests/Services/ExamServiceTests.cs`

### Tests
- Added a "Dictation Tests" region to `ExamServiceTests.cs` covering every acceptance
  criterion: user-not-found / no-words-anywhere errors, a JSON-serialization check that the
  `GET dictation` response contains no word text, audio-lookup returning the same text grading
  scores against, exact/case-insensitive-match/wrong-but-close grading (asserting a partial,
  non-zero-not-necessarily score), `GradeDictationAsync`/`GetDictationWordTextAsync` never
  calling `ConsumeCreditsAsync`, and `RecordExamResultAsync` with `ExamType: "Dictation"`
  updating SM-2 through the existing, unmodified code path.
- Ran `dotnet build --configuration Release` (backend): succeeded, 0 errors (pre-existing
  warnings only, unrelated to this change).
- Ran `dotnet test --filter "Category!=LiveContract"` (offline unit tests): 638 passed, 0
  failed (was 625 before this change — 13 new dictation tests, all green).
- Did not run the `LiveContract` category (unrelated to this task — no third-party API calls
  added) or the frontend suite (this is a backend-only task; `TASK-011` covers the UI).

### Known Issues / Flags for Tech Lead
- Per task instructions, flagging explicitly: `GET api/exam/dictation`'s response
  (`DictationPromptResponse`) deliberately omits `WordText`, unlike Cloze/Translation's GET
  responses which already include the answer. This is intentional (see Architecture doc), not
  an oversight — please don't "fix" it to match the Cloze/Translation convention.
- `CreditCosts.DictationTest = 1` priced at raw `TextToSpeech` cost per the task's guidance;
  flagging in case a different value is preferred.
- Deliberately did not add a `speed` parameter to `dictation/audio` (out of scope per Technical
  Notes) — noting adjustable playback speed as a possible follow-up across all TTS-backed exam
  audio endpoints, not just this one.

### Blockers
None.

## Review
_(Tech Lead fills in after PR review)_
