# TASK-007

## Title
Frontend: Reading comprehension test UI (Skill-Building item 2 — 讀)

## Owner
Frontend

## Status
READY (blocked on TASK-006 merging — needs its response shape)

## Priority
HIGH

## Objective
Surface `TASK-006`'s reading comprehension passages as a new exam mode, so
learners can practice passage-level reading, not just word/sentence-level.

## Requirements

1. **API slice** (`apis/examApis/`): add `getReadingTest` query hitting
   `GET api/exam/reading` (mirrors `getListeningResponseTest` — a plain
   query, no "generate" mutation, since the backend already decides
   generate-vs-reuse internally). Add the response types
   (`ReadingPassageResponse`/`ReadingQuestionDto`/`ReadingQuestionOptionDto`)
   to `apis/examApis/types.ts` matching TASK-006's actual merged DTO field
   names/casing exactly (read the merged C# DTOs + `Program.cs` JSON
   options, don't guess — same verification approach TASK-005 used for
   `DeliveryFeedback`). Add `'Reading'` to the `ExamType` union
   (`apis/examApis/types.ts:89`) and a `readingPassageId?: string` field to
   `RecordExamResultRequest`, alongside the existing `listeningTestId`/
   `listeningResponseTestId` fields.

2. **New `Exam/ReadingTestCard/index.tsx`** (mirrors
   `ListeningResponseTestCard`'s structure): fetch a passage on mount,
   display the passage text, then render each question with its options as
   a radio/button group (MUI), let the user answer each question, reveal
   correct/incorrect + `Explanation` per question after they answer (same
   reveal-after-answer UX already used for Listening), and call
   `record-result` with `examType: 'Reading'` and the passage's ID once the
   user finishes all questions in the passage (one record call for the
   whole passage, or one per question — match whatever granularity
   `ListeningResponseTestCard` already uses for consistency, don't invent a
   third convention).

3. **Wire into the exam menu** (`Exam/index.tsx`): add a new `EXAM_MODES`
   entry (e.g. `path: 'reading'`, `label: '閱讀理解測驗 (Reading
   Comprehension)'`), following the existing single-source-of-truth pattern
   — don't add a route by hand outside that array.

4. **Credit cost display**: add `READING_COMPREHENSION_TEST` to
   `constants/creditCosts.ts` (mirroring however `CREDIT_COSTS.CLOZE_TEST`
   etc. are defined there) and reference it in the new `EXAM_MODES` entry's
   `cost` field, matching TASK-006's actual `CreditCosts.ReadingComprehensionTest`
   value.

## Acceptance Criteria
- [ ] Reading Comprehension appears as a new button on the exam selection
      screen with a correct credit-cost caption.
- [ ] Opening it fetches and displays a passage with 3–4 questions; works
      with zero saved words (no "repository empty" warning should block
      this mode — Reading isn't word-personalized, per the Architecture
      doc's explicit trade-off. If you want to surface that fact to the
      user, a short one-line caption is fine, but it must not read like an
      error).
- [ ] Answering a question reveals correct/incorrect and the Chinese
      explanation for the option the user picked.
- [ ] Finishing the passage records the result via the existing
      `record-result` endpoint with the correct `examType`/`readingPassageId`.
- [ ] `npm run lint` (0 warnings), `npm run build`, `npx vitest run` all
      pass with no regressions to existing suites.

## Technical Notes
- Do not build a new "generate" button/mutation — this mode has no
  user-triggered regeneration, same as `ListeningResponseTestCard`; a fresh
  passage only shows up next time the query re-runs (e.g. navigating away
  and back), consistent with how the other bank-backed mode already works.
- Keep UI copy plain factual Chinese, no overclaiming (this is
  "閱讀理解", not implying it's personalized to the user's saved words).

## Dependencies
`TASK-006` (Backend) — needs the merged response DTO shape before the exact
field names can be finalized. Architecture and requirements are otherwise
final; no need to wait for further Tech Lead sign-off once TASK-006 lands.

## Result
_(Frontend Engineer fills in on completion)_

## Review
_(Tech Lead fills in after PR review)_
