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
