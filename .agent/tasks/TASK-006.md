# TASK-006

## Title
Backend: Reading comprehension passages (Skill-Building item 2 — 讀)

## Owner
Backend

## Status
MERGED

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
- [x] `GET api/exam/reading` returns a passage with 3–4 questions, each with
      3–4 options and a correct answer, without requiring the user to have
      any saved words (works with an empty repository — bank is shared,
      not personalized).
- [x] The first N requests (below the growth threshold) each generate a new
      passage via one OpenAI call; once the bank is past the threshold, most
      requests reuse an existing passage with **no** new AI call — verify
      this with a unit test analogous to whatever test coverage
      `GenerateListeningResponseExamAsync` already has, if any, or a new one
      exercising the threshold boundary.
- [x] A user doesn't see the same passage again within the `NoRepeatWindow`
      (30) most recent Reading attempts, same guarantee Listening already
      gives.
- [x] `RecordExamResultAsync` accepts `ExamType: "Reading"` +
      `ReadingPassageId` and logs an `ExamAttempt` without requiring
      `WordId` — confirm this doesn't also feed the SM-2 schedule (it
      shouldn't; Reading isn't word-scoped, matching Listening's existing
      `scheduleUpdated = false` behavior for wordless types).
- [x] `CreditCosts.ReadingComprehensionTest` is consumed exactly once per
      `GET api/exam/reading` call, cache hit or miss (the credit charge
      doesn't depend on whether a new AI call actually happened — same as
      how `ListeningResponseTest`'s flat charge works today, since the user
      doesn't need to know or care whether their play triggered generation).
- [x] EF migration applies cleanly; no changes to existing tables' data.

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

Implemented following the exact `GenerateListeningResponseExamAsync` shared-
bank pattern, as directed.

### Files Changed
- `Data/Models/SQLWord.cs` — new `ReadingPassage`/`ReadingQuestion`/
  `ReadingQuestionOption` entities.
- `Data/Models/ExamAttempt.cs` — nullable `ReadingPassageId` + nav property.
- `Data/AzureSQLDBContext.cs` — 3 new `DbSet`s, FK/cascade config for the new
  entities, `ExamAttempt.ReadingPassage` FK (NoAction, matching the other two
  test-ID columns).
- `Data/Migrations/20260916043837_AddReadingComprehension.*` — new migration
  (additive only: 3 new tables + 1 nullable column + indexes/FKs; no changes
  to existing table data).
- `DTO/ExamModels.cs` — `ReadingPassageResponse`/`ReadingQuestionDto`/
  `ReadingQuestionOptionDto` (response shape) and `ReadingGenerationResult`/
  `ReadingQuestionGen`/`ReadingQuestionOptionGen` (raw AI shape); added
  `ReadingPassageId` to `RecordExamResultRequest`.
- `Prompt/Exam/ReadingSystem.txt` — new prompt: topic in, one ~120-180 word
  passage + 3-4 mixed-type (main idea / detail / inference / vocab-in-
  context) MCQ questions out, Chinese explanations.
- `Services/Exam/ExamService.cs` — `GenerateReadingExamAsync()` (mirrors
  `GenerateListeningResponseExamAsync`: reuses `NoRepeatWindow`, new
  `ReadingBankGrowthThreshold = 20`, fixed topic rotation, one-shot AI call
  persisting the full passage+questions+options graph, fallback-to-bank on
  AI failure, malformed-shape validation extended to the nested
  question/option structure); `RecordExamResultAsync` gains the `isReading`
  wordless branch + `ReadingPassageId` validation/persistence.
- `Services/Exam/IExamService.cs` — `GenerateReadingExamAsync()` declaration.
- `Common/CreditCosts.cs` — `ReadingComprehensionTest = 3` (per Architecture
  doc's reasoning; flagging per the task note in case real token usage
  suggests otherwise once observed).
- `Controllers/ExamController.cs` — `GET api/exam/reading`, gated by
  `[ConsumeCredits(CreditCosts.ReadingComprehensionTest, ...)]`, same
  pattern as `listening`/`listening/response`.
- `JackyAIApp.Server.Tests/Services/ExamServiceTests.cs` — 8 new tests
  mirroring the existing `ListeningResponse` coverage: bank-below-threshold
  generation+persistence, malformed-question/option rejection, prompt-
  service-failure (empty bank vs. fallback-to-bank), record-result
  validation + no-SM2-feed, recently-seen exclusion, and the large-bank/
  small-unseen-pool forced-generation boundary case.

### Tests
- `dotnet build` (both `JackyAIApp.Server` and `JackyAIApp.Server.Tests`):
  clean, 0 errors (pre-existing unrelated warnings only).
- `dotnet test --filter "Category!=LiveContract"`: **595/595 passed**
  (up from 587 pre-change; +8 new Reading tests, 0 regressions).
- `dotnet ef migrations add AddReadingComprehension`: generated cleanly;
  inspected the generated `Up`/`Down` — additive only (3 new tables, 1
  nullable column + indexes/FKs on `ExamAttempts`), no destructive/data-
  affecting operations on existing tables.
- Did not run the live `dotnet run` server / manual `GET api/exam/reading`
  call against a real OpenAI key or SQL Server instance — no live
  credentials available in this environment. Confidence instead comes from
  the unit tests exercising the actual `ExamService` code path end-to-end
  against an EF Core in-memory database, same level of verification the
  existing `ListeningResponse` tests rely on.

### Known Limitations
- Credit cost (`3`) and model tier (`GPT_4_1_NANO`) are starting estimates
  per the Architecture doc — not measured against real token usage yet, as
  flagged in the task's own acceptance criteria.
- `TASK-007` (Frontend) can now proceed — response DTO shape
  (`ReadingPassageResponse` → `ReadingQuestionDto[]` → `ReadingQuestionOptionDto[]`)
  is stable per this PR.

### Blockers
None.

### PR
https://github.com/twjackysu/JackyAIApp/pull/342 — GitHub shows this PR as
merged (2026-09-16T07:25 UTC). Leaving `Status` as `PR_OPENED` per Backend
Engineer's own rules (only Boss marks a task `MERGED`) — Tech Lead/Boss to
flip the status field once confirmed.

## Review

Confirmed PR #342 merged into `master` (2026-09-16T07:25 UTC). Read the
actual merged code, not just the Result section above: `ExamService.cs`'s
`GenerateReadingExamAsync`/`PickReadingFromPoolAsync` mirror
`GenerateListeningResponseExamAsync`'s structure exactly (bank-growth
threshold, recently-seen exclusion via `ExamAttempts`, no second AI call for
grading), the 3 new entities + migration are additive only, and
`RecordExamResultAsync`'s new `isReading` branch correctly stays out of the
SM-2 schedule update (matches Listening's existing wordless behavior). No
correctness issues found.

**Post-merge addition (not part of this task, noted for the record):** Boss
found and fixed a real gap directly on `master` (PR #345, 2026-09-17,
`ReadingPassageValidator` + prompt hardening) — the original acceptance
criteria only checked question/option *shape* (count, exactly-one-correct),
not *language*, so a passage generated entirely in Chinese passed validation
and would have sat in the shared bank being served to every user until
manually deleted. The fix adds a CJK-content check (English required in
title/passage/questions/options, Chinese required in explanations) with a
one-retry-then-fallback-to-bank policy, plus 81+175 lines of new tests
(`ExamServiceTests`, `ReadingPassageValidatorTests`). Reviewed the diff:
sound design (validator as a hard gate, prompt hardening as a secondary
defense, capped retry so a stuck model can't loop), consistent with how this
task's own shared-bank design already treats "reject bad output before it
reaches the bank" as the correct failure mode. No further action needed —
recording here so the gap and its fix are part of this task's history
instead of only living in a commit message.
