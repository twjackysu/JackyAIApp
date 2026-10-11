# JackyAIApp — Architecture Notes

> Maintained by Tech Lead Agent. Not a full rewrite of `CLAUDE.md` — this file
> only records decisions and technical designs for active initiatives, plus
> pointers to the patterns already established in the codebase.

## Baseline

Frontend: Vite + React 18 + TS, client-side rendered SPA, RTK Query, React
Router v6. Backend: .NET 10 Web API, EF Core / Azure SQL, ASP.NET Cookie
auth (Google OAuth), `[Authorize]` per-controller with `[AllowAnonymous]`
overrides where needed. See root `CLAUDE.md` and module `CLAUDE.md` files
for the full stack description — not duplicated here.

---

## Decision: Public, indexable Dictionary word pages (Growth & Retention — SEO)

### Context
`REQUIREMENTS.md` → "Growth & Retention (No Paid Ads)" (HIGH priority,
DECISION-003) names publicly-viewable, indexable word pages as the
highest-leverage growth idea: zero incremental AI cost (content is already
cached), no login required to view.

Investigated the current implementation (2026-09-12) before scoping tasks.
Findings:

1. **Backend is already public.** `DictionaryController.Get`
   (`GET api/dictionary/{word}`) has no `[Authorize]` and already carries a
   comment stating anonymous lookup is intentional. `DictionaryService.GetWordAsync`
   touches no per-user data. No backend auth change is needed for the read path.
2. **Frontend route is already outside `RequireAuth`.** `/english/dictionary`
   is not wrapped in `RequireAuth` (`EnglishLearning/index.tsx`). So a
   logged-out visitor can already open the page today.
3. **But it's not actually SEO-ready**, for three separate reasons:
   - `Dictionary/index.tsx` unconditionally calls the `[Authorize]`-gated
     `GET api/repository/word/{wordId}` (favorite/bookmark status) even when
     logged out — guaranteed 401 on every anonymous page view, and the
     favorite button renders (and silently fails) for anonymous users.
   - The word is read from a **query string** (`?word=apple`), not a path
     segment. There is no clean canonical URL per word, which hurts both
     crawling and shareability (the two things this initiative is for).
   - The app is **pure client-side rendered**, with a single static
     `<title>` and no meta description/OG tags anywhere, and no per-word
     `document.title`/meta injection. Search engines that execute JS
     (Googlebot) can index CSR content to a degree, but link-preview
     scrapers (LINE, Facebook, etc. — the actual sharing channel named in
     the requirement) generally do **not** execute JS and will see nothing
     useful. So "public" ≠ "indexable/shareable" here; both existing
     `[AllowAnonymous]`-style access AND page metadata are required, and
     metadata is the part that's actually missing.
4. `Word.Id` is a GUID (not slug-friendly); `Word.WordText` is the natural
   slug but has no DB-level unique constraint today (uniqueness is only
   enforced by application logic via `SingleOrDefaultAsync`).

### Decision
Ship this in two phases, split by cost/complexity, per DECISION-003's "no
increase in cost per user" constraint:

