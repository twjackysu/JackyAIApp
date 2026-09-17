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
Implemented by a separate Frontend Engineer session (branch
`claude/amazing-cori-uwfb9y`); this run found the work already done and
verified it rather than reimplementing:
- **PR**: [#348](https://github.com/twjackysu/JackyAIApp/pull/348), against
  `master`. CI green (`lint-and-build-client` + `build-and-test-server`),
  `mergeable_state: clean`, no review comments yet.
- Per the PR description: API slice additions (`getWritingPrompt`,
  `evaluateWriting`, new types matching `TASK-008`'s merged DTOs exactly),
  new `Exam/WritingTestCard/index.tsx` (fetch-on-mount, open-topic empty
  case handled as a normal mode not an error, cost caption under the submit
  button rather than the mode-selection screen), new `EXAM_MODES` entry,
  `WRITING_EVALUATION: 3` in `constants/creditCosts.ts` cross-checked by the
  existing `creditCosts.test.ts` backend-mirror test. `npm run lint`/`build`/
  `vitest run` reported green (135 tests).
- This run's action: updated `Status` to `PR_OPENED` (was still `READY` on
  `master` despite the PR already existing) and subscribed to PR activity so
  review comments/CI events on #348 get picked up. No code changes made —
  nothing to add on top of an already-complete, CI-green implementation.

## Review
_(Tech Lead fills in after PR review)_
