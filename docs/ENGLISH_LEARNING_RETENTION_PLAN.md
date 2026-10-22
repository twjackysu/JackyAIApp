# English Learning — Retention Improvement Plan

> Status doc for the long-term retention work on the English-learning feature set.
> Created 2026-06-14. Keep this updated as items land so work can resume after any interruption.

## Goal & guiding philosophy

Make English learning something people keep using for **months/years**, **without becoming
annoying like Duolingo**.

Duolingo's friction is not streaks themselves — it's **guilt-trip push notifications** and
**weaponised streak-loss anxiety**. Our principles instead:

1. **Close the learning loop** — the system should get to know the learner over time. Effort
   spent (exams, reviews) must feed back into what gets shown next.
2. **Pull, not push** — surface what's due via in-app badges and at most a weekly email digest.
   No nagging push notifications. Once a week is information; three times a day is harassment.
3. **Forgiving by default** — a single missed day should not nuke progress. Streak loss is the
   single biggest churn moment; punishing it harder backfires.
4. **Free habit loop, paid depth** — keep the daily habit loop (Review, Daily Challenge) free;
   charge credits only for AI-heavy practice (exams). Long-term users get hooked by the free loop
   first, then pay for exams. **P0/P1 must not touch credits.**

## Current state (what already exists)

Solid foundation already in place — most early-stage apps lack this:

- **SM-2 spaced repetition**: `UserWord` has `NextReviewDate`, `ReviewIntervalDays`, `EaseFactor`,
  `ReviewCount`, `ConsecutiveCorrect`. Algorithm is inline in
  `JackyAIApp.Server/Controllers/ReviewController.cs` (`SubmitReviews`).
- **Streak / XP / levels**: `User.CurrentStreak`, `LongestStreak`, `LastStreakDate`, `TotalXP`.
- **Daily Challenge**: 5 date-seeded questions, streak + XP rewards, `DailyChallengeResult` table
  (one row per user per day, unique index on `UserId+ChallengeDate`).
- **Review flashcards**: `/review`, 0–5 quality rating, drives SM-2.
- **Exams** (`/exam`): Cloze (client-checked MC), Translation (AI graded), Sentence (AI scored
  0–100), Conversation (multi-turn). Generated from a random repository word. Cost credits.
- **Stats**: weekly report + leaderboard. `/api/review/count` already returns due count.
- **Dictionary / Repository**: search, Word of the Day, favourite → adds `UserWord`.

### Key architecture facts (for implementers)

- **No EF migrations.** `AccountController` calls `Database.EnsureCreatedAsync()` on login — this
  creates the full schema on a *fresh* DB but does **not** alter an existing one. New tables/columns
  therefore need a **manual SQL script in `scripts/`** (convention: see
  `scripts/add_datasourceconfigs_table.sql`) to apply to the live Azure SQL DB, in addition to the
  model + `DbSet` + `OnModelCreating` config so fresh DBs get it automatically.
- **DbContext**: `JackyAIApp.Server/Data/AzureSQLDBContext.cs`.
- **Models**: `JackyAIApp.Server/Data/Models/SQL/` (`User.cs` has `User` + `UserWord`).
- **Exam service**: `JackyAIApp.Server/Services/Exam/ExamService.cs` + `Generators/`.
- **Credit costs**: `JackyAIApp.Server/Common/CreditCosts.cs` +
  `jackyaiapp.client/src/constants/creditCosts.ts`.
- **Frontend nav**: English-learning sub-tabs are in `jackyaiapp.client/src/App.tsx`
  (Daily / Dictionary / Repository / Exam / Review / Stats). Pages are lazy-loaded; feature folders
  under `jackyaiapp.client/src/`.
- **Live demo data verification**: hit `https://jackyai.azurewebsites.net/api/...` to confirm real
  data shapes (the deployed DB can differ from repo seed scripts).

### Retention primitives — present vs. absent

| Primitive | State |
|---|---|
| Spaced repetition (SM-2) | ✅ present (Review only) |
| Streak / XP / levels | ✅ present |
| Daily challenge + history | ✅ present |
| Leaderboard / weekly stats | ✅ present |
| Due-count endpoint | ✅ present, **not surfaced in UI** |
| Exam results → SRS feedback | ❌ absent (loop is broken) |
| Per-word performance history | ❌ absent |
| Cold-start onboarding (word packs) | ❌ absent |
| Habit visualisation (heatmap) | ❌ absent |
| Forgiving streak (freeze/weekly goal) | ❌ absent |
| Reminders (in-app badge / email) | ❌ absent |
| Mastery buckets visualisation | ❌ absent |
| Adaptive daily challenge | ❌ absent |