**Phase 1 — low effort, ships now (see TASK-001, TASK-002):**
- Move word routing from query string to a path param:
  `/english/dictionary/:word` (frontend route + component change only,
  backend endpoint shape unchanged since it's already keyed on word text).
- Skip the `GET api/repository/word/{wordId}` call entirely when the viewer
  is not logged in (check auth state before firing the query), and hide/gate
  the favorite button for anonymous users instead of letting it fail silently.
- Add dynamic per-word `<title>` and `<meta name="description">` (and
  canonical `<link>`) via `react-helmet-async` (new, small, no server
  component — helps JS-executing crawlers and is a prerequisite for Phase 2
  regardless of how Phase 2 is implemented).
- Add a `sitemap.xml` endpoint (backend, enumerating known `WordText`
  values) and a `robots.txt` entry pointing to it, so crawl discovery
  doesn't rely on internal links alone.
- Add a unique index on `Word.WordText` (data-integrity fix, independent of
  SEO, but relevant now that the column becomes a public routing key).

**Phase 2 — resolved 2026-09-13, proceeding with option (a) (see
`.agent/boss/TECH_LEAD.md`):** non-JS-executing scrapers (LINE/Facebook
share previews — the actual sharing mechanic in the requirement) need
server-rendered meta tags, which a pure Vite CSR SPA cannot provide as-is.
Note this is a *separate* problem from Google Search discoverability:
Googlebot executes JavaScript when indexing, so Phase 1 alone already
satisfies "findable via Google search" — Phase 2 only affects how a shared
link previews inside chat apps.
Options considered:
  a. **Chosen.** Lightweight bot-detection middleware in `JackyAIApp.Server`
     that, for known crawler/bot user agents only, serves a small
     server-rendered HTML shell with the right `<title>`/OG tags for that
     word (real users, and Googlebot, still get the normal SPA) — smallest
     blast radius, no new hosting component. Maintenance cost assessed as
     low: link-preview bot UAs (LINE/Facebook/Twitter/Slack/Discord/
     WhatsApp) are officially published and rarely change, unlike
     adversarial bot detection — this is the same "dynamic rendering"
     pattern Google itself previously recommended for CSR SPAs, and is not
     a cloaking risk as long as bots and real users see the same
     underlying content. See TASK-003.
  b. A hosted prerendering service (e.g. prerender.io-style) sitting in
     front of the SPA — no app code change, but a new paid/infra dependency,
     working against the "reduce cloud cost" goal. Rejected — (a) achieves
     the same outcome with no added recurring cost.
  c. Accept Phase 1 only for now and defer proper share-preview support —
     superseded now that (a)'s actual cost/maintenance profile turned out
     low enough to just do.

### Reason
Backend already does the hard part (public, cacheable, no per-user side
effects) for free — no architecture change needed there. The actual gap is
frontend metadata/routing (cheap, Phase 1) and crawler-vs-SPA rendering
(a real trade-off, Phase 2) — those shouldn't be bundled into one task or
decided unilaterally given the cost-sensitivity in DECISION-003.

### Alternatives
- Full SSR framework migration (Next.js/Remix): rejected — disproportionate
  cost/complexity for one feature area, against "simplest architecture that
  satisfies requirements" and against the stated cost-reduction goal.
- Static-generate all word pages at build/deploy time: rejected for now —
  word set grows continuously (AI-generated on demand), so static
  generation would need a rebuild/redeploy trigger per new word; revisit
  only if Phase 2 bot-middleware proves insufficient.

### Trade-offs
Phase 1 ships real value (crawlable by Google, correct canonical URLs, no
more silent 401s) without touching hosting/infra. It does not fully satisfy
the "shareable to LINE/social" acceptance criterion on its own, since link
previews need Phase 2.

### Single-source title/description text (2026-09-13, addresses Boss's drift concern)

Boss's follow-up question on Phase 2: since the bot-facing HTML (TASK-003)
has no interactive UI and nobody ever looks at it, will its text quietly
go stale relative to the real page over time?

Valid concern — "reuse the same underlying word data" (as originally
scoped) is not the same guarantee as "the exact same displayed text."
`DictionaryService.GetWordAsync` returning the same `Meanings`/`Definitions`
data to both surfaces still leaves the *formatting* (how a title/description
string gets built from that data — truncation length, suffix wording, which
definition to pick) as separate logic in two different codebases/languages
(TS for the frontend `<title>`/meta tags, C# for the bot middleware). Two
independent implementations of the same formatting rule are exactly the
kind of thing that drifts silently when one side changes and nobody
notices — which is the real risk Boss flagged, not "will the bots find the
data."

**Fix:** collapse it to one implementation. TASK-001 now adds a single C#
helper that formats `seoTitle`/`seoDescription` from a word's data, and
exposes the result as fields on the `GET api/dictionary/{word}` response.
TASK-002's frontend renders those fields verbatim (no TS-side formatting).
TASK-003's middleware calls the same C# helper in-process (not a second
implementation, not even an HTTP call to its own API). This isn't a
process/checklist rule to remember at review time — after this change
there is exactly one place in the codebase that can produce this text, so
there is nothing left to keep in sync. Updated in TASK-001/002/003; no
runtime/infra cost added (same data, computed once instead of twice).

### Impact
- `jackyaiapp.client`: routing change, new dependency (`react-helmet-async`),
  `Dictionary/index.tsx` conditional-query fix.
- `JackyAIApp.Server`: new `sitemap.xml`/`robots.txt` endpoints, one EF
  migration (unique index on `WordText`).
- No change to `DictionaryController` auth or `DictionaryService` — already
  correct.

---

## Decision: Speaking delivery feedback (pronunciation/fluency) — Skill-Building item 1

### Context
`REQUIREMENTS.md` → "Skill-Building Feature Expansion" (HIGH, DECISION-004/
005) names 說 (speaking) as the skill with the clearest gap: Conversation
Test already records audio, transcribes it via Whisper, and runs a
**grammar**-only correction on the transcript (`ExamController.cs`
`conversation/respond` → `ExamService.RespondToConversationAsync` →
`Prompt/Exam/ConversationResponseSystem.txt`). Nothing today evaluates *how*
something was said — pace, pauses, filler words, pronunciation accuracy.
Boss approved this as the first item to hand to Tech Lead (DECISION-005),
for a feasibility/cost evaluation before scoping.

Investigated the current implementation before scoping (2026-09-15):

1. **Two decoupled calls today.** The client records audio → `POST
   whisper/transcribe` (`ExamController.cs:68`) → `ExamService
   .TranscribeAudioAsync` (`ExamService.cs:463`) → `OpenAIPromptService
   .TranscribeAudioAsync` (`Services/OpenAI/OpenAIPromptService.cs:204`)
   → text. The client then separately posts that plain text as
   `UserMessage` in `POST conversation/respond` — audio itself never
   reaches the grammar-correction step and is not persisted anywhere
   (transient `byte[]` in backend memory during the Whisper call; a
   discarded browser `Blob` on the frontend, `useVoiceRecording.ts`). So
   any delivery feature has to be built on top of the Whisper call
   specifically, not the correction call.
2. **`ResponseFormat` is hardcoded to `"json"`**
   (`OpenAIPromptService.cs:216`), so only `response.Text` comes back
   today. Whisper also supports `"verbose_json"` (same audio-transcription
   billing — response format doesn't change OpenAI's per-minute Whisper
   price), which additionally returns `Duration` and per-segment `Start`/
   `End` timestamps (word-level with `TimestampGranularities: ["word"]`).
   That's enough to derive real, objective delivery signals — words/minute
   (pace), count and length of gaps between segments (pauses), and a
   filler-word count via a fixed word list ("um", "uh", "like", "you
   know") run against the transcript — **without adding a new AI call or
   a new vendor**, since the Whisper call is already happening and already
   paid for via `CreditCosts.AudioTranscription` (`Common/CreditCosts.cs:45`,
   cost `1`).
3. **What this is *not***: true phoneme-level pronunciation-accuracy
   scoring ("did they say the *th* sound correctly") is not obtainable
   from Whisper under any response format — it returns no confidence or
   pronunciation-correctness field at all, in `json` or `verbose_json`.
   Getting real pronunciation accuracy requires a dedicated
   pronunciation-assessment product (e.g. Azure Speech's Pronunciation
   Assessment) — a new third-party vendor, a new per-call cost, and audio
   would need to be sent to it directly (today's audio is discarded after
   the Whisper call, so persisting/forwarding it is itself new plumbing).
   This is a materially different cost/complexity tier from item 3.2.

### Decision
Split into two phases, same reasoning as the SEO work's Phase 1/Phase 2
split — ship the free, no-new-dependency part now; treat the paid,
new-vendor part as a separate decision for Boss.

**Phase 1 — fluency/delivery feedback, zero added cost, proceeding now
(see TASK-004, TASK-005):**
- Switch Whisper's `ResponseFormat` to `"verbose_json"` with word-level
  timestamps, in the same existing `whisper/transcribe` call.
- Compute pace (WPM), long-pause count (segment gaps over a fixed
  threshold, e.g. 1.5s), and filler-word count in C#, purely from the
  richer Whisper response — no second AI call, no new credit gate needed
  (bundled into the same already-gated `AudioTranscription` action).
- Return the metrics as new fields on `WhisperTranscriptionResponse`
  alongside `Text`, plus a short rule-based (non-AI) comment per metric
  ("良好的語速" / "說話有點快，可以放慢一點" style), so the frontend can
  render it next to the existing grammar `CorrectionAlert` without a
  second network round trip.
- This directly closes the "zero feedback on delivery" gap named in
  `REQUIREMENTS.md` for **fluency/delivery** specifically (pace, pauses,
  fillers) — it does not (and does not claim to) cover phoneme-level
  pronunciation accuracy. Naming this precisely in the UI ("表達流暢度
  回饋" not "發音評分") avoids overselling what's actually measured.

**Phase 2 — true pronunciation-accuracy scoring — open question to Boss,
not started:**
- Would need a dedicated speech-assessment API (new vendor, new recurring
  cost per call, and new audio-retention/plumbing since audio isn't kept
  today). This is exactly the kind of cost/external-dependency decision
  that belongs to Boss per DECISION-003's cost discipline, not something
  Tech Lead should greenlight unilaterally the way Phase 1 can proceed
  without waiting. Written up as an open question in
  `.agent/boss/TECH_LEAD.md`.

### Reason
Phase 1 reuses data from a call that already happens and is already billed
— a real, measurable delivery-feedback feature at $0 marginal AI/API cost,
consistent with DECISION-003's "evaluate cost per user" acceptance
criterion and matching the "no new infra, ship now" pattern already used
for the SEO Phase 1 work. Phase 2 (true pronunciation scoring) is a
genuinely different cost tier and should not be bundled into the same
task/decision.

### Alternatives
- Sending raw audio to an OpenAI multimodal audio-capable model and asking
  it to judge pronunciation directly: rejected for Phase 1 — unproven
  reliability for phoneme-level judgments (LLMs are not verified
  pronunciation-assessment tools), would need audio to be persisted/
  forwarded (new plumbing), and would add a second, audio-priced AI call
  per turn (audio tokens cost more than text) — a real cost increase for
  an unvalidated signal. Could be revisited as a Phase 2 alternative to a
  dedicated vendor if Boss wants to explore before committing to a new
  vendor relationship, but not proposed as the default Phase 2 path here.
- Doing nothing until a dedicated pronunciation-assessment vendor is
  approved: rejected — leaves the named gap fully open when a real,
  free, in-stack improvement (Phase 1) is available now.

### Trade-offs
Phase 1 ships genuine delivery feedback (pace/pauses/fillers) at zero
added cost, but does not satisfy a literal reading of "pronunciation
feedback" (phoneme accuracy) — only "fluency/delivery" feedback. Flagged
explicitly so Boss can decide whether that's sufficient or whether Phase 2
(new vendor, new cost) should be pursued.

### Impact
- `JackyAIApp.Server`: `OpenAIPromptService.TranscribeAudioAsync`
  (response format + word timestamps), a small new fluency-metrics
  helper, `WhisperTranscriptionResponse` DTO gains new fields. No new
  credit cost, no new external dependency.
- `jackyaiapp.client`: `ConversationTestCard` renders the new fields next
  to the existing `CorrectionAlert`, reusing the same component pattern.

---

## Decision: Reading comprehension passages — Skill-Building item 2

### Context
`REQUIREMENTS.md` → "Skill-Building Feature Expansion" names 讀 (reading) as
having word/Cloze-level coverage only — no passage-level reading
comprehension. PM handed this item to Tech Lead 2026-09-15 for a
feasibility/cost evaluation (executing the already-approved DECISION-005
priority order, no fresh sign-off needed, same pattern as item 1).

Investigated the existing exam architecture before scoping (2026-09-16):

1. **Two different generation patterns already exist in the codebase, not
   one.** Cloze/Translation/Sentence share `IWordExamGenerator` — anchored
   to a *single* word from the user's own repository, generated once and
   cached forever per word (`word.ClozeTests.Count > 3` → reuse), personal
   in the sense that the word itself is drawn from the user's vocabulary.
   Listening/ListeningResponse use a *different*, already-proven pattern: a
   shared bank, not tied to any user's word list, grown lazily — `shouldGenerate
   = unseenCount < BankGrowthThreshold || Random.Shared.Next(5) == 0`
   (`ExamService.cs:225`) — so most requests reuse an existing bank entry at
   zero AI cost, and only occasionally trigger a new OpenAI call to keep the
   bank growing, tracked per-user via `ExamAttempts`-derived recent-ID
   exclusion (`GetRecentListeningResponseTestIdsAsync`, `NoRepeatWindow = 30`).
2. **A reading passage doesn't fit the per-word pattern.** A passage
   inherently uses several words at once, and which words a given user has
   saved differs per user — so a passage "anchored to one word" the way
   Cloze is would either ignore the rest of its own content for caching
   purposes, or need a cache key of "this exact set of words," which two
   users (or the same user twice) would essentially never share. That kills
   the cross-user reuse Cloze/Translation/Sentence rely on to keep AI cost
   low after the first generation.
3. **Reading comprehension, unlike Cloze, isn't inherently about drilling
   *the user's own saved words* anyway** — `REQUIREMENTS.md`'s named gap is
   "no passage-level reading comprehension," a general comprehension skill,
   not "reinforce my vocabulary list." Listening/ListeningResponse test the
   same kind of general skill (not tied to the user's own words) and already
   made this exact trade-off successfully.

### Decision
Model Reading Comprehension on the **Listening/ListeningResponse shared-bank
pattern**, not the Cloze/Translation/Sentence per-word pattern:

- New tables: `ReadingPassage` (Id, Title, PassageText, CreatedAt) and
  `ReadingQuestion`/`ReadingQuestionOption` (same shape as
  `ListeningResponseTest`/`Option` — question text + 3–4 options, each with
  `IsCorrect` and a short Chinese `Explanation`), 3–4 questions per passage
  covering a mix of main-idea/detail/inference/vocabulary-in-context
  questions (not just detail-recall, so it actually exercises comprehension
  rather than re-testing Cloze-style word recall).
- One OpenAI call generates a whole passage + all its questions in one JSON
  response (same "one call returns the full object graph" shape as
  `ListeningResponseGenerationResult`) — a passage is ~120–180 words at an
  intermediate level, topic chosen from a small fixed rotating list (e.g.
  daily life, travel, workplace, technology, environment) for variety, fed
  as the user-turn text the same way `ClozeExamGenerator` feeds `word.WordText`.
- `ExamService.GenerateReadingExamAsync()` reuses the exact
  `GenerateListeningResponseExamAsync` shape: bank-growth-threshold check,
  exclude the user's recently-seen passage IDs, generate only when the
  unseen pool is thin. No second AI call for grading — like Cloze/Listening,
  `IsCorrect`/`Explanation` are returned in the same response as the
  question (client-trusted, consistent with how every other MCQ-style exam
  here already works).
- `RecordExamResultAsync`: add `"Reading"` to the wordless (`isWordless`)
  branch alongside `"Listening"`/`"ListeningResponse"`, with a
  `ReadingPassageId` field on `ExamAttempt` mirroring `ListeningTestId`.
- Credit cost: propose `CreditCosts.ReadingComprehensionTest = 3` — between
  `ClozeTest`/`SentenceTest` (2, a single sentence) and
  `ListeningResponseTest` (4, bundles multiple TTS calls); a passage-sized
  text-only completion sits between those in raw output size, and — like
  Listening's cost — is charged on every play even though most plays reuse
  a bank entry at zero marginal AI cost, consistent with how
  `ListeningResponseTest` is already priced. Backend/PM should sanity-check
  this once real token usage is observed; it's a starting estimate, not a
  measured number.

### Reason
Reuses an already-proven, already-shipped pattern (Listening's bank +
growth-threshold) instead of inventing a new caching strategy, and avoids
the dead-end of trying to force passage content into the single-word cache
key that Cloze/Translation/Sentence depend on. Zero new vendor, zero new
infrastructure — same OpenAI text-completion call already used everywhere
else in Exam System, consistent with DECISION-003's cost discipline.

### Alternatives
- Anchor a passage to one "primary" word from the user's repository (Cloze-style):
  rejected — as explained above, the cache-key mismatch would make nearly
  every request a fresh AI call, the worst-case cost outcome, not the
  DECISION-003-aligned choice.
- Generate a fresh passage per request, no bank/caching at all: rejected —
  strictly worse cost than the bank approach for no added value; Listening
  already proved the bank pattern works for this "general skill test, not
  tied to my personal words" category of exam.

### Trade-offs
Passages are not personalized to the user's own saved vocabulary the way
Cloze/Translation/Sentence are — a user won't necessarily see their own
saved words inside a reading passage. This matches Listening's existing
trade-off (also not personalized) and is consistent with the requirement's
framing (a comprehension-skill gap, not a vocabulary-drill gap), but is
called out explicitly in case Boss expects every exam mode to be
personalized to saved words.

### Impact
- `JackyAIApp.Server`: new `ReadingPassage`/`ReadingQuestion`/
  `ReadingQuestionOption` tables + EF migration, new
  `Prompt/Exam/ReadingSystem.txt`, `ExamService.GenerateReadingExamAsync()`
  (mirrors `GenerateListeningResponseExamAsync`), new `CreditCosts` entry,
  `ExamAttempt` gains `ReadingPassageId`, `RecordExamResultAsync` gains a
  third wordless branch.
- `jackyaiapp.client`: new `ReadingTestCard` (mirrors
  `ListeningResponseTestCard`'s bank-backed single-query pattern — no
  "generate" mutation needed, `GET api/exam/reading` returns a ready-to-play
  passage+questions), new entry in `EXAM_MODES` (`Exam/index.tsx`).

See `TASK-006` (Backend) and `TASK-007` (Frontend, depends on TASK-006's
response shape).

---

## Decision: Open-ended writing practice with AI feedback — Skill-Building item 3

### Context
`REQUIREMENTS.md` → "Skill-Building Feature Expansion" names 寫 (writing) as
covered only at single-sentence granularity (Translation Test, Sentence
Formation Test) — no open-ended/paragraph-level writing with feedback. PM
handed this item to Tech Lead 2026-09-16 for a feasibility/cost evaluation,
executing the already-approved DECISION-005 priority order (no fresh
sign-off needed, same pattern as items 1 and 2). The requirement's own
framing: "reuses the existing 'AI grades free-text against a rubric' pattern
already proven by Translation Test grading, extended to a short paragraph
prompt (e.g., 'use these 5 saved words in a short paragraph about X')."

Investigated the existing Translation/Sentence grading code before scoping:

1. **Generation and grading are already two separate, independently-costed
   steps for both Translation and Sentence** — `GenerateAsync` (via
   `IWordExamGenerator`, cached per-word) is a different call from
   `GradeTranslationAsync`/`EvaluateSentenceAsync` (`ExamService.cs:541`,
   `:717` — one-shot `GetCompletionAsync<T>` call, credit-gated inline via
   `_creditService.ConsumeCreditsAsync` rather than the `[ConsumeCredits]`
   controller attribute used on generation endpoints). Grading never writes
   an `ExamAttempt` itself — the client separately calls
   `POST api/exam/record-result` afterward. This item only needs to reuse
   the **grading** half of that pattern; there is nothing to generate via AI
   here at all (see next point).
2. **The "prompt" a writing exercise needs — a topic plus a handful of the
   user's own saved words to work into a paragraph — costs zero AI calls to
   produce.** Unlike Cloze/Translation/Sentence (each needs an AI call to
   turn one word into a graded exercise) or Reading (needs AI to write a
   whole passage), a writing prompt is just "pick a topic string from a
   fixed rotation" + "pick up to 5 rows from the user's own `UserWords`" —
   both plain DB queries. This means the credit cost story here is
   structurally different from every existing exam type: **the "generate a
   prompt" step is free, and the entire AI cost sits on the one-shot grading
   call the user triggers by submitting**, not spread across generation vs.
   evaluation the way Translation/Sentence charge both.
3. **No existing pattern for "N required words in one free-text
   submission" feeding the SM-2 schedule.** Every current grading/record-
   result path (`ExamAttempt`, `RecordExamResultRequest`) assumes exactly
   one `WordId` per attempt, or none at all (the wordless
   Listening/ListeningResponse/Reading group). A paragraph naming 5 words at
   once doesn't fit either shape as-is — this is a genuine gap, not
   something the existing schema already answers (see Decision below for how
   it's closed without a schema rework).

### Decision
Reuse Translation/Sentence's **grading pattern only** (one-shot
`GetCompletionAsync<T>` call, inline credit gate, decoupled from attempt
recording); do **not** model this on `IWordExamGenerator` or the
Listening/Reading bank pattern, since neither fits (no AI-authored content to
cache — the prompt itself is free to produce).

- **`GET api/exam/writing`** → `GenerateWritingPromptAsync()`: picks a topic
  from a small fixed rotation (e.g. daily life, a memorable experience, a
  skill you want to learn, an opinion on something — Backend's call, doesn't
  need Tech Lead sign-off on the exact list) and up to 5 distinct rows from
  the calling user's own `UserWords` (plain random DB sample, no AI). If the
  user has fewer than 5 saved words, use however many they have; if they
  have **zero**, still return the prompt with an empty `RequiredWords` list
  (open-topic writing, no forced vocabulary) rather than blocking the
  feature — same "don't punish an empty repository with an error-shaped
  response" principle `TASK-007` already established for Reading.
  **Not credit-gated** — this is the one exam-type "generation" endpoint in
  the whole app with zero underlying AI cost, so charging credits for it
  would break the "credit cost tracks AI cost" principle worse than any
  existing flat-rate charge does. Flag this explicitly in the PR/task so it
  isn't "fixed" later by someone assuming every `GET api/exam/*` must be
  credit-gated by convention.
- **`POST api/exam/writing/evaluate`** → `GradeWritingAsync(...)`: exactly
  the `GradeTranslationAsync` shape — one `GetCompletionAsync<T>` call
  against a new `Prompt/Exam/WritingEvaluationSystem.txt`, inline credit gate
  via a new `CreditCosts.WritingEvaluation`, no `ExamAttempt` write inside
  this call (client calls `record-result` separately, same as
  Translation/Sentence). Input: topic + required words (echoed back by the
  client from the `GET` response, not re-queried — same pattern
  `TranslationTestUserResponse` already uses for `ExaminationQuestion`) +
  the user's submission text. Output mirrors `SentenceTestGradingResponse`'s
  richer multi-field shape (score + grammar/content/vocabulary feedback +
  suggestions) plus one new field: `WordsUsed: List<{ Word, Used }>` so the
  UI (and the record-result call below) can tell which of the required words
  actually made it into the submission.
- **SM-2 / `RecordExamResultAsync`**: add `"Writing"` to the wordless group
  for the `ExamAttempt` row itself (no single `WordId` fits a multi-word
  attempt, so — like Listening/Reading — the attempt log itself carries no
  `WordId`; **no new `ExamAttempt` column, no EF migration**, unlike every
  prior Skill-Building task). Add `WritingWordIds: List<string>?` to
  `RecordExamResultRequest`; when `ExamType == "Writing"`, fan out the
  **existing** `_spacedRepetition.ApplyResult(...)` call across every ID in
  that list, all using the same `MapQuality(request.Correct, request.Score)`
  value already computed once for the whole submission — the AI's grade is
  one holistic judgment of the paragraph, not a separate pass/fail per word,
  so applying the same quality to every required word the paragraph was
  built around is the honest signal, not an approximation that needs its own
  new scoring logic.
- **Credit cost**: propose `CreditCosts.WritingEvaluation = 3` — a paragraph-
  level submission plus multi-field structured feedback is a heavier
  generation than `SentenceEvaluation` (2, single-sentence feedback) but the
  prompt itself carries no separate charge (unlike Translation/Sentence,
  where generation is charged too) — Backend/PM to sanity-check once real
  token usage is observed, same as every other starting-estimate cost in
  this project.

### Reason
Every piece of this reuses something already proven in this codebase
(grading-call shape, inline credit gate, `MapQuality`, the wordless
`ExamAttempt` group) rather than inventing new machinery — the only genuinely
new piece is the SM-2 fan-out, which is a five-line loop around an existing
method, not a new subsystem. No new vendor, no new AI call beyond one
already-costed completion per submission, consistent with DECISION-003.

### Alternatives
- **New junction table** (`WritingSubmission` ↔ `Word`, many-to-many) for
  precise per-word-per-submission SM-2 tracking, instead of fanning out a
  shared quality value: rejected for now as over-engineering relative to
  what the feature needs — "did this paragraph, built around these 5 words,
  go well" is inherently one holistic judgment, and the fan-out already
  reinforces every word's schedule from that single judgment. Revisit only
  if a real product need emerges for tracking per-word pass/fail *within* a
  single writing attempt separately (e.g. "used 3 of 5 correctly" changing
  each word's schedule differently) — not required by this requirement's
  acceptance criteria as written.
- **Credit-gate the `GET` prompt endpoint too**, for consistency with every
  other exam type's generation endpoint: rejected — there's no AI cost to
  recover, so charging would be charging for literally nothing, which is a
  worse inconsistency than "this one endpoint doesn't charge" is.
- **Model this on `IWordExamGenerator`** (anchor to one word, cache per
  word): rejected — this exercise is inherently multi-word by design (the
  entire point is "use several of your saved words together"), so a
  single-word cache key doesn't fit, and there's no AI-authored content to
  cache in the first place.

### Trade-offs
- The `GET` prompt endpoint being free while the `evaluate` endpoint is
  charged is a UX wrinkle other exam modes don't have (elsewhere, the cost
  shown on the exam-selection screen is the cost of the whole interaction;
  here, opening the card is free and the charge only happens on submit) —
  Frontend needs to communicate this clearly (e.g. show the cost near the
  submit button, not just the mode-selection button) rather than silently
  reusing the same cost-caption placement as every other mode.
- SM-2 quality is shared across all required words in one submission rather
  than judged per word — a user who used 4 of 5 words well and 1 badly gets
  the same schedule update on all 5. Accepted per the Alternatives section
  above; flagged here in case Boss or a future PM pass wants finer-grained
  per-word tracking later.

### Impact
- `JackyAIApp.Server`: new `Prompt/Exam/WritingEvaluationSystem.txt`,
  `GenerateWritingPromptAsync()` + `GradeWritingAsync()` on
  `ExamService`/`IExamService`, new DTOs (`WritingPromptResponse`,
  `WritingSubmissionRequest`, `WritingGradingResponse`), new
  `CreditCosts.WritingEvaluation`, `RecordExamResultAsync` gains the
  `"Writing"` wordless branch + `WritingWordIds` fan-out. **No EF
  migration** — no new tables or columns.
- `jackyaiapp.client`: new `Exam/WritingTestCard`, new `EXAM_MODES` entry,
  API slice additions, credit-cost UX handling the free-prompt/charged-
  evaluate split above.

See `TASK-008` (Backend) and `TASK-009` (Frontend, depends on TASK-008's
response shape).

---

## Decision: Dictation practice — Skill-Building item 4 (last item)

### Context
`REQUIREMENTS.md` → "Skill-Building Feature Expansion" names 聽 (listening)
as covered only by multiple-choice formats (Listening Test, Listening
Response Test) — no dictation-style practice (hear something, type what you
heard). PM handed this item to Tech Lead 2026-09-17 for a feasibility/cost
evaluation, executing the already-approved DECISION-005 priority order (no
fresh sign-off needed, same pattern as items 1–3). This is the **last** item
on the original four-item Skill-Building list — once shipped, that
requirement's scope is complete.

Investigated the existing word-picking, TTS, and grading patterns before
scoping (2026-09-19):

1. **A word-picking helper already exists and already handles the
   empty-repository case.** `GetRandomUserWordIdAsync` (`ExamService.cs:985`,
   used by Cloze/Translation/Sentence via `GenerateWordExamAsync`) already
   falls back to a random global word when the calling user has zero saved
   words. Dictation can reuse this directly — no new word-picking logic, no
   new "what if the repository is empty" edge case to design (unlike Reading
   and Writing, which each needed their own empty-state handling because
   their picking needs — a shared bank, or 5 distinct rows — didn't fit this
   existing helper).
2. **The unbilled, resolve-ID-to-text-then-TTS pattern already exists.**
   `GetListeningOptionAudio`/`GetListeningResponsePromptAudio`/
   `GetListeningResponseOptionAudio` all follow the same shape: a small
   service method resolves an ID to text server-side, then the controller's
   shared `ToAudioResultAsync` helper (`ExamController.cs:164`) handles
   cache-or-synthesize-then-serve, unbilled because a flat charge on the
   parent `GET` endpoint already covers it. Dictation needs exactly this
   shape for one word instead of a listening prompt/option.
3. **Grading needs no AI call at all** — "did the user type the word
   correctly" is a string comparison, not a judgment call. This mirrors
   Writing's finding that not every exam-type "action" has to cost
   something: here, *both* halves are cheap (one TTS call to play, one
   free string comparison to grade), making this — as `REQUIREMENTS.md`
   itself already anticipated — the cheapest of the four items to run.
4. **The existing GET-response-includes-the-answer convention (Cloze,
   Translation) doesn't fit here.** Cloze/Translation already put the
   correct answer in their initial `GET` response and rely on the frontend
   not to render it before the user answers — an accepted trust model for
   an MCQ/translation exercise where the "cheat" would just spoil the
   user's own practice. For dictation, though, the entire exercise *is*
   "identify the word from audio alone" — putting the word text in the
   initial JSON response would make the feature's core mechanic trivially
   bypassable by anyone glancing at the Network tab, which is a materially
   bigger practical problem here than for Cloze (where the MCQ options
   already narrow it to one of four anyway). This is the one place in the
   Skill-Building work where the existing "client-trusted" convention is
   deliberately **not** followed — the `GET` response carries only a
   `WordId`; the real text is resolved server-side for the audio endpoint
   and only revealed to the client after grading.
5. **Dictation is word-anchored, not wordless.** Unlike
   Listening/Reading/Writing (each of which needed a new `isWordless`
   branch, and Writing needed a new nullable-list field), a dictation
   attempt has exactly one `WordId`, the same shape Cloze/Translation/
   Sentence already use. `RecordExamResultAsync` needs **zero** code
   changes — `MapQuality`/`ApplyResult`'s existing word-anchored path
   already handles an arbitrary `ExamType` string as long as `WordId` is
   populated.

### Decision
- **`GET api/exam/dictation`** → `GenerateDictationExamAsync()`: picks one
  word via the existing `GetRandomUserWordIdAsync`, returns
  `{ WordId }` only (no word text). Credit-gated via
  `[ConsumeCredits(CreditCosts.DictationTest, ...)]`, a flat charge covering
  the one TTS call the user is expected to make.
- **`GET api/exam/dictation/audio?wordId=X`** → resolves `WordId` → `WordText`
  server-side, then reuses the existing `ToAudioResultAsync` helper verbatim.
  Unbilled, same "the flat parent charge already covers this" reasoning as
  every existing `*/audio` endpoint in this controller.
- **`POST api/exam/dictation/grade`** → `GradeDictationAsync(...)`: looks up
  the real `WordText` server-side (never trust a client-supplied answer),
  compares against the user's submission (trimmed, case-insensitive exact
  match for `IsCorrect`; a normalized Levenshtein-distance score for a
  purely informational "how close were you" `Score`). **Not credit-gated**
  — zero underlying cost, same reasoning as Writing's free `GET` endpoint.
  Returns `CorrectText` so the UI can reveal the answer post-submission.
- **`Common/CreditCosts.cs`**: add `DictationTest = 1` — one TTS clip per
  attempt (a single word, not `ListeningTest`'s four options), priced at
  raw `TextToSpeech` cost rather than `ListeningTest`'s bundled rate.
- **No `RecordExamResultAsync` change, no EF migration.** The client calls
  the existing `record-result` endpoint with `ExamType: "Dictation"` +
  `WordId` + the grading result's `Correct`/`Score` — already fully
  supported today.

### Reason
Every piece of this reuses an already-proven pattern (word-picking,
resolve-ID-to-audio, flat-charge-on-GET, word-anchored SM-2 feed) — the only
genuinely new code is the grading string-comparison itself, a small
self-contained helper with no external dependency. This is the lowest-cost,
lowest-risk item of the four, consistent with `REQUIREMENTS.md`'s own
framing and DECISION-003's cost discipline.

### Alternatives
- **Include `WordText` in the initial `GET` response**, matching Cloze/
  Translation's existing convention exactly: rejected — as explained in
  point 4 above, this would make the feature's core mechanic (guess from
  sound) trivially bypassable in a way that materially undermines the
  feature itself, not just a theoretical trust-model inconsistency.
- **Dictate a short AI-generated sentence built around the word** (closer to
  a "listen and transcribe a sentence" exercise) instead of a bare word:
  rejected for this pass — it would reintroduce an AI generation cost
  `REQUIREMENTS.md` explicitly didn't ask for ("Combines existing TTS...
  and simple string-diff grading — no new AI call needed"), and word-level
  dictation still closes the named gap (spelling-from-sound) on its own.
  Could be a future enhancement if word-level dictation turns out too easy/
  short to be engaging, not proposed as part of this task.
- **Add adjustable playback speed** (a related gap also named in
  `REQUIREMENTS.md`'s skill-coverage table, but not one of the four approved
  proposals): deliberately left out of `TASK-010`'s scope — flagged as a
  cheap, likely-worthwhile follow-up (the underlying TTS call already takes
  a `speed` parameter, used elsewhere for a slow/normal split) rather than
  bundled into this task without being asked for.

### Trade-offs
Word-level dictation (not sentence-level) is a narrower drill than "hear
natural speech and transcribe it" — it trains spelling-from-sound
specifically, not connected-speech listening comprehension. Accepted as the
correct scope for this pass per the Alternatives section above; flagged here
in case Boss wants a sentence-level version later.

### Impact
- `JackyAIApp.Server`: `GenerateDictationExamAsync()` +
  `GetDictationWordTextAsync()` + `GradeDictationAsync()` on
  `ExamService`/`IExamService`, three new DTOs, new `CreditCosts.DictationTest`.
  **No EF migration, no `RecordExamResultAsync` change.**
- `jackyaiapp.client`: new `Exam/DictationTestCard`, new `EXAM_MODES` entry,
  API slice additions.

See `TASK-010` (Backend) and `TASK-011` (Frontend, depends on TASK-010's
response shape). This closes out the originally-approved four-item
Skill-Building list (DECISION-005) once both merge.

---

## Decision: Finance ↔ Portfolio cross-link (Gap Audit item 1, DECISION-005)

### Context
PM's 2026-09-20 gap audit of Finance/Portfolio (`REQUIREMENTS.md` → both
modules' "Gap Audit (2026-09-20)" sections) found the same gap independently
from both sides: a Portfolio holding's detail view (`HoldingDetailDrawer`)
never links to Finance's fuller per-stock analysis for that same symbol,
even though both modules already share the same TW/US price-data providers
underneath. A user who owns a stock currently gets a *narrower* AI view of
it (`PortfolioAiSummary`, one persona, 2 credits) than a non-holder gets by
searching the same symbol directly in Finance (technical + chip + score +
weighted comprehensive analysis, and all four of those are **free,
unauthenticated, zero AI cost** — only Finance's own AI-trend tab is
credit-gated). PM flagged this as the cheapest item in both modules'
proposal lists — "a read-join of two existing services, no new AI calls, no
new infra" — and it is: investigated both sides before scoping and
confirmed there is no missing backend capability at all.

### Investigation
- Finance's quantitative endpoints (`GET api/finance/chip-analysis/{code}`,
  `GET api/finance/score/{code}`, `POST api/finance/comprehensive-analysis`)
  are already `[AllowAnonymous]`-by-default (no `[Authorize]` attribute) and
  already accept a bare stock code + optional `Market` (`TW`/`US`) —
  everything a cross-link needs already exists and needs zero backend
  changes.
- The Finance page (`Finance/index.tsx` + `useFinanceAnalysis.ts`) is pure
  client-side state today: no URL param is read on mount, search is
  triggered only by the user typing into `stockSearchTerm` and clicking
  search. So "link to Finance for symbol X" currently has nowhere to land —
  the receiving side is the only real gap, and it's frontend-only.
- `HoldingDetailDrawer` (`Portfolio/HoldingDetailDrawer.tsx`) already knows
  the exact `symbol`/`market` needed (`PositionDto.market` is literally
  `'TW'`/`'US'`, matching Finance's own `MarketRegion`/`detectMarket`
  convention) — no data-shape translation needed either.
- Confirmed via `PositionsTable.tsx` that the drawer only ever opens for
  `assetType === 'STOCK'` or `'FUND'` (with a symbol). Finance has no
  fund-analysis surface at all (its quant/AI endpoints are stock-only) — so
  the link must be gated to `STOCK` only, not shown for `FUND`.

### Decision
Frontend-only, no backend/DB change, no new endpoint:
1. `Finance/hooks/useFinanceAnalysis.ts` reads an initial `symbol`/`market`
   from the URL (`useSearchParams`), and when present: prefills
   `stockSearchTerm`, forces `analysisTab = TAB_QUANTITATIVE` (the free tab
   — a link must never silently land the user on a page that's one click
   from a credit charge), and auto-fires the same `handleStockSearch` path
   once on mount.
2. `Portfolio/HoldingDetailDrawer.tsx` adds a link, visible only when
   `position.assetType === 'STOCK'`, to `/finance?symbol=${symbol}&market=${market}`,
   opened in a new tab (`target="_blank" rel="noopener noreferrer"`, same
   pattern already used by `JobSearch/components/JobCard.tsx`) so the
   Portfolio page/drawer state isn't lost. Placed near `AiTechSummary` so a
   holder sees "here's my quick AI take" and "here's the full free
   quant breakdown" together.

### Reason
This is the correct minimum fix for the actual gap PM named (discoverability
— the richer analysis already exists and is already free, it's just not
reachable from a holding), not a new feature. A same-tab navigation was
considered and rejected: Portfolio holdings can take a while to load/scroll
to, and losing that state to view one stock's analysis is worse UX than a
new tab for a near-zero-cost feature.

### Alternatives considered
- **Embed Finance's comprehensive-analysis result directly inside the
  drawer** (fetch it in-place instead of linking out): rejected — Finance's
  quant tab already has a large, purpose-built layout
  (`ComprehensiveAnalysisResult` + `CategoryScoreCard`s + `IndicatorTable`s)
  that doesn't fit a 480px-wide drawer without either a second, thinner
  rendering (duplicate presentation logic, same drift risk flagged in the
  SEO Phase 2 decision above) or a cramped drawer. Linking out reuses the
  existing full-size presentation as-is.
- **Auto-run the AI-trend tab instead of the quantitative tab**: rejected —
  that tab is credit-gated; a link a user didn't ask to spend credits on
  must never pre-trigger a charge on load.

### Impact
- `jackyaiapp.client` only: `Finance/hooks/useFinanceAnalysis.ts` (read
  `useSearchParams`, auto-trigger once), `Portfolio/HoldingDetailDrawer.tsx`
  (new link, STOCK-only). No DTO/API contract change, no backend PR needed.

See `TASK-012` (Frontend only).

---

## Decision: Finance prediction-accuracy tracking — Gap Audit item 2

### Context
`REQUIREMENTS.md` → Finance's Gap Audit (2026-09-20) names proposal 2:
nothing in the app ever checks whether its own AI stock-trend calls
(`AnalyzeStockWithAIAsync`, `StockTrendAnalysis.{ShortTermTrend,
MediumTermTrend,LongTermTrend}` — bullish/bearish/neutral per 1-3mo/
3-12mo/1-3yr horizon) turned out to be right. PM's original framing
estimated "medium cost — new table + a scheduled job like
`CakeJobsProvider.cs`"; PM's 2026-09-21 message authorized Tech Lead to
pick this up next without waiting for a fresh Boss reply, same
"already-approved order, proceed by default" basis used throughout
Skill-Building. `TASK-012` (item 1, cross-link) is PR_OPENED
(`READY FOR BOSS REVIEW`, awaiting merge) — picking this up now keeps the
Gap Audit queue moving rather than stalling on that merge.

### Investigation (2026-09-22)
1. **`CakeJobsProvider.cs` is not a scheduled-job example — the original
   estimate's premise doesn't hold.** It's an `IJobProvider` (a
   fetch-on-demand data source for the JobSearch feature), invoked
   synchronously by `JobAggregatorService`, not run on any recurring
   schedule. A repo-wide search for `Hangfire`/`Quartz`/`BackgroundService`/
   `IHostedService`/cron-style infra found **none** — this codebase has no
   recurring-background-job mechanism anywhere today. Every existing
   "once per day" behavior (`MarketDigest`'s per-user daily charge gate,
   the exam bank growth-threshold checks) is implemented as a lazy,
   request-triggered check against `IExtendedMemoryCache` expiry
   (`FinanceQueryService.cs` — `_memoryCache.Set(chargeKey, true,
   DateTime.Today.AddDays(1) - DateTime.Now)`), not an actual timer/cron
   job. So the "new scheduled job" half of the original cost estimate is
   materially wrong: there's no existing pattern to reuse for it, and
   introducing a real `BackgroundService`/`PeriodicTimer` would be new
   infrastructure this codebase has deliberately avoided so far — a bigger
   cost than PM's original framing assumed.
2. **A lazy, request-triggered evaluation avoids that new infrastructure
   entirely**, consistent with point 1's observation that this is how
   every other "periodic" behavior in this codebase already works: persist
   each AI call's prediction at creation time (new table, cheap), then
   opportunistically evaluate a small batch of *due* (horizon-elapsed,
   unevaluated) predictions as a side effect of an already-frequent,
   already-authenticated-user-agnostic request — `market-digest` is the
   natural hook (hit once/user/day per its own existing cache-gate, so
   evaluation happens roughly daily in aggregate across all users, with no
   new endpoint or trigger). No user-facing latency risk: cap the batch
   (e.g. 5 predictions per digest call) and evaluate in a fire-and-forget
   background `Task.Run` the same way `MarketDigest` generation itself
   likely already tolerates being slightly stale — worst case, a handful
   of predictions sit unevaluated a bit longer under low traffic, which is
   fine for an internal accuracy metric with no user-facing SLA.
3. **Evaluation logic is a straightforward price-direction comparison.**
   `StockTrendAnalysis.CurrentPrice` already captures the price at
   prediction time. `Services/Finance/DataProviders` already has a market-
   agnostic historical-price abstraction (`IMarketDataProvider`, backed by
   `PersistentTwHistoricalProvider`/`ITWStockLibHistoricalProvider
   .FetchRangeAsync` for TW and `YahooFinanceMarketDataProvider` for US —
   the same providers Portfolio already uses for dividend-adjusted
   historical pricing) — fetch the closing price nearest the horizon date
   (`PredictedAt + 1-3mo` for short-term, etc.), compare against
   `CurrentPrice`, and classify the actual move as bullish/bearish/neutral
   using a fixed threshold (e.g. ±3% = neutral band, matching the same
   granularity the AI's own three-way `Trend` field already uses) — no AI
   call needed for evaluation itself, same "grading doesn't have to be an
   AI call" finding Dictation (item 4) already established.
4. **Long-term horizon (1-3yr) makes this a very slow-accumulating
   metric.** Short-term (1-3mo) predictions start becoming evaluable within
   weeks of shipping; long-term (1-3yr) predictions won't have any
   evaluated data for up to three years. This isn't a blocker (each
   horizon evaluates independently and short-term data is still useful
   quickly), but it's the reason a results **UI** is scoped as a separate
   Phase 2 below — a "track record" page with zero evaluated long-term
   predictions and only a handful of short-term ones for the first few
   months would show a near-empty, more-confusing-than-useful page at
   launch.

### Decision
Two phases, same reasoning as every prior Phase 1/Phase 2 split in this
document — ship the data-collection half now (no new infra, low cost);
treat the public-facing "here's our track record" UI as a follow-up once
enough evaluated data exists to be worth showing.

**Phase 1 — persist + lazily evaluate, proceeding now (see `TASK-013`,
Backend only):**
- New table `StockPrediction` (Id, StockCode, Market, UserId [who
  triggered the original `analyze-stock` call, for traceability — not
  used for access control, this is an internal accuracy metric], PredictedAtUtc,
  PriceAtPrediction, one row **per horizon** — i.e. a single `analyze-stock`
  call writes 3 rows, one each for short/medium/long — with
  `HorizonMonthsMin`/`HorizonMonthsMax`, `PredictedTrend`
  (bullish/bearish/neutral), `EvaluatedAtUtc` (nullable — null means not yet
  due or not yet evaluated), `ActualTrend` (nullable, same 3-way enum),
  `ActualPriceAtEvaluation` (nullable), `WasCorrect` (nullable bool —
  `PredictedTrend == ActualTrend`)). One row per horizon (not one JSON blob
  per call) so each horizon can be queried/evaluated independently without
  parsing.
- `AnalyzeStockWithAIAsync` (or its caller in `FinanceAnalysisService`)
  writes the 3 rows immediately after a successful AI call — pure
  persistence, no new AI cost.
- New `PredictionEvaluationService.EvaluateDuePredictionsAsync(int batchSize
  = 5)`: queries `StockPrediction` where `EvaluatedAtUtc IS NULL AND
  PredictedAtUtc + HorizonMonthsMin <= now`, orders by oldest-due first,
  takes `batchSize`, resolves each via `IMarketDataProvider`/
  `MarketDataProviderFactory` (existing market-detection + provider
  selection, no new provider code), computes `ActualTrend`/`WasCorrect`,
  writes the result. Hooked into `FinanceController`'s `market-digest`
  endpoint as a fire-and-forget call after the digest response is already
  built (never blocks the response the user is waiting on) — reuses an
  existing, already-hit-once-per-user-per-day endpoint instead of adding a
  new trigger surface or background service.
- No new endpoint, no UI, no credit-cost change (this is pure bookkeeping
  on an already-costed AI call, same "free half of an existing paid
  action" shape TASK-010's grading endpoint used).

**Phase 2 — a "track record" / accuracy display, deferred until Phase 1 has
been running long enough to have meaningful short-term data (recommend:
revisit ~6-8 weeks after Phase 1 ships, once a first batch of short-term
predictions has actually been evaluated) — not started, not blocking
anything:**
- Likely shape (not scoped as a task yet): an aggregate accuracy
  percentage per horizon (e.g. "短期預測準確率 68%（過去 45 次）"), shown
  either on the Finance AI-trend tab itself (builds trust exactly where a
  user is about to trigger a paid-in-usage-cap-sense call) or as a public
  page (also serves the SEO/trust-building growth angle named in PM's
  original proposal 2 rationale). Left open for a future Tech Lead pass
  once there's real data to design the UI around, rather than guessing at
  a page for zero rows today.

### Reason
Phase 1 reuses exactly the historical-price infrastructure Portfolio
already depends on and avoids introducing this codebase's first-ever
scheduled/background-job mechanism by piggybacking evaluation onto an
already-frequent, already-cheap endpoint instead — a real, if unglamorous,
DECISION-003-aligned cost saving over the original "new scheduled job"
framing. Phase 2 is deferred not for cost reasons but because there is
nothing meaningful to show yet — building a UI before there's data to
populate it would be the "half-finished implementation" this project's own
conventions avoid.

### Alternatives
- **A real `BackgroundService`/`PeriodicTimer` hosted job**: rejected for
  Phase 1 — would be new, first-of-its-kind infrastructure in this
  codebase for a feature with no latency requirement at all (a prediction
  being evaluated a day or two late than "exactly on the horizon date" is
  immaterial). Piggybacking on `market-digest` gets the same practical
  cadence (roughly daily, across the whole user base) for zero new
  moving parts. Could be revisited if/when this codebase adopts a real job
  scheduler for other reasons (e.g. if DECISION-006's usage-cap work ends
  up needing one) — not proposed here as the reason to introduce one.
- **Evaluate synchronously inside `analyze-stock` itself** (check the
  *previous* prediction for this stock/user when a new one is requested):
  rejected — ties evaluation cadence to how often a user happens to
  re-analyze the same stock, which could leave predictions unevaluated
  indefinitely for a stock nobody re-queries; the `market-digest` hook
  evaluates independently of any particular stock being re-searched.
- **Store one row per `analyze-stock` call with 3 horizon columns each**
  (JSON or 3x nullable-trend columns) instead of 3 rows: rejected — the
  evaluation query needs to filter/sort by "which specific horizon is due"
  across all predictions, which is far more natural as one row per horizon
  than parsing/filtering 3 embedded fields per call.

### Trade-offs
- Evaluation cadence is opportunistic (tied to `market-digest` traffic),
  not exact-to-the-day — acceptable given this is a trust/track-record
  metric, not a time-sensitive feature.
- No UI in Phase 1 means this ships genuinely invisible to users and to
  Boss for weeks/months before there's anything to look at — flagged
  explicitly so Boss doesn't expect a visible feature immediately, unlike
  every prior Skill-Building item which had user-visible output on day one.

### Impact
- `JackyAIApp.Server`: new `StockPrediction` entity + EF migration, new
  `PredictionEvaluationService`, `FinanceAnalysisService`/
  `FinanceController`'s `analyze-stock` path gains a persistence write,
  `market-digest` gains a fire-and-forget evaluation call. No DTO/contract
  change visible to the frontend, no new credit cost.
- `jackyaiapp.client`: none in Phase 1.

See `TASK-013` (Backend only). No frontend task yet — Phase 2's UI is
intentionally unscoped until there's data to design around.

---

## Decision: Multi-stock/sector peer comparison — Gap Audit item 3

### Context
`REQUIREMENTS.md` → Finance's Gap Audit (2026-09-20) names proposal 3:
render 2-3 stocks' `CategoryScoreCard`s side by side, reusing the existing
free `comprehensive-analysis` endpoint. PM flagged it as the cheapest
remaining item — "mostly frontend" — and approved it as next-up, unblocked,
no further Boss sign-off needed (`REQUIREMENTS.md`, PM 2026-09-22 entry).

### Investigation
- `POST api/finance/comprehensive-analysis` (`FinanceController.cs`,
  `FinanceQueryService.GetComprehensiveAnalysisAsync`) already takes a bare
  `StockAnalysisRequest { StockCode, Market, Include*, *Weight }` and is
  `[AllowAnonymous]` — it has no concept of "one stock per call" baked into
  its contract, it's just called once per code today. Calling it N times
  (once per compared stock) needs no backend change at all; each call is
  independently 12h-memory-cached by `{resolvedStockCode}_{currentDate}_{configHash}`
  already, so re-comparing the same stock across sessions costs nothing extra.
- `useFinanceAnalysis.ts`'s `runComprehensiveAnalysis(code, market)` (added
  for `TASK-012`'s cross-link) is already a single-call-site wrapper around
  `getComprehensiveAnalysis` — reusable as-is, called concurrently via
  `Promise.all` for each compared stock instead of once.
- `CategoryScoreCard` (`Finance/components/comprehensiveAnalysis/`) already
  renders one category's score/direction/summary from a bare `CategoryScore`
  prop with no dependency on being inside `ComprehensiveAnalysisResult`'s
  single-stock layout — reusable directly in a per-category comparison row.
- No existing "sector"/grouping data exists anywhere in the codebase (no
  sector/industry field on any stock model) — a true "sector comparison"
  (auto-pick peers) would need new data. Scoped down to what the proposal's
  own title also allows and what's actually cheap: **user-picked multi-stock
  comparison** (the user types 2-3 codes themselves), not automatic
  sector-peer discovery. This matches the proposal's "reuses the existing
  free endpoint, mostly frontend" cost framing — an auto-peer-discovery
  feature would not be cheap.

### Decision
Frontend-only, no backend/DB change, no new endpoint:
1. New Finance tab, "多股比較" (`TAB_COMPARISON`), free and unauthenticated
   like the existing quantitative tab — sits after "今日市場AI摘要" in the
   tab strip.
2. Up to 3 stock-code chips (add/remove), each resolved via the existing
   `detectMarket` heuristic (or an explicit market picker per chip, engineer's
   call) — no new market-detection logic.
3. On "比較", fire `runComprehensiveAnalysis`-equivalent calls concurrently
   (`Promise.all`, capped at 3) for each entered code, using the *default*
   `AnalysisConfig` (no per-stock weight customization in this first cut —
   `AnalysisConfigPanel` stays scoped to the single-stock tabs).
4. Render results grouped **by category, not by stock**: for each of
   Technical/Chip/Fundamental (whichever categories are present across the
   fetched results), one row of up to 3 `CategoryScoreCard`s side by side,
   one per compared stock, each card unmodified from its existing single-stock
   rendering. A small header per stock (name/code, reusing
   `OverallScoreSection`'s existing name+code+overall-score fragment, not
   the whole section) sits above the grid so the reader knows which column
   is which. A stock whose fetch fails is shown with an inline error in its
   own column/slot rather than aborting the whole comparison — the other
   1-2 stocks' results should still render.
5. No credit cost, no auth requirement — matches the tab it reuses.

### Reason
Category-row layout (not stock-column cards stacked per stock) is what
actually lets a reader compare stocks at a glance — "which of these 3 has
the best technical score" is one horizontal scan, matching the proposal's
own "render side by side" framing. Reusing `CategoryScoreCard` unmodified
keeps this to new composition, not new presentation components — the only
genuinely new UI is the multi-code input and the grouping-by-category
layout wrapper.

### Alternatives considered
- **Auto-discover sector peers and compare against them**: rejected for
  this pass — no sector/industry data exists anywhere in the schema; would
  require a new provider/data source, contradicting the "cheap, reuses
  existing free endpoint" framing PM scoped this at. Could be a later,
  separately-scoped enhancement (peer *discovery* on top of this
  peer *comparison* mechanism) if there's ever a reason to invest in
  sector metadata.
- **Reuse full `ComprehensiveAnalysisResult` per stock, stacked in columns**:
  rejected — that component renders a full indicator table per stock, which
  at 3 stocks wide would be an unreadably dense page; the category-row
  layout is deliberately a *summary* comparison, not three full reports
  side by side. A user who wants the full report for one stock still has
  the existing single-stock quantitative tab for that.
- **Let comparison run the credit-gated AI-trend tab per stock too**:
  rejected — same reasoning as `TASK-012`'s cross-link: nothing should
  silently multiply a credit charge across N stocks from what looks like a
  free comparison view.

### Trade-offs
- Fixed default `AnalysisConfig` (no per-comparison custom weights) is a
  deliberate scope cut to keep this a small, frontend-only task; per-stock
  weight customization inside a 3-wide comparison would also be a UX
  problem (which stock's weights win?) without a clear product need yet.
- User-picked stocks only, no auto sector-peer suggestion — see Alternatives.

### Impact
- `jackyaiapp.client` only: `Finance/hooks/useFinanceAnalysis.ts` (new
  comparison state + concurrent-fetch action), `Finance/index.tsx` (new tab),
  a new `Finance/components/comparison/` folder (multi-code input +
  category-row comparison grid, composed from existing `CategoryScoreCard`).
  No DTO/API contract change, no backend PR needed.

## Decision: Portfolio CSV import for holdings — Gap Audit item 2

### Context
`REQUIREMENTS.md` → Portfolio's Gap Audit (2026-09-20) names proposal 2: CSV
**import** to close the asymmetry with the existing CSV **export**
(`portfolioExport.ts`). PM's original framing claimed "the export schema
already matches `SaveHoldingsRequest`'s shape" and estimated this as mostly
frontend work reusing `PositionInput` + `SaveHoldingsAsync`'s existing
defensive row validation. Unblocked as the next default Tech Lead item per
the already-approved Finance/Portfolio Gap Audit priority order
(`REQUIREMENTS.md`, PM 2026-09-23 entry) — no further Boss sign-off needed.

### Investigation
- **Correction to PM's cost framing**: read `exportPortfolioCsv` in
  `Portfolio/portfolioExport.ts` directly — its schema does **not** match
  `PositionInput`. It's a computed report: `市值(TWD)`/`成本(TWD)`/
  `未實現損益(TWD)`/`損益%`/`估計賣出費用(TWD)`/`累計股息(TWD)`/`曝險倍數`
  are all derived, TWD-converted, or read-only fields with no `PositionInput`
  equivalent, and `成本(TWD)` in particular is the *converted* total cost —
  `PositionInput.CostBasis` is a total in `costCurrency` (the original,
  unconverted currency; confirmed via `CostBasisInput.tsx`'s per-unit-average
  UI, which operates on `costBasis / quantity` in the position's own
  currency). Round-tripping the export file verbatim would silently import
  wrong cost-basis numbers for any foreign-currency holding. Import needs its
  own purpose-built column schema instead — same kind of scope correction as
  the Finance sector-comparison item (PM's cost estimate was directionally
  right — this is still cheap, frontend-only — but the specific mechanism
  needs to change).
- `SaveHoldingsAsync` (`PortfolioService.cs`) already does defensive
  server-side validation on `SaveHoldingsRequest.Positions` (drops
  zero-quantity rows and symbol-less STOCK/FUND rows) — no backend change
  needed; imported rows ride the same save path as manually-entered ones.
- `HoldingsEditor.tsx` already works as a rows-in-state editor
  (`EditorRow[]`, one dialog, add/edit/remove, save-on-submit) fed from
  `editorTypes.ts`. Critically, `toRow(p: PositionInput): EditorRow` already
  exists and does exactly what CSV-import needs per row (attach a fresh
  `_key`) — no new row-construction helper required, just build
  `PositionInput[]` from parsed CSV rows and `.map(toRow)`.
- `StockRow.tsx`/`FundRow.tsx` both key their UI off `picked = !!row.symbol`
  — once `symbol` is non-empty they render as an already-selected chip and
  skip the live `useSearchStocksQuery`/fund-search Autocomplete entirely.
  This means an imported STOCK/FUND row with `symbol`+`name` already
  populated from the CSV renders correctly with **no dependency on the
  search API matching the imported symbol** — the row is trusted as-is, same
  as a row restored from `UpdateHistoryDialog`'s snapshot-load path
  (`loadFromSnapshot`, which also skips the search step).
- Existing per-row validation (`rowError()` in `editorTypes.ts`, surfaced via
  `error={submitted ? rowError(r) : null}` in every `*Row` component) is
  reusable as-is — imported rows land in `rows` state exactly like
  manually-added ones and get the same red-highlight-on-save-attempt
  treatment. No new validation UI needed.
- Found a reusable, already-written generic CSV/TSV parser:
  `DataAnalysis/components/UploadDataDialog.tsx` has local
  `detectSep`/`parseLine`/`parseTabular` functions (quoted-CSV-aware,
  comma/tab auto-detect, header-row-keyed output) built for an unrelated
  feature (ad-hoc data upload for `DataAnalysis`). Writing a second
  independent CSV parser for this task would duplicate that logic and risk
  the two silently drifting apart (same category of risk flagged for the SEO
  text formatter in `TASK-001`–`003`) — extract it into a shared util instead
  of copying it.

### Decision
Frontend-only, no backend/DB/DTO change:
1. **Extract the existing parser**: move `detectSep`/`parseLine`/
   `parseTabular` out of `DataAnalysis/components/UploadDataDialog.tsx` into
   a new shared `jackyaiapp.client/src/utils/tabularParse.ts` (pure
   functions, already have no external deps). `UploadDataDialog` imports
   from there instead of defining its own copy — behavior-preserving
   refactor, `DataAnalysis` gets no functional change. Portfolio's new
   import feature imports the same module. One parser, two callers.
2. **New purpose-built import column schema** (distinct from the export
   schema — see Investigation), documented as a downloadable template:
   `類型` (STOCK/CASH/FUND/LIABILITY/REAL_ESTATE or their `TYPE_LABEL`
   Chinese labels — accept either), `代號` (required for STOCK/FUND),
   `名稱`, `市場` (STOCK only: TW/US, defaults TW), `計價幣別`,
   `成本幣別` (optional, defaults to 計價幣別), `數量`, `成本` (optional —
   **total cost in 成本幣別, unconverted**, matching `PositionInput.CostBasis`
   exactly, not the export's TWD-converted figure), `估價方式` (REAL_ESTATE
   only).
3. **New "匯入 CSV" entry point** inside `HoldingsEditor`'s existing update
   dialog (button near the dialog title, alongside the per-section "新增"
   buttons) — opens a small file-picker sub-dialog: pick a `.csv` file →
   parse via the shared util → map each parsed row to a `PositionInput` per
   the schema above → validate (reuse `rowError`) → show a preview (row
   count, valid/invalid split, first few rows) → "加入" appends the mapped
   rows to the existing `rows` state (`.map(toRow)` then
   `setRows((rs) => [...rs, ...imported])`), same as if the user had added
   and filled each row by hand. Does **not** auto-save — the user still
   reviews/edits in the normal editor UI and clicks the existing "儲存"
   button, so all existing client- and server-side validation applies
   unchanged.
4. **"下載匯入範本" button** next to the import button: generates a small
   template CSV (headers + 1-2 example rows) via the existing `downloadCsv`
   helper in `portfolioExport.ts`, so a user knows the exact expected column
   names without guessing.
5. Invalid/unparseable rows (bad type, missing required field) are flagged
   in the preview and excluded from "加入" by default (or included but
   pre-flagged — engineer's call on the exact UX, as long as a bad row can
   never silently produce a wrong save); this mirrors `SaveHoldingsAsync`'s
   own "defensive, drop what's meaningless" posture rather than hard-failing
   the whole import over one bad line.

### Reason
Reusing `toRow`/`rowError`/the existing rows-state editor means imported
holdings go through exactly the same review-and-validate path as manually
typed ones — no second, parallel "import validation" system to keep in sync
with the editor's own rules. Keeping import as "populate the editor, don't
auto-save" (rather than a direct import → `SaveHoldingsAsync` call) means a
bad CSV can never silently overwrite a snapshot; the user always sees and
can correct what was parsed before it's persisted.

### Alternatives considered
- **Round-trip the existing export CSV format**: rejected — see
  Investigation; the export schema is a computed report, not an input
  schema, and treating `成本(TWD)` as `costBasis` would silently corrupt
  cost basis for every foreign-currency holding.
- **Direct import → `SaveHoldingsAsync` call, bypassing the editor UI**:
  rejected — skips all review/validation UX the manual-entry path already
  has; a parsing mistake would save straight to a snapshot with no chance to
  catch it first.
- **A second independent CSV parser inside Portfolio**: rejected — the
  generic tabular parser in `UploadDataDialog.tsx` already does everything
  needed (quoted-CSV, comma/tab detection, header-keyed rows); duplicating
  it is unnecessary drift risk for zero benefit.

### Trade-offs
- Import format is a new schema the user must learn (via the template
  download), not "just re-upload what you exported" — a real, if small, UX
  cost, but the only correct option given the export schema's computed
  fields (see Investigation). Flagged to Boss as informational, not asking
  for a decision — there is no cheaper correct alternative.
- STOCK/FUND rows imported by symbol are **not** validated against the live
  `useSearchStocksQuery`/fund-search results (see Investigation) — a typo'd
  symbol will save as-is rather than being caught at import time. Acceptable
  because the same is already true of `UpdateHistoryDialog`'s snapshot-load
  path; a follow-up could add an optional "re-verify against search" pass if
  this turns out to matter in practice, not scoped here.

### Impact
- `jackyaiapp.client` only: new `utils/tabularParse.ts` (extracted, shared),
  `DataAnalysis/components/UploadDataDialog.tsx` (import from the extracted
  util instead of its own local copy — behavior-preserving), `Portfolio/
  HoldingsEditor.tsx` (new import entry point), a new `Portfolio/
  csvImport.ts` (or similar — row-mapping/validation logic, unit-testable
  as pure functions per the project's existing `*.test.ts` convention for
  Portfolio helpers like `concentration.ts`/`exposureGauges.ts`), and an
  addition to `portfolioExport.ts` (or a sibling file) for the template
  download. No backend PR needed.

See `TASK-015` (Frontend only).

---

## Decision: Portfolio rebalancing suggestions against a target allocation — Gap Audit item 3

### Context
`REQUIREMENTS.md` → Portfolio's Gap Audit (2026-09-20) names proposal 3: no
rebalancing-suggestion feature exists despite the allocation data needed for
one already being computed elsewhere in the app (`ConcentrationCard`,
`groupBy=category`). PM handed this to Tech Lead 2026-09-25 as the next
default item in the already-approved priority order (cross-link → CSV
import → **rebalancing** → FIRE tracking), no fresh Boss sign-off needed —
DECISION-006's free-usage-cap question remains the only item still waiting
on Boss and does not block this work.

### Investigation (2026-09-26)
1. **The two things `REQUIREMENTS.md` pointed at are not the same
   mechanism.** `ConcentrationCard.tsx`/`concentration.ts` compute
   per-*holding* concentration (each STOCK/FUND position's % of total
   holdings market value) — that's individual-position risk, not
   asset-class allocation. The actual `groupBy=category` logic lives
   server-side in `PortfolioService.GetNetWorthHistoryAsync`
   (`GET api/portfolio/net-worth-history?groupBy=category`), and it returns
   a **daily time series** of category buckets, not a single current-
   allocation snapshot — heavier than this feature needs, and category
   (`PortfolioCategory`, 16 sector-level values: Semiconductor, Financials,
   Healthcare, etc.) is a finer grain than most users think in terms of for
   a rebalancing target.
2. **`PortfolioSummaryDto` (`GET api/portfolio/summary`) already has
   everything needed for an asset-type-level target, with zero backend
   change.** It already returns `TwStockTWD`, `UsStockTWD`, `CashTWD`,
   `FundTWD`, `RealEstateTWD` (each a current TWD market value) plus
   `LiquidGrossAssetsTWD` (= the first four summed) and `LiabilityTWD`
   (debt, already excluded from the other five). Total assets for a
   rebalancing pie = `LiquidGrossAssetsTWD + RealEstateTWD` — a one-line
   client-side sum, not a new endpoint.
3. **No target/goal concept exists anywhere in `Data/Models/Portfolio.cs`**
   (confirmed) — expected, and not something this feature needs to add to
   the database (see next point).
4. **There's already a direct precedent for a purely client-side, per-
   browser "preference" number in this exact module**:
   `Portfolio/feeDiscountRate.ts` stores one number
   (`portfolio.feeDiscountRate`) in `localStorage` via the shared
   `utils/storage.ts` wrapper, with an explicit comment that it's "a
   per-browser preference, not account data" and so doesn't need a backend
   table. A rebalancing target is the same category of thing — the user's
   own preference, not data other users or devices need to see, and not
   something that needs to survive a browser wipe with any guarantee.

### Decision
Frontend-only, zero backend/DB change, modeled directly on
`feeDiscountRate.ts`'s existing localStorage-preference pattern:

- **Allocation buckets = the 5 fields `PortfolioSummaryDto` already
  returns** — TW Stock, US Stock, Fund, Cash, Real Estate. Not sector
  `PortfolioCategory` (16 buckets, needs the heavier time-series endpoint,
  and is a finer grain than "how much of my money is in stocks vs. cash"
  which is what a rebalancing target is normally about). **Liability is
  excluded from the buckets** — debt isn't something you allocate *into*,
  it's a reduction of net worth; the pie's denominator is gross assets
  (`LiquidGrossAssetsTWD + RealEstateTWD`), matching how `PortfolioDto.cs`'s
  own comments already frame `LiquidGrossAssetsTWD` as "NOT net of
  liability."
- New `Portfolio/rebalancingTarget.ts`: `loadRebalancingTarget()` /
  `saveRebalancingTarget()` around a `localStorage` key
  (`portfolio.rebalancingTarget`, same `readJson`/`writeJson` helpers
  `utils/storage.ts` already provides), storing `{ twStock, usStock, fund,
  cash, realEstate }` target percentages. Pure functions
  `computeCurrentAllocation(summary)` (bucket → current % and current TWD,
  from `PortfolioSummaryDto` alone) and `computeRebalancingDeltas(current,
  target, totalAssetsTWd)` (bucket → `{ currentPct, targetPct, deltaTWD }`,
  positive `deltaTWD` = buy more, negative = sell/reduce) — unit-testable
  in isolation, same convention as `concentration.ts`/`exposureGauges.ts`.
- New `Portfolio/RebalancingCard.tsx`: an editable target-% input per
  bucket (5 number fields, sum shown live with a warning styling — not a
  hard block — when it doesn't sum to ~100%, since a deliberately
  under/over-100% target, e.g. "I want to raise cash and haven't decided
  where the rest goes," is a legitimate use, not an error), a bar/row per
  bucket showing current % vs. target % and the buy/sell delta in TWD,
  and a "重設為目前配置" (reset to current allocation) button. First-run
  default (no saved target yet): seed the target inputs with the
  *current* allocation, not zeros — so a first-time viewer sees "already
  balanced, 0 delta everywhere" rather than a wall of red "sell 100%"
  values before they've expressed any actual preference.
- Placement: a new card on the Portfolio summary page, near
  `ConcentrationCard`, since both are "how is my money spread out" cards —
  not inside `HoldingDetailDrawer` (that drawer is per-holding, this is
  whole-portfolio).

### Reason
Every number this feature needs already exists in the `/summary` response
returned on every page load; the only genuinely new state is the user's own
target %, which is exactly the kind of per-browser preference this codebase
already has a working, deliberately-not-backed-by-a-table pattern for
(`feeDiscountRate.ts`). No new AI call, no new endpoint, no new DB table —
the cheapest of the remaining Gap Audit proposals, consistent with
DECISION-003's cost discipline and this Gap Audit's own established
"proceed by default" sequencing.

### Alternatives
- **Model the target on `PortfolioCategory` (sector-level)**: rejected for
  this pass — needs the heavier `groupBy=category` time-series endpoint
  (fetched for one day just to read the last point), 16 buckets is a lot of
  manual target-setting for a first version, and REAL_ESTATE/manually-priced
  holdings don't cleanly map to a sector category anyway. Could be a future
  finer-grained mode once the asset-type version proves useful — not
  proposed as the default here, same "start with the coarser, cheaper cut"
  judgment call already used for the multi-stock comparison item (user-
  picked codes over auto sector-discovery).
- **Persist the target server-side** (new column/table on a per-user
  settings entity): rejected — no user-settings table exists anywhere in
  this app today, and `feeDiscountRate.ts` already established that a
  personal display/planning preference like this doesn't need one. Would
  also be the first "user preferences" table in the schema for a feature
  that doesn't need cross-device sync to be useful.
- **A hard validation block preventing save unless targets sum to
  exactly 100%**: rejected — a partial/under-100% target (e.g., "I know I
  want less in stock, haven't decided the destination yet") is a reasonable
  intermediate state; a soft visual warning communicates the same
  information without blocking a legitimate use.

### Trade-offs
The target is per-browser (`localStorage`), not per-account — clearing
browser data or switching devices loses it, same limitation
`feeDiscountRate.ts` already accepts for a similar kind of preference.
Acceptable for a planning aid the user can re-set in a few seconds; flagged
here in case Boss wants it to persist server-side once there's a real
user-settings table for some other reason.

### Impact
- `jackyaiapp.client` only: new `Portfolio/rebalancingTarget.ts` (pure
  compute + localStorage load/save, unit tested), new
  `Portfolio/RebalancingCard.tsx`, wired into the Portfolio summary page
  near `ConcentrationCard`. No backend/DTO/migration change.

See `TASK-017` (Frontend only).

---

## Decision: Portfolio FIRE/goal tracking — Gap Audit item 4 (last item)

### Context
`REQUIREMENTS.md` → Portfolio's Gap Audit (2026-09-20) names proposal 4: no
FIRE/goal-tracking feature exists (target net worth, projected years-to-goal)
despite the XIRR/net-worth data needed for a projection already existing.
PM handed this to Tech Lead 2026-09-26 as the last item in the
already-approved priority order (cross-link → CSV import → rebalancing →
**FIRE tracking**), no fresh Boss sign-off needed — DECISION-006's
free-usage-cap question and the scheduling-layer duplicate-PR question
remain the only items still waiting on Boss and do not block this work.

### Investigation (2026-09-27)
1. **Everything a simple forward projection needs is already in
   `PortfolioSummaryDto` (`GET api/portfolio/summary`), the same response
   `RebalancingCard`/`ConcentrationCard` already consume — zero backend
   change.** `TotalNetWorthTWD` is the current net worth (net of liability).
   `AnnualizedReturnPct` is a money-weighted XIRR over the user's *entire*
   snapshot history (not the 180-day-clamped window `net-worth-history` uses
   for its chart/groupBy modes — confirmed by reading
   `ComputeAnnualizedReturn`/`GetNetWorthHistoryAsync` directly), including
   dividends — exactly the "assumed growth rate" a FIRE calculator needs as
   a sensible default. It is nullable: null until a user has ≥30 days of
   snapshot history (`MinXirrHistoryDays`), by deliberate design (an
   annualized rate from too short a window is misleading, per the existing
   code comment) — the UI must handle this null case, not treat it as zero.
2. **`net-worth-history`'s 180-day clamp is irrelevant here** — a first
   instinct might be to chart historical growth to project it forward, but
   that endpoint is capped at 180 days server-side
   (`Math.Clamp(days, 7, 180)`) specifically for its groupBy/category
   breakdown mode, and a 6-month trailing window is a poor basis for a
   multi-year compounding assumption anyway. The already-computed
   all-history XIRR (`AnnualizedReturnPct`) is the correct number to build
   on, not a new chart-derived rate.
3. **No target/goal concept exists anywhere in `Data/Models/Portfolio.cs`**
   (confirmed, same as the rebalancing decision above) — expected, and for
   the same reason as `TASK-017`, this doesn't need a new database table.
4. **Same direct precedent as `TASK-017`**: `Portfolio/feeDiscountRate.ts`
   and now `Portfolio/rebalancingTarget.ts` both already establish that a
   personal planning input (a number the user sets and expects to see again
   on their own browser, not account data other users/devices need) belongs
   in `localStorage` via `utils/storage.ts`'s `readJson`/`writeJson`, not a
   new backend table. A FIRE target net worth is the same category of thing.
5. **A monthly-contribution input is a genuinely new concept, not derived
   from existing data** — unlike the growth rate (from `AnnualizedReturnPct`)
   and current net worth (from `TotalNetWorthTWD`), this app has no
   "planned future savings rate" anywhere (it's a snapshot tracker, not a
   budgeting tool). Including it makes the projection meaningfully more
   useful (most real FIRE plans assume ongoing contributions, not a
   lump-sum-only compounding), and it costs nothing extra to add as a third
   plain user-input number alongside target net worth and (optionally)
   growth rate override — not a reason to cut it, just calling out that it's
   the one number in this feature with no data source behind it at all.

### Decision
Frontend-only, zero backend/DB change, same localStorage-preference pattern
as `TASK-017`:

- **`Portfolio/fireTarget.ts`** (new, pure functions + localStorage, unit
  tested): `FireTarget = { targetNetWorthTWD: number; annualGrowthRatePct:
  number; monthlyContributionTWD: number }`. `loadFireTarget()`/
  `saveFireTarget()` around `localStorage` key `portfolio.fireTarget`, via
  `utils/storage.ts`'s existing helpers. `annualGrowthRatePct` defaults to
  `summary.annualizedReturnPct` when available and no saved target exists
  yet (same "seed from real data on first view" pattern `RebalancingCard`
  established), or a clearly-labeled placeholder (e.g. `6`, annotated in the
  UI as "尚無足夠歷史資料計算實際報酬率，請自行輸入預估年化報酬率") when
  `annualizedReturnPct` is null — never silently treat null as 0%, which
  would show an impossibly pessimistic "永遠達不到" result.
- **`computeYearsToGoal(currentNetWorthTWD: number, target: FireTarget):
  number | null`**: simulates year-by-year (not a closed-form log formula,
  because the monthly-contribution term makes the closed form awkward and a
  simple loop is easy to verify/test) — each year, apply
  `annualGrowthRatePct` to the running balance, then add
  `monthlyContributionTWD * 12`; stop and return the elapsed years once the
  balance reaches `targetNetWorthTWD`, capped at e.g. 100 years (returns
  `null` beyond the cap — "not reachable at this rate/contribution", not a
  false precise-looking number). Returns `0` immediately if
  `currentNetWorthTWD >= targetNetWorthTWD` already.
- **`Portfolio/FireCard.tsx`** (new component): target net worth input,
  growth-rate input (seeded from `AnnualizedReturnPct` when available,
  editable either way — this is a forward-looking assumption, not a fact,
  so the user must always be able to override it even when a real number is
  available), monthly-contribution input (defaults to 0 — lump-sum-only
  projection if the user doesn't fill it in), and the computed result
  ("預估 N 年後達成目標" / "已達成目標" / "此假設下無法在 100 年內達成" for
  the three outcome cases). Persists to `localStorage` on every edit, same
  as `RebalancingCard`.
- **Wiring**: add `FireCard` to the Portfolio summary page, near
  `RebalancingCard` (both are forward-looking planning cards, distinct from
  the backward-looking history/analysis cards above them). Reads the same
  already-fetched `PortfolioSummaryDto` — no second `/summary` request.

### Reason
Every number with a real data source behind it (current net worth,
historical annualized return) is already computed and already returned by
an endpoint the page fetches on every load; the two genuinely new pieces
(target net worth, monthly contribution) are exactly the kind of
per-browser planning input this codebase already has a working,
deliberately-not-backed-by-a-table pattern for. No new AI call, no new
endpoint, no new DB table — matches the cost discipline and sequencing this
entire Gap Audit has used throughout (DECISION-003), and closes out
Portfolio's originally-prioritized 4-item Gap Audit list once shipped.

### Alternatives
- **A closed-form `t = ln(target/current) / ln(1+rate)` formula, no
  contribution term**: rejected — cheaper to compute but structurally can't
  represent ongoing contributions, which most real "how many years until
  FIRE" plans assume; the year-by-year simulation loop handles both a
  pure-lump-sum projection (`monthlyContribution = 0`, which degenerates to
  the same answer the closed form would give) and a contribution-aware one
  with one implementation, and is easy to unit-test against hand-computed
  cases.
- **Persist the target server-side** (new user-settings table/column):
  rejected for the same reason `TASK-017` rejected it — no user-settings
  table exists anywhere in this schema, and this planning input doesn't need
  cross-device sync to be useful.
- **Derive the growth-rate default from `net-worth-history`'s recent window**
  instead of `AnnualizedReturnPct`: rejected — that endpoint's 180-day clamp
  makes it a worse basis for a multi-year assumption than the all-history
  XIRR the summary endpoint already computes; reusing the existing number is
  also zero additional computation, versus fetching and processing a second
  endpoint's response just to derive a rate.
- **Silently default a null `AnnualizedReturnPct` to some fixed assumed rate
  (e.g. 6%) without telling the user**: rejected — a new user with under 30
  days of snapshot history would see a rate that looks like it came from
  their own data but didn't; the UI must visibly distinguish "your own
  historical rate" from "a placeholder you should edit."

### Trade-offs
- The year-by-year simulation assumes one constant growth rate and one
  constant monthly contribution for the entire projection horizon — no
  modeling of contribution escalation, sequence-of-returns risk, or
  inflation-adjusted targets (real FIRE calculators often support these).
  Accepted as the correct scope for a first version, consistent with this
  Gap Audit's pattern of shipping the coarser, cheaper cut first
  (rebalancing's asset-type buckets over sector buckets; multi-stock
  comparison's user-picked codes over auto sector-discovery) — flagged here
  in case Boss wants a richer model later.
- Like `TASK-017`, the target is per-browser (`localStorage`), not
  per-account — same accepted limitation, same precedent.

### Impact
- `jackyaiapp.client` only: new `Portfolio/fireTarget.ts` (pure compute +
  localStorage load/save, unit tested), new `Portfolio/FireCard.tsx`, wired
  into the Portfolio summary page near `RebalancingCard`. No backend/DTO/
  migration change.

See `TASK-018` (Frontend only). Once shipped, all four of Portfolio's
originally-prioritized Gap Audit proposals (cross-link, CSV import,
rebalancing, FIRE tracking) will be complete, matching Finance's
already-complete 3-item list.


## Workflow conventions (Boss authorized 2026-09-30, Issue #387)

- **Claiming a task:** an Engineer sets `Status: IN_PROGRESS` on the TASK file and pushes it directly to master *before* starting implementation. GitHub assignee is not used (all Agents share one account). A small race window remains; if a task is already `IN_PROGRESS`, pick another.
- **Boss questions:** new questions to Boss go in GitHub Issues (labels `needs-boss` / `pm` / `tech-lead`), per DECISION-008. `boss/*.md` keeps only an open-items summary.


## Credits & Payments 移除設計（DECISION-006/007，Tech Lead 2026-10-01）

**目標**：拆除 Stripe/PayPal/ECPay/PayUni、點數餘額、訂閱；以「每人每日用量預算」取代扣點，使用者完全看不到金錢/點數概念。

### 設計決定
- **單一加權每日預算，不做 per-feature 計數表。** 沿用 `CreditCosts` 現有權重（Cloze 2、Chat 5、Stock 5…）改名 `UsageCosts`，每位使用者每天（UTC 日）共用一個預算 `UsageLimits:DailyBudget`（appsettings，預設 **60**，約等於舊制每日可得 ~65 點）。理由：一列/人/天、一次原子更新、單一設定值即可，且權重已反映 AI 成本；per-feature 表要為十幾個功能各訂數字且維護成本高。
- **儲存**：新表 `DailyUsage(UserId, Date[UTC date], Used ulong)`，PK = (UserId, Date)。扣用量用單一條件式 `UPDATE ... SET Used = Used + @cost WHERE Used + @cost <= @limit`（列不存在則先 insert-if-not-exists），受影響列數 0 = 超量；避免 read-modify-write race。**不用 in-memory**（重啟/多 instance 會失效）、**不用 `AiUsageLog`**（fire-and-forget、快取命中不記，不可靠）。
- **個別提高上限**（作者收到信後）：`User.UsageLimitMultiplier`（decimal，預設 1）。由作者直接改 DB，不做 Admin UI（Out of Scope）。
- **介面**：`IUsageLimitService { Task<bool> TryConsumeAsync(userId, cost, feature); Task RefundAsync(userId, cost, feature); }` + `[UsageLimit(cost, feature)]` filter（取代 `[ConsumeCredits]`/`ICreditService.ConsumeCreditsAsync` 的所有呼叫點；chatbot 失敗退款改 `RefundAsync`）。呼叫點與扣用量時機維持不變（行為等價，只換機制）。
- **超量回應契約**：HTTP **429**，`error.code = UsageLimitExceeded`（新增 `ErrorCodes.UsageLimitExceeded = 1042900`），message 為中性英文佔位（不含 credit/pay 字樣）。**文案由前端持有**：「您已超出目前的使用量，如果您是重度使用者，請聯絡作者 jacky19918@gmail.com 幫您調高上限」。前端依 `error.code` 判斷，不再 parse message 字串。
- **移除**：每日登入獎勵、挑戰/連勝點數獎勵（XP/streak 保留）、新用戶 50 點初始贈與、`DailyChallengeSubmitResponse.CreditsAwarded`、`/credits` `/pricing` 頁、`InsufficientCreditsModal`、`UserProfileMenu` 餘額/Buy Credits、`creditApis/paypalApis/ecpayApis`。
- **DB 清理（不可逆）**：`CreditTransactions`、`Subscription` 表與 `User.CreditBalance/TotalCreditsUsed/CreditLastUpdated` 欄位的 DROP 獨立成最後一個任務，需 Boss 確認後才做；前面任務只讓程式碼不再引用，欄位保留（`required` 欄位需改可空或給預設，避免 insert 失敗）。無真實金流入帳，無退款義務（REQUIREMENTS 已記載）。

### 部署順序（每次合併即部署）
TASK-021（後端新機制，與舊機制並存）→ TASK-022（前端移除 UI + 改吃新契約）→ TASK-023（後端刪除金流/Credit 程式碼）→ TASK-024（DROP schema，待 Boss）。021→022 之間舊前端遇到 429 只會顯示通用錯誤，視窗很短，可接受。


## Decision: 「回落煞車」加減碼提醒（0050 + 00631L，Issue #431 / DECISION-013，Tech Lead 2026-10-10）

### Context
規格（Boss，#431）與產品面（PM，`REQUIREMENTS.md`「0050 + 00631L『回落煞車』…」DRAFT v1）已定；固定參數、適用範圍與禁止項見 DECISION-013，**Agent 不得調整**（`DD_TRIGGER=0.08`、`REENTER_SMA=60`、`LOOKBACK_HIGH=252`）。PM 分兩期：第一階段＝收盤後正式訊號＋目標配置＋加減碼股數（站內顯示）；第二階段＝盤中預估＋「訊號已更正」＋站外提醒。Q1（對誰開放）、Q2（提醒送達方式）待 Boss；**Q1 未決前功能預設只有 admin 看得到**。

### Investigation (2026-10-10)
1. **報酬指數資料目前完全不存在**：後端無任何 `MFI94U`／報酬指數取得程式（grep 無結果）。TWStockLib 只用於個股日線與即時報價。本排程沙箱的 egress policy 擋掉 `www.twse.com.tw`／`mis.twse.com.tw`（HTTP 403），**MFI94U 與 MIS 回傳格式尚未實測驗證** → 以 LiveContract 測試（GitHub Actions 可連證交所）作為驗證手段，解析器不得「猜欄位」。
2. **持股與價格不需新端點**：`GET api/portfolio/summary` 的 `positions[]` 已含 `symbol/market/quantity/currentPrice/marketValueTWD`（交易時段已疊加即時價），`effectiveDate` 為快照日。
3. **訊號是全站共用的市場資料**（與使用者無關）：一份資料、一次計算、所有人共用；使用者相關的只有「風險等級／策略現金／持股」。
4. **Portfolio 完全沒有股票分割處理**（grep `split`/`分割` 無結果）：`Quantity` 為使用者輸入，市值＝數量×最新價。分割前建立的快照會讓 0050（2025-06-18，1 拆 4）、00631L（2026-03-31，1 拆 22）的市值被嚴重低估，建議股數隨之大錯（PM 的 R2 屬實）。此為 Portfolio 既有限制，非本功能造成。
5. 可沿用的既有模式：背景刷新 `TwListedCompanyRefreshService`（每小時檢查、`TimeProvider` 可測）、LiveContract 測試（`LiveContracts/`）、localStorage 偏好（`rebalancingTarget.ts`／`feeDiscountRate.ts`）、`User.IsAdmin`（作者直接改 DB，無 admin UI）。

### Decision
**後端提供「市場訊號」，前端依使用者持股計算「個人建議」。** 零使用者設定表、零 AI、零 UsageLimit。

**後端（TASK-036，第一階段；TASK-038，第二階段）**
- 新表 `TaiexTotalReturnDaily(TradeDate PK, Close decimal(18,4))`，全站共用、只存已收盤的正式值（同 `DailyStockPrice` 精神，不存盤中值）。不重用 `DailyStockPrice`（避免把指數混進個股表）。
- `TaiexTotalReturnClient`：打 `rwd/zh/TAIEX/MFI94U?date=YYYYMM01&response=json`，**請求間隔 ≥ 3 秒**（Boss 規定 ≥ 2 秒，留餘裕）、全域單一 in-flight、依表頭文字（含「報酬指數」）定位欄位，找不到唯一欄位就失敗並記 log，**不寫入**。解析民國年、千分位、`--`。
- `TaiexTotalReturnRefreshService : BackgroundService`（仿 `TwListedCompanyRefreshService`）：每小時檢查；表空 → 回補近 3 年（約 36 次請求、約 2 分鐘，背景進行）；平日 14:00（台北）後若最新一筆 < 今日 → 抓當月（缺口則自缺口月起逐月補）。失敗吞掉、下小時重試；請求遇非 JSON／非預期格式即停止本輪（避免被證交所封 IP，影響既有 TW 報價）。
- `DrawdownBrakeCalculator`（純函式，**每次從整個序列從頭重算**，不存狀態）：`state=OFFENSE` 起算；`up = sma60 != null && close > sma60`；`down = close/high252 - 1 < -0.08`（嚴格小於）；`up→OFFENSE`、`else down→DEFENSE`、否則維持。`high252` 不足 252 日用現有資料（對應參考實作 `min_periods=1`）。
- 端點 `GET /api/portfolio/drawdown-brake`（`[Authorize]`，另受功能開關限制）。
- **功能開關（Q1 未決前預設關閉）**：設定 `Features:DrawdownBrake:Audience` = `Admin`（預設）｜`All`。端點對不符資格者回 403（新增 `ErrorCodes`，不洩漏資料）；使用者資訊回應（與 `isAdmin` 同處，`accountApis/type.ts`）新增 `drawdownBrakeEnabled`（= `IsAdmin` 或 Audience=All），前端據此決定是否掛載卡片。Boss 決定 Q1 後只需改 App Setting（`Features__DrawdownBrake__Audience=All`），**不需改碼重新部署**。

**API 契約（前後端共同遵守；數值百分比一律用 `...Pct`，與既有 DTO 一致）**
```
GET /api/portfolio/drawdown-brake  → DrawdownBrakeDto
{
  "asOfDate": "2026-10-08",              // 已用到的最新正式資料日
  "state": "OFFENSE" | "DEFENSE",        // asOfDate 收盤後的正式狀態
  "prevState": "OFFENSE" | "DEFENSE" | null,   // asOfDate 前一個交易日狀態（供 advise 判斷「切換」）
  "lastSwitchDate": "2026-05-12" | null,
  "close": 0, "high252": 0, "sma60": 0 | null,
  "drawdownPct": -1.2,                   // close/high252 - 1，百分比
  "distanceToTriggerPct": 6.8,           // drawdownPct 距 -8 的空間；< 0 表示已低於觸發線
  "isStale": false,                      // 應有新資料卻沒有（休市日不算）
  "isInitializing": false,               // 表為空／回補中，此時 state 等欄位為 null
  "estimate": null,                      // 第二階段（TASK-038）前恆為 null
  "estimateCorrected": false,            // 第二階段：先前預估狀態與正式狀態不同
  "lastCheckedAt": "2026-10-11T07:05:00+00:00" | null,  // UTC；伺服器最後一次成功向證交所取得可解析回應（背景或手動），記憶體保存，重啟後到第一次成功前為 null（TASK-041）
  "generatedAt": "2026-10-11T07:06:00+00:00"            // UTC；這份訊號的計算時間（60 秒快取填入時間）（TASK-041）
}
// estimate（第二階段）: { "asOfTime": ISO8601, "estimatedClose": 0, "state": ..., "prevState": ..., "drawdownPct": 0 }
```

**前端（TASK-037）**
- `Portfolio/drawdownBrake.ts`：純函式 `composition(E,k)`、`advise(prevState,state,profile,holdings,prices,band=0.10)`（逐字依 #431 §3、§4；四捨五入用 half-away-from-zero，**不用** Python 的銀行家捨入／`Math.round` 對負數的 half-up）、`loadBrakeSettings/saveBrakeSettings`（localStorage `portfolio.drawdownBrake`：`{ profile, defenseE: 0|0.3, minCashRatio?, strategyCashTWD }`，同 `rebalancingTarget.ts` 先例）。
- `Portfolio/DrawdownBrakeCard.tsx`：掛在 `RebalancingCard` 附近；**僅在 `drawdownBrakeEnabled` 為 true 時掛載並呼叫端點**（`skip`），否則不產生請求。
- 持股取自 `summary.positions`（`assetType==='STOCK' && market==='TW' && symbol ∈ {0050, 00631L}`）；現金＝使用者在卡片內輸入的 `strategyCashTWD`（PM 假設 A1，待 Q3 確認；介面可並列顯示其現金部位合計供參考，但不自動採用）。
- **股票分割硬性防護（PM R2 的技術回應）**：前端常數表 `SPLIT_EVENTS = { '0050': {date:'2025-06-18', ratio:4}, '00631L': {date:'2026-03-31', ratio:22} }`（來源：Boss #431，PM 未驗證）。若 `summary.effectiveDate` 早於該標的有持股的分割日 → **不顯示買賣股數**，改顯示「你的持股快照建立於 {標的} 分割（{日期} 1 拆 {N}）之前，股數可能未調整，請先更新持股」。另加軟性提示：快照距今 > 7 天（PM 假設，待 Boss 確認）顯示「持股資料可能已過期，請先更新持股」。

**第二階段（TASK-038，BLOCKED：待 TASK-036 合併；站外提醒待 Q2，另案）**
- MIS `tse_t00.tw`：`預估報酬指數 = 昨日正式報酬指數 × (z ÷ y)`（`z`＝目前加權指數、`y`＝MIS 回傳的昨收，不另需價格指數序列）；以同一 `DrawdownBrakeCalculator` 對「正式序列＋預估點」重算。今日正式資料入庫後 `estimate=null`。
- MIS 需先驗證格式與是否要先取 cookie；先查 TWStockLib 的 `GetLatestQuoteAsync` 是否能直接取指數，否則自建 client（走 `ExternalReadOnly`）。結果短快取（≈30 秒），一份全站共用，不隨使用者數放大請求。
- 「訊號已更正」：記憶體內（`IMemoryCache`，TTL 2 天）記當日最後一次預估狀態；正式資料入庫後比對，不同則 `estimateCorrected=true`。遺失（重啟）時退化為不顯示，可接受（單 instance、提示性質）。

### Reason
- 訊號是共用市場資料，算一次存一份最便宜、也最容易測；個人建議只需持股＋兩個偏好，沿用既有 localStorage 偏好模式即可，**不必為此新增使用者設定表**。
- 前端純函式＋後端純函式各自有 §9 的單元測試，語意與 Boss 的 Python 參考實作一一對應。
- 用 admin 旗標＋App Setting 做開關，是最小且可逆的機制，直接滿足 PM「Q1 未決前一般使用者看不到」。
- 回補 3 年（≥ 規格要求的 2 年）而非 2003 起：避免第一次部署時對證交所爬約 280 個月份、也避免佔用 IP 額度而拖累既有 TW 報價；遲滯狀態在任一 `up` 日即重置，3 年窗口與 2003 起算的結果只在「3 年內從未出現 up」時才可能不同，實務上不會發生（以測試證明，見 TASK-036）。

### Alternatives
- **後端算完整個人建議＋新增使用者設定表**：否決（第一階段不需要；多一張表、一套 CRUD；站外提醒真的要做時（Q2）再評估）。
- **每次請求即時打證交所**：否決（≥2 秒間隔 × 36 個月份，且對上游放大）。
- **內嵌 2003 起 CSV 當種子**：否決作為正式機制（維護一份大型靜態資料）；但 Boss 的回測 CSV 作為**測試 fixture** 是必要的（見 TASK-036）。
- **Yahoo Finance `^TWII` 當訊號**：否決（Boss 明定須為報酬指數；Yahoo 的 0050/00631L 資料也被 Boss 指為分割調整錯誤）。
- **獨立 Python 服務重用 Boss 的參考實作**：否決（新增部署單元；邏輯僅幾十行，C#／TS 重寫並以同一組驗收測試對照即可）。

### Trade-offs
- 風險等級／策略現金存 per-browser localStorage：換裝置或清除瀏覽器資料會遺失；站外提醒（Q2）若要做必須改為伺服器端儲存（屆時再開 migration 與 task）。
- MIS 預估用價格指數漲跌幅近似報酬指數，**除息旺季（約 6–8 月）會略低估當日報酬**；訊號貼近門檻時預估與正式值可能不同 → 由「訊號已更正」機制承接，不另做修正。
- 3 年回補與 Boss 的 2003 起算回測理論上可能不同（上述極端情況）；以測試固定住。
- 分割防護用硬編碼常數表：新標的分割（0050／00631L 日後再分割）需要改碼；目前只涵蓋策略內兩檔，可接受。
- 休市日判斷（避免休市日誤報「資料未更新」）需一份行事曆：優先用 TWSE OpenAPI `holidaySchedule`（TASK-036 須以 LiveContract 驗證其存在與格式；若不可用，退而求其次「連續缺 ≥ 2 個平日才標 stale」，並回報 Tech Lead）。

### Impact
- 後端：新表＋migration（加法、可安全部署）、`TaiexTotalReturnClient`／`RefreshService`／`DrawdownBrakeCalculator`、`PortfolioController` 一個端點、Audience 開關、user-info 一個欄位、LiveContract 測試（MFI94U、holidaySchedule；第二階段加 MIS）。
- 前端：`drawdownBrake.ts`、`DrawdownBrakeCard.tsx`、`portfolioApis` 新 query、`accountApis` 型別；無新頁面。
- 既有限制（**不在本功能範圍**）：Portfolio 全域不處理股票分割，影響所有持有分割股、且快照早於分割日的使用者之市值／XIRR／淨值走勢；列為 Technical Debt，是否處理由 Boss／PM 排序。
- 合規（PM R1）與回測數字真實性（R3、Q4）屬 Boss／PM 範疇，Tech Lead 不下結論；技術上以功能開關保留「只開給 Boss」的退路。

See `TASK-036`（Backend）、`TASK-037`（Frontend）、`TASK-038`（Backend，第二階段）。

### 回落煞車 — 實作階段修訂（Tech Lead 審查 PR #433／#432 時接受，2026-10-11）
原決策保持不變；以下為實作中經審查接受的細化，之後以此為準：
- **MFI94U client 不走 `ExternalReadOnly`**：專用 named client `TaiexTotalReturn`（30 秒逾時），**刻意不掛** `AddReadOnlyResilience()`——Polly 會在 1–2 秒重送失敗的 GET，違反 ≥3 秒間隔並加劇限流。重試由每小時 refresh 負責；client 為 singleton，`SemaphoreSlim` 單一 in-flight，間隔自上一個回應**結束**起算。`holidaySchedule`（另一個 host、非限流）仍走 `ExternalReadOnly`。
- **回補由新到舊**（當月 → 往回至 3 年前），失敗後下一輪從缺口續補；`isInitializing` ＝ 表空或已存歷史跨度 < 730 天（#431 §1「至少 2 年」）。
- **真實格式（LiveContract 驗證，2026-10-10）**：MFI94U `fields=[日　期 | 發行量加權股價報酬指數]`（日期表頭中間是全形空白 U+3000，解析前先去除所有空白再比對）；`holidaySchedule` 為 `{Name, Date(民國7碼), Weekday, Description}`，目前只涵蓋 2026，含「開始交易日／最後交易日」的列是交易日。
- **功能開關**：`Features:DrawdownBrake:Audience` 非 `All` 的任何值（含打錯）一律視為 `Admin`；`User.DrawdownBrakeEnabled` 為 `[NotMapped]`，只在 `GET api/account/info` 填值。
- **前端 `advise`**：`E_target` 取截斷後 E；`prevState=null` 視為 OFFENSE；「狀態切換」只代表 `asOfDate` 當日剛切換，之後沒照做者走「曝險偏離 > 0.10」→ 再平衡句型。
- **仍待驗證／待決**：Boss 的回測 CSV（真實資料驗收）；MFI94U 當日值公布時間 vs `StaleAfter=17:00`／`PublishedAfter=14:00`；回測期間文案 `BACKTEST_PERIOD`；Q1／Q2／Q3。


## Decision: 回落煞車 — 重新整理、cache 盤點、可觀測性（需求 8／9，Tech Lead 2026-10-11）

### Context
PM 需求 8（資料時間＋手動「重新整理資料」）、需求 9（可觀測性，作為是否開放全部使用者的依據）。Boss 的疑慮：「13:15 可能拿到被 cache 的舊值」「擔心計算量」。以下先盤點事實（讀程式，非推測），再給設計。TASK：041（重新整理）→ 040（可觀測性）→ 038（盤中預估）；前端 043。

### Cache 盤點（事實）
| 層 | 現況 | 新鮮度上限 |
|---|---|---|
| 正式訊號資料 | 證交所 MFI94U → DB 表 `TaiexTotalReturnDaily`；背景服務每小時檢查，**平日台北 14:00 後且已存最新日 < 今日**才抓；14:00 前不存當日值（`IsFinal`） | 公布後約 ≤ 1 小時內入庫 |
| `GET drawdown-brake` | **沒有任何快取**：每次讀全表（現約 750 列，每年 +245）並重算（CPU < 1 ms） | = DB |
| 瀏覽器 RTK Query | 預設 `keepUnusedDataFor` 60 秒（此 query 未覆寫） | 60 秒 |
| 股價（Portfolio summary） | `FetchStockAsync` 30 分鐘（`CacheTtl.Portfolio.StockPrice`）；台股盤中 9:00–13:30 疊加即時報價，快取 10 秒（`CacheTtl.Finance.TwRealtimeQuote`）；既有 `POST holdings/{symbol}/refresh-price` 會清掉 30 分鐘快取與 provider 的 sticky 狀態 | 盤中 ≤ 10 秒；盤後 ≤ 30 分鐘 |
| 第二階段 MIS 預估（TASK-038，未做） | 設計為全站 ≈30 秒；手動重新整理最短 10 秒 | 10–30 秒 |

回答 Boss：「13:15 拿到舊值」——**正式訊號**在 13:15 本來就只有前一交易日的值（當日值 14:00 後才有），不是 cache 造成；**盤中價格**在台股盤中最多 10 秒舊；第二階段的**預估**最多 30 秒（按重新整理最短 10 秒）。真正會讓人誤會的是「看不出資料多新」，所以需求 8 的重點是顯示資料時間。

### 設計
- **端點**（契約；TASK-041）：
  ```
  POST /api/portfolio/drawdown-brake/refresh        // 與 GET 同一個 Audience 把關
  → 200 { result: "Updated"|"NoNewData"|"SourceUnavailable"|"Throttled",
          retryAfterSeconds: number|null, signal: DrawdownBrakeDto }
  DrawdownBrakeDto += lastCheckedAt: string|null (UTC)   // 伺服器最後一次成功向證交所取得可解析回應
                    + generatedAt: string (UTC)          // 此份訊號的計算時間
  ```
  `Throttled` 回 200（429 在本站專指 UsageLimit）。
- **全站單一冷卻 60 秒**（含背景輪詢的嘗試），單飛；只做 forward 補資料，不做 backfill；週末或台北 14:00 前（且已存最新日 ≥ 前一平日）短路為 `NoNewData`，不打證交所。**為什麼不是每人冷卻**：證交所負載必須與使用者數無關，每人冷卻在 100 人時仍可能每分鐘 100 次；全站冷卻時 A 按完、B 看到 `Throttled` 但拿到的訊號已是最新，不吃虧。
- **訊號 60 秒記憶體快取**（全站一份、單飛），refresh 與背景入庫時主動失效 → DB 讀取 ≤ 1 次／分鐘，與使用者數無關。
- **價格的重新整理**沿用既有 `refresh-price`，前端對 0050／00631L 呼叫並加 30 秒客戶端冷卻（TASK-043）；不為此新增後端契約。
- **第二階段**（TASK-038）：MIS 預估在同一個 refresh 內重新取得，全站最短間隔 10 秒；MIS 失敗只讓 `estimate=null`，不得讓 `result` 變成 `SourceUnavailable`。

### 負載由什麼決定（回答 PM／Boss）
- **與使用者數無關**：對證交所 MFI94U 的請求只來自背景服務（穩態每個交易日約 1–4 次——14:00 後每小時重試到當日值入庫為止；休市平日最多每小時 1 次，約 10 次／天；首次回補約 37 次、一次性，部署後約 2 分鐘）＋手動重新整理（全站冷卻 ≤ 1 次／分鐘）；第二階段 MIS 預估 ≤ 1 次／10–30 秒、只在盤中。
- **隨使用者數成長**：每次 GET 的 1 次 DB 讀取（加快取後 ≤ 1 次／分鐘）；Audience=Admin 時多 1 次 `Users` 查詢（Audience=All 時沒有）。
- **零伺服器成本**：個人化建議（目標配置、股數）在瀏覽器計算；伺服器收不到持股。
- **結論（技術事實，不是產品建議）**：計算量不是這個功能的風險點。全站唯一共同瓶頸是共用的 Azure SQL Basic（5 DTU），以 DTU 使用率監看即可。真正擋住「開放全部使用者」的是 PM 的 R1（合規，Boss／PM 範疇）、TASK-042（文案對齊更正值）與觀察期，不是計算量。

### 可觀測性（TASK-040）
- 自動收集（零程式）：App Insights `requests`（回應時間、失敗）、`dependencies`（對 `www.twse.com.tw` 的請求數／失敗數／耗時）——後端已有 `UseAzureMonitor`。
- 自訂：Meter `JackyAIApp.DrawdownBrake`（`drawdownbrake.views{outcome,cache}`、`drawdownbrake.refresh{result}`、`drawdownbrake.compute.duration`）＋每次檢視一行 log，內含「每日、每行程」假名 `UserKey`（SHA256(userId＋UTC 日期＋啟動時隨機 salt) 前 12 碼），只能算當日不重複人數、不可還原、不跨日追蹤。**任何 log／metric 都不含持股、股數、金額、email、Google ID。**
  - 實作（TASK-040）：`views` 的 `cache=miss` 指「這次呼叫自己讀 DB 並重算」，`hit` 指拿到別人建好的那份（含等別人建完的呼叫）；`outcome=forbidden`（不在開放對象內）沒有查訊號，所以**沒有 `cache` 標籤**。`refresh` 的每個 `result` 由 controller 在 `TryRefreshNowAsync` 回來後記一次，refresh 回應裡順帶讀的訊號**不算 view**。`compute.duration` 由快取 miss 的那次建構記錄（refresh 觸發的重建也記）。每次 GET（含 `forbidden`）記一行 log，屬性 `Outcome`、`UserKey`。
- KQL 初稿（**未實測**——沙箱沒有 App Insights 存取；表名用 Application Insights Logs 頁面的傳統名稱，首批資料進來後 Boss 或 Engineer 貼上校正欄位名）：
  ```kusto
  // 每日呼叫數、延遲、失敗
  requests | where timestamp > ago(14d) and name has "drawdown-brake" and name startswith "GET"
  | summarize calls=count(), p50=percentile(duration,50), p95=percentile(duration,95), failed=countif(success==false) by bin(timestamp,1d)
  // 每日不重複使用者（假名；forbidden＝不在開放對象內的呼叫，不算使用者）
  traces | where timestamp > ago(14d) and message startswith "DrawdownBrake view" and tostring(customDimensions.Outcome) != "forbidden"
  | summarize users=dcount(tostring(customDimensions.UserKey)), views=count() by bin(timestamp,1d)
  // 證交所請求與失敗
  dependencies | where timestamp > ago(14d) and target has "twse.com.tw"
  | summarize calls=count(), failed=countif(success==false), p95=percentile(duration,95) by bin(timestamp,1d)
  // 檢視結果、快取命中、重新整理結果（含「資料未更新」次數 = outcome=stale）
  customMetrics | where timestamp > ago(14d) and name in ("drawdownbrake.views","drawdownbrake.refresh")
  | extend outcome=tostring(customDimensions.outcome), cache=tostring(customDimensions.cache), result=tostring(customDimensions.result)
  | summarize total=sum(valueSum) by name, outcome, cache, result, bin(timestamp,1d)
  ```
  KQL 狀態：只依 `Outcome`／`UserKey` 屬性名與 Meter／instrument／標籤名調整過，**仍未對真實資料實測**（首批資料進來後由 Boss 貼上驗證）。`compute.duration` 是 Histogram：若 Azure Monitor 的 customMetrics 只提供 count／sum／min／max 而無百分位，compute 以平均（`valueSum/valueCount`）與 `valueMax` 判讀；`GET` 的 p95 一律看 `requests`。
- **建議的「可開放全部使用者」技術門檻（Tech Lead 提議，Boss 決定）**：觀察期 ≥ 14 天（≥ 10 個交易日）且同時滿足 (1) `GET` p95 < 300 ms（快取 miss 的 `compute.duration` p95 < 800 ms）、5xx < 1%；(2) 共用 SQL 資料庫 DTU p95 < 50%（Azure Portal → SQL database → Metrics → DTU percentage），且此功能時段無尖峰；(3) 證交所請求失敗率每日 < 10%，`stale` 每週 ≤ 1 個交易日且每次 < 3 小時（確認 `StaleAfter=17:00` 不會每天誤報；否則只改兩個常數）。**非技術前提另計**：R1 合規、TASK-042、PM 對文案的最終確認。
- 不做的事：Admin 儀表板（Portal 的 Logs 足夠；之後可另開任務做 Workbook）；把 Google 身分送第三方（DECISION-010）。

### Trade-offs
- 全站冷卻使「按了沒反應」的情況偶爾出現（`Throttled`），代價換來證交所負載與使用者數無關；訊息會明說「剛剛已有人更新過」。
- `UserKey` 每行程 salt：部署重啟會讓同一人當天被算兩次；觀察用途可接受，換來不需新增密鑰設定與較低的隱私風險。
- 60 秒訊號快取使「剛入庫」到「使用者看到」最多多 60 秒，但 refresh 與背景入庫都會主動失效，實務上為 0。


## Decision: Stock Intelligence 大盤走勢比較圖（A 同期間圖設計、B 相似區段提議；Issue #431 延伸 / DECISION-016，Tech Lead 2026-10-11）

### Context
Boss 要在 `/finance` 看：(A) 過去 5 年同一段月/日的走勢（每年一色）；(B) 歷史相似區段比較。標的＝加權指數（價格指數，不含息；PM 假設 A15，Boss 已同意）。與回落煞車互不依賴；不動 TASK-036 的報酬指數資料。

### A 圖設計（TASK-044 後端、TASK-045 前端）
- **資料**：Yahoo `^TWII` `range=6y&interval=1d`（系統已用 Yahoo 做大盤報價與 LiveContract；`MarketOverviewProvider` 只取當日）。**我的沙箱連不到 Yahoo（403），格式由 Actions 的 LiveContract 驗證**；替代佐證：Boss 的 `回測數據/tw_backtest/data/TWII.csv`（同為 Yahoo `^TWII`，1997-07-02～2026-10-08 共 7173 筆）經檢查——升冪無重複、無非正數、>6 天空檔僅 31 處且皆為農曆年假、單日 |漲跌| > 7% 僅 6 次且皆為真實事件（如 2024-08-05 −8.4%、2025-04-07 −9.7%）。→ 資料品質足以支撐 A，但 Yahoo 為非官方端點，故加健全性檢查與「最後成功結果」fallback。
- **端點**：`GET /api/finance/market-index-history`（匿名，與 `market-overview` 一致）回傳約 1,450 筆 `(date, close)`；**不做伺服器端按期間切片**——使用者改起訖月/日是純前端計算，不再打 API。壓縮後約 10 KB。
- **快取**：`IMemoryCache` 1 小時＋單飛＋「最後成功結果」保留 7 天 → Yahoo ≤ 24 次／天，與使用者數（含未登入訪客與爬蟲）無關。不入 DB（只有 6 年、冷啟動只多 1 次請求；與 `TaiexTotalReturnDaily` 不同，這裡不需要「重啟後仍在」）。
- **今日 bar**：Yahoo 盤中會回傳進行中的 bar，台北 14:00 前或非交易日一律丟棄，避免把盤中價畫成收盤。
- **位置**：`Finance/index.tsx` 的 `!hasStockResults` 區塊，`IndicatorExplorer` 之後（大盤層級；現有四個分頁都是個股分析，放進去語意不對）。
- **功能開關**：**不加**。理由：Boss 已明說不限 admin、`/finance` 本就公開、Boss 審查並合併 PR 即是最後一關；加開關只多一個要維護的分支。若日後要退場，revert PR 或隱藏元件即可。（PM 把這項交給 Tech Lead 決定。）
- **工作量估計**：後端約 1 天（provider＋快取＋測試＋LiveContract）；前端約 1.5 天（純計算＋圖＋測試＋手機驗證）。可平行。
- **風險**：Yahoo 端點改版／封鎖（R8）→ 「資料暫時無法取得」；休市日／閏日對齊（前端 TASK-045 已列測試）。

### B 圖提議（相似區段）——**尚未排工，等 Boss 看過下列實測後決定**
**演算法（建議）**：以最近 L 個交易日（預設 L＝60）的收盤，各自除以區段第一天（起點＝0%）；與歷史上每一個 L 日窗口比較：
1. 形狀：Pearson 相關係數 ≥ 0.90；2. 水準：兩條標準化路徑的 RMSE ≤ 3 個百分點。
兩者都滿足才算「相似」，取 RMSE 最小者；歷史窗口須在「現在」窗口開始前至少 F＝60 個交易日結束（不重疊，且之後 60 日有完整資料可畫「過去實際發生的走勢」）。找不到就顯示「找不到過去的相似區段」。使用者調整的「過去區段」＝指定另一段起訖日（長度與「現在」相同）。運算量：7,000 窗口 × 60 點，C# 約數毫秒，可依 `(asOf, L)` 快取。

**我用 Boss 的 TWII.csv（1997–2026，純價格序列）實測的結果**——
範例（L＝60、F＝60；門檻 corr ≥ 0.90 且 RMSE ≤ 3pp）：
| 觀察日 | 現在區段報酬 | 最像的過去區段 | RMSE／相關 | 判定 | 該過去區段「之後 60 日」 | 現在「之後 60 日」實際 |
|---|---|---|---|---|---|---|
| 2022-10-31 | −13.9% | 2015-06-09～2015-09-02 | 1.98pp／0.951 | **找得到** | +3.6% | +20.5% |
| 2020-03-31 | −19.1% | 2001-05-08～2001-08-01 | 2.97pp／0.939 | **找得到** | −10.1% | +19.7% |
| 2025-04-30 | −10.8% | 1998-06-26～1998-09-18 | 3.00pp／0.916 | 找不到（RMSE 剛好壓線） | −2.8% | +15.5% |
| 2015-09-30 | −11.6% | 1998-04-24～1998-07-20 | 2.20pp／0.889 | 找不到（相關不足） | −14.1% | +1.7% |
| 2008-11-28 | −30.4% | 2000-08-17～2000-11-13 | 4.45pp／0.921 | 找不到（跌幅太大，無水準相近者） | +5.4% | +4.0% |
| 2018-12-28 | −7.5% | 2012-05-04～2012-07-26 | 1.85pp／0.596 | 找不到（形狀不像） | +5.8% | +11.7% |
| **2026-10-08（今天）** | **+8.1%** | 2005-10-11～2006-01-02 | 2.21pp／0.884 | **找不到**（相關差 0.016） | （+4.6%，僅供參考） | — |

統計（2008 年起每 10 個交易日取一個觀察日，共 453 個；**樣本彼此重疊、且為樣本內，僅供量級判斷**）：
- 「找得到」比例：corr ≥ 0.9 且 RMSE ≤ 3pp → 31%；放寬到 corr ≥ 0.8 且 RMSE ≤ 5pp → 66%；收緊到 corr ≥ 0.95 且 RMSE ≤ 2.5pp → 6%。**沒有門檻時永遠找得到「最像的一段」**（7,000 個窗口足以找到形狀近似者），所以門檻是必要的，也決定了畫面約有 7 成時間顯示「找不到」。
- **預測力：幾乎為零**。相似區段「之後 60 日」報酬與現在「之後 60 日」實際報酬的相關係數＝ +0.04（n＝453；門檻內 n＝139 時同為 +0.04）；方向相同的比例 51–56%，低於「永遠猜漲」的基準 67%。上表兩個「找得到」的例子，之後走勢都與實際相反。
- 這是技術量測，不是產品結論：它不影響 Boss「想看歷史對照」的用意，但支持 PM 的做法——圖上維持警語、不放預測文字、之後走勢標示為「過去實際發生的走勢」。

**資料需求**：B 需要 1997 起 29 年日線（≈7,200 筆）。Yahoo `range=max` 可取得，但建議另案決定：內嵌一份歷史快照（Boss 的 TWII.csv 檔頭至 2026-10-08）＋只向 Yahoo 補最近差額，可避免 Yahoo 改寫歷史或失效時整張圖消失。**B 不做的話 A 不受影響。**

**工作量估計（若 Boss 仍要）**：後端約 2 天（資料、演算法、端點、快取、測試）、前端約 2 天（可拖曳／輸入調整區段、雙線＋延伸線圖、手機版）。

**建議**：先做 A；B 請 Boss 看過上表（尤其「今天找不到」與「預測力≈0」）再決定要不要排。若要做，需 Boss／PM 確認：門檻是否採 0.90／3pp、預設長度 60、之後長度 60、找不到時的文案。


## Decision: 回落煞車 — 以整個流動資產判斷曝險、分桶顯示（需求 12 / DECISION-017·018·020，Tech Lead 2026-10-11）

### Context
Boss 試用後發現卡片只算 0050＋00631L＋策略現金，與頁面右上角「水位」對不上（DECISION-017）。後續決定：NTSD 排除於策略範圍（DECISION-018）；**所有其他持股計入整體曝險，但建議只針對 0050、00631L、現金，其他持股一律「不動」**（DECISION-020，Q10＝B＋C）。訊號、風險等級、固定參數、觸發規則（DECISION-013）**不變**。

### Decision
1. **純前端改動，後端與 API 契約不動。** 卡片已拿得到所需資料：`summary.positions[]` 的 `marketValueTWD`、`exposureFactor`（後端 `PortfolioExposure` 算好，與水位同一套倍數）、`assetType/market/symbol/currency/priceFetchFailed`，以及 `liquidGrossAssetsTWD`。伺服器仍收不到持股（隱私與負載結論不變）。
2. **曝險只用 DTO 的 `exposureFactor`，不在前端另寫倍數規則**——這是「卡片 E 與水位同一個數字」的保證（PM 要求避免兩套算法）。分母 A＝`liquidGrossAssetsTWD` − 被排除持股市值；分子＝Σ(範圍內持股 `marketValueTWD` × `exposureFactor`)。無被排除持股時 E ＝ `marketExposureTWD / liquidGrossAssetsTWD`（單元測試以 `exposureOfLiquidAssetsPct` 對帳，差異僅四捨五入）。
3. **「策略範圍」與「是否給建議」各是一個地方**（PM 要求容易切換）：新檔 `drawdownBrakeScope.ts`
   - `STRATEGY_EXCLUDED`：`[{ market: 'US', symbol: 'NTSD' }]`——要增減被排除的標的只改這張表。
   - 分桶函式 `classifyPositions(positions)` 依序判定：REAL_ESTATE／LIABILITY（不在流動資產，略過）→ 被排除 → `TW` 且 symbol＝`0050`／`00631L`（核心）→ `exposureFactor === 0`（現金類：全部現金含外幣，加債券／貨幣型）→ `exposureFactor === 1`（其他一倍商品）→ 其餘（其他倍數商品，含反向）。
   - 「只有核心有建議」是**結構性**的：`advise()` 只產生 0050／00631L 的 `orders`，其他持股列沒有任何建議欄位可填，不做成旗標。
4. **核心 0050／00631L 的倍數防護**：`composition` 假設 0050＝1 倍、00631L＝2 倍（策略定義）。分桶時若 DTO 的 `exposureFactor` 與此不符（名稱分類意外）→ 視為資料異常，不給建議並提示，而不是靜默算錯。

### 12c 核心目標與建議算法（提議；Boss 可推翻參數層面的選擇，見下方「需要你知道的選擇」）
符號：`v50`、`v631`、`cash`＝核心三桶現值（TWD）；`X_o`＝其他持股（含其他倍數商品）Σ(市值×倍數)，可為負（反向 ETF）；`V_o`＝其他持股市值；`E_t`＝等級表的目標 E（進攻 `eOn`／防守 `eOff`）；`k`＝最低現金比例。

```
C      = v50 + v631 + cash                 // 核心可動用資金（內部調整，不引入外部資金）
A      = C + V_o                           // 策略範圍資產＝分母
E_act  = (v50 + 2·v631 + X_o) / A
c_min  = min(C, k·A)                       // 最低現金：以 A 為基準；核心現金不足時以 C 為上限
I      = C − c_min                         // 核心可投入
e*     = E_t·A − X_o                       // 核心必須提供的曝險（TWD）
e      = clamp(e*, 0, 2·I)                 // 核心可達的曝險
e ≤ I :  t50 = e,        t631 = 0,      tc = C − e
e > I :  t50 = 2I − e,   t631 = e − I,  tc = c_min       // 與 #431 §3 composition 同一規則
E_reach= (X_o + e) / A                     // 整體可達曝險
aboveTarget = (e* < 0)                     // 其他持股單獨已超過目標
capped      = (e* > 2·I)                   // 目標在其他持股＋最低現金限制下無法達到
```
觸發（沿用，只把比較對象由「截斷後 E」換成「可達 E」）：`switched || |E_act − E_reach| > 0.10` → 算 orders；否則「不動」。`orders ＝ roundHalfAwayFromZero((t − v)/price)`，先賣後買；全為 0 → 「不動」（A3）。方向：`E_reach < E_act ? 減碼 : 加碼`。現金類列的金額變動＝ −Σ(實際建議股數×價格)（不是理想值，避免與股數矛盾）。

**為什麼比較「可達 E」而不是「目標 E」**：與 TASK-037 的既有選擇相同的理由（目標不可達時，執行完仍永遠差 > 0.10、提醒不會消失）。這也自然滿足 PM 的規則：「核心已無可調整、卻因其他持股而偏離 > 0.10 → 顯示『不動』並附說明」。

**性質（Engineer 以測試鎖定）**
1. `V_o = X_o = 0` ⇒ `A = C`，`t/A` 與 `composition(E_t, k)` 完全相同，`advise` 結果與現行版本逐項相同（既有測試全保留，只換輸入型別）。
2. 冪等：照 orders 執行（忽略取整）後再算，必為「不動」（S1、S2 驗證）。
3. 各列市值合計＝A；其他持股列永遠沒有 orders；改 NTSD 金額只改對帳行，不改 E、桶或建議。
4. 任一範圍內持股 `priceFetchFailed`（價值被算成 0，E 與 A 都會錯）→ 列出標的、**不給建議**（寧可暫時沒有建議，也不給錯的）。這取代現行「只擋 0050／00631L 缺價」的做法；沒持有的 0050／00631L 仍用 `useLookupStockQuery` 取價以算買進股數（A4）。

### 情境（以獨立 Python 參考實作算出，先驗證性質 1 對 3 等級×2 狀態六組全過；**金額與價格皆為示意，不是 Boss 的實際持股**——排程沙箱看不到持股資料。價格：0050＝60、00631L＝30；平衡等級 `E_on=1.4, k=0.2`）
| 情境 | 持股（NT$ 百萬） | A／E_act | 目標 → 可達 | 目標市值（0050／00631L／現金） | 建議 |
|---|---|---|---|---|---|
| **S1 進攻，可達**（含 NTSD 1.0 被排除、其他一倍 1.5） | 0050 4.0／00631L 2.5／現金類 2.0／其他一倍 1.5 | 10.0／1.05 | 1.40 → **1.40** | 0.5／6.0／2.0 | 加碼：賣 0050 58,333 股、買 00631L 116,667 股；現金類不動；其他持股「不動」。對帳行：流動總資產 11.0（水位 109.1%）；不含 NTSD 1.0；策略範圍 10.0 → 曝險 1.05。執行後再算＝**不動** |
| **S2 切換到防守，其他持股單獨超過目標**（其他一倍 4.0） | 0050 2.5／00631L 2.5／現金 1.0 | 10.0／1.15 | 0 → **0.40**（`aboveTarget`） | 0／0／6.0（核心降到最低） | 減碼：賣 0050 41,667 股、賣 00631L 83,333 股；標示「即使 0050／00631L 全部轉為現金，整體曝險仍有 0.40 倍，高於目標 0 倍；差額來自您自行決定的其他持股」。執行後再算＝**不動**＋同一標示 |
| **S3 進攻，其他持股太多**（其他一倍 7.0） | 0050 0／00631L 1.0／現金 2.0 | 10.0／0.90 | 1.40 → **0.90**（`capped`：最低現金 2.0 ＋ 其他 7.0 之後只剩 1.0 可投入） | 0／1.0／2.0 | **不動**＋標示「受其他持股與最低現金限制，整體曝險最高只能到 0.90 倍（目標 1.40）」。同一人若 00631L 尚未買、現金 3.0（S3b，E_act 0.70）→ 加碼：買 00631L 33,333 股 |

S1 同時示範：NTSD 不影響 E、桶與建議，只出現在對帳行（DECISION-018）。

### 需要你知道的選擇（PM／Boss 可推翻；改動點各只有一處）
1. **最低現金 k 的基準＝策略範圍資產 A**（不是核心資金 C）。理由：原規格的 k 是「策略總資產的最低現金比例」，範圍擴大後 A 就是策略總資產；S3 即是此選擇的後果（其他持股占 70% 時，20%×A 的現金底線讓核心只剩 1.0 可投入）。若 Boss 希望 k 只相對於核心，改 `c_min` 一行。
2. **現金類包含債券／貨幣型 ETF**（PM A19，倍數 0）。現金類列顯示的金額變動，實務上可能需要賣債券 ETF 或換匯才能執行；卡片只標示金額，不對債券 ETF／外幣給買賣建議。
3. **「目標不可達」文案**：沒有其他持股時沿用原文「現金比例過高，無法達到此曝險」；有其他持股時用新文案（上表 S3）。字句為提議，PM 可改（常數在 `drawdownBrakeCopy.ts`）。

### 對已合併程式的影響（TASK-037／042 → TASK-046 調整）
- `drawdownBrake.ts`：`StrategyHoldings` 改為 `ScopeHoldings`（加 `othersValue`、`othersExposure`）；`advise` 依上述算法；`composition`、`PROFILES`、`REBALANCE_BAND`、`roundHalfAwayFromZero` 不動。`BrakeSettings.strategyCashTWD` **移除**（`sanitizeBrakeSettings` 忽略舊 localStorage 的這個欄位，不報錯）。
- `drawdownBrakeHoldings.ts`：`strategyShareOfLiquidAssetsPct`（需求 11 的占比）**移除**——需求 12 讓「策略部位占總資產」失去意義（PM 預設不需要），分桶表本身就顯示每列占比；`sumTwdCash` 與策略現金輸入一併移除。**分割防護（A8）與過期提示維持原樣**。
- `DrawdownBrakeCard.tsx`：`AdvicePanel` 改為「對帳行（有被排除持股才顯示）＋分桶表＋建議」；`SettingsPanel` 移除策略現金輸入。
- 後端：**無**。`TASK-041／040／038／044` 完全不受影響；`TASK-043`（重新整理按鈕）改的是同一張卡片，應排在 TASK-046 之後。

### Trade-offs
- 把算法留在瀏覽器：延續既有「伺服器收不到持股」的隱私與負載優勢；代價是規則要在前端測試鎖定（上表即 golden）。
- 不為「其他持股」提供旗標或選項（例如「讓衛星也減碼」）：Boss 已決定一律不動（DECISION-020）；若日後要開放，是新需求，不是切換開關。
- 任一範圍內持股價格失敗就暫停建議：可能讓「一檔不重要的美股抓不到價」就擋住整張卡片。代價低（暫時性、有明確訊息），換來不會給出錯的股數；若實際常見，再改為「排除失敗者並標示」。

### 驗證限制
本次只改文件，未執行任何前端測試。演算法以獨立 Python 參考實作驗證（六組退化對照＋S1～S3 的冪等檢查），TypeScript 實作與其測試結果以 GitHub Actions 為準。
