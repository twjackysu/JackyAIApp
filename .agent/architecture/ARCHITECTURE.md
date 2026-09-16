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
