# TASK-011

## Title
Frontend: Dictation practice test UI (Skill-Building item 4 — 聽, last item)

## Owner
Frontend

## Status
READY

## Priority
MEDIUM-LOW

## Objective
Surface `TASK-010`'s dictation practice as a new exam mode: fetch a word
(without seeing its text), play its audio, let the user type what they
heard, submit for grading, reveal the correct spelling, and record the
result.

## Requirements

1. **API slice** (`apis/examApis/`): add `getDictationPrompt` (query, `GET
   api/exam/dictation`) and `gradeDictation` (mutation, `POST
   api/exam/dictation/grade`). Add response/request types
   (`DictationPromptResponse`/`DictationGradeRequest`/
   `DictationGradeResponse`) to `apis/examApis/types.ts` matching
   `TASK-010`'s actual **merged** DTO field names/casing exactly (read the
   merged C# DTOs + confirm camelCase via `Program.cs`'s JSON options, same
   verification approach every prior Skill-Building frontend task used —
   don't guess). Add `'Dictation'` to the `ExamType` union. **No new field
   needed on `RecordExamResultRequest`** — dictation reuses the existing
   `wordId`/`correct`/`score` fields exactly like Cloze does (confirm this
   against `TASK-010`'s Result section before assuming otherwise).

2. **New `Exam/DictationTestCard/index.tsx`**:
   - On mount (or on "play"), fetch the prompt (`getDictationPrompt`) —
     response contains only a `wordId`, no word text. Do not attempt to
     display or log the word before grading; there is nothing to show yet
     by design.
   - A play button that fetches/plays the audio from
     `GET api/exam/dictation/audio?wordId=<id>` — mirror however
     `ListeningQuizCard`/`ListeningResponseTestCard` already fetch and play
     their per-clip audio (same unbilled-per-clip pattern backing this
     endpoint); a replay button re-plays the same clip without a new
     network fetch if the existing pattern already caches the blob, same
     as those cards do.
   - A single-line text field for the user's typed answer + a submit
     button (disabled while empty or while a submission is in flight).
   - On submit, call `gradeDictation` with `{ wordId, userInput }`. Render
     the result: whether it was correct, the revealed `correctText`, and
     the `score` as a supporting "how close you were" detail (not the
     primary correct/incorrect signal — `isCorrect` is).
   - Call `record-result` with `examType: 'Dictation'`, `wordId` (from the
     original prompt fetch), `correct: <isCorrect>`, `score: <score>` —
     same shape Cloze already uses for a single-word attempt (word-anchored,
     not the wordless group). Fire once, after the grading result is shown,
     same "record after reveal" ordering every other card uses.
   - A "換一題" / next-word action that refetches `getDictationPrompt` and
     resets local state (typed answer, grading result) — same pattern
     `ReadingTestCard`/`WritingTestCard` use for their own "next" action.

3. **Wire into the exam menu** (`Exam/index.tsx`): add a new `EXAM_MODES`
   entry (e.g. `path: 'dictation'`, label along the lines of "聽寫練習
   (Dictation)"), following the existing single-source-of-truth pattern.
   This mode is charged up front (same as Cloze/Listening, not the
   Writing-style free-then-charged split) — its cost caption should read
   the same way every other flat-charged mode's caption already does.

4. **Credit cost display**: add `DICTATION_TEST` to
   `constants/creditCosts.ts` (mirroring the other `CREDIT_COSTS.*`
   entries), matching `TASK-010`'s actual `CreditCosts.DictationTest` value.

## Acceptance Criteria
- [ ] Dictation Practice appears as a new button on the exam selection
      screen with the standard flat-cost caption.
- [ ] Opening it fetches a prompt whose response contains no word text
      (verify by inspecting the actual network payload during manual
      testing, not just the TypeScript types) and plays audio on demand.
- [ ] Submitting an answer calls `gradeDictation`, shows whether it was
      correct, reveals the correct spelling, and shows the closeness score
      as a secondary detail.
- [ ] After viewing the result, `record-result` fires once with
      `examType: 'Dictation'` and the `wordId` from the original prompt.
- [ ] `npm run lint` (0 warnings), `npm run build`, `npx vitest run` all
      pass with no regressions.

## Technical Notes
- This is the first exam card whose GET response deliberately withholds
  the answer — double-check you're not accidentally rendering `wordId` (a
  GUID, harmless) in a way that looks like it's showing the word, and don't
  add any client-side "peek" affordance.
- Reuse `ExamRoundShell`/`useExamRound` for the round-progression shell,
  same as every other exam card.
- Keep UI copy plain: "聽寫練習", not implying a formal listening exam.

## Dependencies
`TASK-010` (Backend) — needs the merged response DTO shape and confirmation
that no `RecordExamResultRequest` field was added before implementing.
Architecture and requirements are otherwise final; no need to wait for
further Tech Lead sign-off once `TASK-010` lands.

## Result
_(Frontend Engineer fills in after implementation)_

## Review
_(Tech Lead fills in after PR review)_
