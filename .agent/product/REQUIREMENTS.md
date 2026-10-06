# JackyAIApp — Product Requirements

> Baseline document. Established 2026-09-12 by PM Agent from a scan of the existing
> codebase (see root `CLAUDE.md` and module `CLAUDE.md` files) plus a code-level check
> of the Credits/Payments module. This is a snapshot of **what already exists**, framed
> as requirements, so future changes can be tracked as diffs against a known baseline —
> it is not a request for new work.

## Product Positioning

JackyAIApp is an AI-powered English-learning + personal-finance web app for Taiwanese
users. Two pillars today:

1. **English learning loop**: look up a word → save it to a personal repository →
   get tested on it (cloze / translation / conversation / sentence / listening) →
   spaced-repetition review → daily challenge / streak / XP — a retention-driven
   learning product.
2. **Personal finance tools**: Taiwan stock market news/analysis (TWSE) and a
   portfolio tracker (stocks/funds/cash/liabilities, TW + US + fund NAV).

A third, unrelated utility (PDF Unlocker) also lives in the product.

Monetization model as built: a **prepaid credit system** — users buy credit packs
(PayPal / ECPay) and AI-powered actions (test generation, grading, conversation,
TTS/STT, stock analysis, chatbot) consume credits (`Common/CreditCosts.cs`). Some
actions are free (dictionary lookup, Daily Challenge, spaced review).

**2026-09-12 update (DECISION-003)**: monetization work is paused. Near-term
product priority is (1) features that keep users coming back long-term, (2)
lower cloud/operating cost, and (3) growing the user base into something
genuinely popular — **without paid advertising**. Revenue is explicitly not
the current goal; see the new Growth & Retention requirement below and the
downgraded Credits & Payments requirement.

**2026-09-14 update (DECISION-005)**: Boss set an explicit standing
long-term direction — keep continuously improving the product so users want
to keep using it, and this is **not limited to the English-learning
pillar**. The Skill-Building work below (讀/寫/聽/說) is the first concrete
instance of this, but Finance/Portfolio/PDF Unlocker are equally in scope
for the same "find the gap, close it" treatment in future planning — see
Growth & Retention below.

**2026-09-21 update (DECISION-006)**: The "monetization model as built"
paragraph above is now being actively dismantled, not just paused. Every
Taiwan payment path is blocked by the same prepaid-credit trust (信託)
regulation (Stripe/PayPal unavailable in Taiwan; ECPay/Payuni rejected on
this exact basis), and Boss does not want to pursue a trust arrangement or
company registration to fix it (DECISION-001). Boss has decided to remove
all payment/purchase infrastructure and make the product fully free — see
the rewritten Credits & Payments requirement below.

---

## Requirement: Growth & Retention (No Paid Ads, Product-Led)

### Goal
Grow a real, engaged user base and keep people using the product long-term,
without spending on paid advertising — per DECISION-003 — and, per
**DECISION-004 (2026-09-14)**, primarily by making the product itself
genuinely improve users' English ability, not by adding sharing/promotion
mechanics on top of it. Boss's explicit reasoning: a learner has little
incentive or confidence to broadcast their own study activity, so
word-of-mouth has to be earned by product quality, not engineered via
share buttons.

**Per DECISION-005 (2026-09-14)**: this "keep making the product genuinely
better" mandate is explicitly **product-wide, not English-learning-only**.
Skill-Building (below) is the first workstream under it because the audit
was already done there, but Finance (market analysis), Portfolio, and PDF
Unlocker are equally candidates for the same treatment — find the concrete
gap, close it — in future planning passes, not a one-time English-only
initiative.

### User Story
As Boss, I want the product to become genuinely popular because it actually
makes people better at English, so that word-of-mouth happens naturally and
a real user base exists before we ever revisit charging money for it.

### Requirements
1. **SEO / organic discovery** (already shipped — see `TASK-001`/`TASK-002`/
   `TASK-003`, merged 2026-09-13/14): public, indexable Dictionary word
   pages, sitemap/robots.txt, per-word metadata and bot-preview cards. This
   is the one acquisition mechanic still in scope; it exposes already-free,
   already-cached content rather than asking users to promote anything.

   **Gap Audit (2026-09-28) — Boss reports it still can't be found on
   Google** (tried searching "JackyAI English Apple", expecting to land on
   the Apple word page). PM investigated the live product rather than
   re-reading the merged-PR descriptions:
   - Confirmed via `WebSearch` that `site:jackyai.azurewebsites.net`
     returns **zero results** — not "ranks low," but **not indexed by
     Google at all**, for any page on the domain, not just word pages. This
     means the gap is not a code defect in `TASK-001`–`003` (sitemap.xml,
     robots.txt, and per-word `seoTitle`/`seoDescription` are all present
     and correct on `master`, re-checked directly in this run) — it's that
     **nothing has ever told Google to crawl the site**. Shipping a
     technically-correct sitemap doesn't make Google visit it on its own in
     any predictable timeframe; that discovery step normally has to be
     triggered by verifying the domain in **Google Search Console** and
     submitting the sitemap URL there — a one-time, no-code action **only
     Boss can do**, since it requires proving ownership of the domain via
     the Google account tied to it. This is not an engineering task Tech
     Lead can pick up from the repo.
   - Separately, found a real branding gap: the actual crawlable page text
     never contains the word "JackyAI" anywhere. The site's `<title>` is
     "EngAI Learn" (`jackyaiapp.client/index.html`) and every word page's
     SEO title is "`{word} - EngAI Learn Dictionary`"
     (`WordSeoTextFormatter.cs`) — only the **domain** (`jackyai.
     azurewebsites.net`) contains "JackyAI"; none of the actual on-page
     text or title does. Once the site is indexed, this could still hurt
     matching for a query that includes "JackyAI" as a keyword, since
     Google weighs page title/content text more than a substring of the
     hostname. Whether to fold "JackyAI" into the visible title/branding
     (and if so, how it should read next to the existing "EngAI Learn"
     name) is a product naming decision, not a pure bug fix — flagged to
     Boss in `.agent/boss/PM.md` rather than decided unilaterally.
   - Not a code/architecture problem in the sense Boss's message
     anticipated ("如果架構困難請 Tech Lead 給選項") — there's no hard
     technical trade-off here, just one manual operational step outside the
     codebase (Search Console) and one small naming question. Handing to
     Tech Lead anyway for: (a) writing Boss a copy-pasteable Search
     Console/sitemap-submission checklist, (b) confirming there's no
     additional on-page technical blocker (e.g. checking rendered HTML
     actually contains the metadata, not just the API response), and (c)
     implementing the title/branding change once Boss answers the naming
     question. See Priority/Scope below — this now jumps the queue ahead of
     the Credits & Payments removal per Boss's explicit request.
2. **Long-term retention via real skill-building** (redirected by
   DECISION-004 — see the new **Skill-Building Feature Expansion**
   requirement below for the concrete feature list):
   - Strengthen the free daily loop (Daily Challenge / Review / Streak) —
     already the retention engine per its own requirement and should not
     regress.
   - Close the gaps in the four core English skills (聽/說/讀/寫) so the
     product delivers more real learning value per session, which is the
     mechanism Boss wants driving both retention and organic recommendation.
   - Push notifications / reminders for streak-at-risk users remain a
     possible retention lever (subject to Tech Lead evaluating
     feasibility/cost of a notification channel) — not dropped, just not
     the primary redirect from this decision.
3. **Cloud cost reduction**
   - Any change should be evaluated for its effect on AI/API/DB cost per
     active user, not just feature value. Caching, rate-limiting expensive
     AI calls, and reusing already-generated content counts as product work
     here, not just a technical nice-to-have.
   - **Shipped 2026-09-15 (outside the PM/Tech Lead pipeline — Boss
     committed directly)**: Azure AI Speech is now the default TTS backend
     (`AzureSpeechTextToSpeechService`), replacing OpenAI TTS for the
     common case; OpenAI TTS remains as a fallback when Azure isn't
     configured. Azure's F0 tier is free up to 500k characters/month
     against ~2k/month measured usage. Product-relevant because it directly
     closes a real user-facing failure mode noted in the commit: an
     exhausted OpenAI credit balance used to silence *all* TTS at once
     (dictionary audio, Listening exam, conversation replies), not just the
     credit-gated features — this removes that single point of failure for
     free-tier users, not only a cost win. No product/requirements change
     needed (`AudioController`/`ExamController` callers are unaware, same
     interface) — noted here per this run's "check for meaningful product
     changes" pass; PM did not initiate this.