---

## The two structural problems (highest value)

### A. The learning loop is broken (→ P0, item 1)

Users spend credits on Cloze/Translation/Sentence exams; the AI grades, then the result is
**thrown away**. There is no per-word attempt history and exam outcomes do **not** influence that
word's `NextReviewDate`/`EaseFactor`. The system never "learns the learner". Fixing this makes the
Review queue noticeably smarter and makes the app feel personalised — the strongest, quietest
retention lever.

### B. Cold-start death spiral (→ P0, item 2)

The only way to grow the word repository is manual dictionary search + favourite. A new user has an
empty repository → Review has no cards, exams can't generate → no reason to return day two. Needs
low-cost seeding paths.

---

## Prioritised roadmap

### P0 — do first (biggest impact)

#### P0.1 — Exam results feed back into SRS + `ExamAttempt` log

**Why:** closes the learning loop (problem A); the system starts personalising.

**Design:**
- Extract SM-2 update logic out of `ReviewController.SubmitReviews` into a reusable
  `ISpacedRepetitionService.ApplyResult(UserWord, int quality)` (or a static helper) so Review and
  Exam feedback share **one** implementation.
- New table **`ExamAttempt`**: `Id`, `UserId`, `WordId`, `ExamType` (Cloze/Translation/Sentence/
  Conversation), `IsCorrect` (bool), `Score` (int?, for graded types 0–100), `CreatedAt`. Index on
  `(UserId, WordId)` and `(UserId, CreatedAt)`.
- New endpoint **`POST /api/exam/record-result`** taking `{ wordId, examType, correct, score? }`.
  It (a) inserts an `ExamAttempt`, (b) looks up the user's `UserWord` for that word and applies an
  SM-2 update: map exam outcome → quality. Suggested mapping:
  - Wrong / low score (<60) → quality 2 (treated as "forgot": interval resets, ease drops, word
    comes back to the queue soon).
  - Correct / score 60–84 → quality 4.
  - Correct / score ≥85 → quality 5 (mild boost).
  - No credits charged here (recording an outcome is free; generation/grading already charged).
- Exam response DTOs must expose the `wordId` so the client can report back. Verify each generator
  (`ClozeExamGenerator`, `TranslationExamGenerator`, `SentenceExamGenerator`) includes the word id
  in its returned payload; add it if missing.
- Frontend: each exam card calls `record-result` once the outcome is known (Cloze: on MC check;
  Translation/Sentence: after grading returns). Show a tiny "added back to review" / "marked known"
  confirmation so the user feels the loop.

**Touches:** `Data/Models/` (new `ExamAttempt.cs`), `AzureSQLDBContext.cs` (DbSet + config),
`scripts/add_examattempt_table.sql`, `ReviewController.cs` (extract SM-2), new
`Services/SpacedRepetition/`, `ExamController.cs` + `ExamService.cs`, exam DTOs, exam generators,
`jackyaiapp.client/src/Exam/*`.

#### P0.2 — Topic word packs + add-from-challenge (fix cold start)

**Why:** solves problem B; a new user can fill the repository in seconds and has something to do
on day two.

**Design:**
- **Topic word packs**: curated lists (e.g. Workplace, Interview, Travel, Tech, Everyday) each with
  ~20 words. One click adds them all to the user's repository (creates `UserWord`s, due immediately).
  - Backend: `GET /api/repository/packs` (list packs + sizes), `POST /api/repository/packs/{id}`
    (add pack to repo). Pack word lists can start as a static seed (JSON/const) of common words; the
    `Word` rows are created on demand via the existing dictionary generation path if missing, or
    limited to words already present. Start simple: packs reference word **text**; for each, ensure a
    `Word` exists (reuse dictionary lookup/generation), then create `UserWord` if not already saved.
  - No credits (onboarding must be frictionless).
  - Frontend: surface on Repository when empty (and a small "browse packs" entry always).
- **Add-from-challenge**: in Daily Challenge results/review, each wrong-answer word gets a one-click
  "save to repository" (the most natural moment to capture a word). Reuse existing
  `PUT /api/repository/word/{wordId}`.

**Touches:** `RepositoryController.cs`, a packs definition (server const/JSON),
`jackyaiapp.client/src/Repository/*`, `jackyaiapp.client/src/DailyChallenge/*`,
`jackyaiapp.client/src/apis/*`.

### P1 — do next (cheap, high leverage)

#### P1.1 — Nav due-count badge + "Today" landing card

