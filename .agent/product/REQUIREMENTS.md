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
1. **SEO / organic discovery** (unchanged, already shipped — see
   `TASK-001`/`TASK-002`/`TASK-003`, merged 2026-09-13/14): public,
   indexable Dictionary word pages, sitemap/robots.txt, per-word metadata
   and bot-preview cards. This is the one acquisition mechanic still in
   scope; it exposes already-free, already-cached content rather than
   asking users to promote anything.
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

### Priority
HIGH — the current top product priority per DECISION-003, ahead of Credits &
Payments and ahead of any feature that only serves paying users. The growth
mechanism is now specifically "make the product better," not "add sharing
mechanics" — see DECISION-004.

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
   Combines existing TTS (audio generation) and simple string-diff grading
   (no new AI call needed for grading) — likely the cheapest of the four to
   run, but trains a narrower combined listening+spelling skill rather than
   a skill with a current zero-coverage gap.

### Acceptance Criteria
- [x] At least one of the four proposals above is scoped into a Tech Lead
      architecture/task write-up — item 1 (speaking delivery feedback)
      scoped 2026-09-15, see `ARCHITECTURE.md` → "Decision: Speaking
      delivery feedback" and `TASK-004`/`TASK-005`; item 2 (reading
      comprehension) scoped 2026-09-16, see `TASK-006`/`TASK-007`.
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
- **PM handed item 4 (dictation practice) to Tech Lead — 2026-09-17.** All
  three prior items in the approved priority order are now shipped and
  product-verified; this executes the already-approved order (DECISION-005)
  rather than a new scope decision, same "proceed by default" pattern used
  for items 1–3. This is also the **last item** on the original
  Skill-Building list — once it ships, this requirement's four-item scope
  is complete and future growth work should look at the Future
  Consideration items below (product-wide gap audit beyond English
  learning) instead of inventing a fifth Skill-Building item without a
  fresh Boss mandate.

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
- Daily financial news AI summary sourced from TWSE Open API (charged once/user/day).
- Individual stock trend analysis: short (1–3mo) / medium (3–12mo) / long (1–3yr)
  term predictions with confidence levels and bullish/bearish recommendation.

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
- Asset types: STOCK, FUND, CASH, LIABILITY.
- TW pricing via TWStockLib with realtime overlay during trading hours; US pricing
  + dividends via Yahoo Finance; fund NAV/dividends via FundRich.
- Total return (含息) walks dividend history against the snapshot timeline; XIRR
  includes dividends as inflows.
- Exposure ratio accounts for leveraged/inverse ETF factors.

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
- TW + US equities, funds, cash, liabilities as listed.
#### Out of Scope
- Crypto, real estate, or other asset classes not currently modeled.

---

## Requirement: PDF Unlocker

### Goal
Provide a simple, unrelated utility (password removal / compression for PDFs)
that may serve as a low-friction entry point to the app.

### Requirements
- Remove passwords from protected PDFs.
- Batch processing and file compression.

### Acceptance Criteria
- [ ] Uploading a password-protected PDF with the correct password returns an
      unlocked file.
- [ ] User-supplied PDFs are sanitized/validated before processing.

### Priority
LOW — utility feature, no clear tie to the learning/finance product lines or to
monetization; keep maintained but not a focus for new investment.

### Scope
#### In Scope
- Current password-removal/compression functionality.
#### Out of Scope
- Any expansion of PDF editing capability without a specific Boss-approved reason
  (avoid scope creep into a general PDF-tools product).

---

## Requirement: Credits & Payments

### Goal
Monetize the AI-powered features via a prepaid credit balance, spendable across
every paid feature in the app.

### User Story
As a user, I want to buy a credit pack once and spend it across any AI feature I
like, so I don't need a separate purchase per feature.

### Requirements (as built)
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

### Acceptance Criteria
- [ ] Every AI-gated action deducts the exact cost listed in `CreditCosts.cs` and
      logs a `CreditTransaction`.
- [ ] Purchases via each connected provider credit the correct pack amount.
- [ ] Balance and history are visible to the user at all times (`/credits`).

### Priority
**LOW — ON HOLD (DECISION-003, 2026-09-12).** Was HIGH/BLOCKED on the trust/
信託 legal question; Boss has since deprioritized monetization entirely until
the product has a real, engaged user base. Keep the existing implementation
running as-is (it costs nothing to leave alone), but no further product
investment here for now — see Growth & Retention requirement below for where
effort is going instead.

### Scope
#### In Scope
- The three-provider, three-pack model as currently built — maintained,
  not extended.
#### Out of Scope (until Boss revisits monetization)
- Any new pricing model work (including the 2026-09-12 "single-transaction
  purchase" proposal in `.agent/boss/PM.md` — parked, not abandoned).
- Marketing or in-app pushes to purchase credits.
- Adding more credit packs or raising prices.
- Enabling real (non-sandbox) transactions on any provider.

### Constraints
- **金流未審核通過**: per Boss, none of the payment providers are approved for
  live transactions yet, so no real revenue can currently be collected regardless
  of what the code supports.
- **法規風險 — 預付款信託**: per Boss, Taiwan regulation on prepaid-type (預付)
  consumer credit balances requires a trust (信託) or equivalent safeguard for
  funds collected before the corresponding service is delivered, which the
  business does not currently have. The credit-pack model as built (pay once,
  draw down over unlimited future time) is a textbook prepaid-balance model, so
  **this constraint applies directly to the current implementation**, not just
  to a hypothetical future one.
- Both constraints are logged as an **Open Question to Boss** in
  `.agent/boss/PM.md` — the charging model is not being changed by this
  document; it is being flagged, per PM Agent instructions on ambiguous/
  significant product decisions, for a Boss decision before any monetization
  push.

### Open Questions
- ~~Whether to keep the prepaid-credit model~~ — **resolved (2026-09-12, DECISION-001
  / DECISION-002)**: Boss ruled out trust/履約保證/company registration AND
  subscription. PM verified (LINE Pay's own individual-merchant rules) that
  *any* deferred/stored-value model — credits, point packs, courses,
  memberships — hits the same regulatory wall regardless of payment provider.
  A replacement proposal ("single-transaction, delivered-immediately"
  purchases per feature) was written up in `.agent/boss/PM.md` (2026-09-12
  補充).
- **Superseded (2026-09-12, DECISION-003)** before Boss answered those 4
  questions: monetization itself is now on hold. The single-transaction
  proposal is parked for whenever Boss revisits revenue — not rejected, just
  not current work. No Tech Lead engagement on payment-flow redesign for now.