### Acceptance Criteria
- [x] At least one previously-gated piece of content (generated word pages)
      is publicly viewable and indexable without login — shipped.
- [ ] No initiative in this requirement relies on paid advertising or paid
      promotion.
- [ ] No initiative here is shipped without at least a rough answer to
      "does this increase cost per user, and is that acceptable" — flagged
      to Tech Lead if unclear.
- [ ] **(New, 2026-09-28)** Boss has a concrete, no-code checklist for
      getting `jackyai.azurewebsites.net` verified and its sitemap submitted
      in Google Search Console.
- [ ] **(New, 2026-09-28)** Boss has decided whether "JackyAI" should appear
      in the visible page title/branding, and if approved, the change is
      shipped.

### Priority
**HIGH — top of queue as of 2026-09-28**, ahead of the otherwise-next-up
Credits & Payments removal, per Boss's explicit request to handle the SEO-
indexing gap first ("插單處理"). Otherwise unchanged: this is the current top
product priority per DECISION-003, ahead of Credits & Payments and ahead of
any feature that only serves paying users. The growth mechanism is now
specifically "make the product better," not "add sharing mechanics" — see
DECISION-004.

### Scope
#### In Scope
- SEO/public-content exposure of already-cached, already-free content
  (shipped).
- Retention improvements to the existing free loop.
- Cost-reduction changes that don't degrade the free experience.
- Skill-building feature work (see dedicated requirement below) — this is
  now the primary growth lever, not a separate track.

#### Out of Scope
- Any paid advertising or paid user-acquisition spend.
- Anything that requires re-opening the Credits & Payments question.

#### Rejected (DECISION-004, 2026-09-14 — was previously In Scope, Boss ruled it out)
- **Shareable Daily Challenge/streak/exam results** (share-out links/images).
  Boss's reasoning: learners generally lack the confidence or desire to
  publicize their own study activity/progress; this is very unlikely to
  drive real sharing behavior.
- **Referral / invite mechanics / leaderboard-based invites.** Not
  explicitly named by Boss, but rests on the same assumption Boss just
  rejected (users wanting to invite others into their learning activity) —
  parked alongside it rather than pursued until product quality itself is
  proven to generate word-of-mouth.

#### Future Consideration
- Community/classroom features (e.g. teachers or cram schools inviting
  student groups) — not scoped, needs Boss input on whether that audience
  is a deliberate target. Lower priority than skill-building work per
  Boss's 2026-09-14 redirect (general/unspecified audience, no existing
  channel to seed from — see `.agent/boss/PM.md`).
