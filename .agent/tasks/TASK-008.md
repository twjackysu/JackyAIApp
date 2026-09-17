# TASK-008

## Title
Backend: Open-ended writing practice with AI feedback (Skill-Building item 3 — 寫)

## Owner
Backend

## Status
MERGED

## Priority
MEDIUM

## Objective
Close the "寫 writing" gap named in `REQUIREMENTS.md` → "Skill-Building
Feature Expansion": today writing practice stops at single-sentence
granularity (Translation Test, Sentence Formation Test). Add open-ended
paragraph-level writing: the user is given a topic plus a handful of their
own saved words, writes a free-text paragraph, and gets AI feedback graded
against a rubric — reusing the grading pattern already proven by
`GradeTranslationAsync`/`EvaluateSentenceAsync`, not the `IWordExamGenerator`
or Listening/Reading bank patterns (neither fits — see reasoning below).

See `.agent/architecture/ARCHITECTURE.md` → "Decision: Open-ended writing
practice with AI feedback — Skill-Building item 3" for the full
investigation and reasoning, including why this needs no EF migration.

## Requirements

1. **`GET api/exam/writing`** → new `GenerateWritingPromptAsync()` on
   `ExamService`/`IExamService`:
   - Pick a topic from a small fixed rotation (e.g. daily life, a memorable
     experience, a skill you want to learn, an opinion on something you
     care about — pick whatever set reads naturally in the prompt; not
     prescribed exactly).
   - Pick up to 5 distinct rows from the calling user's own `UserWords`
     (plain random DB sample — no AI call). If the user has fewer than 5
     saved words, use however many exist; if they have zero, still return
     the prompt with `RequiredWords: []` (open-topic writing, no forced
     vocabulary) rather than failing or blocking — same principle
     `TASK-007` used for Reading's empty-repository case.
   - **Do not credit-gate this endpoint** — it is the one exam-type
     "generation" endpoint with zero underlying AI cost (see Architecture
     doc's Alternatives section for why charging here would be wrong, not
     just inconsistent). Add a code comment on the controller action noting
     this is deliberate, so a future pass doesn't "fix" it by adding
     `[ConsumeCredits]` out of habit.
   - Response DTO: `WritingPromptResponse { Topic: string, RequiredWords:
     List<WritingPromptWordDto> }`, `WritingPromptWordDto { WordId,
     WordText }`.

2. **New prompt template** `Prompt/Exam/WritingEvaluationSystem.txt`:
   - Input: topic + required words + the user's submitted paragraph.
   - Output JSON contract: `{ score (0-100), grammarFeedback,
     contentFeedback, vocabularyFeedback, wordsUsed: [{ word, used }],
     suggestions: [string] }`. `contentFeedback` covers topic relevance and
     structure/coherence; `vocabularyFeedback` is an overall comment on how
     the required words were used (or not); `wordsUsed` is a per-word
     used/not-used flag the AI reports by reading the submission (needed
     for the SM-2 fan-out in requirement 5 below and for the UI to show
     which words landed). All feedback text in Traditional Chinese, same
     convention as `TranslationQualityGradingSystem.txt`/
     `SentenceEvaluationSystem.txt`. Include a few-shot example in the
     generator code, same convention as the other grading prompts.

