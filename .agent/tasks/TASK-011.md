# TASK-011

## Title
Frontend: Dictation practice test UI (Skill-Building item 4 — 聽, last item)

## Owner
Frontend

## Status
PR_OPENED

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
- Verified `TASK-010`'s **merged** DTOs directly from `DTO/ExamModels.cs` and
  `Controllers/ExamController.cs` on `master` (PR #352) before writing any
  frontend code: `DictationPromptResponse { WordId }`,
  `DictationGradeRequest { WordId, UserInput }`,
  `DictationGradeResponse { CorrectText, IsCorrect, Score }`, routes
  `GET api/exam/dictation`, `GET api/exam/dictation/audio?wordId=X`,
  `POST api/exam/dictation/grade`. Confirmed `RecordExamResultAsync` needed
  no request field change per the task's own Result section — `wordId` +
  `correct` + `score` is all `RecordExamResultRequest` needed, exactly like
  Cloze.
- **`apis/examApis/types.ts`**: added `'Dictation'` to `ExamType`;
  `DictationPromptResponse`/`DictationGradeRequest`/`DictationGradeResponse`
  (camelCase, matching `Program.cs`'s default JSON options — same
  verification approach every prior Skill-Building frontend task used).
- **`apis/examApis/index.ts`**: `getDictationPrompt` (query, `GET
  dictation`) and `gradeDictation` (mutation, `POST dictation/grade`),
  hooks exported.
- **`constants/creditCosts.ts`**: `DICTATION_TEST: 1`, matching
  `CreditCosts.DictationTest`; `creditCosts.test.ts`'s backend-mirror check
  passes.
- **New `Exam/DictationTestCard/index.tsx`**: `useGetDictationPromptQuery`
  fetches on mount (flat charge, same as Cloze — response holds only
  `wordId`, never rendered as if it were the word). A play button (mirrors
  `ListeningQuizCard`'s `useOneShotAudio` play/playingKey pattern) hits
  `GET /api/exam/dictation/audio?wordId=...`; deliberately **not**
  one-shot-restricted like the MCQ listening cards — replay is allowed at
  any time (dictation isn't multiple-choice, so repeat listens aren't
  "cheating" the way they would be for an MCQ option), captioned "可重複播放".
  Single-line `TextField` + submit calls `gradeDictation({ wordId,
  userInput })`, renders `isCorrect` as the primary signal, `correctText`
  revealed, `score` shown as a secondary "接近程度" detail. `record-result`
  fires once, right after grading (`examType: 'Dictation'`, the original
  `wordId`, `correct: isCorrect`, `score`) — same call-site timing
  `ClozeTestCard` uses (submit already reveals the result in the same
  render, so there's no separate "reveal" step to wait for). "換一題" reuses
  `round.advance()`/`refetch()`, same as `ReadingTestCard`/`WritingTestCard`.
  Uses `ExamRoundShell`/`useExamRound` like every other card.
- **`Exam/index.tsx`**: new `EXAM_MODES` entry, `path: 'dictation'`, label
  "聽寫練習 (Dictation)", flat-charged cost caption (not the Writing-style
  free-then-charged split, per the task).
- No dedicated component test added — matches the existing convention (no
  other test card has one either, per `TASK-009`'s own note); manually
  verified via `npm run build` + `npx vitest run` that nothing regressed.
- `npm run lint` (0 warnings), `npm run build`, `npx vitest run` (136
  tests, including `creditCosts.test.ts`'s backend-mirror check for the
  new `DICTATION_TEST` entry) all pass.
- Committed and pushed to `claude/amazing-cori-fc4dxu`.

### PR
https://github.com/twjackysu/JackyAIApp/pull/354

## Known Issues
- Manual end-to-end verification (opening the mode in a running app,
  inspecting the actual network payload for `GET api/exam/dictation` to
  confirm no word text leaks, playing audio, submitting) was **not**
  performed in this session — no backend/DB available to run the app
  against. Flagging per the task's own acceptance criterion ("verify by
  inspecting the actual network payload during manual testing, not just
  the TypeScript types") as not yet done; recommend a manual pass before
  merge, or QA picking this up per the normal task lifecycle.

Implemented against `TASK-010`'s merged backend (PR #352) — API slice
additions (`getDictationPrompt`/`gradeDictation`), `DictationPromptResponse`/
`DictationGradeRequest`/`DictationGradeResponse` types matching the merged
backend DTOs exactly, `'Dictation'` added to `ExamType`, new
`Exam/DictationTestCard/index.tsx` (prompt fetch shows only `wordId`, never
rendered as word text; play/replay button via `useOneShotAudio` hitting
`GET /api/exam/dictation/audio?wordId=...`, deliberately **not** one-shot
restricted — replay allowed anytime, captioned "可重複播放", since this is a
free-typing exercise, not an MCQ where replay would be "cheating"; submit →
`gradeDictation` → `isCorrect` as the primary signal, `correctText`
revealed, `score` shown as a secondary "接近程度" detail; `record-result`
fires once after grading with `examType: 'Dictation'`, including the same
cross-card SRS-feedback caption — "✓ 已記錄" / "↻ 已記錄" — every other
word-anchored exam card, Cloze/Translation/Sentence/Writing, already shows;
built on `ExamRoundShell`/`useExamRound` like every other card), new
`EXAM_MODES` entry (`path: 'dictation'`, flat-charged cost caption),
`CREDIT_COSTS.DICTATION_TEST: 1`.

**Duplicate-PR note**: two independent Frontend sessions implemented this
task in parallel against the same merged `TASK-010` backend — PR #353 and
PR #354, both structurally identical (same 7 files, same DTO shapes, both
CI-green). #354 was kept as canonical because it carries the cross-card
SRS-feedback caption described above, which #353 omitted entirely (a real
feature-parity gap, not a style preference). #353 was closed as a
duplicate. Two follow-up documentation-only PRs (#355, #356) were then
opened independently by two more sessions to record that same resolution
in `TASK-011.md`/`PROJECT_STATUS.md` — themselves a duplicate of each
other. Tech Lead recorded the outcome directly in this commit instead
(per this role's own git rule, §16: agent-state doc changes go straight to
`master`, no PR needed) and closed both #355 and #356 as redundant.

### Tests
`npm run lint` (0 warnings), `npm run build`, `npx vitest run` (136 tests,
including `creditCosts.test.ts`'s backend-mirror check for the new
`DICTATION_TEST` entry) all pass per #354's own report. CI
(`lint-and-build-client`, `build-and-test-server`) both green on #354's
head commit. No dedicated component test added for `DictationTestCard`,
matching the existing convention (no other exam card has one either).

**Known limitation**: manual end-to-end verification (inspecting the
actual `GET api/exam/dictation` network payload to confirm no word text
leaks, playing audio against a live backend, submitting) was not performed
in any of the implementing sessions — no backend/DB available in that
sandbox. Recommend a manual pass (or QA) before Boss merges.

### PR
https://github.com/twjackysu/JackyAIApp/pull/354 (canonical; #353 closed
as duplicate; #355/#356 closed as redundant documentation duplicates)

## Review

Reviewed 2026-09-20, reading #354's actual diff (not only its self-report):
`DictationTestCard/index.tsx` correctly withholds `wordId` from ever being
rendered as visible text, replay is intentionally unrestricted (matches
the task's own reasoning that dictation isn't an MCQ where repeat listens
would be "cheating"), `record-result` fires exactly once per submission
with the word-anchored shape (`wordId`/`correct`/`score`, no wordless
fields), and the DTOs in `apis/examApis/types.ts` match the merged backend
DTOs field-for-field. `EXAM_MODES`/`CREDIT_COSTS.DICTATION_TEST` wiring is
consistent with every other flat-charged exam mode. No correctness issues
found — technically acceptable. CI is green
(`lint-and-build-client`/`build-and-test-server` both `success`),
`mergeable_state: clean`.

**READY FOR BOSS REVIEW** — the one open item is the manual
end-to-end/network-payload check neither implementing session could run
(no live backend in their sandboxes); recommend that check (or a QA pass)
before merging #354, per this task's own acceptance criteria. Not a
blocker Tech Lead can resolve unilaterally since it requires a running
app, not a code change.
