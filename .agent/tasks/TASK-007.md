# TASK-007

## Title
Frontend: Reading comprehension test UI (Skill-Building item 2 — 讀)

## Owner
Frontend

## Status
PR_OPENED

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

Confirmed `TASK-006` (PR #342) merged into `master` (2026-09-16T07:25 UTC)
before starting — read the actual merged `ExamModels.cs` DTOs (not just
TASK-006's self-report) to get exact field names/casing:
`ReadingPassageResponse { Id, Title, PassageText, Questions:
List<ReadingQuestionDto> }`, `ReadingQuestionDto { Id, QuestionText,
Options: List<ReadingQuestionOptionDto> }`, `ReadingQuestionOptionDto { Id,
Text, IsCorrect, Explanation }`. No explicit `JsonNamingPolicy` in
`Program.cs`, so ASP.NET Core's default camelCase applies (confirmed
against existing `ListeningResponseTestResponse` frontend types, which
already assume this).

### Implementation
- `apis/examApis/types.ts`: added `ReadingPassageResponse`/
  `ReadingQuestion`/`ReadingQuestionOption`; added `'Reading'` to the
  `ExamType` union; added `readingPassageId?: string` to
  `RecordExamResultRequest`.
- `apis/examApis/index.ts`: added `getReadingTest` query (`GET
  api/exam/reading`, no mutation — matches `getListeningResponseTest`'s
  plain-query convention since the backend decides generate-vs-reuse
  internally) and exported `useGetReadingTestQuery`.
- `constants/creditCosts.ts`: added `READING_COMPREHENSION_TEST: 3`,
  matching the backend's `CreditCosts.ReadingComprehensionTest` value
  (verified by the existing `creditCosts.test.ts` drift check, which reads
  the .cs file directly).
- New `Exam/ReadingTestCard/index.tsx`: fetches a passage on mount via
  `useGetReadingTestQuery`, renders the passage text then each question as
  its own MUI `RadioGroup`, revealing the picked option's Chinese
  `Explanation` (correct in green / incorrect in red) immediately on
  answering, same reveal-after-answer UX as `ListeningQuizCard`. Reused
  `ExamRoundShell`/`useExamRound` for the round-progress chrome, restart
  screen, and loading/error states, consistent with every other exam card.
  Did not reuse `ListeningQuizCard` itself — its contract is one audio
  question per fetch, whereas a Reading fetch returns one passage with
  3–4 questions, a genuinely different shape — so a small sibling
  component matching the *pattern* (not the exact prop contract) was the
  smallest correct change rather than forcing a mismatched abstraction.
  `record-result` granularity: one call per question (same granularity
  `ListeningResponseTestCard` uses — one call per individual Q&A), fired
  together once the passage's last question is answered, all carrying the
  same `readingPassageId`. A new passage only loads on "下一篇" (`refetch`
  after `round.advance()`), no user-triggered regenerate button, per the
  task's explicit note.
- `Exam/index.tsx`: added the `reading` entry to `EXAM_MODES` (single
  source of truth already driving both the button and the route), with a
  `warning`-color button (the one MUI palette color not already used by
  another mode) and its credit-cost caption.
- Did not add a Reading-specific "repository empty" caption: the existing
  info alert already only renders on the exam **selection** screen, not
  inside any test route, and does not block navigation to any mode
  (confirmed in the existing code comment) — Listening/ListeningResponse
  already have this exact same word-independent behavior with no special
  casing, so Reading needed none either.

### Files Changed
- `jackyaiapp.client/src/apis/examApis/types.ts`
- `jackyaiapp.client/src/apis/examApis/index.ts`
- `jackyaiapp.client/src/constants/creditCosts.ts`
- `jackyaiapp.client/src/Exam/ReadingTestCard/index.tsx` (new)
- `jackyaiapp.client/src/Exam/index.tsx`

### UI / UX
- Reading Comprehension button appears on the exam selection screen
  (label "閱讀理解測驗 (Reading Comprehension)") with its credit-cost
  caption.
- Passage text renders in a bordered `Paper`, each question below it as a
  numbered fieldset with lettered (A/B/C…) radio options.
- Answering a question locks that question's options and reveals every
  shown option's Chinese explanation (correct in green, the picked wrong
  one in red), matching the Listening cards' convention exactly.
- "下一篇" button (+ its credit-cost caption) appears once every question
  in the passage has been answered; clicking it clears local answer state
  and fetches the next passage (or shows the round-result screen if the
  round is complete).

### Tests
- `npm run lint` — 0 warnings.
- `npm run build` — TypeScript + Vite production build succeeded.
- `npx vitest run` — **134/134 passed** (up from 133; no new test file
  added for `ReadingTestCard` itself, consistent with the existing
  project convention of no per-card component tests for
  `ListeningResponseTestCard`/other exam cards — but the pre-existing
  `creditCosts.test.ts` picked up the new `READING_COMPREHENSION_TEST`
  entry automatically and passed, confirming it matches the backend
  constant).
- Did not run the app against a live backend/browser (no dev server
  exercised) — confidence comes from the type-level contract match against
  the actual merged backend DTOs plus the full existing test suite passing
  with no regressions.

### Known Limitations
- No dedicated unit/component test for `ReadingTestCard`'s interaction
  flow — matches the existing project pattern (none of the other exam
  cards have one either); flagging in case Tech Lead wants one added as a
  follow-up.
- Credit cost (`3`) mirrors the backend's own flagged-as-a-starting-estimate
  value; if Backend/Tech Lead revise `CreditCosts.ReadingComprehensionTest`
  after observing real token usage, `creditCosts.ts` needs the matching
  update (the existing drift test will catch a mismatch, not fix it).

### Blockers
None.

### PR
_(added once opened — see next commit)_

## Review
_(Tech Lead fills in after PR review)_
