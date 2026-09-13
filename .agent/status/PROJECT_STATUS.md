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

## 2026-09-13 — Tech Lead: Answered Boss's Phase 2 questions, opened TASK-003 (scheduled run)

- Boss asked in `.agent/boss/TECH_LEAD.md` about the lightweight
  bot-detection middleware option (leaning toward it): downsides,
  maintenance burden, and whether it actually helps get word pages found
  via Google search.
- Replied with two points: (1) the Google-search goal is already fully
  served by Phase 1 (TASK-001/TASK-002) alone — Googlebot executes JS, so
  it doesn't need the middleware at all; that pair of tasks was already
  READY and unblocked by this question. (2) The middleware only affects
  LINE/Facebook-style link-preview cards. Its maintenance cost is low
  because those platforms' crawler User-Agents are official/public and
  rarely change (unlike adversarial bot detection); the only real downside
  is an unlisted platform's bot still getting a generic preview, which is
  cosmetic, not functional.
- Given the downsides turned out minor, proceeded to scope `TASK-003`
  (Backend: social-preview middleware for known bot UAs) as READY, and
  updated `ARCHITECTURE.md`'s Phase 2 section from "open trade-off" to
  "resolved, proceeding with option (a)". Did not wait for further Boss
  sign-off since Boss had already signaled the direction and the only
  open item was risk clarification, which is now answered; flagged in the
  reply that Tech Lead will keep going this direction unless told
  otherwise.
- No code changes — still task/architecture documentation only.

## 2026-09-13 — Tech Lead: Closed drift risk in Phase 2 bot middleware design (scheduled run)

- Boss raised a follow-up in `.agent/boss/TECH_LEAD.md`: the bot-facing HTML
  from `TASK-003` has no interactive UI and nobody looks at it, so could its
  title/description text quietly drift out of sync with the real page over
  time if the two implementations aren't kept manually in sync?
- Agreed this was a real gap: the original task scoping only required both
  surfaces to read the *same underlying word data*, not the *same formatted
  text* — the actual title/description string was still going to be
  formatted twice, once in TypeScript (frontend) and once in C# (backend
  middleware), which can diverge silently since nothing exercises the
  bot-facing path in normal use.
- Fix: collapsed the formatting logic to a single C# helper. Updated
  `TASK-001` to add `seoTitle`/`seoDescription` fields (computed once,
  server-side) to the `GET api/dictionary/{word}` response; updated
  `TASK-002` so the frontend renders those fields as-is instead of building
  its own copy; updated `TASK-003` so the middleware calls that same C#
  helper in-process instead of re-deriving the text. Added a PR-review
  checkpoint to TASK-003's acceptance criteria to verify this at review
  time. Documented the reasoning in `ARCHITECTURE.md`.
- No cost added — same data, formatted once instead of twice. All three
  tasks were still unclaimed/READY, so no rework was needed. Replied to
  Boss in `.agent/boss/TECH_LEAD.md` with the explanation.
- No code changes — task/architecture documentation only.

## 2026-09-13 — Backend Engineer: Fixed CI-blocking flaky test, opened PR #330 (scheduled run)

- Boss asked in `.agent/boss/TECH_LEAD.md`: "我看工程師發的PR CI壞了，能請人另外
  發PR先修CI的問題嗎?" — a PR's CI was red.
- Investigated: both open PRs (`#328` TASK-001, `#329` TASK-002) had
  `build-and-test-server` failing on the same test —
  `SECInsiderTradingProviderTests.FetchAsync_ParsesTransactionReportTable`
  (expected 4 transactions, got 1). Confirmed via `git log` this test/fixture
  pair was last touched by a merge already on `master` (`7044419`), so the
  failure is pre-existing on `master` and unrelated to either PR's actual
  changes — not something TASK-001/TASK-002's authors need to fix in their
  own branches.
- Root cause: `SECInsiderTradingProvider.FetchAsync` filters to a rolling
  90-day window relative to `DateTime.UtcNow`, but the test fixture used
  hardcoded absolute dates (`2026-06-15`/`2026-06-16`). As real time passed,
  those dates aged out of the window — a time-bomb test, not a product bug.
- Fix (test-only, no production code touched): replaced the hardcoded dates
  in the fixture with placeholders, substituted at test-run time with dates
  relative to `DateTime.UtcNow`, matching the existing convention already
  used in `PersistentTwHistoricalProviderTests`.
- Opened **PR #330** (`claude/magical-heisenberg-t0rkzg` → `master`),
  targeting `master` directly per Boss's ask for a separate PR, so `#328`/
  `#329` don't need to be touched — merging `#330` unblocks CI for both once
  they pick up `master`. Could not run `dotnet test` locally (no SDK in this
  sandboxed environment) — verified the fix by manually tracing the
  parsing/filtering logic against the fixture instead; relying on CI to
  confirm. Subscribed to `#330` to drive it to green.
- Also corrected stale info below: `TASK-001`/`TASK-002` are **not**
  unassigned — `#328` and `#329` already exist and (per their diffs) look
  functionally complete, just blocked by the CI issue above. `TASK-003` is
  still genuinely unclaimed and, per its own task notes, should wait for
  `TASK-001`/`TASK-002` to merge (it needs to call TASK-001's shared C#
  helper in-process) — not started this run.

## 2026-09-13 — Backend Engineer: PR #330 (CI fix) merged

- CI went green on `#330` (`build-and-test-server` + `lint-and-build-client`
  both passed) and Boss merged it into `master`.
- `#328` (TASK-001) and `#329` (TASK-002) still need their own branches
  synced with `master` (merge or rebase) to pick up the fix before their CI
  will go green — that's for whoever owns those branches next, not done as
  part of this run to avoid touching another session's branch.

## Open Items
- Waiting on Boss's answer on target audience / seed channel for growth
  (see `.agent/boss/PM.md`) before sequencing the rest of the Growth &
  Retention backlog beyond the SEO item now underway.
- `TASK-001` (PR #328) and `TASK-002` (PR #329) are implemented; still need
  `master` merged into their branches to clear CI, plus Tech Lead review —
  not unassigned.
- `TASK-003` is READY and genuinely unclaimed; blocked on `TASK-001`/
  `TASK-002` merging first per its own dependency notes.
- `#330` (CI fix) merged — closed out.