**Why:** gives a returning user a 30-second landing spot; surfaces the already-existing due count.

**Design:**
- **Badge**: on the English-learning **Review** sub-tab in `App.tsx`, show a MUI `Badge` with the
  due count from `GET /api/review/count` (poll/refetch on section entry). Zero-nag.
- **"Today" card**: a compact panel (top of Daily Challenge page, the natural landing) showing the
  day's three things: *Review N words → Daily Challenge → (optional) one exam*, each a button with
  done/!done state. Pure aggregation of existing endpoints (`review/count`,
  `dailychallenge/stats`).

**Touches:** `App.tsx`, `DailyChallenge/*` (or a small shared `Today` component), client apis.

#### P1.2 — Activity heatmap (GitHub-style)

**Why:** self-motivating, zero-nag habit visualisation; data already exists.

**Design:**
- Backend: `GET /api/stats/activity?days=365` → per-day activity counts derived from
  `DailyChallengeResult.ChallengeDate` + review activity. (Review currently has no per-day log; use
  `DailyChallengeResult` for v1 and optionally a lightweight daily activity rollup later, or derive
  review days from `UserWord` updates if feasible. Keep v1 simple: challenges + reviews-done days.)
- Frontend: calendar heatmap in Stats (new tab or under Weekly Report). Can hand-roll a small SVG
  grid (avoid heavy deps).

**Touches:** `StatsController.cs`, `jackyaiapp.client/src/Stats/*`.

#### P1.3 — Forgiving streak (weekly goal / freeze)

**Why:** turns the streak from an anxiety source into a tolerant habit record; reduces churn at the
single biggest drop-off moment.

**Design (pick one, weekly-goal preferred):**
- **Weekly-goal model**: streak counts *weeks* in which the user hit a goal (e.g. ≥5 active days);
  a single missed day doesn't break it. OR
- **Auto streak-freeze**: grant one automatic freeze per week; a missed day consumes a freeze
  instead of resetting `CurrentStreak`.
- Implement in the streak-update logic (currently in the Daily Challenge submit path). Add fields as
  needed (e.g. `User.StreakFreezesAvailable`, `LastFreezeGrantDate`) with a `scripts/` column-add
  script. Never show guilt-trip copy.

**Touches:** Daily Challenge submit / streak logic, `Data/Models/SQL/User.cs`, `scripts/`, Stats display.

### P2 — later

- **Weekly email digest** (the *only* outbound reminder; uses weekly-report data). Needs email infra
  — check what's available before committing.
- **Mastery buckets** in Repository: New / Learning / Mastered counts (derive from SM-2 fields,
  e.g. mastered = `ConsecutiveCorrect ≥ N` & long interval). "Mastered 124 words" beats raw XP.
- **Adaptive Daily Challenge**: add 1–2 personalised questions drawn from the user's recent wrong
  words (also fixes shareable-answers since everyone currently gets identical questions).

### P3 — much later (risk of becoming noise)

- Achievement badges, social/sharing features. Do only if they can be done tastefully.

---

## Credit policy (must hold)

The current split is healthy and must be preserved while doing this work:

- **Free:** Review, Daily Challenge, due-count, packs/onboarding, recording exam results, stats,
  heatmap, streak. (All of P0/P1.)
- **Paid (unchanged):** AI exam generation & grading, conversation turns, Whisper, NL summarize.

---

## Execution notes

- All P0 + P1 ship in **one PR**, **separate commits per item** (P0.1, P0.2, P1.1, P1.2, P1.3, plus
  this plan doc as the first commit).
- Each backend schema change ships with: model + DbSet + `OnModelCreating` config **and** a matching
  `scripts/*.sql` for the live DB. Call out in the PR description that the script must be run.
- Verify against live data via the public API before hard-coding any values.
- Backend tests live in `JackyAIApp.Server.Tests`; keep them green (`dotnet test`). Frontend must
  pass `tsc` + `eslint` + `vite build`.

## Progress checklist

- [x] Plan doc (this file)
- [x] P0.1 — Exam → SRS feedback + `ExamAttempt`
- [x] P0.2 — Topic word packs + add-from-challenge
- [x] P1.1 — Nav due-count badge + Today card
- [x] P1.2 — Activity heatmap
- [x] P1.3 — Forgiving streak
- [ ] P2 — weekly email digest, mastery buckets, adaptive daily challenge
- [ ] P3 — achievement badges, social/sharing

### Live-DB scripts to run for this PR (EnsureCreated covers fresh DBs only)
- `scripts/add_examattempt_table.sql`
- `scripts/add_streak_freeze_columns.sql`
