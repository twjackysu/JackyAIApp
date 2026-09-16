# TASK-006

## Title
Backend: Reading comprehension passages (Skill-Building item 2 — 讀)

## Owner
Backend

## Status
READY

## Priority
HIGH

## Objective
Close the "讀 reading" gap named in `REQUIREMENTS.md` → "Skill-Building
Feature Expansion": today reading practice stops at single-word (Dictionary)
or single-sentence (Cloze) granularity. Add passage-level reading
comprehension: a short passage plus 3–4 multiple-choice comprehension
questions, generated and cached via a shared bank (like
Listening/ListeningResponse), not tied to any single user's saved word.

See `.agent/architecture/ARCHITECTURE.md` → "Decision: Reading comprehension
passages — Skill-Building item 2" for the full reasoning, including why this
follows the Listening bank pattern instead of the Cloze per-word pattern.

## Requirements

1. **New EF entities** (mirror `ListeningResponseTest`/`Option` shape):
   - `ReadingPassage`: `Id` (string/GUID), `Title`, `PassageText`,
     `CreatedAt`.
   - `ReadingQuestion`: `Id`, `PassageId` (FK), `QuestionText`.
   - `ReadingQuestionOption`: `Id`, `QuestionId` (FK), `Text`, `IsCorrect`
     (bool), `Explanation` (short Chinese rationale, same style as
     `ListeningResponseOptionGen.Explanation`).
   - One passage has 3–4 questions; each question has 3–4 options, exactly
     one correct. Add the `DbSet`s to `AzureSQLDBContext` and one EF
     migration.

2. **New prompt template** `Prompt/Exam/ReadingSystem.txt`:
   - Input: a topic string (rotate through a small fixed list — e.g. daily
     life, travel, workplace, technology, environment, health — picked
     server-side, not user-supplied).
   - Output: one passage (~120–180 words, intermediate/CEFR B1-ish
     vocabulary) plus 3–4 comprehension questions covering a *mix* of
     question types — main idea, specific detail, inference, and
     vocabulary-in-context — not all the same type, so this actually
     exercises comprehension rather than duplicating Cloze's word-recall
     style.
   - JSON contract mirrors `ListeningResponseGenerationResult`'s shape:
     `{ title, passageText, questions: [{ questionText, options: [{ text,
     isCorrect, explanation }] }] }`. Include a few-shot example in the
     generator code, same convention as `ListeningResponseFewShotExample`.

3. **`ExamService.GenerateReadingExamAsync()`** — copy the exact structure
   of `GenerateListeningResponseExamAsync` (`ExamService.cs:216`):
   - Track recently-seen passage IDs per user via `ExamAttempts` (new
     `ReadingPassageId` column, see below), same `NoRepeatWindow = 30`
     constant already used for Listening.
   - `ReadingBankGrowthThreshold` constant (start at the same `20` used for
     `ListeningResponseBankGrowthThreshold` unless you have a reason to
     differ) driving the same `shouldGenerate = unseenCount < threshold ||
     Random.Shared.Next(5) == 0` rule.
   - When generating: pick the next topic from the fixed rotation (simple
     round-robin or random pick — no need to track per-user topic history),
     call `IOpenAIPromptService.GetCompletionAsync<T>` against the new
     prompt file, persist the passage + questions + options, return the DTO.
   - When reusing: pick a random unseen (or, if none, any) passage from the
     bank, same `PickFromPoolAsync`-style logic.
   - No second AI call for grading — return `IsCorrect`/`Explanation` in the
     same response as the question, exactly like
     `ListeningTestOptionDto`/`ListeningResponseOptionDto` already do.

4. **New DTOs**: `ReadingPassageResponse` (Id, Title, PassageText,
   Questions: `List<ReadingQuestionDto>`), `ReadingQuestionDto` (Id,
   QuestionText, Options: `List<ReadingQuestionOptionDto>`),
   `ReadingQuestionOptionDto` (Id, Text, IsCorrect, Explanation).

5. **`ExamController`**: `GET api/exam/reading`, decorated with
   `[ConsumeCredits(CreditCosts.ReadingComprehensionTest, "reading_test",
   "Reading comprehension test")]`, same pattern as the `listening`/
   `listening/response` endpoints.

6. **`CreditCosts.cs`**: add `ReadingComprehensionTest = 3` (see
   Architecture doc for the reasoning behind this starting number — flag in
   the PR if you think a different value fits better after seeing actual
   token usage; not a hard requirement to match exactly).

7. **`RecordExamResultAsync`** (`ExamService.cs:546`): add `"Reading"` to
   the wordless branch (`isReading = request.ExamType == "Reading"`,
   `isWordless = isListening || isListeningResponse || isReading`), require
   `ReadingPassageId` when `ExamType == "Reading"` (mirrors the existing
   `ListeningTestId` validation), add `ReadingPassageId` to `ExamAttempt`
   (nullable, same as the other two test-ID columns) and to
   `RecordExamResultRequest`.

## Acceptance Criteria
- [ ] `GET api/exam/reading` returns a passage with 3–4 questions, each with
      3–4 options and a correct answer, without requiring the user to have
      any saved words (works with an empty repository — bank is shared,
      not personalized).
- [ ] The first N requests (below the growth threshold) each generate a new
      passage via one OpenAI call; once the bank is past the threshold, most
      requests reuse an existing passage with **no** new AI call — verify
      this with a unit test analogous to whatever test coverage
      `GenerateListeningResponseExamAsync` already has, if any, or a new one
      exercising the threshold boundary.
- [ ] A user doesn't see the same passage again within the `NoRepeatWindow`
      (30) most recent Reading attempts, same guarantee Listening already
      gives.
- [ ] `RecordExamResultAsync` accepts `ExamType: "Reading"` +
      `ReadingPassageId` and logs an `ExamAttempt` without requiring
      `WordId` — confirm this doesn't also feed the SM-2 schedule (it
      shouldn't; Reading isn't word-scoped, matching Listening's existing
      `scheduleUpdated = false` behavior for wordless types).
- [ ] `CreditCosts.ReadingComprehensionTest` is consumed exactly once per
      `GET api/exam/reading` call, cache hit or miss (the credit charge
      doesn't depend on whether a new AI call actually happened — same as
      how `ListeningResponseTest`'s flat charge works today, since the user
      doesn't need to know or care whether their play triggered generation).
- [ ] EF migration applies cleanly; no changes to existing tables' data.

## Technical Notes
- Read `ExamService.cs:170–313` (Listening + ListeningResponse) end-to-end
  before writing new code — this task is explicitly "copy that pattern,"
  not "invent a new one." Reuse `NoRepeatWindow`, don't redefine it.
- Keep passage/question/option persistence and DTO-mapping code in
  `ExamService.cs` next to the Listening methods (or split into a
  `ReadingExamService`/generator class if you think the file is getting too
  long — Backend's call, not prescribed here, but keep the shared-bank
  logic itself unchanged from the Listening pattern either way).
- Do **not** build this as an `IWordExamGenerator` — it doesn't take a
  `SQL.Word` and doesn't fit that interface's per-word contract. Give it its
  own method on `ExamService`/`IExamService`, same as the two Listening
  methods.
- `NewModels.GPT_4_1_NANO` is the model already used for Cloze/Sentence —
  use the same one unless passage quality at that tier turns out
  insufficient (flag it in the PR if you have to switch, don't silently
  upgrade to a pricier model).

## Dependencies
None — can start immediately. `TASK-007` (Frontend) depends on this task's
response DTO shape.

## Result
_(Backend Engineer fills in on completion)_

## Review
_(Tech Lead fills in after PR review)_
