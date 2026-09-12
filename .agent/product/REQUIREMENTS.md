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

---

## Requirement: Growth & Retention (No Paid Ads)

### Goal
Grow a real, engaged user base and keep people using the product long-term,
without spending on paid advertising and without increasing cloud cost per
user — per DECISION-003, this is the top product priority for now, ahead of
any monetization work.

### User Story
As Boss, I want the product to become genuinely popular through people
actually wanting to use and share it, so that a real user base exists before
we ever revisit charging money for it.

### Requirements
Two goals that should be optimized together wherever possible — initiatives
that grow the user base *and* cost little/nothing extra per new user should
be prioritized over ones that grow cost linearly with users:

1. **Organic acquisition without ad spend**
   - The Dictionary word-lookup pages are a natural fit: once a word is
     AI-generated it's cached (`Word`/`WordMeaning`) and served to everyone
     at near-zero marginal cost. Making individual word pages publicly
     viewable and indexable by search engines (no login required to *view*
     an already-generated word, login still required to save/track it)
     turns existing cached content into free SEO-driven traffic — this is
     the single highest-leverage idea because it grows users and barely
     grows cost at the same time.
   - Shareable results: a user's Daily Challenge score, streak, or exam
     result should be easy to share outside the app (social media / LINE),
     since word-of-mouth is the only acquisition channel in scope.
   - Referral / invite mechanics between users (e.g. friends on the same
     leaderboard) — reward with non-monetary incentives (cosmetic badges,
     streak protection), not credits, since Credits & Payments is on hold.
2. **Long-term retention (making people want to keep using it)**
   - Strengthen the free daily loop (Daily Challenge / Review / Streak) —
     this is already the retention engine per the existing requirement below
     and should not regress.
   - Push notifications / reminders for streak-at-risk users (subject to
     Tech Lead evaluating feasibility/cost of a notification channel).
   - Finish or improve features that are currently incomplete or rough
     (e.g. Sentence Formation Test) only where doing so measurably improves
     retention, not as general polish.
3. **Cloud cost reduction**
   - Any change should be evaluated for its effect on AI/API/DB cost per
     active user, not just feature value. Caching, rate-limiting expensive
     AI calls, and reusing already-generated content (as above) counts as
     product work here, not just a technical nice-to-have.

### Acceptance Criteria
- [ ] At least one previously-gated piece of content (e.g. a generated word
      page) is publicly viewable and indexable without login.
- [ ] Users can share a Daily Challenge/streak/exam result outside the app
      (a shareable link or image) without needing credits or payment.
- [ ] No initiative in this requirement relies on paid advertising or paid
      promotion.
- [ ] No initiative here is shipped without at least a rough answer to
      "does this increase cost per user, and is that acceptable" — flagged
      to Tech Lead if unclear.

### Priority
HIGH — the current top product priority per DECISION-003, ahead of Credits &
Payments and ahead of any feature that only serves paying users.

### Scope
#### In Scope
- SEO/public-content exposure of already-cached, already-free content.
- Sharing/referral mechanics using non-monetary rewards.
- Retention improvements to the existing free loop.
- Cost-reduction changes that don't degrade the free experience.
#### Out of Scope
- Any paid advertising or paid user-acquisition spend.
- Anything that requires re-opening the Credits & Payments question.

#### Future Consideration
- Community/classroom features (e.g. teachers or cram schools inviting
  student groups) as a possible higher-leverage growth channel for the
  English-learning audience specifically — not scoped yet, needs Boss input
  on whether that audience is a deliberate target.

### Open Questions
- Is there a specific target audience to focus growth on (general public vs.
  students / cram schools / adult self-learners), or general organic growth
  across the board? Affects which of the ideas above to sequence first —
  tracked in `.agent/boss/PM.md`.
- Any existing channel (社群、LINE 社團、既有使用者) worth seeding growth from,
  or starting fully from zero?

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
- **Sentence Formation Test**: in development.
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
- Sentence Formation Test completion (tracked as in-progress, not a regression if
  still incomplete).

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
