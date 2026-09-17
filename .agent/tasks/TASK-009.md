# TASK-009

## Title
Frontend: Writing practice test UI (Skill-Building item 3 — 寫)

## Owner
Frontend

## Status
PR_OPENED

## Priority
MEDIUM

## Objective
Surface `TASK-008`'s open-ended writing practice as a new exam mode: fetch a
topic + a few of the user's own saved words, let them write a paragraph,
submit it for AI feedback, and record the result.

## Requirements

1. **API slice** (`apis/examApis/`): add `getWritingPrompt` (query, `GET
   api/exam/writing` — plain query, no generation trigger from the client,
   mirrors `getReadingTest`'s convention) and `evaluateWriting` (mutation,
   `POST api/exam/writing/evaluate`). Add response/request types
   (`WritingPromptResponse`/`WritingPromptWordDto`/`WritingSubmissionRequest`/
   `WritingGradingResponse`/`WritingWordUsageDto`) to `apis/examApis/types.ts`
   matching `TASK-008`'s actual merged DTO field names/casing exactly (read
   the merged C# DTOs + confirm camelCase via `Program.cs`'s JSON options,
   same verification approach `TASK-005`/`TASK-007` used — don't guess). Add
   `'Writing'` to the `ExamType` union and a `writingWordIds?: string[]`
   field to `RecordExamResultRequest`.

2. **New `Exam/WritingTestCard/index.tsx`**:
   - On mount, fetch the prompt: show the topic and the required words as
     chips (if `RequiredWords` is empty, show the topic alone with a short,
     non-alarming note that this one is open-topic — same "don't make an
     empty-personalization case read like an error" rule `TASK-007` used
     for Reading).
   - A multiline text field for the user's paragraph + a submit button
     (disabled while empty or while a submission is in flight).
   - On submit, call `evaluateWriting`; while it's loading, show the credit
     cost near the submit button, not on the mode-selection screen (see
     point 4 below — this mode's cost only applies at submission time,
     unlike every other mode).
   - Render the returned feedback: score, the three feedback fields
     (grammar/content/vocabulary), the per-word `WordsUsed` list (used vs.
     not, e.g. green/grey chips), and the `Suggestions` list.
   - Call `record-result` with `examType: 'Writing'`,
     `writingWordIds: <the WordIds from the original prompt fetch>`, and
     `correct`/`score` derived from the grading response — check how
     `SentenceTestCard`/`TranslationTestCard` currently turn their own
     grading response (score/grade) into the `Correct`/`Score` fields on
     `record-result`, and mirror that exact mapping rather than inventing a
     new threshold. Fire this once, after the user has seen their feedback
     (not before), same "record after reveal" ordering the other cards use.
   - A "換一題" / next-prompt action that just refetches `getWritingPrompt`
     and resets the local submission state (no separate "regenerate"
     mutation needed — same as `ReadingTestCard`/`ListeningResponseTestCard`,
     a fresh prompt only shows up on refetch).