- **Product-wide gap audit beyond English learning (DECISION-005,
  2026-09-14)**: apply the same "audit existing coverage → name the
  concrete gap → propose a feature" method used for Skill-Building to
  Finance/Market Analysis, Portfolio Tracker, and PDF Unlocker. Not done
  yet — flagged here so a future PM run picks it up once Skill-Building's
  first item is handed off (this run's actionable capacity went to closing
  out Skill-Building's open questions instead).

---

## Requirement: Skill-Building Feature Expansion (聽說讀寫能力強化)

### Goal
Close concrete gaps in the four core English skills — listening, speaking,
reading, writing — so the product delivers more real ability improvement per
session. Per **DECISION-004 (2026-09-14)**, this is Boss's redirect for how
growth should actually happen: build something genuinely good enough that
people want to keep using it and recommend it on their own, instead of
engineering sharing/referral mechanics.

### User Story
As a learner, I want practice that actually trains the skill I'm weak in
(not just vocabulary recall), so that using the app measurably improves my
English rather than just testing what I already saved.

### Requirements
Current per-skill coverage audit (as of 2026-09-14, from the codebase) and
the gap each proposal below closes:

| Skill | Already covered by | Gap |
|---|---|---|
| 讀 Reading | Dictionary lookup, Cloze Test (word/blank level) | No passage-level reading comprehension — everything stops at single-word or single-sentence granularity |
| 寫 Writing | Translation Test (C→E, one sentence), Sentence Formation Test (already fully shipped — generate + evaluate endpoints + frontend card exist; **the "in development" status in this doc's Exam System section below was stale and is corrected**) | No open-ended/paragraph-level writing with feedback — both existing modes are single-sentence |
| 聽 Listening | Listening Test (TOEIC-style multiple choice), Listening Response Test | No dictation-style practice (hear → transcribe yourself), no adjustable playback speed |
| 說 Speaking | Conversation Test: voice recording, Whisper transcription, real-time **grammar** correction on the transcribed text | **No feedback at all on pronunciation/fluency/delivery** — the AI only ever judges what the transcript says, never how it was spoken. This is the single skill with a real, easily-named gap despite already having the full audio pipeline (record → Whisper) in place to build on |

Proposed features, in the priority order PM recommended and **Boss approved
2026-09-14** (see Decisions):

1. **Speaking delivery feedback (pronunciation/fluency), not just grammar**
   — HIGH. **Shipped 2026-09-15** (`TASK-004`/`TASK-005`, merged; product
   review passed — see Acceptance Criteria above). Highest leverage because: (a) it's the only skill with zero
   feedback on *how* something was said, (b) the audio-capture pipeline
   already exists (Conversation Test's recording + Whisper transcription),
   so this is additive feedback on data already being captured, not new
   infrastructure. Also a natural extension of Dictionary's existing
   per-word audio: "hear the correct pronunciation, then record yourself
   saying it and get feedback" turns a passive feature (audio playback)
   into an active practice loop.
2. **Reading comprehension passages** — MEDIUM-HIGH. **Shipped 2026-09-16**
   (`TASK-006`/`TASK-007`, both merged; product review passed — see
   Acceptance Criteria below) — see `ARCHITECTURE.md` → "Decision: Reading
   comprehension passages". One correction from this requirement's
   original framing: passages are **not** generated from the user's own
   saved vocabulary the way Cloze/Sentence are — investigation found that
   pattern doesn't cache well for multi-word content (see Architecture
   doc's Alternatives section), so this instead reuses the shared-bank
   pattern already proven by Listening/ListeningResponse (general
   comprehension-skill practice, not personalized vocabulary drilling).
   Flagged to Boss in `.agent/boss/TECH_LEAD.md` 2026-09-16 — **no Boss
   reply yet**, treated as non-blocking per Tech Lead's note that Engineers
   were told to proceed regardless.
3. **Open-ended writing practice with AI feedback** — MEDIUM. **Shipped
   2026-09-17** (`TASK-008`/`TASK-009`, both merged via PR #347/#348;
   product review passed — see Acceptance Criteria below). Reuses
   Translation Test's **grading** call shape only (one-shot AI feedback
   call, decoupled from attempt recording) — the "prompt" itself (a topic +
   up to 5 of the user's own saved words) costs zero AI calls to produce,
   unlike every other exam type's generation step, so only the evaluation
   call is credit-gated (`CreditCosts.WritingEvaluation = 3`). Feeds the
   SM-2 schedule for every required word via the paragraph's single
   holistic AI grade (no new per-word tracking table — flagged as a
   trade-off in the Architecture doc, non-blocking per Tech Lead's
   2026-09-17 note to Boss). Unlike item 2 (reading), this one **is**
   personalized to the user's own saved vocabulary, matching this
   requirement's original "use these 5 saved words" framing exactly.
4. **Dictation practice (listen → type what you hear)** — MEDIUM-LOW.
   **Shipped 2026-09-20** (`TASK-010`/`TASK-011`, both merged — PR #352,
   #354; product review passed, see Acceptance Criteria below) — see
   `ARCHITECTURE.md` → "Decision: Dictation practice — Skill-Building item 4
   (last item)". Confirmed as anticipated: reuses the existing word-picking
   helper, the existing unbilled resolve-ID-to-audio pattern, and a zero-AI
   string-comparison grader — no EF migration, no `RecordExamResultAsync`
   change, `CreditCosts.DictationTest = 1` (cheapest of the four). One
   deliberate deviation from every prior exam type: the `GET` prompt
   response withholds the word text entirely (only a `WordId`) since —
   unlike Cloze/Translation — showing the answer upfront would trivially
   defeat this specific exercise's whole mechanic. This is also the **last
   item** on the originally-approved four-item list — **all four
   Skill-Building items are now shipped and product-complete.** Further
   growth work moves to the product-wide gap audit (Finance/Portfolio/PDF
   Unlocker, DECISION-005) rather than adding a fifth item without a fresh
   Boss mandate.

### Acceptance Criteria
- [x] At least one of the four proposals above is scoped into a Tech Lead
      architecture/task write-up — item 1 (speaking delivery feedback)
      scoped 2026-09-15, see `ARCHITECTURE.md` → "Decision: Speaking
      delivery feedback" and `TASK-004`/`TASK-005`; item 2 (reading
      comprehension) scoped 2026-09-16, see `TASK-006`/`TASK-007`; item 3
      (writing feedback) scoped 2026-09-17, see `TASK-008`/`TASK-009`; item
      4 (dictation) scoped 2026-09-19, see `TASK-010`/`TASK-011`. All four
      proposals are now scoped **and shipped** — this criterion is fully
      satisfied.
- [x] Whichever ships first includes a rough per-use AI-cost estimate
      (OpenAI/Whisper/TTS calls) reviewed against `CreditCosts.cs`-style
      gating, consistent with DECISION-003's cost discipline — even though
      monetization is on hold, the credit-gate mechanism itself still exists
      as the app's built-in usage/cost control and should keep being used
      for any new AI-calling feature. Item 1's Phase 1 scope adds **zero**
      marginal AI/API cost (reuses the existing, already-gated Whisper
      call) — see Architecture doc. True pronunciation-accuracy scoring
      (Phase 2, a new vendor) is flagged to Boss separately, not yet costed
      or scoped as a task. Item 2 (reading comprehension) is gated at
      `CreditCosts.ReadingComprehensionTest = 3` and uses a shared-bank
      growth-threshold pattern (new AI generation only when the unseen pool
      drops below 20 passages), matching the Listening Test's existing cost
      model rather than a per-request AI call.
- [x] The feature measurably exercises the named skill gap (e.g. speaking
      feedback must comment on delivery, not only re-state grammar
      corrections already produced elsewhere). **Verified 2026-09-15** by
      PM reading the shipped code directly (`SpeechFluencyAnalyzer.cs`,
      `DeliveryFeedbackAlert.tsx`, both merged to `master` via
      `TASK-004`/`TASK-005`): pace (WPM), long-pause count and filler-word
      count are computed from Whisper's own word timestamps and rendered as
      a separate alert next to — not merged into — the existing grammar
      `CorrectionAlert`; UI copy consistently says "流暢度" and never
      "發音"/pronunciation, matching the Phase-1-is-fluency-not-pronunciation
      scoping. **Item 1 is fully shipped and closed out from a product
      standpoint** (engineering task bookkeeping — flipping `TASK-004.md`/
      `TASK-005.md` `Status:` to `MERGED` — was subsequently done by Tech
      Lead per its 2026-09-16 review pass).
- [x] **Item 2 (reading comprehension) verified 2026-09-16** by PM reading
      the shipped code on `master` directly (`ExamService
      .GenerateReadingExamAsync`, `ReadingSystem.txt`, frontend
      `Exam/ReadingTestCard`): each generated passage carries 3–4
      multiple-choice comprehension questions (enforced server-side —
      generation results outside that shape are rejected and a bank
      fallback is used instead), rendered as a distinct `EXAM_MODES` entry
      separate from Cloze/Listening. Matches the requirement's actual gap
      (passage-level comprehension) though — as flagged to Boss and noted
      above — it does not personalize passage content to the user's saved
      vocabulary. **Item 2 is fully shipped and product-complete.**
- [x] **Item 3 (open-ended writing practice) verified 2026-09-17** by PM
      reading the shipped code on `master` directly
      (`ExamService.GenerateWritingPromptAsync`/`GradeWritingAsync`,
      `Controllers/ExamController.cs`'s `GET api/exam/writing` / `POST
      api/exam/writing/evaluate`, frontend `Exam/WritingTestCard`): prompt
      fetch is free (no credit charge, no AI call — a topic pick + up to 5
      random saved words), grading is a single credit-gated AI call
      (`CreditCosts.WritingEvaluation = 3`) returning score plus
      grammar/content/vocabulary feedback and a per-word used/not-used
      list. Frontend copy correctly reflects the "free to open, charged on
      submit" cost shape (mode-selection caption reads "提交批改需消耗 N
      點使用額度", not implied at entry). `RecordExamResultAsync` has a
      distinct `"Writing"` wordless branch, matching the requirement's
      note that this fans a single holistic grade across every required
      word rather than judging each word separately. **Item 3 is fully
      shipped and product-complete.**
- [x] **Item 4 (dictation practice) verified 2026-09-20** by PM reading the
      shipped code on `master` directly
      (`jackyaiapp.client/src/Exam/DictationTestCard/index.tsx`, PR #354,
      built on merged `TASK-010` backend PR #352): the word text is never
      rendered to the user before grading — the fetched prompt exposes only
      `wordId`, and the correct spelling (`gradeResult.correctText`) only
      appears in the UI after submission, matching the deliberate
      answer-withholding design. Playback is genuinely unrestricted (button
      only disables mid-playback, not after — captioned "可重複播放"),
      unlike the one-shot-restricted MCQ listening cards, which is correct
      for a free-typing exercise where replay isn't "cheating." Cost caption
      ("每次出題消耗 1 點使用額度") matches `CreditCosts.DictationTest = 1`.
      SRS feedback fires through the same `recordExamResult` call every
      other word-anchored card uses. **Item 4 is fully shipped and
      product-complete — all four Skill-Building items are now done.**

### Priority
HIGH — this is the concrete workstream behind DECISION-004's redirect; it is
what "Growth & Retention" now means in practice.

### Scope
#### In Scope
- The four proposals above, evaluated and sequenced by Tech Lead for
  technical feasibility/cost once Boss confirms or adjusts the priority
  order.
#### Out of Scope
- Any new skill area beyond listening/speaking/reading/writing (e.g.
  grammar-rule reference content, vocabulary-only games) unless Boss
  requests it.
- Sharing/referral mechanics tied to these features (see the Rejected
  section above — a feature shouldn't smuggle sharing back in as a side
  effect).

### Open Questions
- ~~Does the priority order match Boss's intuition?~~ — **resolved
  (2026-09-14, DECISION-005)**: Boss approved the order as proposed
  (speaking feedback → reading comprehension → writing feedback →
  dictation).
- ~~Hand item 1 to Tech Lead now?~~ — **resolved (2026-09-14,
  DECISION-005)**: Boss approved. Item 1 (speaking delivery/pronunciation
  feedback) is now formally handed to Tech Lead for a feasibility/cost
  evaluation — see `.agent/status/PROJECT_STATUS.md` for the handoff note.
- ~~Item 1 shipped, item 2 handed to Tech Lead~~ — **resolved**: item 1
  shipped 2026-09-15; item 2 scoped by Tech Lead 2026-09-16 and now also
  **fully shipped** (see Acceptance Criteria above).
- ~~PM handed item 3 (open-ended writing practice) to Tech Lead~~ —
  **resolved (2026-09-17)**: scoped into `TASK-008`/`TASK-009`, both
  merged (PR #347/#348); product review passed same day — see Acceptance
  Criteria above. Tech Lead's still-open, non-blocking flag to Boss about
  item 2's passages not being personalized (`.agent/boss/TECH_LEAD.md`,
  2026-09-16) remains unanswered — not a blocker for anything currently in
  flight.
- ~~PM handed item 4 (dictation practice) to Tech Lead~~ — **resolved
  (2026-09-20)**: shipped, merged, and product-verified (see Acceptance
  Criteria above). **All four Skill-Building items are now complete** —
  this requirement's scope is fully delivered. Future growth work moves to
  the product-wide gap audit (Finance/Portfolio/PDF Unlocker, DECISION-005)
  rather than inventing a fifth Skill-Building item without a fresh Boss
  mandate.

---

## Requirement: Dictionary & Word Repository

### Goal
Let users look up English words and build a personal vocabulary they can be tested
on and review later.

### User Story
As a learner, I want to look up a word and save it to my own list, so that I can
study words I actually care about instead of a fixed word bank.

### Requirements
- Word lookup with spell-check fallback (Hunspell), KK phonetics, audio pronunciation.
- Bilingual (English/Chinese) definitions, synonyms, antonyms, related words, example
  sentences with translation.
- Recent search history and daily word recommendation.
- Users can bookmark/save words into a personal repository (`UserWord`).
- A word is generated via AI once and cached (`Word`/`WordMeaning`); repeat lookups
  by any user do not re-trigger the AI call.

### Acceptance Criteria
- [ ] Looking up a new word returns definitions, phonetics, audio, and examples.
- [ ] Looking up a previously-generated word does not incur a new AI credit cost.
- [ ] A saved word appears in the user's Repository and is available to the exam
      generators (Cloze/Translation/Sentence draw from it).

### Priority
HIGH — this is the core retention loop the rest of the product is built on.

### Scope
#### In Scope
- Free-tier word lookup and repository management (already free by design).
#### Out of Scope
- Multi-language dictionaries beyond English/Chinese.

---

## Requirement: Exam System

### Goal
Give learners multiple ways to be tested on their own vocabulary so studying
doesn't feel repetitive, and give the AI a natural place to charge credits.

### User Story
As a learner, I want several kinds of quizzes generated from my saved words, so
that practice stays varied and relevant to what I'm actually learning.

### Requirements
- **Cloze Test**: fill-in-the-blank from the user's vocabulary.
- **Translation Test**: Chinese→English with AI grading and feedback.
- **Conversation Test**: scenario-based dialogue (pre-built + custom scenarios),
  voice recording/transcription (Whisper), real-time grammar correction; does not
  require saved vocabulary.
- **Sentence Formation Test**: generate + evaluate endpoints and frontend
  card are fully implemented (corrected 2026-09-14 — this doc previously
  listed it as "in development," which was stale; see
  `Skill-Building Feature Expansion` above).
- **Listening Test** (TOEIC Part 1/2 style): question + TTS audio for 4 candidate
  statements/responses.
- Each generation/grading/turn is credit-gated per `CreditCosts.cs`.

### Acceptance Criteria
- [ ] Each test type can be generated on demand and returns a gradable result.
- [ ] Conversation test works with zero saved words.
- [ ] Insufficient credits blocks generation with a clear "buy more credits" prompt
      (`InsufficientCreditsModal`), not a silent failure.

### Priority
HIGH — primary paid-feature surface; most credit consumption happens here.

### Scope
#### In Scope
- The five test modes listed above.
#### Out of Scope
- (none currently — all five modes are shipped; see
  `Skill-Building Feature Expansion` for the next layer of test/practice
  features under consideration.)

#### Future Consideration
- Additional test modes should be evaluated against actual usage of existing
  modes first (see Open Questions in `.agent/boss/PM.md` re: cloud-cost goal).

---

## Requirement: Daily Challenge / Review / Stats (Retention & Gamification)

### Goal
Keep users coming back daily without needing to pay, and give the paid features
a bigger installed base to convert.

### User Story
As a learner, I want a short daily quiz and spaced-repetition review that don't
cost credits, so that I have a reason to open the app every day even when I'm
not buying credits.

### Requirements
- Daily Challenge: 5 date-seeded questions generated from the word DB, no AI
  cost, always free.
- Review: SM-2 spaced-repetition flashcards, shared scheduling with exam results.
- Streak + XP + level system, weekly report, anonymized leaderboard.

### Acceptance Criteria
- [ ] Daily Challenge never calls the AI/credit system.
- [ ] Missing a day breaks the streak per whatever streak rule is currently
      implemented (documented here as-is; changing the rule is a product decision,
      not a bug).

### Priority
HIGH — per DECISION-003 (2026-09-12) this loop *is* the current product
priority in its own right (retention, not a funnel to paid conversion, which
is on hold), and should not be weakened.

### Scope
#### In Scope
- Free daily engagement loop as described above.
#### Out of Scope
- Any change that makes this loop cost credits.

---

## Requirement: Finance — Market Analysis

### Goal
Give users AI-digested Taiwan market information and stock-specific analysis as
a paid, non-learning feature (diversifies the product beyond English learning).

### User Story
As a user interested in TW stocks, I want a daily AI summary of market news and
an on-demand analysis of a specific stock, so I can get a quick read without
reading raw TWSE filings myself.

### Requirements
- Daily financial news AI summary sourced from TWSE Open API + Finnhub global
  news (charged once/user/day, shared-generation pattern).
- Individual stock trend analysis: short (1–3mo) / medium (3–12mo) / long (1–3yr)
  term predictions with confidence levels and bullish/bearish recommendation
  (credit-gated AI call).
- **Correction (2026-09-20 gap audit)**: the feature is broader than
  previously documented here — free, unauthenticated, non-AI quant tools
  also exist and are wired to the frontend: technical indicators, chip
  (institutional flow) analysis, a composite score, a comprehensive
  multi-category analysis, and macro-economy/market-overview snapshots.
  These are pure computation (no OpenAI call), cached, and cost nothing —
  see Gap Audit below for the product question this raises.

### Acceptance Criteria
- [ ] Market digest is charged at most once per user per calendar day regardless
      of how many times the page is opened.
- [ ] Stock analysis returns a recommendation with an explicit confidence level,
      not just raw data.

### Priority
MEDIUM — valuable diversification, but the learning product is the primary
acquisition channel today; this is a monetization feature for an existing base,
not (yet) an acquisition driver of its own.

### Scope
#### In Scope
- TWSE-listed securities only.
#### Out of Scope
- Non-TW markets for the AI analysis feature (portfolio tracking already covers
  US stocks for tracking purposes, see below — that's a different requirement).

### Gap Audit (2026-09-20, DECISION-005 follow-through)
Per DECISION-005's "audit each module for gaps, same as Skill-Building"
mandate, now that Skill-Building's four items are fully shipped. PM read the
actual code (`FinanceController.cs`, `FinanceQueryService.cs`,
`StockAnalysisBuilder.cs`) rather than relying on this doc's prior
description. Findings:
- The feature is broader than this doc previously described: 8 endpoints, not
  2. Only `analyze-stock` (credit-gated, `CreditCosts.StockAnalysis = 5`,
  calls OpenAI) and `market-digest` (`CreditCosts.MarketDigest = 3`, shared
  once/day like the exam shared-bank pattern) cost credits. Four quant
  endpoints (`technical-indicators`, `chip-analysis`, `score`,
  `comprehensive-analysis`) are **free and unauthenticated** — pure
  computation, no AI call — plus two free macro endpoints. Frontend wires all
  of them; no backend/frontend drift found here.
- **The single biggest, cheapest gap — and it's the same gap Portfolio's own
  audit independently found from the other side**: a user's Finance stock
  analysis has zero awareness of their own Portfolio holdings. Asking about a
  stock you actually own gives no "you hold N shares" context, no link to
  Portfolio's richer per-holding page. See the cross-cutting proposal below.
- No prediction-accuracy tracking exists: `StockTrendAnalysis` (the AI's
  bullish/bearish call) is cached 12h in-memory only, never persisted, so
  the app has never once checked whether its own past calls were right.
- No watchlist/price-alert model exists anywhere in `Data/Models`.
- Flagged inconsistency (not yet a decision): the four free quant endpoints
  internally compute the *same* quant bundle that `analyze-stock` re-derives
  and charges 5 credits for (`BuildQuantitativeContextSafeAsync`). Worth a
  product call on whether that's an intentional "free funnel into the paid
  AI tab" or an oversight — no action taken until Boss/PM decides, since it
  touches pricing.

Proposed features (effort/reuse assessed by Tech Lead once prioritized; PM's
suggested order below is provisional pending Boss sign-off, same as the
original Skill-Building proposal):
1. **Cross-link Finance ↔ Portfolio for a given stock** (see Portfolio's Gap
   Audit below — this is one proposal, found independently from both
   directions). Near-zero new infra: a read-join of two existing services.
2. **Prediction-accuracy tracking** — persist each AI call + a later
   actual-outcome check via existing TWSE historical-price providers.
   Medium cost (new table + a scheduled job like `CakeJobsProvider.cs`), but
   directly serves the "no paid ads, build something people trust and
   recommend" growth strategy (DECISION-003/004) — nobody today can tell if
   this app's stock calls are any good.
3. **Multi-stock/sector peer comparison** — cheap, reuses the existing free
   `comprehensive-analysis` endpoint, mostly frontend (render 2-3 stocks'
   `CategoryScoreCard`s side by side).
4. **Watchlist/price alerts** — highest new-infra cost of this batch (new
   `Watchlist`/`Alert` model + a notification channel); most-expected finance
   feature by users, but should wait behind the cheaper wins above per
   cost-discipline.
5. **Broaden `market-digest` sentiment beyond TWSE announcements/Finnhub
   global news** — e.g. Taiwan-specific financial media — medium cost (new
   provider implementation, but fits the existing provider-interface
   pattern).

### Open Questions
- ~~Is the free-quant-vs-paid-AI-analysis credit-cost overlap intentional?~~
  — **moot (2026-09-21, DECISION-006)**: Boss decided to remove all paid AI
  features/credit gating entirely (see Credits & Payments requirement
  above), so there's no "paid AI tab" left for the free quant endpoints to
  funnel into. No longer an open question.
- **Priority order (1→5 above): approved by Boss (2026-09-21, "1. 是，可以")。**
  **Proposal 1 (Finance↔Portfolio cross-link) — MERGED, product-verified
  (2026-09-22)**: `TASK-012` (Frontend only, PR #359) landed on `master`.
  PM read the merged code directly —
  `Portfolio/HoldingDetailDrawer.tsx`'s `FinanceCrossLink` renders only for
  `assetType === 'STOCK'` (not `FUND`, matching scope) and opens
  `/finance?symbol=...&market=...` in a new tab; `Finance/hooks/
  useFinanceAnalysis.ts`'s mount effect reads the `symbol`/`market` params,
  forces `TAB_QUANTITATIVE` (never the credit-gated AI-trend tab — the
  scope constraint Tech Lead called out explicitly), and fires the free
  search once via a `useRef` guard. Matches the requirement exactly.
  **Proposal 2 (prediction-accuracy tracking, Phase 1) — MERGED,
  product-verified (2026-09-22)**: `TASK-013` (Backend only, PR #363)
  landed on `master`. PM read the merged code — `StockPredictionRecorder`
  persists one row per horizon (short/medium/long) after each
  `analyze-stock` call, wrapped in its own try/catch so a bookkeeping
  failure can never surface as an analysis failure; `PredictionEvaluationService`
  lazily grades due rows against a price window centered on the actual
  target date (not `IMarketDataProvider.FetchAsync`'s fixed ~6-month
  trailing-from-now window, which would silently mis-grade a long-horizon
  row — caught and fixed before merge, see the PR body), classifies
  bullish/bearish/neutral against a ±3% band, and is triggered
  fire-and-forget off the existing daily `market-digest` call — no new
  scheduled-job infrastructure, no new endpoint, no credit-cost change, no
  frontend surface. Matches the architecture decision exactly. **Phase 2**
  (a user-facing track-record UI) is deliberately unscoped — short-term
  predictions take weeks and long-term ones up to 3 years to become
  evaluable, so there is nothing meaningful to show yet; revisit once rows
  have actually been evaluated.
- **Proposal 3 (multi-stock peer comparison) — MERGED, product-verified
  (2026-09-23)**: Tech Lead scoped this as `TASK-014` (Frontend only); PR
  #364 merged same day (Boss merged directly). Cut down to user-picked
  multi-stock comparison (2-3 codes the user types themselves) rather than
  automatic sector-peer discovery — no sector/industry data exists anywhere
  in the schema, so true "sector" comparison would not be cheap the way the
  proposal assumed; user-picked comparison still satisfies the actual
  proposal ("render 2-3 stocks' `CategoryScoreCard`s side by side") at the
  originally-scoped cost (frontend-only, reuses the existing free
  `comprehensive-analysis` endpoint, no backend change). See
  `ARCHITECTURE.md` → "Decision: Multi-stock/sector peer comparison — Gap
  Audit item 3" for the full reasoning.
  **PM read the merged code directly** (`ComparisonResult.tsx`,
  `useFinanceAnalysis.ts` on `master`): the new "多股比較" tab has no
  `isAuthenticated` gate (unlike the credit-gated AI-trend tab), fetches are
  concurrent (`Promise.all`) against the free `comprehensive-analysis`
  endpoint using the fixed `DEFAULT_CONFIG`, one failed code renders an
  inline error in its own column without blocking the others, and each
  present category (Technical/Chip/Fundamental) renders one row of unmodified
  `CategoryScoreCard`s. Matches the requirement and the scope-cut exactly —
  **this item is fully shipped and product-complete.** This closes out all
  three items PM's 2026-09-20 Finance Gap Audit prioritized so far (1: cross-
  link, 2: prediction tracking Phase 1, 3: multi-stock comparison).
  One correction to proposal 2's original cost framing (recorded for the
  history): "a scheduled job like `CakeJobsProvider.cs`" was based on a
  mistaken premise — `CakeJobsProvider.cs` is a fetch-on-demand data
  source, not an example of a recurring/scheduled job, and this codebase
  has **no** background-job infrastructure at all today (no Hangfire/
  Quartz/`BackgroundService`, confirmed via a repo-wide search). Scoped
  instead as a lazy evaluation piggybacked onto the already-daily
  `market-digest` endpoint — avoids introducing this codebase's first-ever
  scheduled-job mechanism, a real cost saving over the original estimate.
  Note: proposal 5's `market-digest` was a credit-gated feature; its
  scoping should account for DECISION-006 (free-with-usage-cap, not
  credit-gated) rather than the now-superseded credit-cost assumption.

---

## Requirement: Portfolio Tracker

### Goal
Let users track their actual holdings (TW/US stock, funds, cash, liabilities) in
one place, including total return with dividends — a utility that increases
product stickiness independent of the learning/finance-content features.

### User Story
As a user, I want to record my holdings by date and see their current value and
total (含息) return, so I don't need a separate tool to track my portfolio.

### Requirements
- Snapshot-based (not a transaction ledger) — each save replaces the latest
  snapshot for that date.
- Asset types: STOCK, FUND, CASH, LIABILITY, and **REAL_ESTATE** (manual
  value + free-text valuation method, zero market exposure but counts toward
  net worth — correction, 2026-09-20: this existed in code already and was
  missing from this doc/CLAUDE.md's asset-type list).
- TW pricing via TWStockLib with realtime overlay during trading hours; US pricing
  + dividends via Yahoo Finance; fund NAV/dividends via FundRich.
- Total return (含息) walks dividend history against the snapshot timeline; XIRR
  includes dividends as inflows.
- Exposure ratio accounts for leveraged/inverse ETF factors.
- **Correction (2026-09-20 gap audit)**: this doc previously implied the
  feature only shows the latest snapshot. That's stale — net-worth history
  charts, max-drawdown analysis, and benchmark comparison (vs. 0050 or a
  custom symbol) already exist and ship in the frontend, alongside CSV
  export and a concentration/HHI card. This is a mature, feature-rich module,
  not an MVP.

### Acceptance Criteria
- [ ] Saving a snapshot for a date that already has one replaces it (no duplicate
      accumulation).
- [ ] A LIABILITY entry reduces net worth (sign-flip applied at the single
      valuation point).
- [ ] Total-return calculation reflects a dividend against the quantity held on
      its ex-date, not current quantity.

### Priority
MEDIUM — retention/stickiness feature; not currently a primary credit-consumption
surface (`PortfolioAiSummary` is the only paid action here).

### Scope
#### In Scope
- TW + US equities, funds, cash, liabilities, real estate as listed.
#### Out of Scope
- Crypto or other asset classes not currently modeled.

### Gap Audit (2026-09-20, DECISION-005 follow-through)
PM read the actual code (`PortfolioController.cs`, `PortfolioService.cs`,
`Data/Models/Portfolio.cs`, the `Portfolio/` frontend) rather than relying on
this doc's prior (stale, now-corrected above) description. Findings:
- **Same cross-module gap Finance's own audit found independently**: a
  portfolio holder gets *less* AI insight on a stock they actually own than
  a non-holder gets from the Finance tab. `HoldingDetailDrawer` only offers
  the narrower `PortfolioAiSummary` (single technical-analysis persona,
  `CreditCosts.PortfolioAiSummary = 2`) and never links to Finance's richer
  chip-analysis/comprehensive-analysis/score endpoints for that same symbol
  — despite both modules already sharing the same TW price-data providers
  under the hood.
- CSV **export** exists (`portfolioExport.ts`) but there's no matching
  **import** — a real, easily-closed asymmetry. **Correction (2026-09-24,
  Tech Lead scoping)**: the export schema does *not* actually match
  `SaveHoldingsRequest`'s shape as originally assumed here — it's a computed
  report (TWD-converted market value/cost/P&L), not an input schema. Import
  needs its own purpose-built column schema; see `TASK-015` and
  `ARCHITECTURE.md`. Still cheap/frontend-only, just not a literal
  round-trip of the export file.
- No rebalancing-suggestion feature (target allocation vs. actual) despite
  the allocation/category data needed for it already being computed
  (`ConcentrationCard`, `groupBy=category`).
- No FIRE/goal-tracking (target net worth, projected years-to-goal) despite
  the XIRR/trend data needed for a projection already existing
  (`GetNetWorthHistoryAsync`).
- Minor tech debt: `HoldingDetailDrawer.tsx` and the backend's
  `GetAiTechSummaryAsync` each independently hardcode a `market === 'FUND'`
  check instead of sharing one — a small drift risk, not urgent.

Proposed features (PM's suggested order below is provisional pending Boss
sign-off, same as the original Skill-Building proposal):
1. **Cross-link a Portfolio holding to Finance's fuller analysis for that
   symbol** — same proposal as Finance's Gap Audit above, found
   independently from both sides. Recommended as the first item: cheapest
   (a read-join of two existing services, no new AI calls, no new infra)
   and fixes a real, visible product inconsistency (owning a stock currently
   gets you *worse* AI insight on it than not owning it).
2. **CSV import for holdings** — closes the export/import asymmetry, mostly
   frontend work reusing the existing `PositionInput` schema and the
   existing defensive row-validation in `SaveHoldingsAsync`.
3. **Rebalancing suggestions against a target allocation** — reuses already-
   computed category/allocation data; new work is mostly a target-input UI +
   a buy/sell-delta calculation.
4. **FIRE/goal tracking** — reuses existing XIRR/net-worth-history data for
   a forward projection; new work is a target input + projection UI.

### Open Questions
- Same as Finance's — see that section's Open Questions. The cross-module
  link (proposal 1) is one proposal serving both modules; priority order
  **approved by Boss (2026-09-21)**. **Now MERGED and product-verified
  (2026-09-22)** — see Finance's Gap Audit section for the code-level
  verification (`TASK-012`, PR #359). `PortfolioAiSummary` (currently
  `CreditCosts.PortfolioAiSummary = 2`) is itself a credit-gated feature in
  scope for the Credits & Payments removal (DECISION-006) — its own scoping
  should assume free-with-usage-cap, not a credit charge.
- **Proposal 2 (CSV import for holdings) — MERGED, product-verified
  (2026-09-25)**: `TASK-015`, PR
  https://github.com/twjackysu/JackyAIApp/pull/367 (kept over three
  competing duplicate implementations — see `TASK-015.md`'s Review section),
  merged by Boss. PM read the merged code directly on `master`
  (`Portfolio/csvImport.ts`, `Portfolio/ImportCsvDialog.tsx`): the import
  column schema (`類型`/`代號`/`名稱`/`市場`/`計價幣別`/`成本幣別`/`數量`/
  `成本`/`估價方式`) is genuinely distinct from the export's TWD-converted
  report schema — `成本` maps straight to `PositionInput.CostBasis` in the
  row's own `成本幣別`, not a converted figure — matching the correctness
  fix Tech Lead's investigation required. Every row is validated through
  the same `toRow`/`rowError` the manual editor uses (no second, drifting
  validation copy), invalid rows are excluded from "加入" with a visible
  skip count, and import only appends to the editor's row state — the
  existing "儲存快照" button is still the only thing that actually
  persists, so a bad import is always reviewable before it can do damage.
  A template-download button removes the need to guess column names.
  Matches the requirement exactly — **this item is fully shipped and
  product-complete.** This closes out the second of Portfolio's four
  prioritized Gap Audit proposals (1: cross-link — done; 2: CSV import —
  done). Next after this: proposal 3 (rebalancing suggestions), unblocked
  below per the established "proceed by default" pattern.
- Separately, a QA-found bug on the already-merged Finance multi-stock
  comparison tab (`TASK-016`, PR #371, merged) is also **product-verified
  (2026-09-25)**: PM read `Finance/hooks/useFinanceAnalysis.ts` directly —
  the comparison tab's per-stock failures no longer leak into the
  single-stock tab's shared error `Alert` (`showComprehensiveErrorAlert` is
  now gated on `analysisTab === TAB_QUANTITATIVE`), with an interaction-level
  regression test covering the exact leak QA reported. This is a bug fix,
  not a new Gap Audit feature, so no Requirements scope change — noted here
  for the record since it was flagged as an open item in `PROJECT_STATUS.md`.
- **Proposal 3 (rebalancing suggestions against a target allocation) —
  MERGED, product-verified (2026-09-26)**: `TASK-017`, PR
  https://github.com/twjackysu/JackyAIApp/pull/376, merged. PM read the
  merged code directly (`Portfolio/rebalancingTarget.ts`,
  `Portfolio/RebalancingCard.tsx`): the 5 buckets are exactly the
  `PortfolioSummaryDto` asset-type fields (TW Stock/US Stock/Fund/Cash/Real
  Estate) with liability correctly excluded from both the bucket list and
  the allocation denominator (`liquidGrossAssetsTWD + realEstateTWD`); the
  target percentages persist as a per-browser `localStorage` preference
  (`portfolio.rebalancingTarget`, same pattern as `feeDiscountRate.ts`), not
  a new backend table — zero backend/DB change as scoped. First view with no
  saved target seeds the 5 inputs from the current allocation (so a
  first-time viewer sees "±持平" everywhere, not a wall of sell
  instructions); the sum-not-100% case is a soft caption warning only,
  never blocking; delta math is buy when target % exceeds current, sell
  otherwise, with a small TWD tolerance before showing "±持平". Matches the
  requirement exactly — **this item is fully shipped and product-complete.**
  This closes out the third of Portfolio's four prioritized Gap Audit
  proposals (1: cross-link — done; 2: CSV import — done; 3: rebalancing —
  done). Next after this: proposal 4 (FIRE/goal tracking), unblocked below
  per the established "proceed by default" pattern.
- **Proposal 4 (FIRE/goal tracking) — scoped as `TASK-018`
  (2026-09-27), Frontend only, READY, unclaimed.** Correction to the
  original framing: it reuses `PortfolioSummaryDto`'s existing
  `totalNetWorthTWD`/`annualizedReturnPct` (all-history XIRR) fields, not
  `GetNetWorthHistoryAsync` (that endpoint clamps to a 180-day window, a
  poor basis for a multi-year compounding assumption — see
  `ARCHITECTURE.md`). Target net worth/growth-rate override/monthly
  contribution are a per-browser `localStorage` preference, same pattern as
  `TASK-017`'s rebalancing target — zero backend change. This is the last
  of the four originally-prioritized Portfolio Gap Audit proposals — once
  shipped, Portfolio's Gap Audit list (from 2026-09-20) is fully worked
  through, same as Finance's already is.

---

## Requirement: PDF Unlocker

### Goal
Provide a simple, unrelated utility (password removal / compression for PDFs)
that may serve as a low-friction entry point to the app.

### Requirements
- Remove passwords from protected PDFs, with compression applied to the
  output.
- **Resolved (2026-09-21)**: "batch processing" was listed here and in root
  `CLAUDE.md` but does not exist in code — `PDFController.UnlockPdf` and the
  frontend `PDFUnlocker` page both handle exactly one file per request,
  single-select file input only. Boss confirmed (2026-09-21, "暫時不需要PDF
  Unlocker的批次處理"): keep the current single-file behavior, do not build
  batch support. This doc's own description is now corrected above (no
  "batch processing" claim). Root `CLAUDE.md`'s Key Features list still says
  "Batch PDF processing" — that's outside `.agent/` and not PM's file to
  edit; flagged as a small housekeeping item for whoever next touches that
  file (e.g. Tech Lead/an engineer doing an unrelated PDF-area change).

### Acceptance Criteria
- [ ] Uploading a password-protected PDF with the correct password returns an
      unlocked file.
- [ ] User-supplied PDFs are sanitized/validated before processing.

### Priority
LOW — utility feature, no clear tie to the learning/finance product lines or to
monetization; keep maintained but not a focus for new investment. Given this
priority, PM is not proposing new PDF Unlocker features from this audit round
— only flagging the batch-processing doc/behavior mismatch above.

### Scope
#### In Scope
- Current password-removal/compression functionality.
#### Out of Scope
- Any expansion of PDF editing capability without a specific Boss-approved reason
  (avoid scope creep into a general PDF-tools product).

---

## Requirement: Credits & Payments → Removal (DECISION-006)

### Goal
**Superseded 2026-09-21 (DECISION-006).** Original goal was to monetize
AI-powered features via a prepaid credit balance. Boss has decided to remove
all payment/monetization infrastructure instead: every viable Taiwan payment
path is blocked by the same prepaid-credit trust (信託) regulation (Stripe/
PayPal unavailable in Taiwan; ECPay/Payuni rejected on this exact basis, per
DECISION-001/002), and Boss does not want to pursue a trust arrangement or
company registration to unblock it. The new goal is: **make every currently
credit-gated AI feature free to use, while still bounding per-user AI cost
through a non-monetary usage limit** (so removing payment doesn't also
remove the only existing cost-control mechanism — the two goals must be
solved together, not just "delete the paywall").

### User Story
As a user, I want to use the app's AI features without paying, so I don't
need a payment method that doesn't even work in Taiwan today.

### Requirements (as built — being removed)
- Credit packs: 入門方案 (500 credits / $5 / NT$150), 人氣方案 (1100 credits / $10 /
  NT$300, +10% bonus), 超值方案 (2400 credits / $20 / NT$600, +20% bonus) —
  `Services/CreditPack.cs`.
- Payment providers wired: Stripe, PayPal, ECPay (綠界).
- Credit balance, transaction history (paginated), and a "check sufficient
  credits" endpoint.
- Per-feature credit costs centralized in `Common/CreditCosts.cs`; daily
  login/challenge/streak bonuses grant credits.
- `InsufficientCreditsModal` in-app prompts purchase when a user can't afford
  an action.

### Acceptance Criteria (removal — none of these are shipped yet, this is the target state)
- [ ] No payment provider SDK/integration (Stripe, PayPal, ECPay) remains wired
      into the backend or frontend.
- [ ] `/pricing` and `/credits` purchase UI (buy-a-pack flow) is removed;
      `InsufficientCreditsModal`'s "go buy credits" call-to-action is removed
      or repurposed into a plain usage-limit-reached message.
- [ ] Every action currently gated by `CreditCosts.cs` still works, but is
      free — gated (if at all) only by the new non-monetary daily/usage limit
      below, not by a spendable balance.
- [ ] No user-facing "balance" or "credit transaction history" concept remains
      (nothing to purchase, so nothing to track spending against).
- [ ] **(DECISION-007, 2026-09-28)** No UI text anywhere in the app uses the
      words credit/點數/付費/購買/儲值 or any synonym implying money or a
      spendable balance — the feature should just look like a normal free
      feature, not "temporarily free" or "discounted."
- [ ] **(DECISION-007)** When a user hits a feature's daily/periodic usage
      cap, the feature becomes unavailable and shows a message stating they
      exceeded their usage and, if they're a heavy user, to contact
      `jacky19918@gmail.com` to get their limit raised — no self-serve
      purchase path, no mention of price.

### Priority
**HIGH — ACTIVE REMOVAL (DECISION-006, 2026-09-21), design fully confirmed
2026-09-28 (DECISION-007).** Supersedes the 2026-09-12 "LOW / ON HOLD" call
(DECISION-003): this is no longer "leave it running, don't invest further" —
Boss wants the payment machinery actively torn out. Portfolio Gap Audit's
last item (`TASK-018`, FIRE tracking) is now implemented, so this is the
**next default Tech Lead handoff** — except it is queued **behind** the
2026-09-28 SEO-indexing investigation below, which Boss explicitly asked to
be handled first ("插單處理").

### Scope
#### In Scope
- Full removal of Stripe/PayPal/ECPay integration code, purchase UI, balance/
  transaction-history UI and endpoints.
- Converting every `CreditCosts.cs`-gated action to a free, non-monetary
  usage-limit gate instead (see PM's proposed replacement design below).
#### Out of Scope
- Any paid/monetized feature work of any kind — monetization is not being
  redesigned, it's being removed. The 2026-09-12 "single-transaction
  purchase" proposal is now formally abandoned, not parked (DECISION-006
  makes clear Boss wants no payment path at all, not just a redesigned one).

### PM's proposed replacement design (needs Boss confirmation + Tech Lead feasibility pass)
Removing the credit balance also removes the app's only existing cost-control
mechanism on expensive AI calls (exam generation/grading, conversation,
TTS/STT, stock AI analysis, chatbot) — that has to be solved at the same time,
not left as a side effect, or "remove payment" quietly becomes "make AI calls
unlimited and free," which directly conflicts with Boss's separate standing
goal (2026-09-12) to lower cloud cost.

**Proposal**: replace each `CreditCosts.cs` entry with a **free daily (or
otherwise time-boxed) usage cap per user per feature**, enforced server-side,
no money involved anywhere. This isn't a new pattern for this codebase — it's
the exact mechanism `MarketDigest` (once/user/day) and the exam shared-bank
generation logic already use, just applied to the features that currently
cost points instead of being capped by frequency. Example caps (illustrative,
not final — actual numbers are Tech Lead's/Boss's call once the direction is
confirmed): N exam generations/day per type, 1 stock AI analysis/day, a
monthly TTS/STT ceiling. Existing credit balances need no migration or
refund handling — per the Constraints previously documented here, no real
transaction has ever cleared on any provider, so no real money is owed to
anyone.

### Open Questions
- ~~Whether to keep the prepaid-credit model~~ / ~~single-transaction purchase
  redesign~~ — **superseded (2026-09-21, DECISION-006)**: Boss decided to
  remove payment entirely rather than redesign it. No longer an open
  question.
- ~~Does the free-usage-cap replacement design match what Boss has in mind?~~
  — **resolved (2026-09-28, DECISION-007)**: Boss confirmed the daily/
  periodic-cap direction, with two specifics that weren't in PM's original
  proposal: (1) the cap and any notion of "usage limit" must be **completely
  invisible** to a normal user — no credit/point/payment language anywhere,
  it should just read as an ordinary free feature; (2) when a user actually
  hits the cap, show a message naming the overage and inviting heavy users
  to email `jacky19918@gmail.com` for a higher limit, instead of any
  self-serve purchase flow. **This requirement is now unblocked and ready
  for Tech Lead to scope** — no further Boss sign-off needed on the design
  itself; only the per-feature numeric caps remain Tech Lead's call.
- Related, now resolved by this same decision: Finance Gap Audit's "is the
  free-quant-vs-paid-AI-analysis overlap intentional" question is moot — see
  that section.

---

## Requirement: Portal 首頁「今日行動」化 + 產品聚焦（Issue #409）

### Goal
讓每位回訪用戶打開網站的第一屏就知道「今天該做什麼」，並讓第一次來的訪客在 10 秒內理解價值。
目前 Portal（`/`）只是 9 個應用的靜態目錄，沒有個人化資訊，也沒有登入／價值引導。

### Audit 事實（2026-10-03 PM 讀 `Portal/index.tsx`、`constants/apps.tsx`）
- Portal 對所有人顯示相同的 9 張卡片（Wealth Portfolio、Stock Intelligence、English Coach、PDF、
  Slideshow、Job Finder、Data Analysis、Data Dashboard、Connected Services）；唯一個人化是「歡迎回來，{name}」。
- streak／XP／待複習數量等已存在於 `/stats`、`/review`、`/daily`，但 Portal 完全不露出——留存機制被藏在二層頁面。
- 9 個應用地位相同，沒有主次；有已知使用量資料前，無法判斷 Slideshow／Job Finder／Data Analysis 是否值得持續投入（假設，未驗證）。
- 未登入訪客沒有登入 CTA 或「這站能做什麼」的說明（以程式碼判斷，未實機驗證）。

### User Story
As a 回訪用戶, I want 打開網站就看到今天的待辦（複習、每日挑戰、持股變化）,
so that 我不必自己回想要點進哪個應用，養成每天回來的習慣。

### Requirements
1. 已登入：Portal 頂部顯示「今日」區塊，僅整合**既有**資料：連續天數（streak）、今日每日挑戰是否完成、待複習單字數、投資組合總資產較上次的變化（有持倉資料時）。每項可一鍵進入對應頁面。
2. 已登入且該項無資料（如無持倉、無待複習）：該項不顯示，不得顯示空白或 0 造成雜訊。
3. 未登入：顯示一行價值說明與登入入口；英文學習（含免登入的字典）排在最前面。
4. 應用卡片分群（依 Boss 2026-10-03 回覆修訂，見 DECISION-009）：English Coach、Wealth Portfolio、Stock Intelligence 排在最前（主力）；其餘六個首頁應用**不隱藏、不弱化**，同樣視為重要，並需各自持續改善 UX 與留存。
5. 不新增 AI 呼叫、不新增後端運算成本（符合 DECISION-003 成本紀律與 DECISION-007 用量機制）。

### Acceptance Criteria
- [ ] 已登入且有待複習單字的用戶，Portal 第一屏可見待複習數量，點擊直接進入 `/review`。
- [ ] 今日每日挑戰已完成時顯示完成狀態；未完成時顯示進入入口。
- [ ] 連續天數與 `/stats` 顯示一致。
- [ ] 無持倉用戶不會看到投資組合項目。
- [ ] 未登入訪客在 Portal 可見價值說明與登入入口，且字典入口不需登入即可進入。
- [ ] 手機寬度（≤ 400px）版面無橫向捲動。
- [ ] Portal 載入不產生任何會消耗 UsageLimit 的請求。

### Priority
HIGH（直接對應 DECISION-003「留得住用戶」，且為純前端整合既有資料，預期成本低）

### Scope
#### In Scope
- Portal 今日區塊、訪客引導、卡片分群

#### Out of Scope
- 推播／Email 提醒、分享機制（DECISION-004）、新增任何付費／點數概念（DECISION-006）

#### Future Consideration
- 逐模組小改進盤點（見下方 Issue #409 回覆的 backlog，需 Tech Lead 估工）
- 下架或合併低使用量應用——需先有使用量數據，並由 Boss 決定

### Constraints
- 僅描述行為；資料來源、API 是否需新增彙總端點，由 Tech Lead 決定。

### Open Questions
- ~~主力應用名單~~ 已由 Boss 回覆（DECISION-009）：三個主力 + 其餘六個皆重要。
- ~~用量統計方案~~ 已由 Boss 選定 Azure Application Insights（DECISION-010）。


## Requirement: 匿名用量統計（DECISION-010）

### Goal
取得各模組的真實使用數據，作為「功能去留／投入優先序」的依據（Issue #409）。

### User Story
As a 產品負責人,
I want 看到各頁面／模組的造訪與使用量,
so that 我能依數據決定改善優先序，而不是憑感覺。

### Requirements
- 使用 Azure Application Insights（Boss 已確認 Azure 端已啟用）。
- 至少能回答：各模組（Dictionary／Exam／Finance／Portfolio／PDF 等首頁應用）近 7／30 天的造訪量與不重複使用者數、登入 vs 訪客比例、回訪率。
- 匿名：不得記錄姓名、email、Google ID 等可識別個人資訊於分析資料中；不引入第三方廣告追蹤。
- 不得明顯拖慢頁面載入或增加月費（維持在免費額度內，超出前須通知 Boss）。

### Acceptance Criteria
- [ ] Boss 能在 Azure Portal 看到按頁面路徑分群的造訪數與不重複使用者數。
- [ ] 分析資料中查無個人可識別資訊。
- [ ] 啟用後首頁載入體感無明顯變慢。
- [ ] 若需隱私揭露／告知文字，已列出並交 Boss 決定。

### Priority
MEDIUM（TASK-028 之後；為決策提供數據，不直接影響用戶）

### Scope
#### In Scope
- 頁面層級造訪統計。
#### Out of Scope
- 逐用戶行為分析、A/B 測試、自建後台。
#### Future Consideration
- 關鍵事件（完成每日挑戰、完成考試）計數。

### Constraints
- 實作方式（前端 SDK 或後端遙測）由 Tech Lead 決定；是否需 cookie 同意提示屬法規問題，PM 未查證，請 Boss／Tech Lead 確認而非假設。

### Open Questions
- 隱私政策／cookie 告知是否需更新（未查證）。


## Requirement: 逐模組 UX／留存改進（Issue #409 後續，2026-10-06 PM 盤點）

### Goal
依 DECISION-009，六個首頁應用皆須持續改善 UX 與留存。本輪盤點以程式碼讀取為依據（未實機驗證），
找出「任務完成後沒有下一步」與「失敗時使用者什麼都看不到」兩類最大缺口。

### Audit 事實（由程式碼閱讀，檔案以 `jackyaiapp.client/src` 為根）
- Exam 一輪結束（`Exam/components/RoundResultScreen.tsx`）只有分數＋「再來一輪／返回選單」，沒有答錯單字清單、沒有「加入複習」、沒有 Review 連結；逐題的 SRS 排程其實已在背景記錄。
- Conversation 摘要的 `suggestedWords` 是不可點擊的 Chip，無法一鍵加入單字庫。
- Repository 加入單字包後的成功訊息只有文字，沒有連結；單字查詢失敗會被當成「單字庫是空的」顯示。
- PDFUnlocker 失敗（密碼錯誤、伺服器錯誤）只寫 `console.error`，使用者看不到任何訊息；成功也無提示，下載檔名固定 `unlocked.pdf`。
- Repository 與 Exam 需登入，訪客被直接導向 Google 登入，沒有先看到價值。
- JobSearch 預設查詢固定為 "senior software engineer"，不記住上次搜尋、無儲存職缺；DataAnalysis 的 AI／Dashboard／自訂資料源需登入，暫存上傳 2 小時後消失。
- Connectors 空狀態是死路、介面僅英文；DashboardView 空狀態文字沒有連結。

### 改進項（依優先序；僅描述行為，做法由 Tech Lead 決定）
1. **HIGH — Exam／Conversation 結束後的「下一步」**：一輪結束顯示答錯的單字，可一鍵加入／加強複習，並有前往 Review 的入口；Conversation 建議單字可一鍵加入單字庫。對應 DECISION-004（真的提升英文能力）與留存。
2. **MEDIUM — 不得無聲失敗**：PDF 解鎖失敗須顯示原因（如密碼錯誤）、成功須有提示；Repository 查詢失敗須顯示錯誤而非「空單字庫」；單字包加入失敗須提示；Exam 取題失敗須可重試。
3. **MEDIUM — 訪客先看到價值**：Repository／Exam 未登入時，導向 Google 前先顯示簡短說明（能做什麼、免費、為何要登入）。不改變「需登入」的資料規則。
4. **MEDIUM — Repository 加入單字包後**成功訊息提供前往 Review／Exam 的按鈕。
5. **LOW — JobSearch** 記住上次關鍵字／策略（可存在瀏覽器）；錯誤訊息提供重試。
6. **LOW — DataAnalysis／Dashboard／Connectors** 首次使用引導（解釋 Live Demo）、空狀態加 CTA、Connectors 文字與其他頁一致。
7. **LOW — Exam 選單**手機寬度不得橫向溢出，並標示建議的起手測驗。

### Acceptance Criteria
- [ ] 完成一輪 Cloze 或 Translation 後，結果頁列出答錯單字，且有一鍵加入複習與前往 `/review` 的入口；全對時不顯示空區塊。
- [ ] Conversation 摘要中的建議單字可點擊加入單字庫，並有成功／失敗回饋。
- [ ] PDF 解鎖輸入錯誤密碼時，畫面顯示明確錯誤訊息；成功時顯示完成提示。
- [ ] 模擬單字查詢失敗時，Repository 顯示錯誤而非「沒有單字」警示。
- [ ] 未登入訪客開啟 Repository 或 Exam，先看到價值說明與登入按鈕，而非空白頁。
- [ ] 手機寬度（≤ 400px）Exam 選單無橫向捲動。
- [ ] 以上項目不新增 AI 呼叫或 UsageLimit 消耗。

### Priority
第 1 項 HIGH；第 2–4 項 MEDIUM；其餘 LOW（不全部標 HIGH）。

### Scope
#### In Scope
- 上述 7 項的使用者可見行為

#### Out of Scope
- 推播／Email、分享機制（DECISION-004）、新增金流或點數概念（DECISION-006）、下架任何應用（需先有 App Insights 數據，並由 Boss 決定）

#### Future Consideration
- Portal 顯示投資組合變化（需輕量後端端點，見 PROJECT_STATUS 2026-10-04）
- JobSearch 儲存職缺／提醒；DataAnalysis 匿名結果匯出
- #407 SEO：單字頁內部連結（相關單字、每日單字）以加速收錄，Tech Lead 已提出、待 PM 排序——PM 建議列 MEDIUM，成長面唯一的低成本槓桿

### Constraints
- 修改 DataAnalysisLibrary 前須先與 Boss 確認範圍（DECISION-009）。

### Open Questions
- 無需 Boss 決定。第 3 項若 Tech Lead 認為與既有 RequireAuth 衝突，請回報。
