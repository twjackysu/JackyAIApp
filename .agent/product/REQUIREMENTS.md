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
HIGH — this is the free funnel that is supposed to lead to paid conversion; per
Boss's short-term goal (more features users are willing to pay for), this is the
top of that funnel and should not be weakened while paid features are expanded.

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
**HIGH, but currently BLOCKED — see Constraints.** This is the entire monetization
path for the product; per Boss's short-term goal it's also the thing that most
needs product attention once unblocked.

### Scope
#### In Scope
- The three-provider, three-pack model as currently built.
#### Out of Scope (until the blocker below is resolved)
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
- Whether to keep the prepaid-credit model (needs 信託 or a legal workaround),
  or move to a pay-per-use / subscription model that isn't legally "prepaid" —
  **this is a Boss decision**, tracked in `.agent/boss/PM.md`, not decided here.
