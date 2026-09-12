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

## 2026-09-12 — PM: Strategy pivot away from monetization (DECISION-003)

- Boss replied before answering the 4 monetization-proposal questions:
  pause revenue work entirely. New top priority — retention (features
  people keep using long-term), lower cloud cost, and growing the user
  base into something popular **without paid ads**.
- Recorded **DECISION-003** in `.agent/product/DECISIONS.md`.
- `REQUIREMENTS.md`: Credits & Payments downgraded HIGH/BLOCKED →
  LOW/ON HOLD (kept running as-is, no further investment, single-transaction
  proposal parked not abandoned). Added new top-priority requirement
  **"Growth & Retention (No Paid Ads)"** — key idea: expose already-cached
  Dictionary word pages publicly for SEO (near-zero marginal cost, doubles
  as both growth and cost-conscious), plus shareable Daily
  Challenge/streak/exam results, referral mechanics with non-monetary
  rewards, and cost-per-user discipline on any new initiative.
- Open question sent to Boss in `.agent/boss/PM.md`: target audience for
  growth (general public vs. students/cram schools) and any existing
  channel to seed from.
- No code/architecture changes. Tech Lead has no action yet on payments;
  may get engaged later specifically on cloud-cost-reduction technical
  options once Boss confirms audience/direction for growth.

## 2026-09-12 — Tech Lead: Architecture + tasks for public word-page SEO (scheduled run)

- First Tech Lead run. Read REQUIREMENTS/DECISIONS/PM log; picked the
  "public, indexable Dictionary word pages" item (top of Growth & Retention,
  HIGH priority) as the highest-priority actionable work — no open PRs, no
  engineer work pending review, no blockers reported.
- Investigated current implementation before scoping: backend word lookup
  (`GET api/dictionary/{word}`) is **already anonymous** and touches no
  per-user data; the frontend `/english/dictionary` route is **already**
  outside `RequireAuth`. So "public" is effectively already true — the real
  gap is SEO/share-readiness: an anonymous 401 bug (unconditional call to
  an authenticated favorite-status endpoint), query-string-based URLs
  instead of clean per-word paths, and no per-word page metadata at all
  (pure client-rendered SPA, static `<title>` only).
- Documented findings + a two-phase decision in
  `.agent/architecture/ARCHITECTURE.md`: Phase 1 (low-cost, no infra change)
  ships now; Phase 2 (server-rendered metadata for LINE/Facebook share
  previews) needs a cost/complexity call, written up as an open item to
  Boss rather than decided unilaterally.
- Created `TASK-001` (Backend: sitemap.xml/robots.txt + unique index on
  `Word.WordText`) and `TASK-002` (Frontend: path-param routing, fix the
  anonymous-401 bug, per-word meta tags via react-helmet-async). Both
  `READY`, no blocking dependencies between them beyond agreeing on the URL
  path/casing convention.
- Wrote to `.agent/boss/TECH_LEAD.md`: flagged the Phase 2 trade-off
  (custom bot-detection middleware vs. a paid prerendering service vs.
  deferring) — leaning toward deferring for now (no real sharing traffic
  yet to justify the cost), consistent with DECISION-003's cost discipline,
  but left it open for Boss input rather than closing it myself.
- No code changes made — this run only produced architecture/task
  documentation for Backend/Frontend Engineers to pick up.

## Open Items
- Waiting on Boss's answer on target audience / seed channel for growth
  (see `.agent/boss/PM.md`) before sequencing the rest of the Growth &
  Retention backlog beyond the SEO item now underway.
- `TASK-001` / `TASK-002` are READY and unassigned — waiting for Backend/
  Frontend Engineers to pick them up.
- Phase 2 (share-preview metadata for LINE/Facebook) is an open technical
  trade-off flagged to Boss in `.agent/boss/TECH_LEAD.md` — not scheduled,
  pending Boss input or evidence that it's worth the cost.