3. **`POST api/exam/writing/evaluate`** → new `GradeWritingAsync(request)`:
   - Request DTO `WritingSubmissionRequest { Topic: string, RequiredWords:
     List<string>, Submission: string }` — `Topic`/`RequiredWords` are
     echoed back by the client from the `GET` response (don't re-query),
     same pattern `TranslationTestUserResponse` already uses for
     `ExaminationQuestion`.
   - Response DTO `WritingGradingResponse { Score, GrammarFeedback,
     ContentFeedback, VocabularyFeedback, WordsUsed:
     List<WritingWordUsageDto>, Suggestions: List<string> }`,
     `WritingWordUsageDto { Word, Used }`.
   - Credit-gated inline via `_creditService.ConsumeCreditsAsync(...,
     CreditCosts.WritingEvaluation, "writing_evaluation", "Writing
     evaluation")`, exactly like `GradeTranslationAsync`'s existing inline
     gate — not the `[ConsumeCredits]` attribute.
   - No `ExamAttempt` write inside this call — same decoupling
     Translation/Sentence grading already has; the client calls
     `record-result` separately.

4. **`Common/CreditCosts.cs`**: add `WritingEvaluation = 3` (see Architecture
   doc for the reasoning behind this starting number; flag in the PR if you
   think a different value fits better after seeing actual token usage).

5. **`RecordExamResultAsync`** (`ExamService.cs:769`):
   - Add `"Writing"` to the wordless group: `isWriting = request.ExamType ==
     "Writing"`, `isWordless = isListening || isListeningResponse ||
     isReading || isWriting`. The `ExamAttempt` row itself carries no
     `WordId` for a Writing attempt (no single word fits) — **no new
     `ExamAttempt` column, no EF migration**.
   - Add `WritingWordIds: List<string>?` to `RecordExamResultRequest`.
     Validation: require it non-empty when `ExamType == "Writing"` **and**
     the prompt actually had required words (a zero-required-words
     open-topic submission legitimately has nothing to fan out — don't
     force an empty-list error in that case).
   - When `ExamType == "Writing"`: after inserting the wordless
     `ExamAttempt` row, loop over `request.WritingWordIds`, look up each
     `UserWord` (same `SingleOrDefaultAsync` pattern already used for the
     single-word case), and call the **existing**
     `_spacedRepetition.ApplyResult(userWord, MapQuality(request.Correct,
     request.Score), DateTime.UtcNow.Date)` for each one found. Same
     `MapQuality` value applied to every word — one holistic grade for the
     whole paragraph, not a per-word score (see Architecture doc's
     Trade-offs section; this is a deliberate, already-reasoned-through
     choice, not something to redesign mid-implementation).
   - `RecordExamResultResponse.ScheduleUpdated`: true if at least one of the
     words in the list had a matching `UserWord` and got updated.

## Acceptance Criteria
- [ ] `GET api/exam/writing` returns a topic + up to 5 of the calling user's
      own saved words, works with an empty repository (returns
      `RequiredWords: []`, not an error), and is **not** credit-gated
      (verify with a test that asserts no `ConsumeCreditsAsync` call
      happens on this path).
- [ ] `POST api/exam/writing/evaluate` charges `CreditCosts.WritingEvaluation`
      exactly once per call, fails cleanly on insufficient credits (same
      `ErrorCodes.Forbidden` shape as `GradeTranslationAsync`), and returns
      structured feedback including `WordsUsed` reflecting the actual
      submission (a submission that plainly doesn't use a required word
      should come back with that word's `Used: false` — cover this with at
      least one test case, not just the happy path where everything is
      used).
- [ ] `RecordExamResultAsync` accepts `ExamType: "Writing"` +
      `WritingWordIds`, writes exactly one `ExamAttempt` row with no
      `WordId`, and calls `ApplyResult` once per ID in the list that has a
      matching `UserWord` — confirm via test that N required words produces
      N `ApplyResult` calls (or however many have a matching `UserWord`,
      if some were removed from the repository since the prompt was
      fetched), all with the same mapped quality.
- [ ] A Writing attempt with `WritingWordIds: []` (open-topic, no required
      words) is accepted and simply updates nothing — not rejected as
      invalid.
- [ ] No EF migration is part of this PR — if you find yourself needing one,
      stop and re-check against the Architecture doc; that's a signal the
      implementation has drifted from the scoped design.

## Technical Notes
- Read `ExamService.cs:541–588` (`GradeTranslationAsync`) end-to-end before
  writing new code — this task is "copy that grading shape," not "invent a
  new one." Same for `RecordExamResultAsync` (`:769`) and its existing
  wordless branches (Listening/ListeningResponse/Reading) — add Writing as
  a fourth, don't restructure the existing three.
- `NewModels.GPT_4_1_NANO` is the model already used for
  Cloze/Sentence/Reading — use the same one unless feedback quality at that
  tier turns out insufficient for a whole-paragraph judgment (flag it in
  the PR if you have to switch, don't silently upgrade).
- Do **not** build this as an `IWordExamGenerator` — it isn't anchored to a
  single `SQL.Word` and there's no AI-authored content to cache (see
  Architecture doc point 2). Give it its own methods on
  `ExamService`/`IExamService`, same as the Listening/Reading methods.
- Whatever query you use to pick up to 5 random `UserWords` rows, keep it
  DB-level random sampling (no loading the whole collection into memory) —
  there's already a comment-documented pattern for single-word random
  selection nearby (`ExamService.cs`, just above/below
  `RecordExamResultAsync`) to use as a reference for the DB-level technique,
  even though this needs 5 distinct rows instead of 1.

## Dependencies
None — can start immediately. `TASK-009` (Frontend) depends on this task's
response DTO shape.

## Result

Implemented per spec, reusing the Translation/Sentence grading shape as directed:

- `GET api/exam/writing` → `ExamService.GenerateWritingPromptAsync()`: picks a topic from a
  4-item fixed rotation + up to 5 distinct `UserWords` rows via a new `GetRandomUserWordsAsync`
  helper (DB-level COUNT + distinct-offset sampling, extending the existing single-word
  `GetRandomUserWordIdAsync` technique — no AI call, no full-collection load). Empty repository
  returns `RequiredWords: []`, not an error. **Not credit-gated** — controller action has an
  explicit comment warning against adding `[ConsumeCredits]`.
- New `Prompt/Exam/WritingEvaluationSystem.txt` + `POST api/exam/writing/evaluate` →
  `ExamService.GradeWritingAsync()`: one-shot `GetCompletionAsync<WritingGradingResponse>` call
  (model `GPT_4_1_NANO` per the task's technical note), inline credit gate via new
  `CreditCosts.WritingEvaluation = 3`, no `ExamAttempt` write (client calls `record-result`
  separately, same as Translation/Sentence). `WordsUsed` is read directly from the AI's per-word
  used/not-used judgment.
- `RecordExamResultAsync`: added `"Writing"` to the wordless group (`isWriting`), added
  `WritingWordIds: List<string>?` to `RecordExamResultRequest`. Validation rejects only a missing
  (`null`) list, not an empty one — an empty list (open-topic, zero required words) is accepted
  and simply updates nothing. When present, fans out the existing `_spacedRepetition.ApplyResult`
  call across every id with a matching `UserWord`, all with one shared `MapQuality` value. No new
  `ExamAttempt` column, no EF migration.
- New DTOs: `WritingPromptResponse`, `WritingPromptWordDto`, `WritingSubmissionRequest`,
  `WritingGradingResponse`, `WritingWordUsageDto` in `DTO/ExamModels.cs`.

### Files Changed
- `JackyAIApp.Server/Services/Exam/ExamService.cs`
- `JackyAIApp.Server/Services/Exam/IExamService.cs`
- `JackyAIApp.Server/Controllers/ExamController.cs`
- `JackyAIApp.Server/DTO/ExamModels.cs`
- `JackyAIApp.Server/Common/CreditCosts.cs`
- `JackyAIApp.Server/Prompt/Exam/WritingEvaluationSystem.txt` (new)
- `JackyAIApp.Server.Tests/Services/ExamServiceTests.cs`

### Tests
Added to `ExamServiceTests.cs` (offline unit tests, xUnit + Moq, in-memory EF provider):
prompt generation (user-not-found, empty repository → `RequiredWords: []`, fewer-than-5 words
returns all, more-than-5 returns exactly 5 distinct, never consumes credits), grading (null
input, insufficient credits → `Forbidden` + no OpenAI call, exactly-once credit charge with a
deliberately-unused required word coming back `Used: false`, OpenAI failure path), and
`record-result` (missing `WritingWordIds` → `BadRequest`, empty list accepted and updates
nothing, N required word ids → `ApplyResult` applied to exactly the ones with a matching
`UserWord`, verified via `NextReviewDate` changing only on the matched words).

**Could not execute `dotnet test`** — this sandbox has no .NET SDK installed and outbound access
to `builds.dotnet.microsoft.com` for `dotnet-install.sh` is blocked by the environment's network
policy (confirmed: 403 from the egress proxy). Compensated with a careful manual review of every
changed file (brace/paren balance checked programmatically, method signatures cross-checked
against `IOpenAIPromptService`, `ISpacedRepetitionService.ApplyResult`, `SQL.UserWord`/`SQL.Word`
field names, and the existing `GradeTranslationAsync`/`EvaluateSentenceAsync`/
`GenerateReadingExamAsync` call shapes this task was scoped to copy). Tech Lead/CI should run the
real test suite before merge — flagging this explicitly rather than claiming a pass I couldn't
observe.

### Known Issues
- `CreditCosts.WritingEvaluation = 3` is a starting estimate per the Architecture doc — flag for
  revisit once real token usage on whole-paragraph grading is observed.
- `NextReviewDate` on `RecordExamResultResponse` is left `null` for a Writing attempt (same as
  every other wordless type) even when the SM-2 fan-out did update one or more words — the
  response shape has one `NextReviewDate` field and a multi-word update doesn't map onto it
  cleanly. Not called out as a requirement in the task; flagging in case the frontend (TASK-009)
  needs per-word next-review dates rather than just the boolean `ScheduleUpdated`.

### Blockers
None.

### PR
https://github.com/twjackysu/JackyAIApp/pull/347 — CI (`build-and-test-server`,
`lint-and-build-client`) both passed. **Merged into `master`** at
2026-09-17T10:04:37Z (commit `7e6c831`).

### Duplicate PR closed
A second, independently-generated backend session opened **PR #346**
(branch `claude/magical-heisenberg-5pk8jh`) implementing this same task in
parallel with #347. It went stale/conflicting once #347 merged (both
defined the same DTOs/methods). Closed #346 as a duplicate of the
already-merged #347 rather than resolving its conflicts — merging both
would have produced duplicate type definitions in `ExamModels.cs`/
`ExamService.cs`. No unique work was lost: #346's implementation covered
the same scope as #347's.

## Review
_(Tech Lead fills in after PR review)_