3. **Wire into the exam menu** (`Exam/index.tsx`): add a new `EXAM_MODES`
   entry (e.g. `path: 'writing'`, label along the lines of "寫作練習
   (Writing Practice)"), following the existing single-source-of-truth
   pattern. Since this mode is free to open and only charges on submit
   (see `TASK-008`), the button's cost caption should say something like
   "提交批改需消耗 N 點" rather than implying entering the mode itself
   costs anything — don't reuse the exact same caption wording every other
   mode uses without checking it still reads correctly for this one.

4. **Credit cost display**: add `WRITING_EVALUATION` to
   `constants/creditCosts.ts` (mirroring the other `CREDIT_COSTS.*`
   entries), matching `TASK-008`'s actual `CreditCosts.WritingEvaluation`
   value. Reference it in the `EXAM_MODES` entry's cost caption per point 3,
   and again near the submit button inside `WritingTestCard` itself (this
   is the one mode where the cost needs to appear in two places rather than
   one, precisely because generation and evaluation aren't bundled together
   here the way they are everywhere else).

## Acceptance Criteria
- [ ] Writing Practice appears as a new button on the exam selection
      screen, with a cost caption that reads correctly for "free to open,
      charged on submit" rather than implying the whole mode costs credits
      up front.
- [ ] Opening it fetches and displays a topic + up to 5 required words
      (chips); works with zero saved words (open-topic case reads as a
      normal mode, not an error/warning).
- [ ] Submitting a paragraph shows the credit cost near the submit action,
      calls `evaluateWriting`, and renders all of: score, grammar/content/
      vocabulary feedback, per-word used/not-used status, and suggestions.
- [ ] After viewing feedback, `record-result` fires once with
      `examType: 'Writing'` and the correct `writingWordIds` (omitted or
      empty array for the open-topic case).
- [ ] `npm run lint` (0 warnings), `npm run build`, `npx vitest run` all
      pass with no regressions.

## Technical Notes
- Do not reuse `SentenceTestCard`/`TranslationTestCard` verbatim — this
  mode's fetch-prompt/submit-for-grading split, and its free-then-charged
  cost shape, are different enough from those cards' generate-then-grade
  flow that a new sibling component is the smallest correct change, same
  reasoning `TASK-007` used for not forcing `ReadingTestCard` into
  `ListeningQuizCard`'s shape. Feel free to reuse smaller pieces (a
  feedback-chip component, the overall card layout) if they extract
  cleanly, but don't force this into an existing card's prop contract.
- Keep UI copy plain and factual — this is "寫作練習", not implying a
  formal writing exam or a guaranteed personalization (the required-words
  list can legitimately be empty).

## Dependencies
`TASK-008` (Backend) — needs the merged response DTO shape before exact
field names can be finalized. Architecture and requirements are otherwise
final; no need to wait for further Tech Lead sign-off once `TASK-008` lands.

## Result

### 2026-09-17 — Frontend: implemented (unblocked after TASK-008 merged via #347)

`TASK-008` landed on `master` through **PR #347** (a duplicate backend
session's implementation; PR #346 was closed as redundant — see
`TASK-008.md`'s Result section). Read the actual merged DTOs directly from
`JackyAIApp.Server/DTO/ExamModels.cs` and the controller routes from
`JackyAIApp.Server/Controllers/ExamController.cs` rather than guessing —
`camelCase` confirmed via `Program.cs`'s default `AddJsonOptions` (no
explicit `PropertyNamingPolicy` override, matching every other exam DTO
already in `apis/examApis/types.ts`).

Implemented exactly as scoped:
- **`apis/examApis/types.ts`**: `WritingPromptResponse`/`WritingPromptWordDto`/
  `WritingSubmissionRequest`/`WritingGradingResponse`/`WritingWordUsageDto`
  matching the merged C# DTOs field-for-field. Added `'Writing'` to
  `ExamType` and `writingWordIds?: string[]` to `RecordExamResultRequest`.
- **`apis/examApis/index.ts`**: `getWritingPrompt` (query, `GET
  api/exam/writing`) and `evaluateWriting` (mutation, `POST
  api/exam/writing/evaluate`), same shape as `getReadingTest`/
  `evaluateSentence`.
- **New `Exam/WritingTestCard/index.tsx`**: fetches the prompt on mount
  (auto-fetching RTK Query hook, same as `ReadingTestCard`); shows topic +
  required-word chips, or a plain (non-alarming) info `Alert` for the
  zero-required-words open-topic case; multiline submission field + submit
  button (disabled while empty/in flight) with the credit cost caption
  directly under it; on submit, calls `evaluateWriting`, then renders
  score/grammar/content/vocabulary feedback (accordions, mirroring
  `SentenceTestCard`), per-word used/not-used chips (green filled vs. grey
  outlined), and suggestions; fires `record-result` once right after the
  feedback is set (`examType: 'Writing'`, `writingWordIds` from the
  original prompt fetch, `correct: score >= 60` — same threshold
  `SentenceTestCard` uses for its own 0–100 score — `score`). "換一題"
  resets local state and refetches via the shared `useExamRound`/
  `ExamRoundShell` round machinery every other mode uses (no separate
  regenerate mutation).
