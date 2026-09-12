# Project Status

## 2026-09-12 — PM: Initial product baseline established

- `.agent/product/REQUIREMENTS.md` created: baseline requirements for all 8
  current feature modules (Dictionary/Repository, Exam System, Daily
  Challenge/Review/Stats, Finance market analysis, Portfolio, PDF Unlocker,
  Credits & Payments), derived from the existing codebase.
- **Credits & Payments module is fully implemented** (Stripe/PayPal/ECPay,
  3 credit packs, balance/history/insufficient-credit UX) but marked
  **HIGH priority / BLOCKED** in Requirements pending a Boss decision on two
  open questions raised in `.agent/boss/PM.md`:
  1. Prepaid-credit model may require a trust (信託) arrangement under
     Taiwan law that the business does not currently have — charging model
     may need to change (pay-per-use / subscription instead of prepaid
     balance).
  2. Payment provider approval status/timeline unconfirmed.
- Until Boss decides, expanding paid/credit-consuming features and any
  push to promote purchases are out of scope. No architecture or
  implementation changes have been made — Tech Lead has no action yet.
- No `DECISIONS.md` entries yet — nothing has been finalized.

## 2026-09-12 — PM: Monetization redesign proposal (scheduled run)

- Boss replied in `.agent/boss/PM.md`: rejected trust/履約保證/company
  registration as a fix, rejected subscription, and asked for a concrete
  plan referencing how other TW sites (591, LINE Pay/Google Pay) charge.
  Recorded as finalized decisions: **DECISION-001** (no trust/company route)
  and **DECISION-002** (no subscription) in `.agent/product/DECISIONS.md`.
- Researched LINE Pay's own individual-merchant (個人商店) rules: they
  explicitly bar deferred/stored-value products (儲值、課程、會員) from
  the no-company-needed tier — same wall as ECPay/Payuni. Corrected Boss's
  hypothesis that LINE Pay/Google Pay might dodge this; they don't. What
  they *do* allow without a company is single-transaction,
  delivered-immediately sales (實支實付) — which matches the 591 model
  Boss referenced.
- Proposed replacing the entire Credits wallet with per-feature one-time
  purchases (e.g. one cloze set, one stock report — paid and delivered in
  the same transaction, no balance/points/expiry). Proposal + tradeoffs +
  a parallel low-risk track (AdSense on free pages, one-off donation
  button) written up in `.agent/boss/PM.md` (2026-09-12 補充).
- `REQUIREMENTS.md` Credits & Payments section updated: old Open Question
  marked resolved-in-direction, new open items (pilot scope, per-item
  pricing, which provider to file with first) pending Boss's next reply.
- No code or architecture changes. Tech Lead has no action yet — waiting
  on Boss to approve the direction before scoping the payment-flow rework.

## Open Items
- Waiting on Boss response to the 2026-09-12 補充 proposal in
  `.agent/boss/PM.md` (4 open questions) before Credits & Payments
  requirements can be finalized or Tech Lead engaged on the payment-flow
  redesign.
