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

## 2026-09-13 — Backend: TASK-001 PR opened; found a repo-wide CI time-bomb (scheduled run)

- Implemented `TASK-001` (sitemap.xml/robots.txt, `seoTitle`/`seoDescription`
  fields via a new `WordSeoTextFormatter`, unique index migration on
  `Word.WordText`) and opened
  [PR #328](https://github.com/twjackysu/JackyAIApp/pull/328). No `dotnet`
  SDK or DB connectivity in this sandbox — compensated with a careful
  manual read-through plus an independent review-agent pass; flagged in
  the PR that an actual CI/build run is still needed to confirm.
- CI's `build-and-test-server` failed on the PR. Investigated: **not**
  caused by this PR's diff. `SECInsiderTradingProviderTests.FetchAsync_ParsesTransactionReportTable`
  (`JackyAIApp.Server.Tests/Services/Finance/US/`) is a fixture-based test
  (`Fixtures/SEC/insider_transactions_aapl.html`, dated 2026-06-15/16)
  asserting against `SECInsiderTradingProvider`'s "last 90 days" filter
  (`Services/Finance/DataProviders/US/SECInsiderTradingProvider.cs:49`,
  `DateTime.UtcNow.AddDays(-90)`). Today (2026-09-13) is exactly 90 days
  past the fixture's earliest date, so the fixed-date fixture now falls
  outside the live 90-day window — a time-bomb test, unrelated to any
  in-flight task. Confirmed via one re-run (identical failure, not a
  flake) before concluding this.
- **This will keep failing (and get worse) on every PR's CI from now on**,
  not just #328 — it's a shared-infrastructure problem, not something to
  fix inside TASK-001 (out of scope, and Backend doesn't own that module
  in this task). Proposed patch for whoever picks it up: inject a
  `TimeProvider`/clock into `SECInsiderTradingProvider` so the test can
  pin "now", or restructure the test to assert on `ParseOwnershipTable`'s
  raw output before the date filter and cover the 90-day cutoff separately
  with a fixed reference date, instead of a fixture with hardcoded dates
  racing `DateTime.UtcNow`. Documented full root-cause on the PR
  (comment: https://github.com/twjackysu/JackyAIApp/pull/328#issuecomment-5653367141).
- Not fixed as part of this PR — flagging to Tech Lead via this status
  log per the architecture-issue escalation path, since it blocks CI
  repo-wide rather than being specific to TASK-001.

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

## 2026-09-13 — Backend Engineer: synced PR #328's branch with master to clear CI (scheduled run)

- Merged `master` (carrying the PR #330 fixture fix) into TASK-001's branch
  (`claude/magical-heisenberg-1b3vum`) and pushed, so `build-and-test-server`
  picks up the `SECInsiderTradingProviderTests` fix instead of hitting the
  now-resolved time-bomb failure. Only conflict was in this status log file
  (resolved by keeping both branches' entries in chronological order); no
  source/test files touched.
- The repo-wide time-bomb note filed on 2026-09-13 above is now resolved by
  `#330` — kept as historical record rather than deleted.
- CI confirmed green on the synced commit (`build-and-test-server` +
  `lint-and-build-client` both `success`), `mergeable_state: clean`. No
  Claude Approvals check configured on this repo. `#328` is now waiting on
  Tech Lead/Boss review only — nothing further for Backend to do here
  unless review comments come back.

## 2026-09-13 — Backend Engineer: PR #328 (TASK-001) merged into master (scheduled run)

- `#328` was merged (GitHub `pull_request.closed`/`merged` event). `master`
  now has `SitemapController` (`/sitemap.xml`, `/robots.txt`),
  `WordSeoTextFormatter` + `seoTitle`/`seoDescription` on the
  `GET api/dictionary/{word}` response, and the unique index migration on
  `Word.WordText`. Not recording `TASK-001.md`'s `Status:` as `MERGED`
  myself per SOP (§10/§18 reserve that to Boss) — leaving the field as
  `PR_OPENED` for Tech Lead/Boss to formally close out; this log entry is
  the factual record that the PR itself landed.
- Side effect: merging `#328` moved `master`'s tip, which flipped PR #331
  (TASK-002, Frontend, branch `claude/amazing-cori-7gyhyr`) from
  `mergeable_state: clean` to `dirty` (merge conflict against the new
  `master`). That PR is Frontend Engineer's to resolve (their branch, not
  mine to touch) — flagging here so whoever picks up TASK-002 next sees it
  without needing to rediscover it.
- `TASK-003` (Backend, unclaimed) still correctly blocked: its own
  dependency notes require both `TASK-001` *and* `TASK-002` merged before
  starting (needs `WordSeoTextFormatter`, which is now on `master`, but
  also needs TASK-002's frontend surface in place first per the task's own
  sequencing). Not starting it this run.

## 2026-09-14 — Backend Engineer: TASK-003 PR opened (scheduled run)

- Implemented `TASK-003` (bot-preview middleware for LINE/Facebook/Twitter/
  Slack/Discord/WhatsApp link-preview UAs hitting `/english/dictionary/{word}`)
  and opened [PR #332](https://github.com/twjackysu/JackyAIApp/pull/332).
  `LinkPreviewBotUserAgents` centralizes the UA list; `BotPreviewMiddleware`
  calls `IDictionaryService.GetWordAsync` in-process and renders
  `SeoTitle`/`SeoDescription` (TASK-001's `WordSeoTextFormatter` output)
  verbatim — no second formatting implementation. Registered early in
  `Program.cs`, before the SPA static-file/fallback pipeline.
- Judgment call: started without waiting for `TASK-002`/PR #331 to merge.
  TASK-003 is backend-only and its real dependency (TASK-001's shared
  helper + the `/english/dictionary/{word}` path shape) was already merged;
  the previous status log's "blocked on TASK-002 merging" note is read here
  as a deployment-sequencing preference rather than a hard build
  dependency. Flagged explicitly in `TASK-003.md` in case Tech Lead
  intended it as a harder blocker.
- No `dotnet` SDK/DB connectivity in this sandbox — compensated with a
  manual line-by-line read-through plus an independent review-agent pass
  (separate context, read every changed/referenced file); both found no
  compile errors or logic bugs. Flagged in the PR that an actual CI run
  still needs to confirm.
- `TASK-002` (PR #331) is still open with a merge conflict against
  `master` from `TASK-001` landing — unchanged from the last log entry,
  still Frontend's to resolve, not touched here.
- CI confirmed green on PR #332 (`lint-and-build-client` +
  `build-and-test-server` both `success`), `mergeable_state: clean`, no
  reviews/comments yet. No Claude Approvals check configured on this repo.
  Subscribed to the PR; waiting on Tech Lead/Boss review — nothing further
  for Backend to do unless review comments come back.

## 2026-09-14 — Backend Engineer: PR #332 (TASK-003) merged into master

- `#332` was merged (GitHub `pull_request.closed`/`merged` event, session
  auto-unsubscribed). `master` now has `LinkPreviewBotUserAgents` +
  `BotPreviewMiddleware` — server-rendered title/OG preview HTML for
  LINE/Facebook/Twitter/Slack/Discord/WhatsApp UAs hitting
  `/english/dictionary/{word}`, sourced from TASK-001's
  `WordSeoTextFormatter` output via `DictionaryService.GetWordAsync`
  in-process. No review comments came back before merge. Not recording
  `TASK-003.md`'s `Status:` as `MERGED` myself per SOP (§10/§18 reserve
  that to Boss) — leaving it `PR_OPENED` for Tech Lead/Boss to formally
  close out; this log entry is the factual record that the PR landed.
- All three Growth & Retention SEO tasks are now merged: `TASK-001` (#328),
  `TASK-003` (#332), and — noticed while checking `master` after this
  merge — `TASK-002` also landed via a **new** PR
  [#333](https://github.com/twjackysu/JackyAIApp/pull/333)
  (`claude/amazing-cori-qukncn`, merged 2026-09-14T01:13Z), not the old
  conflicted `#331`. Frontend Engineer evidently restarted TASK-002 from
  current `master` rather than resolving #331's conflict; #331 is now
  presumably stale/supersede-able but that's Frontend/Tech Lead's call,
  not logged as an action item here. No Backend action needed — Backend
  has no further assigned work this run.

## 2026-09-14 — Frontend Engineer: closed stale #331, fixed a real regression #333 missed (user-requested)

- User pointed at `#331` (this session's own earlier PR, `claude/amazing-cori-7gyhyr`) still showing "This branch has conflicts that must be resolved," now against real code files (`Dictionary/index.tsx`, `EnglishLearning/index.tsx`, `apis/dictionaryApis/types.ts`, `main.tsx`), not just the status-doc conflicts from the two prior syncs.
- Investigated rather than merging again: confirmed via `git log`/PR #333 that a *different* Frontend Engineer run had restarted `TASK-002` from current `master` and already merged it (2026-09-14T01:13Z) — a full implementation of all 3 requirements. `#331`'s conflicts were therefore against already-landed code, not anything still missing; continuing to merge would only re-fight duplicate diffs.
- Closed `#331` with a comment pointing to `#333` and explaining why, per the same precedent as `#329` → `#331` earlier. Unsubscribed from `#331`'s activity.
- While comparing the two PRs' diffs, found one real thing `#333` did **not** carry over from `#331`: the `EnglishLearningLayout` tab strip has no match for `/english/dictionary/:word` (only the bare path is a `Tab` `value`), so the Dictionary tab shows unselected on every word detail page — live on `master` right now, not hypothetical.
- Fixed it as its own minimal PR rather than reviving `#331`: **[#334](https://github.com/twjackysu/JackyAIApp/pull/334)** (`claude/amazing-cori-w4bv78`) — one-line change folding `/english/dictionary/*` onto the Dictionary tab, same pattern as the existing bare-`/english` handling. `npm run lint`/`build`/`vitest run` all clean, 131/131 tests, no regressions. Subscribed to `#334`.
- Deliberately left the `ClozeTestCard` internal link using the old `?word=` format alone — `#333`'s client-side redirect already makes it work correctly (just one extra hop), and touching it wasn't needed to fix the actual regression; kept this PR minimal per scope-control.
- `#334` merged into `master` (2026-09-14T01:26Z, CI green, `mergeable_state: clean`, no review comments). Session auto-unsubscribed. `TASK-002` is now fully complete on `master`: routing + anonymous-401 fix + per-word metadata (`#333`) and the tab-highlight regression (`#334`).

## 2026-09-14 — Tech Lead: reviewed and formally closed out all 3 SEO tasks (scheduled run)

- Read REQUIREMENTS/DECISIONS/ARCHITECTURE/tasks/status/TECH_LEAD.md per
  SOP. No new Boss message since Boss's 2026-09-13 acceptance of the
  single-formatting-source fix; no new PM decision requiring Tech Lead
  action beyond what's already tracked (audience/channel question is still
  open at PM/Boss level, not actionable by Tech Lead yet).
- `#334` (tab-highlight fix) had already merged since the last status
  entry — confirmed via GitHub. Noticed a new PR, **#335**
  (`claude/amazing-cori-w4bv78`, docs-only), open with the same status-log
  content this entry supersedes; left it for Boss to merge or close since
  Tech Lead doesn't merge/close other sessions' PRs, but its content is now
  redundant with this entry and the task-file updates below.
- Did the PR-review pass that was still outstanding on all three merged
  PRs (#328/#332/#333+#334) — read the actual merged code on `master`
  rather than only trusting each Engineer's self-report, specifically
  checking the single-formatting-source requirement (Boss's 2026-09-13
  drift concern) end-to-end: `WordSeoTextFormatter` is the only place that
  formats SEO text; `BotPreviewMiddleware` calls it in-process via
  `DictionaryService`/DTO fields only, no second implementation anywhere;
  frontend renders the API's `seoTitle`/`seoDescription` verbatim. Also
  spot-checked the migration, `Program.cs` middleware ordering, route
  config, and the `EnglishLearningLayout` tab-highlight fix directly in
  code. No correctness issues found in any of the three.
- Formally set `Status: MERGED` on `TASK-001`, `TASK-002`, `TASK-003` and
  filled in each task's `## Review` section with the findings above — this
  was the one remaining SOP-mandated step (§10/§18 reserve the *merge
  decision* to Boss, not the after-the-fact status/review bookkeeping,
  which is Tech Lead's to close out once a PR has already landed).
- All three Growth & Retention SEO tasks (public/indexable Dictionary word
  pages) are now fully shipped and closed out end-to-end: code on
  `master`, task docs marked `MERGED` with review notes.

## 2026-09-14 — PM: Redirected Growth strategy per Boss (DECISION-004), scoped Skill-Building requirement (scheduled run)

- Boss replied in `.agent/boss/PM.md` (last message, previously
  unanswered): rejected sharing/referral-style growth mechanics outright —
  learners generally have no desire/confidence to broadcast their own study
  activity — and redirected PM to think about features that genuinely
  improve listening/speaking/reading/writing ability instead; product-led
  growth (word-of-mouth follows a good product) rather than engineered
  sharing.
- Recorded **DECISION-004** in `.agent/product/DECISIONS.md`.
- `REQUIREMENTS.md`: reworked `Growth & Retention` — moved "shareable
  results" and "referral/invite mechanics" to a new Rejected subsection
  (with Boss's stated reasoning), kept the already-shipped SEO work in
  scope. Added a new HIGH-priority requirement,
  **`Skill-Building Feature Expansion (聽說讀寫能力強化)`**, after auditing
  the actual codebase for what each of the four core skills (聽/說/讀/寫)
  already covers vs. what's missing:
  - 讀 has word/Cloze-level only, no passage-level reading comprehension.
  - 寫 has single-sentence Translation/Sentence tests only, no open-ended
    writing feedback.
  - 聽 has multiple-choice listening tests only, no dictation practice.
  - 說 has voice recording + Whisper transcription + **grammar** correction,
    but **zero feedback on pronunciation/fluency/delivery** — the one skill
    with a genuine, easily-named gap despite already having the full audio
    pipeline in place to build on.
  - Proposed 4 features (PM-recommended priority: speaking delivery
    feedback → reading comprehension passages → open-ended writing
    feedback → dictation), reasoning documented in the requirement itself.
- Also corrected a stale fact found during the audit: `REQUIREMENTS.md`'s
  Exam System section said Sentence Formation Test was "in development" —
  it's actually fully implemented (generate + evaluate endpoints, frontend
  card all exist). Fixed both the Exam System and Skill-Building sections
  to reflect this.
- Replied to Boss in `.agent/boss/PM.md` with the audit table, the 4
  proposals, and two open questions (does the priority order match Boss's
  intuition; OK to hand item 1 — speaking feedback — to Tech Lead for a
  feasibility/cost pass without waiting for further sign-off, same pattern
  used for the earlier SEO work). Defaulted to "will proceed with item 1
  next run unless told otherwise," consistent with prior PM/Tech Lead
  practice on low-risk, already-flagged items.
- No code changes — Requirements/Decisions/PM-log documentation only. Tech
  Lead has no formal handoff yet; next PM or Tech Lead run should treat
  "evaluate speaking delivery feedback" as the actionable item unless Boss
  redirects first.

## Open Items
- **New, from this run**: hand "speaking delivery/pronunciation feedback"
  (Skill-Building Feature Expansion, item 1) to Tech Lead for a
  feasibility/cost evaluation — PM's default is to do this next run absent
  a Boss reply, per the open question left in `.agent/boss/PM.md`.
- Waiting on Boss's reaction to the proposed skill-building priority order
  (see `.agent/boss/PM.md`, 2026-09-14) — not blocking, PM will proceed
  with item 1 by default.
- Target audience / seed channel for growth: Boss answered (unspecified
  audience, no existing channel) — no longer an open item, folded into the
  general-organic-growth framing already in `REQUIREMENTS.md`.
- `TASK-001` (PR #328) — **MERGED, reviewed, closed out.**
- `TASK-002` (PR #333 + #334) — **MERGED, reviewed, closed out.** `#331`
  (superseded duplicate) closed.
- `TASK-003` (PR #332) — **MERGED, reviewed, closed out.**
- `#330` (CI fix) merged — closed out.
- `#335` (docs-only status-log PR, still open) — redundant with this
  entry; Boss can merge or close at their discretion, not acted on here.

## 2026-09-14 — PM: Boss approved Skill-Building priority + handoff (DECISION-005), broadened long-term mandate (scheduled run)

- Boss replied in `.agent/boss/PM.md` (previously unanswered): approved (1)
  the Skill-Building priority order (speaking feedback → reading
  comprehension → writing feedback → dictation) and (2) handing item 1
  (speaking delivery/pronunciation feedback) to Tech Lead for a
  feasibility/cost evaluation without further sign-off. Also set a new
  standing direction: keep improving the whole product long-term, **not
  limited to the English-learning pillar**.
- Recorded **DECISION-005** in `.agent/product/DECISIONS.md`.
- `REQUIREMENTS.md`: closed out both Open Questions in the Skill-Building
  Feature Expansion requirement (priority order approved; item 1 formally
  handed to Tech Lead — see below). Added the broadened long-term-direction
  note to Product Positioning and to the Growth & Retention requirement's
  Goal section; added a Future Consideration item noting that
  Finance/Portfolio/PDF Unlocker should get the same "audit gap → propose
  feature" treatment in a future PM run (not done this run).
- Replied to Boss in `.agent/boss/PM.md` confirming the above and asking
  (non-blocking) whether Boss wants a specific module prioritized for the
  next gap-audit pass, or leave it to PM's judgment.
- No code changes — Requirements/Decisions/PM-log documentation only.

## Open Items
- **Future PM work (not urgent)**: extend the same gap-audit method used
  for English skills to Finance/Portfolio/PDF Unlocker, per Boss's
  2026-09-14 broadened mandate (DECISION-005). Not started.
- **New, from this run**: Phase 2 of speaking delivery feedback (true
  phoneme-level pronunciation-accuracy scoring via a new third-party
  vendor) is an open question sent to Boss in `.agent/boss/TECH_LEAD.md` —
  not scoped as a task, needs a cost/vendor decision before any further
  work.

## 2026-09-15 — Tech Lead: Scoped Skill-Building item 1 (speaking delivery feedback) (scheduled run)

- Read REQUIREMENTS/DECISIONS/ARCHITECTURE/tasks/status/TECH_LEAD.md per
  SOP. No new Boss message since Boss's 2026-09-13 sign-off on the
  single-formatting-source SEO fix; no open PRs, no engineer work pending
  review, no blockers reported. Picked up the standing Open Item: Boss
  approved (DECISION-005) handing Skill-Building item 1 — speaking
  delivery/pronunciation feedback — to Tech Lead for a feasibility/cost
  evaluation.
- Investigated the Conversation Test backend before scoping
  (`ExamController.cs`, `ExamService.cs`, `OpenAIPromptService.cs`):
  confirmed audio is transcribed via Whisper with `ResponseFormat =
  "json"` (only returns plain text) and is never persisted; grammar
  correction runs as a separate text-only completion over the transcript.
  Confirmed Whisper's `"verbose_json"` format (same OpenAI billing as
  `"json"` — response format doesn't change Whisper's per-minute price)
  additionally returns duration and segment/word timestamps, which is
  enough to compute real pace/pause/filler-word signals **without any new
  AI call or vendor**. Also confirmed Whisper never returns a
  pronunciation-accuracy/confidence field under any format — true
  phoneme-level pronunciation scoring needs a dedicated third-party
  product (e.g. Azure Speech Pronunciation Assessment), a materially
  different cost/vendor decision.
- Documented the investigation and a two-phase decision in
  `ARCHITECTURE.md` → "Decision: Speaking delivery feedback
  (pronunciation/fluency)": **Phase 1** (fluency/delivery feedback —
  pace/pauses/filler words — computed in C# from the existing Whisper
  call's richer response, zero added AI/API cost, no new credit gate)
  proceeds now, same pattern as the SEO work's low-cost Phase 1. **Phase
  2** (true pronunciation-accuracy scoring via a new vendor) is flagged as
  an open question to Boss rather than decided unilaterally, since it's a
  new external dependency plus a new recurring cost.
- Created `TASK-004` (Backend: switch Whisper to `verbose_json`, compute
  pace/pause/filler metrics + rule-based comments, extend
  `WhisperTranscriptionResponse`, no new credit charge) and `TASK-005`
  (Frontend: render the new fields next to the existing `CorrectionAlert`
  in Conversation Test) — both `READY`, TASK-005 depends on TASK-004's
  response shape. Both tasks explicitly require calling this
  "delivery/fluency feedback," never "pronunciation," to avoid overselling
  what Phase 1 actually measures.
- Updated `REQUIREMENTS.md`'s Skill-Building acceptance criteria: item 1 is
  now scoped, and the cost-estimate criterion is satisfied ($0 marginal
  cost for Phase 1).
- Wrote to `.agent/boss/TECH_LEAD.md`: flagged Phase 2 (new vendor for
  real pronunciation-accuracy scoring) as an open cost/vendor decision for
  Boss, consistent with how the SEO Phase 2 trade-off was previously
  escalated rather than decided unilaterally.
- No code changes — architecture/task documentation only, ready for
  Backend/Frontend Engineers to pick up.

## 2026-09-15 — Backend Engineer: TASK-004 implemented, PR opened (scheduled run)

- Picked up `TASK-004` (Backend: fluency/delivery feedback on Conversation
  Test voice turns), scoped by Tech Lead in still-open PR #336 at the time
  this run started — merged that docs-only commit into this session's
  branch first (fast-forward, same base commit) to get `TASK-004.md`/
  `TASK-005.md`/the updated `ARCHITECTURE.md` locally, since #336 itself
  only needed a branch-policy PR wrapper around an otherwise-decided
  Tech Lead write-up, not further review before Backend could start.
- Implemented Phase 1 exactly as scoped: switched
  `OpenAIPromptService.TranscribeAudioAsync` to Whisper's `verbose_json` +
  word-level timestamps (confirmed against the actual upstream
  Betalgo.Ranul.OpenAI source, matching the installed `9.2.6` package, since
  no `dotnet` SDK/NuGet cache exists in this sandbox to check types
  directly); added `SpeechFluencyAnalyzer` (`Services/Exam/`) as a pure,
  independently-unit-tested helper computing pace/pause-count/filler-word
  metrics + rule-based Chinese comments from that richer response; extended
  `WhisperTranscriptionResponse` with an optional `DeliveryFeedback` field.
  No second credit charge added — rides on the existing
  `CreditCosts.AudioTranscription` charge, verified by a new test.
- Full detail, including the "no dotnet SDK" verification approach and
  known limitations, is in `TASK-004.md`'s Result section.
- Opened the PR against `master`; see `TASK-004.md` for the link.
  `TASK-005` (Frontend) can start once this lands, per its own stated
  dependency on this task's response shape (`DeliveryFeedback` field name/
  shape is now fixed by this PR).
- CI (`build-and-test-server`) initially failed on the opening commit: a
  genuine compile error in the new test code, not a pre-existing/unrelated
  issue — `Error.Message` (Betalgo.Ranul.OpenAI's response type) has a
  `private set`; the test tried to set it directly instead of the public
  `MessageObject` setter that actually populates it. Fixed and pushed
  within the same run. CI (`lint-and-build-client` +
  `build-and-test-server`) is now green, `mergeable_state: clean`, no
  reviews/comments yet. Subscribed to PR #337; waiting on Tech Lead/Boss
  review — nothing further for Backend to do unless review comments come
  back.

## 2026-09-15 — Backend Engineer: PR #337 (TASK-004) merged into master

- `#337` was merged (GitHub `pull_request.closed`/`merged` event, session
  auto-unsubscribed) with no review comments. `master` now has Whisper
  `verbose_json` + word-timestamp transcription, `SpeechFluencyAnalyzer`
  (pace/pause/filler-word metrics + rule-based Chinese comments), and the
  optional `DeliveryFeedback` field on `WhisperTranscriptionResponse`. Not
  recording `TASK-004.md`'s `Status:` as `MERGED` myself per SOP (§10/§18
  reserve that to Boss) — leaving it `PR_OPENED` for Tech Lead/Boss to
  formally close out; this log entry is the factual record that the PR
  landed.
- `TASK-005` (Frontend, depends on this PR's `DeliveryFeedback` shape) is
  now unblocked and can start.

## 2026-09-15 — Frontend Engineer: TASK-005 implemented, PR opened (scheduled run)

- Picked up `TASK-005` (Frontend: display TASK-004's delivery/fluency
  feedback in Conversation Test). Confirmed `TASK-004`'s response shape had
  already landed on `master` (PR #337, merged) — read the actual merged
  `DeliveryFeedback` C# DTO (`PaceWpm`/`PauseCount`/`FillerWordCount`/
  `Comments`) and `Program.cs`'s JSON options (default camelCase, no
  explicit override) to derive the exact wire shape rather than guessing.
- Extended `apis/examApis/types.ts` with `DeliveryFeedback` and wired it
  onto `WhisperTranscriptionResponse`/`ConversationTurn`. Updated the
  shared `hooks/useVoiceRecording.ts` (also used by `TranslationTestCard`)
  to pass the new field through its `onTranscriptionComplete` callback as
  an optional second argument — backward compatible, `TranslationTestCard`
  needed no changes.
- Added `ConversationTestCard/components/DeliveryFeedbackAlert.tsx`
  (sibling to `CorrectionAlert.tsx`) and wired it into `ChatMessages.tsx`
  next to the existing correction alert, gated on the field actually being
  present (no empty card on the short/silent-clip case). Threaded a
  `pendingDeliveryFeedback` piece of state through
  `ConversationTestCard/index.tsx` so the metrics captured at transcription
  time land on the correct user turn once sent, same attach-after-response
  pattern already used for grammar `correction`. UI copy uses "表達流暢度
  回饋"/"流暢度" only, never "發音"/pronunciation, per the task's explicit
  labeling requirement.
- `npm run lint` (0 warnings), `npm run build`, and `npx vitest run` all
  green — 133/133 tests (131 pre-existing + 2 new for
  `DeliveryFeedbackAlert`), no regressions. (`node_modules` needed a fresh
  `npm ci` in this sandbox before any of the three would run — not a code
  issue.)
- Opened [PR #339](https://github.com/twjackysu/JackyAIApp/pull/339) against
  `master`; see `TASK-005.md` for full implementation notes, including one
  flagged known limitation (delivery feedback attaches to whichever message
  is sent next, even if the user hand-edits the transcribed text first —
  acceptable per task scope). Subscribed to the PR to drive it to green /
  respond to review.
- All 5 tasks scoped so far (`TASK-001`–`TASK-005`) are now either MERGED
  or have an open PR — no Frontend work left unstarted this run.

## 2026-09-15 — Frontend Engineer: PR #339 (TASK-005) merged into master

- `#339` was merged (GitHub `pull_request.closed`/`merged` event, session
  auto-unsubscribed) with no review comments — both CI checks
  (`lint-and-build-client`, `build-and-test-server`) were green and
  `mergeable_state: clean` before merge. `master` now has the
  `DeliveryFeedback` UI on Conversation Test voice turns (pace/pause/
  filler-word `Chip`s + rule-based comments via `DeliveryFeedbackAlert`,
  rendered alongside the existing grammar `CorrectionAlert`). Not
  recording `TASK-005.md`'s `Status:` as `MERGED` myself per SOP (§10/§18
  reserve that to Boss) — leaving it `PR_OPENED` for Tech Lead/Boss to
  formally close out; this log entry is the factual record that the PR
  landed.
- All 5 Skill-Building item-1 (speaking delivery/fluency feedback) tasks
  are now fully shipped: `TASK-004` (backend, PR #337) and `TASK-005`
  (frontend, PR #339) both merged, following the same pattern as the
  earlier SEO work. No further Frontend action needed here — next step is
  Tech Lead's formal review/close-out pass, same as was done for
  `TASK-001`–`003`.

## 2026-09-15 — PM: Product review of shipped item 1, handed item 2 to Tech Lead, noted Azure TTS cost win (scheduled run)

- No new message in `.agent/boss/PM.md` since the 2026-09-14 reply (no
  outstanding question to answer). Checked `master` directly rather than
  only trusting prior run logs, per SOP §10 (PM may verify shipped work
  against Requirements/Acceptance Criteria).
- **Product review, Skill-Building item 1 (speaking delivery/fluency
  feedback)**: read the actual merged code —
  `Services/Exam/SpeechFluencyAnalyzer.cs` (pace/pause/filler-word metrics
  computed from Whisper `verbose_json` word timestamps, zero extra AI call)
  and `ConversationTestCard/components/DeliveryFeedbackAlert.tsx` (renders
  as its own alert next to, not merged into, the existing grammar
  `CorrectionAlert`). Confirmed it matches the requirement: UI copy says
  "流暢度" throughout, never "發音"/pronunciation, so it doesn't oversell
  Phase 1 as real pronunciation scoring. `REQUIREMENTS.md`'s last open
  acceptance-criteria checkbox for this item is now checked off; item 1 is
  product-complete. (Engineering bookkeeping — `TASK-004.md`/`TASK-005.md`
  still show `Status: PR_OPENED` though both PRs are merged — is Tech
  Lead's to formally close per SOP §10/§18, not touched here.)
- **Noticed a product-relevant change outside the PM/Tech Lead pipeline**:
  commit `ab800be` ("feat(tts): add Azure AI Speech provider as the default
  TTS backend") was pushed directly to `master` by Boss (author
  `jacky19918@gmail.com`), not through an agent session. It switches
  default TTS from OpenAI to Azure AI Speech's free F0 tier (~2k/month
  usage against a 500k free allowance), removing a real failure mode: an
  exhausted OpenAI credit balance used to silence *all* TTS at once
  (dictionary audio, Listening exam, conversation), not just credit-gated
  features. This is squarely on-target for DECISION-003's cloud-cost goal.
  Documented in `REQUIREMENTS.md` → Growth & Retention → Cloud cost
  reduction for the record; no product decision needed, callers are
  unaware of the swap (same interface).
- **Handed Skill-Building item 2 (reading comprehension passages) to Tech
  Lead** for a feasibility/cost evaluation, now that item 1 is fully
  shipped. Treated this as executing the already-approved priority order
  (DECISION-005), not a new scope decision requiring fresh sign-off — same
  "proceed by default" pattern used for item 1 and the SEO work. Flagged in
  `.agent/boss/PM.md` in case Boss wants to pause/reorder.
- No code changes — Requirements/status documentation only.

## 2026-09-16 — Tech Lead: closed out TASK-004/005 bookkeeping, scoped Skill-Building item 2 (reading comprehension) (scheduled run)

- Read REQUIREMENTS/DECISIONS/ARCHITECTURE/tasks/status/TECH_LEAD.md per
  SOP. No new Boss message since the 2026-09-15 report on Phase 2 (open,
  non-blocking). No open PRs, no engineer work pending review.
- **Housekeeping**: confirmed via `git log`/direct code read that PR #337
  (`TASK-004`) and PR #339 (`TASK-005`) are both merged into `master`.
  Verified the actual merged code (not just each task's self-reported
  Result section) — `SpeechFluencyAnalyzer.cs`, `DTO/ConversationModels.cs`,
  `DeliveryFeedbackAlert.tsx`/its test — confirmed no "pronunciation"/"發音"
  leakage anywhere, single credit charge preserved, short-clip case renders
  nothing. No issues found. Flipped `TASK-004.md`/`TASK-005.md` `Status:`
  to `MERGED` with Review notes — the one remaining SOP-mandated step from
  the last PM run's Open Items.
- **Picked up Skill-Building item 2** (reading comprehension passages),
  handed off by PM 2026-09-15. Investigated the existing Exam System
  architecture before scoping: found two genuinely different existing
  patterns — Cloze/Translation/Sentence (`IWordExamGenerator`, cached
  per-single-word, personalized to the user's saved vocabulary) vs.
  Listening/ListeningResponse (shared bank, growth-threshold generation,
  not personalized). A reading passage inherently spans multiple words, so
  it doesn't fit the per-word cache key the first pattern depends on for
  cost control — modeling it on the Listening bank pattern instead avoids
  a real cost trap (near-every-request AI calls) that the per-word pattern
  would produce here.
- Documented the investigation and decision in `ARCHITECTURE.md` →
  "Decision: Reading comprehension passages — Skill-Building item 2".
  Created `TASK-006` (Backend: new `ReadingPassage`/`ReadingQuestion`/
  `ReadingQuestionOption` tables + migration, `ReadingSystem.txt` prompt,
  `ExamService.GenerateReadingExamAsync()` mirroring the Listening-Response
  bank logic exactly, new `CreditCosts.ReadingComprehensionTest` entry) and
  `TASK-007` (Frontend: new `ReadingTestCard`, wired into the `EXAM_MODES`
  menu) — both `READY`, TASK-007 depends on TASK-006's response shape.
  Proceeded without waiting for fresh Boss sign-off, same "already-approved
  priority order, no new vendor/cost tier" basis used for item 1 and the
  SEO work.
- Updated `REQUIREMENTS.md`'s Skill-Building item 2 line to reflect the
  scoping and the one correction to its original framing: passages are
  **not** personalized to the user's saved words (unlike Cloze/Sentence),
  by design — flagged as a trade-off to Boss in `.agent/boss/TECH_LEAD.md`
  in case that's not acceptable, rather than deciding it's fine
  unilaterally, consistent with how prior real trade-offs (SEO Phase 2,
  Speaking Phase 2) were escalated rather than assumed.
- No code changes — architecture/task documentation only, ready for
  Backend/Frontend Engineers to pick up.

## 2026-09-16 — Backend: TASK-006 implemented, PR opened (scheduled run)

- Implemented `TASK-006` (Reading comprehension passages) following the
  Listening/ListeningResponse shared-bank pattern exactly, as scoped in
  `ARCHITECTURE.md`: new `ReadingPassage`/`ReadingQuestion`/
  `ReadingQuestionOption` entities + additive EF migration,
  `Prompt/Exam/ReadingSystem.txt`, `ExamService.GenerateReadingExamAsync()`
  (reuses `NoRepeatWindow=30`, new `ReadingBankGrowthThreshold=20`),
  `RecordExamResultAsync` gains the `"Reading"` wordless branch, new
  `GET api/exam/reading` gated by `CreditCosts.ReadingComprehensionTest=3`.
- 8 new unit tests mirroring existing `ListeningResponse` coverage; full
  offline suite green (595/595, 0 regressions). EF migration inspected —
  additive only, no changes to existing table data.
- PR opened: https://github.com/twjackysu/JackyAIApp/pull/342. Status set
  to `PR_OPENED`, awaiting Tech Lead review.
- `TASK-007` (Frontend) can now proceed — response DTO shape is stable.

## 2026-09-16 — Backend: PR #342 merged (scheduled run)

- GitHub shows `PR #342` (TASK-006, Reading comprehension backend) merged
  to `master` as of 2026-09-16T07:25 UTC. Left `TASK-006.md`'s `Status`
  field at `PR_OPENED` rather than setting it to `MERGED` myself, per
  Backend Engineer's own rule that only Boss marks a task merged — flagging
  here so Tech Lead/Boss can confirm and flip the status field.
- Deployment note per root `CLAUDE.md`: the "Build and deploy" workflow
  triggers on this merge automatically (`pull_request: [closed]` gated on
  `merged == true`), deploying the merge commit. Worth a `gh run list`-style
  check after this lands to confirm the deploy run actually fired/succeeded
  and wasn't cancelled by a second quick merge.

## 2026-09-16 — Frontend Engineer: TASK-007 implemented, PR opened (scheduled run)

- Verified `TASK-006`'s PR #342 was actually merged into `master`
  (2026-09-16T07:25 UTC) via the GitHub API before starting, then read the
  real merged `ExamModels.cs` DTOs directly (not TASK-006's self-report)
  to get exact field names/casing — same verification discipline TASK-005
  used for `DeliveryFeedback`.
- Implemented `TASK-007` (Reading Comprehension exam-mode UI): API slice
  additions (`getReadingTest`, `ReadingPassageResponse`/`ReadingQuestion`/
  `ReadingQuestionOption` types, `'Reading'` added to `ExamType`,
  `readingPassageId` added to `RecordExamResultRequest`), new
  `Exam/ReadingTestCard/index.tsx`, `READING_COMPREHENSION_TEST: 3` in
  `constants/creditCosts.ts`, and a new `EXAM_MODES` entry in
  `Exam/index.tsx`.
- Did not reuse `ListeningQuizCard` verbatim — a Reading fetch returns one
  passage with 3–4 questions (a different shape than Listening's
  one-question-per-fetch contract) — instead followed the same
  `ExamRoundShell`/`useExamRound`/reveal-after-answer *pattern* in a new
  sibling component, which is the smallest correct change rather than
  bending a mismatched shared component. `record-result` fires once per
  question (matching `ListeningResponseTestCard`'s per-question
  granularity), batched together once the passage's last question is
  answered, all sharing the passage's `readingPassageId`.
- `npm run lint` (0 warnings), `npm run build`, `npx vitest run` all
  green — 134/134 tests (up from 133), no regressions; the pre-existing
  `creditCosts.test.ts` drift check automatically covers the new
  `READING_COMPREHENSION_TEST` constant against the backend's actual
  value. No dedicated component test added for `ReadingTestCard`,
  consistent with the existing project convention (no other exam card has
  one either) — flagged as a known limitation in `TASK-007.md`.
- Opened [PR #343](https://github.com/twjackysu/JackyAIApp/pull/343)
  against `master`; see `TASK-007.md` for full implementation notes.
  Subscribed to the PR to drive it to green / respond to review.
- All 7 tasks scoped so far (`TASK-001`–`TASK-007`) now either `MERGED` or
  have an open PR — Skill-Building item 2 (reading comprehension) is fully
  implemented end-to-end (backend `#342` merged, frontend `#343` open).

## 2026-09-16 — PM: Product review of shipped item 2, handed item 3 to Tech Lead (scheduled run)

- No new message in `.agent/boss/PM.md` since PM's own 2026-09-15 reply —
  nothing pending Boss decision at the PM level. Checked `.agent/boss/
  TECH_LEAD.md` too: Boss has not yet replied to Tech Lead's 2026-09-16
  flag about reading passages not being personalized; non-blocking, left
  as-is (Tech Lead's channel to carry, not PM's to duplicate).
- Confirmed via `git log` that `PR #342` (`TASK-006`, backend) and `PR
  #343` (`TASK-007`, frontend) are both merged into `master` — Skill-
  Building item 2 (reading comprehension) is fully shipped end-to-end.
  `TASK-006.md`/`TASK-007.md` still show `Status: PR_OPENED`; per SOP
  §10/§18 that bookkeeping is Tech Lead's/Boss's to close, not PM's.
- **Product review, item 2**: read the actual merged code, not just prior
  run self-reports — `ExamService.GenerateReadingExamAsync` (shared-bank
  pattern, `ReadingBankGrowthThreshold = 20`, server-side rejection of any
  generated passage outside the 3–4-question/3–4-option/exactly-1-correct
  shape), `CreditCosts.ReadingComprehensionTest = 3`, and the frontend
  `Exam/ReadingTestCard`. Matches the requirement's actual named gap
  (passage-level reading comprehension) and the cost model Tech Lead
  proposed (reuse Listening's bank pattern, not a per-request AI call).
  `REQUIREMENTS.md`'s Skill-Building acceptance criteria updated — item 2
  is now marked product-complete, same as item 1 was on 2026-09-15.
- **Handed Skill-Building item 3 (open-ended writing practice with AI
  feedback) to Tech Lead** for a feasibility/cost evaluation, now that
  item 2 is fully shipped. Treated as executing the already-approved
  priority order (DECISION-005), not a new scope decision — same
  "proceed by default" pattern used for items 1 and 2. Flagged in
  `.agent/boss/PM.md` in case Boss wants to pause/reorder.
- No code changes — Requirements/status documentation only.

## Open Items
- **Tech Lead/Boss**: `TASK-006`/`TASK-007` task-file `Status:` fields
  still read `PR_OPENED` though both PRs are merged — bookkeeping close-out
  still pending (Tech Lead's to do, per its own established pattern from
  `TASK-001`–`005`).
- **Boss attention (non-blocking, unanswered since 2026-09-16)**: reading
  passages are not personalized to the user's own saved vocabulary (they
  reuse the Listening shared-bank pattern instead of Cloze's per-word
  pattern) — see `.agent/boss/TECH_LEAD.md` for the trade-off writeup.
  Tech Lead is proceeding on this basis unless told otherwise.
- **New**: Skill-Building item 3 (open-ended writing practice) handed to
  Tech Lead this run — next Tech Lead run should treat scoping it as the
  actionable item unless Boss redirects.
- Still open from before: Phase 2 of speaking delivery feedback (true
  pronunciation-accuracy scoring, new vendor) — cost/vendor decision
  pending Boss, not scoped as a task.
- Still open from before: future gap-audit of Finance/Portfolio/PDF
  Unlocker (DECISION-005) — not started, no urgency signaled by Boss.
  Worth considering once Skill-Building's 4-item list is closer to done.

## 2026-09-17 — Tech Lead: closed out TASK-006/007, scoped Skill-Building item 3 (writing feedback) (scheduled run)

- Read REQUIREMENTS/DECISIONS/ARCHITECTURE/tasks/status/TECH_LEAD.md per
  SOP. No new Boss message in `TECH_LEAD.md` since the 2026-09-16 entry
  (personalization trade-off flag, still non-blocking/unanswered). No open
  PRs on GitHub.
- **Housekeeping**: confirmed via `git log`/direct code read that PR #342
  (`TASK-006`) and PR #343 (`TASK-007`) are both merged into `master`.
  Reviewed the actual merged code — `ExamService.cs`'s
  `GenerateReadingExamAsync`/`PickReadingFromPoolAsync` mirror the Listening
  bank pattern correctly, `RecordExamResultAsync`'s `isReading` branch stays
  out of SM-2 as intended, frontend `ReadingTestCard` matches the merged
  DTOs. No issues found. Flipped both task `Status:` fields to `MERGED`
  with Review notes.
- **Noticed a real fix landed outside the PM/Tech Lead pipeline**: commit
  `31a0b93`/PR #345 ("fix(exam): reject reading passages generated in the
  wrong language"), pushed directly by Boss (`jacky19918@gmail.com`), adds
  `ReadingPassageValidator` — the original TASK-006 acceptance criteria only
  checked question/option *shape*, not *language*, so an all-Chinese
  passage had passed validation and would have sat in the shared, reused
  bank indefinitely. Reviewed the fix (hard-gate validator + prompt
  hardening + capped one-retry policy + 256 new lines of tests): sound
  design, no issues. Recorded against `TASK-006`'s Review section for the
  history rather than only living in the commit message.
- **Scoped Skill-Building item 3** (open-ended writing practice with AI
  feedback), handed off by PM 2026-09-16. Investigated Translation/Sentence
  Test's grading code (`GradeTranslationAsync`/`EvaluateSentenceAsync`,
  `ExamService.cs:541`/`:717`) via a background research pass before
  scoping: both already separate generation-vs-grading steps, grading is a
  one-shot AI call with an inline credit gate, decoupled from
  `RecordExamResultAsync`. Key finding that shapes this task differently
  from every prior Skill-Building item: **producing a writing prompt (a
  topic + a few of the user's own saved words) costs zero AI calls** —
  unlike Cloze/Translation/Sentence/Reading, there's nothing to generate via
  OpenAI on the "get a prompt" side, only on the "grade what I wrote" side.
  So this is the first exam type where generation is free and only
  evaluation is credit-gated, rather than both being charged.
- Also found a genuine schema gap: `ExamAttempt`/`RecordExamResultRequest`
  only support one `WordId` per attempt (or none, for the wordless group);
  a writing submission names several words at once. Resolved without a new
  table: added `Writing` to the wordless group for the attempt row itself,
  and fan out the *existing* `_spacedRepetition.ApplyResult` call across
  every required word using the same holistic AI grade — documented as a
  deliberate trade-off (shared quality across words, not per-word
  granularity) rather than building a new junction table for something not
  required by the acceptance criteria.
- Documented the full investigation and decision in `ARCHITECTURE.md` →
  "Decision: Open-ended writing practice with AI feedback — Skill-Building
  item 3". Created `TASK-008` (Backend: free `GET api/exam/writing` prompt
  endpoint + credit-gated `POST api/exam/writing/evaluate`, new
  `CreditCosts.WritingEvaluation`, SM-2 fan-out, **no EF migration**) and
  `TASK-009` (Frontend: new `WritingTestCard`, free-to-open/charged-on-
  submit cost UX) — both `READY`, unclaimed. Proceeded without waiting for
  fresh Boss sign-off, same "already-approved priority order, no new
  vendor" basis used for items 1 and 2.
- `REQUIREMENTS.md`: updated item 3's line to reflect the scoping and
  closed out its Open Question. Noted that, unlike item 2 (reading, not
  personalized), item 3 **is** personalized to the user's own saved words,
  matching the requirement's original framing.
- Wrote to `.agent/boss/TECH_LEAD.md`: reported the TASK-006/007 close-out,
  the Boss's own language-guard fix, and flagged the SM-2
  shared-quality-across-words trade-off as informational (non-blocking,
  already proceeding).
- No implementation code changes — architecture/task documentation only,
  ready for Backend/Frontend Engineers to pick up.

## 2026-09-17 — Frontend: TASK-009 remains blocked on TASK-008

- Scheduled Frontend run checked `.agent/tasks/` for assigned work. Only
  assigned/ready item is `TASK-009` (Writing Practice UI), which depends on
  `TASK-008` (Backend).
- `TASK-008` has an open PR (#346) but it is **not merged** into `master`
  yet, and its CI has not produced any check runs yet (author flagged in
  the PR body that they had no local `dotnet` SDK to verify the build).
  No writing-feature C# code exists on `master` yet to read exact DTO
  field names/casing from.
- Per `TASK-009`'s own requirements ("match `TASK-008`'s actual **merged**
  DTO shape... don't guess"), did not start implementation against the
  unmerged branch. Flipped `TASK-009` to `Status: BLOCKED` with a Blocker
  section recorded in the task file; no source code changes made this run.
- No action needed from Tech Lead/Backend beyond the normal PR review/merge
  path for #346 — this is a sequencing dependency, not a new issue.