- **`Exam/index.tsx`**: new `EXAM_MODES` entry (`path: 'writing'`, "寫作練習
  (Writing Practice)"), cost caption reads "提交批改需消耗 N 點" rather than
  implying the mode itself costs anything up front.
- **`constants/creditCosts.ts`**: `WRITING_EVALUATION: 3`, referenced both
  in the `EXAM_MODES` cost caption and inside the card next to the submit
  button, matching backend `CreditCosts.WritingEvaluation = 3` exactly
  (verified by `creditCosts.test.ts`'s cross-check against the `.cs` file).

### Files Changed
- `jackyaiapp.client/src/apis/examApis/types.ts`
- `jackyaiapp.client/src/apis/examApis/index.ts`
- `jackyaiapp.client/src/constants/creditCosts.ts`
- `jackyaiapp.client/src/Exam/WritingTestCard/index.tsx` (new)
- `jackyaiapp.client/src/Exam/index.tsx`

### Tests
`npm run lint` (0 warnings), `npm run build`, and `npx vitest run` (135
tests, 16 files, including `creditCosts.test.ts`'s backend cross-check) all
pass with no regressions. No new automated tests were added for
`WritingTestCard` itself — the existing test cards
(`SentenceTestCard`/`ReadingTestCard`/etc.) have no dedicated component
tests either, so this matches the established convention rather than
introducing new test scaffolding unilaterally; flagging in case Tech Lead
wants component-level coverage added as a follow-up.

### Known Issues
None found. `RecordExamResultResponse.nextReviewDate` stays `null` for a
Writing attempt per `TASK-008`'s own known limitation (multi-word update
doesn't map onto a single field) — the UI doesn't surface a per-word next
review date, only the existing `srsNote` derived from `scheduleUpdated`,
consistent with how every other exam type's UI already handles this.

### Blockers
None — see the now-resolved blocker record below for history.

### PR
_(added after PR is opened)_

---

### 2026-09-17 — Frontend: checked in, still blocked on TASK-008

Checked out the assigned task per the scheduled Frontend run. `TASK-008`'s
backend PR (#346, `feat(exam): open-ended writing practice with AI feedback
(TASK-008)`, branch `claude/magical-heisenberg-5pk8jh`) is open against
`master` but **not merged** — `mergeable_state: clean`, but its CI status
check returns zero runs (`state: pending`, `total_count: 0`), and the PR's
own body flags that its author had no `dotnet` SDK available to run
`dotnet build`/`dotnet test` locally, so the build is unconfirmed pending a
real CI pass.

No C# evidence of the writing feature exists yet on `master` (no `Writing`
references in `JackyAIApp.Server`, no `Prompt/Exam/WritingEvaluationSystem.txt`)
— confirmed the DTOs this task needs
(`WritingPromptResponse`/`WritingPromptWordDto`/`WritingGradingResponse`/
`WritingWordUsageDto`) are not yet available to read from merged code.

Per this task's own Dependencies note ("needs the **merged** response DTO
shape before exact field names can be finalized... don't guess") and
Technical Notes, starting the frontend slice against the unmerged PR's
branch would risk building against field names/casing that could still
change in review. Standing down without code changes; will pick this back
up once PR #346 merges into `master`.

## Blocker (resolved 2026-09-17 — see Result above)

### Problem
`TASK-008` (Backend, PR #346) is open but not yet merged into `master`,
and its CI has not yet run to confirm the build is green.

### Why It Blocks Implementation
This task's own requirements explicitly require matching `TASK-008`'s
**actual merged** DTO field names/casing exactly, verified by reading the
merged C# DTOs — not by reading an unmerged branch that could still change
during review, and not by guessing.

### What Is Needed
PR #346 merged into `master` (and ideally a green CI run confirming the
backend build/tests actually pass, since the PR author could not run
`dotnet build`/`dotnet test` locally).

### Suggested Resolution
No action needed from Frontend beyond waiting — this is purely a
sequencing dependency already called out in the task's own Dependencies
section. Re-check on the next scheduled run; implement immediately once
`TASK-008` lands on `master`.

## Review
_(Tech Lead fills in after PR review)_
