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

## 2026-09-17 — Backend Engineer: TASK-008 implementation (scheduled run)

- Picked up `TASK-008` (Status: READY, Owner: Backend) — open-ended writing
  practice with AI feedback. Read the task, `ARCHITECTURE.md`'s writing
  decision, and `ExamService.cs`'s `GradeTranslationAsync`/
  `EvaluateSentenceAsync`/`RecordExamResultAsync` end-to-end before writing
  any code, per the task's own instruction to copy the grading shape rather
  than invent one.
- Implemented exactly as scoped: `GenerateWritingPromptAsync()` (free,
  topic rotation + up to 5 distinct `UserWords` via a new DB-level
  distinct-offset sampling helper, `RequiredWords: []` for an empty
  repository), `GradeWritingAsync()` (one-shot `GetCompletionAsync`,
  inline credit gate, new `Prompt/Exam/WritingEvaluationSystem.txt`), new
  `CreditCosts.WritingEvaluation = 3`, and a `"Writing"` branch in
  `RecordExamResultAsync` that fans the existing `ApplyResult` SM-2 call
  out across every required word sharing one holistic quality value. No EF
  migration, matching the architecture doc's explicit scoping constraint.
  Added unit tests for the new prompt/grading/record-result paths
  (not-credit-gated assertion, insufficient-credits path, unused-word
  `Used: false` case, SM-2 fan-out onto only the matching words).
- **Could not run `dotnet build`/`dotnet test`**: this session's sandbox has
  no .NET SDK, and installing one is blocked by the environment's network
  policy (confirmed 403 from the egress proxy on
  `builds.dotnet.microsoft.com`). Compensated with a thorough manual
  review (brace/paren balance checks, cross-checking every new call against
  its real interface signature) but this is not a substitute for an actual
  test run — flagged explicitly in `TASK-008.md`'s Result section and in
  the PR description; Tech Lead/CI should run the suite before merge.
- Opened a PR on branch `claude/magical-heisenberg-7lvfal`, flipped
  `TASK-008.md` Status to `PR_OPENED`, filled in its Result/Files
  Changed/Tests/Known Issues sections.

## 2026-09-17 — Frontend: closed duplicate TASK-008 PR, flipped status to MERGED (user-directed)

- Boss asked directly to resolve merge conflicts on PR #346. Investigating
  first (rather than blindly fixing text conflicts) turned up that #346
  was a **second, independent backend session's implementation of the same
  `TASK-008`**, opened in parallel with #347 from a different branch
  (`claude/magical-heisenberg-5pk8jh` vs. `claude/magical-heisenberg-7lvfal`).
  #347 had already merged into `master` (commit `7e6c831`) with CI green,
  which is exactly why #346 showed conflicts — both defined the same new
  DTOs/methods.
- Confirmed via `git show origin/master` that the writing feature
  (`CreditCosts.WritingEvaluation`, `WritingPromptResponse`/
  `WritingGradingResponse` DTOs, etc.) is fully present on `master` through
  #347. Reported the duplicate-PR finding to Boss instead of merging #346's
  conflicts, since resolving them would have reintroduced duplicate type
  definitions and broken compilation.
- Boss confirmed: close #346 (as duplicate of #347) and correct
  `TASK-008.md`'s bookkeeping. Closed PR #346 with an explanatory comment.
  Flipped `TASK-008.md` `Status` from the stale `PR_OPENED` to `MERGED` and
  added a note under Result documenting the duplicate-PR closure.
- This also unblocks `TASK-009` (Frontend, own assigned task) — real merged
  DTO shapes are now available on `master` to implement against.

## 2026-09-17 — Frontend: implemented TASK-009 (Writing Practice UI, user-directed)

- Boss asked to proceed straight into `TASK-009` after the #346 cleanup.
  Read the actual merged DTOs off `master` (`ExamModels.cs`,
  `ExamController.cs`) rather than guessing, confirmed camelCase JSON via
  `Program.cs`'s default `AddJsonOptions`.
- Added `getWritingPrompt`/`evaluateWriting` to `apis/examApis`, matching
  DTO types, `'Writing'` on `ExamType`, `writingWordIds?` on
  `RecordExamResultRequest`. New `Exam/WritingTestCard`: free-to-open
  prompt fetch (topic + up to 5 required-word chips, or a plain info note
  for the open-topic zero-word case), submit-for-grading with the credit
  cost caption at the point of charge rather than on the mode-selection
  screen, full feedback rendering (score/grammar/content/vocabulary,
  per-word used/not-used chips, suggestions), `record-result` fired once
  after feedback reveal with `correct: score >= 60` (same threshold
  `SentenceTestCard` already uses). Wired into `Exam/index.tsx`'s
  `EXAM_MODES` with a caption that reads correctly for "free to open,
  charged on submit". Added `CREDIT_COSTS.WRITING_EVALUATION: 3`.
- `npm run lint` (0 warnings), `npm run build`, `npx vitest run` (135
  tests) all pass. No new component-level tests added — matches the
  existing convention (no other test card has dedicated component tests
  either); flagged in `TASK-009.md` in case Tech Lead wants that changed.
- Opened a PR, flipped `TASK-009.md` Status to `PR_OPENED`, filled in its
  Result/Files Changed/Tests/Known Issues sections.

## 2026-09-17 — PM: Product review of shipped item 3, handed item 4 (last item) to Tech Lead (scheduled run)

- No new message in `.agent/boss/PM.md` since PM's own 2026-09-16 reply —
  nothing pending Boss decision at the PM level.
- Confirmed via `git log` that PR #347 (`TASK-008`, backend) and PR #348
  (`TASK-009`, frontend) are both merged into `master` (current branch tip
  `64c8c68` already carries both). Skill-Building item 3 (open-ended
  writing practice) is fully shipped end-to-end.
- **Product review, item 3**: read the actual merged code —
  `ExamService.GenerateWritingPromptAsync` (free: topic pick + up to 5
  random saved words, zero AI call) / `GradeWritingAsync` (single
  credit-gated AI call, `CreditCosts.WritingEvaluation = 3`, returns
  score + grammar/content/vocabulary feedback + per-word used/not-used
  list), `ExamController`'s `GET api/exam/writing` / `POST
  api/exam/writing/evaluate` routes, and the frontend
  `Exam/WritingTestCard`. Confirmed the "free to open, charged on submit"
  cost shape is correctly reflected in UI copy (mode-selection caption:
  "提交批改需消耗 N 點使用額度", not implied at entry) and that
  `RecordExamResultAsync` has a distinct `"Writing"` wordless branch.
  Matches the requirement exactly, including the flagged trade-off (one
  holistic grade fanned across all required words, not per-word scoring).
  `REQUIREMENTS.md`'s Skill-Building acceptance criteria updated — item 3
  is now marked product-complete, same as items 1 and 2.
- **Handed Skill-Building item 4 (dictation practice) to Tech Lead** — the
  last item on the originally-approved 4-item list (DECISION-005 priority
  order: speaking → reading → writing → dictation). Treated as executing
  the already-approved order, not a new scope decision — same
  "proceed by default" pattern used for items 1–3. Flagged in
  `.agent/boss/PM.md` that once this ships, the Skill-Building requirement
  as originally scoped is complete, and future growth work should look at
  the still-open Future Consideration item (product-wide gap audit of
  Finance/Portfolio/PDF Unlocker, per DECISION-005) rather than inventing
  a fifth Skill-Building feature without a fresh Boss mandate.
- No code changes — Requirements/status documentation only.

## Open Items
- **New, from this run**: Skill-Building item 4 (dictation practice)
  handed to Tech Lead — next Tech Lead run should treat scoping it as the
  actionable item unless Boss redirects. This is the last item in the
  currently-approved Skill-Building list.
- Still open from before: Phase 2 of speaking delivery feedback (true
  pronunciation-accuracy scoring, new vendor) — cost/vendor decision
  pending Boss, not scoped as a task.
- Still open from before: future gap-audit of Finance/Portfolio/PDF
  Unlocker (DECISION-005) — not started. Worth picking up once item 4
  (dictation) ships and closes out the Skill-Building list, since there
  will then be no other approved English-skill work left in the queue.

## 2026-09-20 — Tech Lead: closed out TASK-010 bookkeeping, reviewed TASK-011, cleaned up a duplicate-PR pile-up (scheduled run)

- Read REQUIREMENTS/DECISIONS/ARCHITECTURE/tasks/status/TECH_LEAD.md per
  SOP. No new Boss message in `TECH_LEAD.md` since the 2026-09-19 entry
  (dictation handoff + deliberate design-deviation note, non-blocking,
  unanswered). `master` confirmed up to date (`94dcea5`, carrying PR #352 —
  `TASK-010` backend — already merged).
- **Housekeeping, TASK-010**: reviewed the actual merged code (not just the
  task's self-report) — `ExamController.cs`'s three new `dictation*`
  routes, `ExamService.cs`'s `GenerateDictationExamAsync`/
  `GetDictationWordTextAsync`/`GradeDictationAsync`. Confirmed the
  GET-response-withholds-the-answer deviation is implemented correctly
  (`DictationPromptResponse` has no word-text field anywhere), the flat
  charge/unbilled split matches the Decision doc, and no
  `RecordExamResultAsync`/EF-migration change exists in the diff (as
  scoped). No issues found. Flipped `TASK-010.md` `Status` to `MERGED` with
  Review notes.
- **Found three open PRs, not one, for `TASK-011`** (frontend dictation
  UI): #354 (the actual implementation), and #355/#356 — two independent
  sessions' documentation-only PRs, both recording the *same* prior
  duplicate-PR resolution (#353 closed in favor of #354), i.e. a duplicate
  bookkeeping PR on top of an already-resolved duplicate code PR. Read all
  three PR bodies plus #353's (closed) body to reconstruct the full
  timeline before acting, rather than trusting any single PR's self-report.
- **Reviewed #354** (the real code): diff matches `TASK-011`'s spec exactly
  — `DictationPromptResponse.wordId` is never rendered as visible text,
  replay is deliberately unrestricted (correct per the task's own
  reasoning that dictation isn't an MCQ), `record-result` fires once with
  the word-anchored shape and includes the cross-card SRS-feedback caption
  every other word-anchored card already shows (the exact gap #353 was
  missing, confirmed by direct diff read rather than trusting #355/#356's
  claim about it), DTOs match the merged backend field-for-field. CI green
  (`lint-and-build-client`/`build-and-test-server` both `success`),
  `mergeable_state: clean`. No issues found — posted a review comment
  marking it **READY FOR BOSS REVIEW**, flagging the one real open item
  (manual end-to-end / network-payload check, not performed in any
  implementing session's sandbox) as worth a manual or QA pass before
  merge, not a code blocker.
- **Resolved the docs duplication directly** rather than merging either
  #355 or #356: per this role's own git rule (§16 — agent-state/task file
  changes go straight to `master`, no PR needed), wrote the consolidated,
  verified `TASK-011.md` Result/Review update and this status entry
  directly in this commit, then closed both #355 and #356 as redundant
  with a comment pointing here — avoids merging either as "the" record
  when Tech Lead can just author the authoritative one directly, and avoids
  a fourth session someday needing to reconcile *those* two.
- `REQUIREMENTS.md`: updated item 4's line to reflect current state exactly
  (backend merged/reviewed, frontend PR open/reviewed/pending Boss merge)
  rather than the stale "both READY, unclaimed."
- Wrote to `.agent/boss/TECH_LEAD.md`: summarized the above, and flagged
  (informationally, not blocking) that this is now the **third** time a
  parallel-session duplicate PR has happened on this project (`TASK-002`'s
  #331/#333, `TASK-008`'s #346/#347, now `TASK-011`'s #353/#354 plus the
  #355/#356 docs echo) — pure process-timing (two scheduled sessions
  picking up the same `READY` task before either's PR lands), not a design
  flaw needing a fix, but naming the pattern in case Boss wants a mitigation
  (e.g. claiming a task by flipping its Status before starting).
- No application code changes this run — task/status/requirements
  documentation only, committed directly to `master`.

## Open Items
- **New, from this run**: `TASK-011` PR #354 is `READY FOR BOSS REVIEW` —
  waiting on Boss merge. Once merged and PM product-verifies it, the
  originally-approved four-item Skill-Building list (DECISION-005) is
  fully complete.
- Still open from before: Phase 2 of speaking delivery feedback (true
  pronunciation-accuracy scoring, new vendor) — cost/vendor decision
  pending Boss, not scoped as a task.
- Still open from before: future gap-audit of Finance/Portfolio/PDF
  Unlocker (DECISION-005) — not started. This is the natural next Tech
  Lead/PM workstream once TASK-011 merges, since there will be no other
  approved English-skill work left in the queue.

## 2026-09-19 — Tech Lead: closed out TASK-008/009 bookkeeping, scoped Skill-Building item 4 — dictation, last item (scheduled run)

- Read REQUIREMENTS/DECISIONS/ARCHITECTURE/tasks/status/TECH_LEAD.md per
  SOP. No new Boss message in `TECH_LEAD.md` since the 2026-09-17 entry
  (writing feedback close-out + item 3 handoff write-up, still
  non-blocking/unanswered). No open PRs on GitHub (`gh`/API confirmed zero
  open PRs); `master` at `a24bcde`.
- **Housekeeping**: confirmed via GitHub + direct code read that `PR #347`
  (`TASK-008`) and `PR #348` (`TASK-009`) are both merged into `master`.
  Reviewed the actual merged code, not just each task's self-report —
  `GenerateWritingPromptAsync`/`GradeWritingAsync` (not credit-gated vs.
  exactly-once-gated, matching the split scoped in `ARCHITECTURE.md`),
  `RecordExamResultAsync`'s `isWriting` branch (validation rejects only
  `null`, not an empty list; SM-2 fan-out applies one shared quality value),
  no EF migration in the diff, and the frontend `WritingTestCard` (cost
  caption under the submit button, zero-required-words case reads as a
  normal info alert not an error, `record-result` fires once after reveal
  with the correct `writingWordIds`/threshold). No issues found. Flipped
  `TASK-008.md`'s empty `Review` section and `TASK-009.md`'s stale
  `Status: PR_OPENED` (already merged) to `MERGED` with review notes.
- **Picked up Skill-Building item 4** (dictation practice — 聽), handed off
  by PM 2026-09-17. This is the **last item** on the originally-approved
  four-item list (DECISION-005). Investigated the existing word-picking
  (`GetRandomUserWordIdAsync`), TTS-serving (`ToAudioResultAsync` +
  `GetListeningOptionAudio`'s resolve-ID-then-audio pattern), and
  `CreditCosts` conventions before scoping — found this item reuses more
  existing machinery than any prior Skill-Building item (no new word-
  picking logic, no new empty-repository edge case, no `RecordExamResultAsync`
  change at all since dictation is word-anchored like Cloze, not wordless
  like Listening/Reading/Writing) and needs zero new AI calls (grading is a
  plain string comparison), confirming `REQUIREMENTS.md`'s own framing that
  this would be the cheapest of the four.
- **One deliberate design deviation flagged explicitly**: every prior exam
  type (Cloze, Translation, Reading's options, etc.) already puts the
  correct answer in its initial `GET` response and relies on the frontend
  not to render it — an accepted "client-trusted" convention in this
  codebase. For dictation specifically, doing the same would make the
  feature's entire mechanic (identify the word from audio alone) trivially
  bypassable by glancing at the Network tab — a materially bigger practical
  problem here than for Cloze's MCQ. Scoped `TASK-010` so `GET
  api/exam/dictation` returns only a `WordId`, with the real text resolved
  server-side for the audio endpoint and only revealed to the client after
  grading. Documented as an explicit exception to the established pattern,
  not a silent inconsistency, in `ARCHITECTURE.md`.
- Documented the full investigation and decision in `ARCHITECTURE.md` →
  "Decision: Dictation practice — Skill-Building item 4 (last item)".
  Created `TASK-010` (Backend: `GET api/exam/dictation` (flat-charged,
  `WordId`-only response) + unbilled `GET api/exam/dictation/audio` +
  unbilled `POST api/exam/dictation/grade` (exact-match + Levenshtein
  closeness score, no AI) + `CreditCosts.DictationTest = 1`, no EF
  migration, no `RecordExamResultAsync` change) and `TASK-011` (Frontend:
  new `DictationTestCard`, wired into `EXAM_MODES`) — both `READY`,
  unclaimed. Proceeded without waiting for fresh Boss sign-off, same
  "already-approved priority order, no new vendor/cost tier" basis used for
  items 1–3.
- Deliberately scoped **out** of `TASK-010`: adjustable playback speed (a
  related gap also named in `REQUIREMENTS.md`'s skill-coverage table, but
  not one of the four approved proposals) — flagged in the Architecture doc
  as a cheap, likely-worthwhile follow-up rather than bundled in without
  being asked for, to avoid silently expanding this task's scope.
- `REQUIREMENTS.md`: updated item 4's line to reflect the scoping and
  flagged that this closes out the requirement's four-item scope once
  shipped; updated the Skill-Building "scoped into a task write-up"
  acceptance criterion to note all four items are now scoped.
- No implementation code changes — architecture/task documentation only,
  ready for Backend/Frontend Engineers to pick up. Committing and pushing
  these `.agent/` doc changes directly to `master`, per this role's own
  standing git rule (§16) for architecture/task/status file updates — no
  branch/PR for documentation-only changes.

## 2026-09-19 — Frontend Engineer: TASK-011 dictation practice UI (scheduled run)

- Read `.agent/tasks/TASK-011.md` (Owner: Frontend, `READY`). Its stated
  dependency, `TASK-010` (Backend), had already merged (PR #352, confirmed
  via `mcp__github__pull_request_read`) since the task was written —
  fetched `origin/master` and confirmed this session's branch
  (`claude/amazing-cori-fc4dxu`) already carried that merge commit.
- Verified the **merged** DTO/route shapes directly from
  `DTO/ExamModels.cs` and `Controllers/ExamController.cs` before writing
  any frontend code (per the task's own instruction not to guess):
  `DictationPromptResponse { WordId }`,
  `DictationGradeRequest { WordId, UserInput }`,
  `DictationGradeResponse { CorrectText, IsCorrect, Score }`; routes
  `GET api/exam/dictation`, `GET api/exam/dictation/audio?wordId=X`,
  `POST api/exam/dictation/grade`. Confirmed no `RecordExamResultRequest`
  field was added, matching the task's expectation.
- Implemented: `apis/examApis/types.ts` (`'Dictation'` on `ExamType` +
  the three new DTOs, camelCase), `apis/examApis/index.ts`
  (`getDictationPrompt`/`gradeDictation`), `constants/creditCosts.ts`
  (`DICTATION_TEST: 1`), new `Exam/DictationTestCard/index.tsx` (play
  button using the existing `useOneShotAudio` pattern but deliberately
  **not** one-shot-restricted — replay is allowed anytime, since this
  isn't a multiple-choice question where replay would be "cheating"),
  wired into `Exam/index.tsx`'s `EXAM_MODES`.
- `npm run lint` (0 warnings), `npm run build`, `npx vitest run` (136
  tests, including `creditCosts.test.ts`'s backend-mirror check) all pass.
  No dedicated component test added — matches the existing convention (no
  other exam test card has one).
- Opened PR #354, flipped `TASK-011.md` to `PR_OPENED`, filled in its
  Result/PR/Known Issues sections.
- **Flagged as not done in this session**: manual end-to-end verification
  (opening the mode in a running app, inspecting the actual network
  payload to confirm the GET response has no word text, playing audio,
  submitting) — no backend/DB available in this sandboxed session. Recorded
  in the PR and task file as an open item for a manual pass or QA before
  merge.

## Open Items
- **New, from this run**: `TASK-011` PR #354 is open — needs Tech Lead
  review and, per the task's own flag, a manual end-to-end pass (network
  payload check, audio playback, submit flow) before merge, since this
  session had no way to run the app against a live backend.
- Once item 4 ships and is product-verified, the originally-approved
  Skill-Building four-item list (DECISION-005) will be fully complete —
  the next PM run should then pivot to the still-open product-wide gap
  audit (Finance/Portfolio/PDF Unlocker, DECISION-005) rather than
  inventing a fifth Skill-Building item without a fresh Boss mandate.
- Still open from before: Phase 2 of speaking delivery feedback (true
  pronunciation-accuracy scoring, new vendor) — cost/vendor decision
  pending Boss, not scoped as a task.

## 2026-09-20 — PM: Closed out Skill-Building (item 4 verified), started Finance/Portfolio/PDF gap audit (scheduled run)

- Read `.agent/boss/PM.md` (no new Boss message since 2026-09-17),
  `REQUIREMENTS.md`, `DECISIONS.md`, and confirmed via GitHub that PR #354
  (`TASK-011`, dictation frontend) is merged (Boss merged it directly).
- **Product-verified Skill-Building item 4 (dictation)** by reading the
  actual merged frontend code (`jackyaiapp.client/src/Exam/
  DictationTestCard/index.tsx`): the prompt response never exposes word
  text before grading, playback is genuinely unrestricted (not one-shot,
  unlike the MCQ listening cards), cost caption matches
  `CreditCosts.DictationTest = 1`. `REQUIREMENTS.md` acceptance criteria for
  item 4 flipped to `[x]`. **All four Skill-Building items (DECISION-004/
  005) are now shipped and product-verified — the requirement is complete.**
- **Started the DECISION-005 "future consideration" workstream**: gap audit
  of Finance, Portfolio, and PDF Unlocker, same method used for
  Skill-Building (read actual code, not docs; find concrete reusable-
  infrastructure gaps). Delegated two focused code-reading passes (Finance,
  Portfolio) and did a quick direct check on PDF Unlocker myself.
- **Finance findings**: the module is broader than `REQUIREMENTS.md`
  described — 8 endpoints, not 2; 4 free/unauthenticated quant tools
  alongside the 2 credit-gated AI features. No prediction-accuracy tracking
  exists (AI trend calls are never checked against what actually happened).
  No watchlist/alert model exists.
- **Portfolio findings**: also broader than documented — net-worth history,
  drawdown analysis, and benchmark comparison already exist (this doc
  previously implied snapshot-only); a `REAL_ESTATE` asset type already
  exists in code but was undocumented. CSV export exists with no matching
  import.
- **Same gap found independently from both sides**: a Portfolio holding's
  detail view never links to Finance's fuller per-stock analysis for that
  same symbol, despite both modules sharing the same TW price-data
  providers underneath — a portfolio holder currently gets *less* AI
  insight on a stock they own than a non-holder gets from Finance directly.
  Flagged as the top-priority, lowest-cost item (pure read-join, no new AI
  calls or infra).
- **PDF Unlocker**: confirmed by direct code read
  (`Controllers/PDFController.cs`, frontend `PDFUnlocker/index.tsx`) that
  "batch processing," claimed in root `CLAUDE.md` and the old
  `REQUIREMENTS.md`, does not exist — single-file only, both ends. Flagged
  as a documentation/behavior drift, not silently fixed (could mean
  correcting docs or actually building the feature — Boss's call). No new
  feature proposals raised for this module given its LOW priority.
- `REQUIREMENTS.md`: corrected the stale Finance/Portfolio/PDF descriptions
  above, added a "Gap Audit (2026-09-20)" subsection to Finance and
  Portfolio with prioritized proposals (Finance↔Portfolio cross-link →
  prediction-accuracy tracking → multi-stock comparison for Finance;
  cross-link → CSV import → rebalancing suggestions → FIRE tracking for
  Portfolio — the cross-link is one shared proposal).
- Wrote the full findings + proposed priority to `.agent/boss/PM.md`,
  following the same pattern as the original Skill-Building proposal: flag
  now, and unless Boss redirects, hand off the first item (Finance↔Portfolio
  cross-link) to Tech Lead next run by default.
- No code changes — documentation only, committed directly to `master` per
  this role's own git rule (§11) for PM documentation.

## Open Items
- **New, from this run**: Finance/Portfolio gap-audit proposals await
  either a Boss reply or PM's next default-proceed run (handing off the
  Finance↔Portfolio cross-link to Tech Lead). See `REQUIREMENTS.md`'s Gap
  Audit subsections and `.agent/boss/PM.md` (2026-09-20) for the full list.
- **New, from this run**: PDF Unlocker's "batch processing" doc/behavior
  mismatch flagged to Boss — not resolved, no urgency.
- Skill-Building (DECISION-004/005) is now fully complete — no further
  action needed on that requirement unless Boss requests a fifth item.
- Still open from before: Phase 2 of speaking delivery feedback (true
  pronunciation-accuracy scoring, new vendor) — cost/vendor decision
  pending Boss, not scoped as a task.
- Still open from before (Tech Lead, 2026-09-20): whether a task-claiming
  mechanism (flip Status to IN_PROGRESS before starting) is worth adding to
  avoid the recurring duplicate-PR pattern (3 occurrences so far) — pending
  Boss preference, not urgent.

## 2026-09-21 — Tech Lead: scoped Finance↔Portfolio cross-link, Gap Audit item 1 (scheduled run)

- Read REQUIREMENTS/DECISIONS/ARCHITECTURE/tasks/status/TECH_LEAD.md per
  SOP. No new Boss message in `TECH_LEAD.md` since the 2026-09-20 entry
  (duplicate-PR pattern + TASK-011 review, non-blocking, unanswered). No
  new message in `.agent/boss/PM.md` since PM's 2026-09-20 gap-audit
  write-up either. Confirmed via GitHub: zero open PRs. `master` up to date
  with `origin/master` (`87d0a6b`).
- Per PM's stated default ("if Boss doesn't redirect, hand off proposal 1 —
  Finance↔Portfolio cross-link — to Tech Lead next run"), and with no Boss
  reply since, picked this up as the highest-priority actionable item —
  same "proceed by default" pattern used throughout Skill-Building and the
  original SEO work.
- **Investigated before scoping**: read `FinanceController.cs` (confirmed
  `chip-analysis`/`score`/`comprehensive-analysis` are already
  `[AllowAnonymous]`, no `[Authorize]`, already accept a bare stock code +
  optional market), `Finance/hooks/useFinanceAnalysis.ts` (confirmed the
  Finance page is pure client-side state today — no URL param read on
  mount, nothing to auto-trigger a search), `Portfolio/HoldingDetailDrawer.tsx`
  + `PositionsTable.tsx` (confirmed the drawer only opens for
  `STOCK`/`FUND` positions, and `PositionDto.market` already uses the exact
  `'TW'`/`'US'` values Finance's own market-detection expects). **Net
  finding: this is a frontend-only task.** The backend already supports
  everything needed — the actual gap is that Finance's page has no way to
  receive a "show me this symbol" link, and Portfolio's drawer has no link
  to send. No new endpoint, no DTO change, no backend PR.
- One scope decision made explicitly: the cross-link must land on Finance's
  **quantitative** tab (free/unauthenticated), never the AI-trend tab
  (credit-gated) — a discoverability link must not be able to silently
  trigger a charge. Also gated the link to `STOCK` holdings only, not
  `FUND` — Finance has no fund-analysis surface at all.
- Documented the full investigation and decision in `ARCHITECTURE.md` →
  "Decision: Finance ↔ Portfolio cross-link (Gap Audit item 1,
  DECISION-005)". Created `TASK-012` (Frontend only: `useFinanceAnalysis`
  reads `symbol`/`market` from the URL and auto-runs the free quant search
  once on mount; `HoldingDetailDrawer` adds a new-tab link for STOCK
  holdings) — `READY`, unclaimed.
- `REQUIREMENTS.md`: updated the Finance Gap Audit's Open Questions to
  record that proposal 1 has been scoped into `TASK-012` (proposals 2-5
  remain unscoped, pending Boss's priority-order reply or a future run).
- Wrote to `.agent/boss/TECH_LEAD.md`: summarized the scoping and flagged
  (non-blocking) the one product question this raises — whether landing on
  the free quant tab vs. a plain "search this symbol" is the right
  behavior, in case Boss has a different expectation.
- No implementation code changes — architecture/task documentation only,
  ready for a Frontend Engineer to pick up. Committed and pushed directly
  to `master` per this role's own git rule (§16) for architecture/task/
  status documentation.

## Open Items
- **New, from this run**: `TASK-012` (Finance↔Portfolio cross-link,
  frontend-only) is `READY`, unclaimed — next Frontend run should pick it
  up.
- Gap-audit proposals 2-5 (prediction-accuracy tracking, multi-stock
  comparison, Portfolio CSV import, rebalancing/FIRE tracking) remain
  unscoped — natural next items once `TASK-012` ships, pending Boss's
  priority-order reply.
- Still open from before: Phase 2 of speaking delivery feedback (true
  pronunciation-accuracy scoring, new vendor) — cost/vendor decision
  pending Boss, not scoped as a task.
- Still open from before (Tech Lead, 2026-09-20): whether a task-claiming
  mechanism (flip Status to IN_PROGRESS before starting) is worth adding to
  avoid the recurring duplicate-PR pattern (3 occurrences so far) — pending
  Boss preference, not urgent.

## 2026-09-21 — PM: Boss approved Gap Audit priority order + decided to remove all payment/monetization (DECISION-006) (scheduled run)

- Read `.agent/boss/PM.md`: Boss had replied (unanswered until this run) to
  PM's 2026-09-20 three open questions: (1) approved the Finance/Portfolio
  Gap Audit priority order as proposed, (2) — the significant one — stated
  intent to remove **all** payment methods from the site, since every
  Taiwan payment path is blocked by the same prepaid-credit trust (信託)
  regulation Boss already ruled out solving via trust/company registration
  (DECISION-001), (3) confirmed no PDF Unlocker batch-processing work.
- **Recorded DECISION-006**: full removal of Stripe/PayPal/ECPay
  integration, credit-pack purchase UI, and the balance/transaction-history
  concept — not a pause (DECISION-003) but an active teardown. Flagged the
  necessary companion problem explicitly rather than treating "remove
  payment" as a pure deletion: the credit balance is currently the app's
  *only* cost-control mechanism on expensive AI calls (exam generation/
  grading, conversation, TTS/STT, stock AI analysis, chatbot), so removing
  it without a replacement would make those calls unlimited and free —
  directly conflicting with Boss's separate standing cloud-cost goal
  (2026-09-12). Proposed replacement: a free, non-monetary daily/periodic
  usage cap per feature per user, reusing the `market-digest`
  once-per-day/shared-bank pattern already in the codebase rather than
  inventing a new mechanism. No real credit transactions have ever cleared
  on any provider (sandbox-only per this doc's own Constraints), so removal
  needs no refund/migration handling.
- `REQUIREMENTS.md`: rewrote the `Credits & Payments` requirement (Goal,
  Acceptance Criteria, Priority HIGH/ACTIVE REMOVAL, Scope, and a new "PM's
  proposed replacement design" section) to reflect removal instead of
  on-hold maintenance. Closed out Finance Gap Audit's "free-quant-vs-paid-AI
  funnel" open question as moot (no paid tab left to funnel into) and
  marked the Gap Audit priority order approved — proposal 2 (prediction-
  accuracy tracking) is now the default next Tech Lead handoff once
  `TASK-012` ships, no further sign-off needed. Closed PDF Unlocker's
  batch-processing question (keep single-file behavior); flagged root
  `CLAUDE.md`'s stale "batch processing" claim as a small out-of-scope-for-
  PM housekeeping item for whoever next touches that file.
- **Deliberately did not hand the removal work to Tech Lead yet** — unlike
  prior "proceed by default" items, this one has a real open design
  question (is a usage cap the right replacement, and how generous) that
  materially changes what gets built, so PM asked Boss to confirm before
  Tech Lead scopes the teardown. Wrote the proposal + two open questions to
  `.agent/boss/PM.md`. Gap Audit item 2 (prediction-accuracy tracking) is
  explicitly *not* blocked by this and can proceed independently.
- No code changes — Requirements/Decisions/PM-log documentation only.
  Committed and pushed directly to `master` per this role's own git rule
  (§11) for PM documentation.

## Open Items
- **New, blocking Tech Lead handoff on this one item only**: Credits &
  Payments removal (DECISION-006) needs Boss's answer on the proposed
  free-usage-cap replacement design (generous vs. conservative caps, or no
  cap at all) before Tech Lead scopes the teardown. See `.agent/boss/PM.md`
  (2026-09-21) for the two questions.
- **Unblocked, next default Tech Lead item**: Finance Gap Audit proposal 2
  (prediction-accuracy tracking) — proceed once `TASK-012` ships, no Boss
  reply needed.
- `TASK-012` (Finance↔Portfolio cross-link, frontend-only) still `READY`,
  unclaimed — carried over from the previous entry.
- Still open from before: Phase 2 of speaking delivery feedback (true
  pronunciation-accuracy scoring, new vendor) — cost/vendor decision
  pending Boss, not scoped as a task.
- Still open from before (Tech Lead, 2026-09-20): whether a task-claiming
  mechanism (flip Status to IN_PROGRESS before starting) is worth adding to
  avoid the recurring duplicate-PR pattern (3 occurrences so far) — pending
  Boss preference, not urgent.
- Small housekeeping, non-urgent: root `CLAUDE.md`'s PDF Unlocker feature
  list still claims "batch processing," which doesn't exist and won't be
  built (Boss confirmed 2026-09-21) — needs a doc fix outside `.agent/`,
  not PM's file to touch.

## 2026-09-22 — Tech Lead: resolved a 6-way duplicate-PR pile-up on TASK-012, scoped Finance Gap Audit item 2 (scheduled run)

- Read REQUIREMENTS/DECISIONS/ARCHITECTURE/tasks/status/TECH_LEAD.md per
  SOP. No new Boss message in `TECH_LEAD.md` since the 2026-09-21 entry
  (TASK-012 scoping, non-blocking, unanswered). `PM.md`'s last message
  (2026-09-21, DECISION-006) still has no Boss reply on the free-usage-cap
  question — Credits & Payments removal stays not-handed-to-Tech-Lead per
  PM's own explicit note, so it's correctly out of scope for this run.
- **Found `TASK-012` had six open PRs, not one**: #357, #358, #359, #360,
  #361, #362 — six different scheduled sessions had each independently
  picked up the same `READY`, unclaimed task before any of the others'
  PRs landed, and all six shipped functionally identical implementations
  (same two files: `useFinanceAnalysis.ts`, `HoldingDetailDrawer.tsx`),
  all CI-green. This is the same recurring pattern flagged three times
  before (`TASK-002`, `TASK-008`, `TASK-011`) but a full 6x instead of
  2-3x — see the escalation below.
- Read and compared all six diffs in full rather than picking one at
  random. Kept **#359**: the only one where the shared search helper is
  properly `useCallback`-wrapped with a complete, honest `useEffect`
  dependency array (no `eslint-disable-next-line react-hooks/exhaustive-deps`
  needed) and a `useRef` guard that protects against React 18 StrictMode's
  dev double-invoke; it also leaves `handleStockSearch`'s existing public
  signature untouched (unlike #357, which added new params to it and
  needed a follow-up fix in `Finance/index.tsx`'s `onClick`), and gates the
  cross-link on an explicit `assetType === 'STOCK'` check rather than
  `!isFund` (unlike #361). Posted a full review comment on #359 marking it
  **READY FOR BOSS REVIEW**. Closed #357/#358/#360/#361/#362, each with a
  comment explaining the specific reason #359 was preferred over it and
  pointing to #359. `TASK-012.md` updated: `Status: PR_OPENED`, `Result`/
  `Review` sections filled in documenting the six-way pile-up and the
  resolution.
- **Scoped Finance Gap Audit proposal 2** (prediction-accuracy tracking),
  per PM's 2026-09-21 authorization to proceed without waiting for further
  Boss sign-off. Investigated before scoping: PM's original cost estimate
  ("medium cost — new table + a scheduled job like `CakeJobsProvider.cs`")
  turned out to rest on a mistaken premise — `CakeJobsProvider.cs` is a
  fetch-on-demand data provider, not a scheduled/recurring job, and a
  repo-wide search confirmed this codebase has **no** background-job
  infrastructure at all (no Hangfire/Quartz/`BackgroundService`/cron
  anywhere). Every existing "periodic" behavior (`MarketDigest`'s
  once-per-day gate, exam bank growth thresholds) is actually a lazy,
  request-triggered cache-expiry check, not a real timer. Scoped
  accordingly: persist a `StockPrediction` row per horizon
  (short/medium/long) whenever `analyze-stock` runs, then lazily evaluate
  due-and-unevaluated rows as a fire-and-forget side effect of the
  already-daily `market-digest` endpoint — avoids introducing this
  codebase's first scheduled-job mechanism entirely, a real cost saving
  over the original framing. A results/track-record UI is deliberately
  deferred to an unscoped Phase 2, since short-term predictions take weeks
  and long-term ones take up to 3 years to become evaluable — nothing
  meaningful to show at launch.
- Documented the full investigation and decision in `ARCHITECTURE.md` →
  "Decision: Finance prediction-accuracy tracking — Gap Audit item 2".
  Created `TASK-013` (Backend only: `StockPrediction` entity + migration,
  write path off `analyze-stock`, `EvaluateDuePredictionsAsync` hooked into
  `market-digest`, no new endpoint, no credit-cost change) — `READY`,
  unclaimed, no hard dependency on `TASK-012`.
- `REQUIREMENTS.md`: updated the Finance Gap Audit's Open Questions entry
  to reflect `TASK-012`'s current state (PR_OPENED/READY FOR BOSS REVIEW)
  and record `TASK-013`'s scoping + the `CakeJobsProvider.cs` cost-estimate
  correction.
- Wrote to `.agent/boss/TECH_LEAD.md`: reported both items above, and
  escalated the duplicate-PR pattern more concretely than the last three
  times it was raised — proposed a lightweight mitigation (flip a task's
  `Status` to `IN_PROGRESS` as the very first commit when starting
  implementation, so a parallel session doing the same SOP-mandated
  "check GitHub for an existing PR" step before starting has a much
  earlier, more reliable signal than "does a PR exist yet") and noted
  intent to start applying that convention to tasks Tech Lead creates going
  forward, as a process improvement within Tech Lead's own remit rather
  than a product decision requiring Boss sign-off — while flagging that
  Boss may still want a heavier mechanism if this doesn't help, given the
  pattern just got worse, not better.
- No application code changes — architecture/task/requirements/status
  documentation only, plus GitHub PR review/close actions. Committed and
  pushed directly to `master` per this role's own git rule (§16).

## Open Items
- **New, from this run**: `TASK-012` PR #359 is `PR_OPENED`/`READY FOR
  BOSS REVIEW` — waiting on Boss merge. `TASK-013` (Finance
  prediction-accuracy tracking, Backend only) is `READY`, unclaimed.
- Carried over, unresolved: Credits & Payments removal (DECISION-006)
  still blocked on Boss's free-usage-cap design answer in
  `.agent/boss/PM.md` (2026-09-21) — over a day with no reply as of this
  run; worth a direct nudge if it stays quiet much longer, since it's the
  single biggest piece of remaining Tech Lead-assigned work once
  `TASK-012`/`TASK-013` ship.
- Carried over: Phase 2 of speaking delivery feedback (pronunciation
  accuracy, new vendor) — cost/vendor decision pending Boss.
- **Escalated further this run**: the recurring duplicate-PR pattern is
  now at 6 parallel PRs for one task (previously 2-3) — see
  `.agent/boss/TECH_LEAD.md` (2026-09-22) for the proposed lightweight
  mitigation and Tech Lead's intent to start applying it without waiting
  for Boss sign-off, since it's a process convention rather than a
  product/architecture decision.
- Carried over: root `CLAUDE.md`'s stale PDF Unlocker "batch processing"
  claim still needs a doc fix outside `.agent/`.

## 2026-09-22 — PM: Product-verified Gap Audit items 1 & 2, unblocked item 3 (scheduled run)

- Read `.agent/boss/PM.md`: no new Boss reply since PM's 2026-09-21 entry —
  the Credits & Payments free-usage-cap question (DECISION-006) is still
  unanswered. Not re-raised this run (already flagged twice); nothing new
  to add until Boss responds.
- Checked GitHub directly rather than trusting the last status entry alone:
  both `TASK-012` (PR #359, Finance↔Portfolio cross-link) and `TASK-013`
  (PR #363, Finance prediction-accuracy tracking Phase 1) merged into
  `master` since the last log entry (Tech Lead's 2026-09-22 run only
  reported #359 as scoped/open and #363 as newly-scoped — both landed
  after that entry was written).
- **Product-verified both by reading the actual merged code** (not the PR
  descriptions alone):
  - `TASK-012`: `HoldingDetailDrawer.tsx`'s `FinanceCrossLink` renders only
    for `assetType === 'STOCK'`; `useFinanceAnalysis.ts`'s mount effect
    forces the free `TAB_QUANTITATIVE` tab, never the credit-gated AI-trend
    tab. Matches the scope constraint exactly.
  - `TASK-013`: `StockPredictionRecorder` persists one row per horizon with
    a self-contained try/catch (can't fail `analyze-stock`);
    `PredictionEvaluationService` grades due rows against a price window
    centered on the actual target date (confirmed it does *not* use
    `IMarketDataProvider.FetchAsync`'s fixed trailing-window, which would
    have silently mis-graded long-horizon predictions), triggered
    fire-and-forget off the existing `market-digest` call. No new endpoint,
    no credit-cost change, no frontend surface (Phase 2 UI correctly
    deferred — nothing evaluable yet). Matches the architecture decision.
  - `REQUIREMENTS.md`'s Finance and Portfolio Gap Audit sections updated to
    record both as MERGED/product-verified.
- **Unblocked the next item**: proposal 3 (multi-stock/sector peer
  comparison) is now the default next Tech Lead handoff — no new Boss
  sign-off needed, same "proceed by default" pattern used throughout this
  Gap Audit. Not scoped by PM (that's Tech Lead's job); just confirmed
  ready for pickup in `REQUIREMENTS.md`.
- No code changes — Requirements/status documentation only, committed
  directly to `master` per this role's own git rule (§11).

## Open Items
- **Next default Tech Lead item**: Finance Gap Audit proposal 3
  (multi-stock/sector peer comparison) — ready to scope, no Boss reply
  needed.
- Carried over, still unanswered: Credits & Payments removal (DECISION-006)
  blocked on Boss's free-usage-cap design answer in `.agent/boss/PM.md`
  (2026-09-21) — now over a day with no reply across two PM runs.
- Carried over: Phase 2 of speaking delivery feedback (pronunciation
  accuracy, new vendor) — cost/vendor decision pending Boss.
- Carried over: Phase 2 of Finance prediction-accuracy tracking (track-record
  UI) — deliberately unscoped until rows are actually evaluable.
- Carried over: the task-claiming-mechanism proposal (Tech Lead, 2026-09-20/
  22) to reduce duplicate-PR pile-ups — pending Boss preference, not urgent.
- Carried over: root `CLAUDE.md`'s stale PDF Unlocker "batch processing"
  claim still needs a doc fix outside `.agent/`.

## 2026-09-23 — Tech Lead: fixed stale task statuses, scoped Finance Gap Audit item 3 (scheduled run)

- **Housekeeping**: `TASK-011`/`TASK-012`/`TASK-013` were all still marked
  `PR_OPENED` in their own task files despite being merged days ago (PR #354,
  #359, #363 respectively — confirmed directly against the GitHub API's
  `pull_request_read` `get` method, which reports `merged: true`/`merged_at`
  correctly; note `list_pull_requests`'s `merged` field is unreliable and
  reported `false` for all three even though they're merged — don't trust it
  for merge status, use `pull_request_read`). Flipped all three task files'
  `Status` to `MERGED`. No open PRs exist on the repo right now.
- **Scoped Finance Gap Audit proposal 3** (multi-stock peer comparison), per
  PM's 2026-09-22 unblock — no further Boss sign-off needed, same
  "proceed by default" pattern used throughout this Gap Audit. Investigated
  before scoping: confirmed `comprehensive-analysis` needs zero backend
  changes to be called multiple times (already per-code, already
  `[AllowAnonymous]`, already cached per-stock), and `CategoryScoreCard` has
  no dependency on its current single-stock parent layout, so it's reusable
  verbatim in a category-row comparison grid. One scope correction from the
  original proposal framing: a true "sector" comparison (auto-picking peers)
  would need new sector/industry data that doesn't exist anywhere in this
  schema — scoped down to user-picked multi-stock comparison (2-3 codes the
  user types themselves), which still satisfies the proposal's actual ask
  ("render 2-3 stocks' `CategoryScoreCard`s side by side") at the originally
  estimated cost (frontend-only, no backend change).
- Documented the full investigation and decision in `ARCHITECTURE.md` →
  "Decision: Multi-stock/sector peer comparison — Gap Audit item 3". Created
  `TASK-014` (Frontend only: new "多股比較" tab, up to 3 stock-code chips,
  concurrent fetch reusing `useFinanceAnalysis.ts`'s existing
  `getComprehensiveAnalysis` call pattern, category-row layout composed from
  the existing `CategoryScoreCard`, per-stock failure isolation) — `READY`,
  unclaimed, no dependency on any other open task.
- `REQUIREMENTS.md`: updated the Finance Gap Audit's Open Questions entry to
  record `TASK-014`'s scoping and the sector-data scope correction.
- No application code changes — architecture/task/requirements/status
  documentation only. Committed and pushed directly to `master` per this
  role's own git rule (§16).

## 2026-09-23 — Frontend: implemented TASK-014 (Finance multi-stock peer comparison)

- Implemented the "多股比較" tab exactly as scoped in `ARCHITECTURE.md`'s
  "Multi-stock/sector peer comparison — Gap Audit item 3" decision:
  frontend-only, no backend/DB change, reuses the existing free
  `comprehensive-analysis` endpoint called once per entered code
  (`Promise.all`, capped at 3), category-row layout composed from the
  existing `CategoryScoreCard` unmodified, fixed `DEFAULT_CONFIG` (no
  per-comparison weight customization).
- Extracted `StockIdentitySummary` (market chip + name/code + direction
  chip) out of `OverallScoreSection.tsx` as a shared, behavior-preserving
  sub-piece reused by both the single-stock view and the new comparison
  header, and `fetchComprehensiveAnalysis` out of
  `useFinanceAnalysis.ts`'s `runComprehensiveAnalysis` as the shared
  request-building call site — `runComprehensiveAnalysis`'s own external
  behavior is unchanged, comparison always passes `DEFAULT_CONFIG` instead
  of the user-adjustable `analysisConfig`.
- Added pure `addComparisonCode`/`removeComparisonCode` helpers
  (`Finance/utils/comparisonCodes.ts`, unit tested) for the chip list's
  add/dedupe/cap-at-3 logic, plus `renderToStaticMarkup`-based component
  tests for the new `ComparisonCodeInput`/`ComparisonResult` components
  (no `@testing-library/react` in this project, so interaction tests
  weren't available — see `TASK-014`'s Result section for the full
  rationale).
- `npm run lint` (0 warnings), `npm run build`, `npx vitest run`
  (147/147, 19 files, no regressions) all pass. Not manually verified in a
  running browser this session — flagged as a Known Issue on the task for
  a quick pass before/at QA.
- `TASK-014` moved `READY` → `PR_OPENED`; PR
  https://github.com/twjackysu/JackyAIApp/pull/364 recorded on the task.

## Open Items
- **New, from this run**: `TASK-014`'s PR (#364) is open — needs Tech Lead review
  (and a manual browser smoke-test, per its Known Issues) before
  `READY_FOR_QA`.
- Carried over, unresolved: Credits & Payments removal (DECISION-006) still
  blocked on Boss's free-usage-cap design answer in `.agent/boss/PM.md`
  (2026-09-21/22) — now over two days with no reply across three PM runs;
  flagged again in `.agent/boss/TECH_LEAD.md` this run.
- Carried over: Phase 2 of speaking delivery feedback (pronunciation
  accuracy, new vendor) — cost/vendor decision pending Boss.
- Carried over: Phase 2 of Finance prediction-accuracy tracking (track-record
  UI) — deliberately unscoped until rows are actually evaluable.
- Carried over: the task-claiming-mechanism proposal (Tech Lead, 2026-09-20/
  22) to reduce duplicate-PR pile-ups — pending Boss preference, not urgent;
  no new duplicate-PR incidents this run (no open PRs at all right now).
- Carried over: root `CLAUDE.md`'s stale PDF Unlocker "batch processing"
  claim still needs a doc fix outside `.agent/`.

## 2026-09-23 — PM: Product-verified Gap Audit item 3, unblocked Portfolio's CSV import (scheduled run)

- Read `.agent/boss/PM.md`: no new Boss reply since PM's 2026-09-22 entry —
  the Credits & Payments free-usage-cap question (DECISION-006) is still
  unanswered (now the third PM run in a row with no reply; Tech Lead also
  re-flagged it 2026-09-23). Not re-raised a fourth time with new text this
  run — nothing new to add beyond what's already on record twice over.
- Checked GitHub directly: `TASK-014`'s PR #364 (Finance multi-stock
  comparison, opened by Frontend Engineer this run per Tech Lead's
  2026-09-23 scoping) was merged by Boss directly
  (`merged_at: 2026-09-23T11:25:08Z`), not yet reflected in
  `REQUIREMENTS.md`/task bookkeeping.
- **Product-verified by reading the actual merged code** (not the PR
  description alone): `Finance/index.tsx`'s "多股比較" tab carries no
  `isAuthenticated` gate (that check only applies to the separate,
  credit-gated AI-trend tab); `useFinanceAnalysis.ts`'s `runComparison`
  fetches all entered codes concurrently via `Promise.all` against the free
  `comprehensive-analysis` endpoint using the fixed `DEFAULT_CONFIG`, each
  call individually caught so one bad code doesn't block the others;
  `ComparisonResult.tsx` renders one row per present category
  (Technical/Chip/Fundamental) of unmodified `CategoryScoreCard`s, with a
  failed slot's error shown inline in its own column. Matches `TASK-014`'s
  requirements and the Architecture doc's scope cut (user-picked codes, not
  auto sector-discovery) exactly. `REQUIREMENTS.md`'s Finance Gap Audit
  section updated — **all three of PM's originally-prioritized Finance Gap
  Audit items (cross-link, prediction tracking, multi-stock comparison) are
  now merged and product-complete.**
- **Unblocked the next item**: per the already-approved priority order
  (cross-link → prediction tracking → multi-stock comparison → **Portfolio
  CSV import** → rebalancing → FIRE tracking → watchlist/alerts), Portfolio's
  own proposal 2 (CSV import for holdings) is now the default next Tech Lead
  handoff — no new Boss sign-off needed, same "proceed by default" pattern
  used throughout this Gap Audit. Not scoped by PM (Tech Lead's job); just
  confirmed ready for pickup in `REQUIREMENTS.md`.
- No code changes — Requirements/status documentation only, committed
  directly to `master` per this role's own git rule (§11).

## Open Items
- **Next default Tech Lead item**: Portfolio Gap Audit proposal 2 (CSV
  import for holdings) — ready to scope, no Boss reply needed.
- Carried over, unresolved: Credits & Payments removal (DECISION-006)
  blocked on Boss's free-usage-cap design answer in `.agent/boss/PM.md`
  (2026-09-21) — now three PM runs and two Tech Lead runs with no reply.
  This is the single biggest remaining piece of work once the Gap Audit
  finishes; worth Boss's attention when convenient, still not urgent
  (current system is dormant, not causing active harm).
- Carried over: Phase 2 of speaking delivery feedback (pronunciation
  accuracy, new vendor) — cost/vendor decision pending Boss.
- Carried over: Phase 2 of Finance prediction-accuracy tracking (track-record
  UI) — deliberately unscoped until rows are actually evaluable.
- Carried over: the task-claiming-mechanism proposal (Tech Lead, 2026-09-20/
  22) to reduce duplicate-PR pile-ups — pending Boss preference, not urgent.
- Carried over: root `CLAUDE.md`'s stale PDF Unlocker "batch processing"
  claim still needs a doc fix outside `.agent/`.

## 2026-09-24 — Tech Lead: scoped Portfolio Gap Audit item 2 (CSV import) (scheduled run)

- Read REQUIREMENTS/DECISIONS/ARCHITECTURE/tasks/status/TECH_LEAD.md per
  SOP. No new Boss message since the 2026-09-13 sign-off on the SEO
  middleware direction — DECISION-006's free-usage-cap question is still
  the only unanswered item, and it doesn't block this work (Gap Audit is
  explicitly sequenced ahead of the Credits removal per `REQUIREMENTS.md`).
  Confirmed via GitHub: no open PRs, nothing pending review. Picked up the
  standing Open Item: PM's 2026-09-23 unblock of Portfolio Gap Audit
  proposal 2 (CSV import for holdings).
- **Found and corrected a cost-framing error in the original proposal**:
  PM's writeup (and this doc, until this run) claimed "the export schema
  already matches `SaveHoldingsRequest`'s shape." Read `portfolioExport.ts`
  and `PortfolioDto.cs` directly before scoping — that's false. The export
  CSV is a computed report (TWD-converted market value/cost basis/P&L);
  `PositionInput.CostBasis` is an unconverted total in the position's own
  `costCurrency`. Treating the export's `成本(TWD)` column as importable
  `costBasis` would silently corrupt cost-basis data for every
  foreign-currency (US stock, foreign-NAV fund) holding on import. Same
  category of correction as the Finance multi-stock item's sector-data gap
  (2026-09-23) and the reading-comprehension personalization note
  (2026-09-16) — PM's cost estimate stays right (still cheap, frontend-only),
  the specific mechanism needed a fix before handing it to an Engineer.
- Also found a reusable asset: `DataAnalysis/components/UploadDataDialog.tsx`
  already has a generic, quoted-CSV-aware CSV/TSV parser
  (`detectSep`/`parseLine`/`parseTabular`) built for an unrelated feature.
  Scoped this task to extract it into a shared `utils/tabularParse.ts`
  rather than writing a second independent parser — avoids the same kind of
  silent-drift risk the SEO text formatter was fixed for in `TASK-001`–`003`.
- Documented the full investigation and decision in `ARCHITECTURE.md` →
  "Decision: Portfolio CSV import for holdings — Gap Audit item 2". Created
  `TASK-015` (Frontend only: extract the shared parser, a new
  purpose-built import column schema distinct from the export schema, an
  "匯入 CSV" entry point inside `HoldingsEditor`'s existing dialog that
  populates the editor's row state — reusing `toRow`/`rowError` as-is —
  rather than saving directly, plus a "下載匯入範本" template download) —
  `READY`, unclaimed, no dependency on any other open task.
- `REQUIREMENTS.md`: corrected the stale "export schema already matches"
  claim in Portfolio's Gap Audit findings, and updated the Open Questions
  entry to point at `TASK-015` instead of "ready to scope."
- No application code changes — architecture/task/requirements/status
  documentation only. Committed and pushed directly to `master` per this
  role's own git rule (§16).

## Open Items
- **New, from this run**: `TASK-015` (Portfolio CSV import) is `READY`,
  unclaimed — next Backend/Frontend Engineer run should pick it up (it's
  frontend-only, so Frontend Engineer specifically).
- Carried over, unresolved: Credits & Payments removal (DECISION-006)
  blocked on Boss's free-usage-cap design answer in `.agent/boss/PM.md`
  (2026-09-21) — now over a day into a second week with no reply; still not
  urgent (current system is dormant, not causing active harm), and does not
  block Gap Audit work.
- Carried over: Phase 2 of speaking delivery feedback (pronunciation
  accuracy, new vendor) — cost/vendor decision pending Boss.
- Carried over: Phase 2 of Finance prediction-accuracy tracking (track-record
  UI) — deliberately unscoped until rows are actually evaluable.
- Carried over: the task-claiming-mechanism proposal (Tech Lead, 2026-09-20/
  22) to reduce duplicate-PR pile-ups — pending Boss preference, not urgent.
- Carried over: root `CLAUDE.md`'s stale PDF Unlocker "batch processing"
  claim still needs a doc fix outside `.agent/`.

## 2026-09-24 — Frontend Engineer: TASK-015 implemented, PR opened (scheduled run)

- Picked up `TASK-015` (Portfolio CSV import for holdings), scoped `READY`
  by Tech Lead earlier the same day. Read `ARCHITECTURE.md`'s "Decision:
  Portfolio CSV import for holdings — Gap Audit item 2" in full before
  starting, in particular the import-vs-export schema distinction and the
  extract-don't-duplicate note on `UploadDataDialog.tsx`'s existing parser.
- Extracted `detectSep`/`parseLine`/`parseTabular` into new
  `utils/tabularParse.ts`; `UploadDataDialog.tsx` now imports from there —
  behavior-preserving, its own new component test plus `npx vitest run`
  confirm no regression (that file had no pre-existing tests to preserve).
- New `Portfolio/csvImport.ts`: `mapImportRow` (the purpose-built schema —
  `類型`/`代號`/`名稱`/`市場`/`計價幣別`/`成本幣別`/`數量`/`成本`/`估價方式`,
  reverse-looking-up `類型`'s Chinese label against `portfolioExport.ts`'s
  now-exported `TYPE_LABEL` instead of a second copy), `buildImportPreview`
  (reuses `toRow`/`rowError` from `editorTypes.ts` unmodified — no second
  validator), `downloadImportTemplateCsv` (reuses `portfolioExport.ts`'s
  now-exported `downloadCsv`). New `Portfolio/ImportCsvDialog.tsx`
  (self-contained trigger+dialog, same pattern as `UpdateHistoryDialog.tsx`)
  wired into `HoldingsEditor.tsx`'s update-dialog title alongside a new
  "下載匯入範本" button. Import only populates the editor's `rows` state
  (`setRows((rs) => [...rs, ...imported])`) — never calls
  `useSaveHoldingsMutation` directly, so a bad import is always
  reviewable/editable before the existing "儲存快照" button persists it.
  Invalid rows are excluded from "加入" by default with a visible
  "N 筆略過，欄位有誤" count, per the task's first listed acceptable UX.
- Added a dedicated test that downloads the template CSV and round-trips it
  back through `mapImportRow`/`buildImportPreview`, confirming every
  template row (including the example STOCK/CASH rows) parses back into a
  fully valid, importable row — the acceptance criterion that asked for a
  manual round-trip check, automated instead of left as a manual step.
- `npm run lint` (0 warnings), `npm run build`, `npx vitest run` all
  green — 182/182 tests (up from 147), no regressions. (`node_modules`
  needed a fresh `npm ci` in this sandbox before any of the three would
  run — not a code issue, same as prior sessions' notes.)
- Opened PR (see `TASK-015.md`'s Result section for the link once
  available); `TASK-015` moved `READY` → `PR_OPENED`. Not manually verified
  in a running browser this session — flagged as a Known Issue on the task,
  same limitation `TASK-014` had.
- **Flagging for Tech Lead, not acted on this run** (out of this role's
  scope — no task file exists for it yet): a QA comment landed on the
  already-merged `TASK-014` PR (#364,
  https://github.com/twjackysu/JackyAIApp/pull/364#issuecomment-5798485642,
  2026-09-23T16:20Z, after this doc's last update) reporting a real MEDIUM
  bug — entering an invalid code in the "多股比較" tab also pops the
  single-stock tab's shared `Alert`, because `runComparison()` and
  `runComprehensiveAnalysis` share one `useGetComprehensiveAnalysisMutation()`
  trigger whose `error`/`isLoading` only reflect the *last* call, and
  `Finance/index.tsx` renders that shared error regardless of which tab is
  active. QA's own suggested fix: give the comparison tab its own mutation
  trigger instead of sharing the single-stock tab's hook state. QA also
  flagged that `#364` was merged before reaching `READY_FOR_QA` (Tech
  Lead review and QA were both skipped), and asked Tech Lead/Boss to
  confirm whether that was intentional. Needs a new Tech Lead-scoped task
  (per QA's own recommendation) — not picked up here since it isn't yet an
  assigned Frontend task and this run's actual assignment (`TASK-015`) was
  unrelated.

## Open Items
- **New, from this run**: `TASK-015`'s PR is open — needs Tech Lead review
  (and a manual browser smoke-test, per its Known Issues) before
  `READY_FOR_QA`.
- **New, from this run, needs Tech Lead**: QA-reported bug on the merged
  `TASK-014` PR (#364) — comparison-tab per-stock failures leak into the
  single-stock tab's shared error `Alert` via a shared RTK Query mutation
  hook. See PR comment
  https://github.com/twjackysu/JackyAIApp/pull/364#issuecomment-5798485642
  for full repro/root-cause/suggested fix. No task file exists for this
  yet — needs Tech Lead to scope a follow-up task (QA's own recommendation).
  Also carries QA's process question: should `READY_FOR_QA`/Tech-Lead-review
  be enforced before merge going forward, since this is the first PR to skip
  both.
- Carried over, unresolved: Credits & Payments removal (DECISION-006)
  blocked on Boss's free-usage-cap design answer in `.agent/boss/PM.md`
  (2026-09-21) — now over a day into a second week with no reply; still not
  urgent (current system is dormant, not causing active harm), and does not
  block Gap Audit work.
## 2026-09-25 — Tech Lead: resolved a 4-way TASK-015 duplicate-PR pile-up, scoped a QA-found Finance bug (scheduled run)

- Read REQUIREMENTS/DECISIONS/ARCHITECTURE/tasks/status/TECH_LEAD.md per
  SOP. No new Boss message in `TECH_LEAD.md` since the 2026-09-13 sign-off
  on the SEO middleware direction — DECISION-006's free-usage-cap question
  is still the only unanswered item there, non-blocking (Gap Audit work is
  explicitly sequenced ahead of it). No new message in `.agent/boss/PM.md`
  since PM's 2026-09-23 unblock of `TASK-015`.
- **`TASK-015` had four independent full implementations open at once**:
  #366, #367, #368 (all opened 2026-09-24, before any of their authors saw
  another's PR), plus a docs-only PR **#369** from a session that compared
  those three and kept #367 (closing #366/#368) — and then a **fourth**
  implementation, **#370**, opened 2026-09-25 by a session that started
  before #369 landed and so never saw its decision. Read all four PRs' full
  diffs directly (not any PR's self-report) before deciding, since #369's
  comparison only covered three of the four.
- **Confirmed #369's #366-rejection reasoning was correct**, and found
  **#370 has the exact same defect**: the task's own Technical Notes say
  "reuse `toRow`/`rowError`/`isRowEmpty` from `editorTypes.ts` as-is — do
  not fork or duplicate row validation logic for the import path." #367 and
  #368 both defer entirely to the shared `rowError()`; #370's
  `parseImportRow` hard-codes its own `'缺少代號'`/`'數量須為正數'` checks
  and returns before `rowError()` ever runs for those two cases — verified
  against `editorTypes.ts`'s actual `rowError()` output (`'請選擇股票'`/
  `'請輸入金額'`/etc., which don't match #370's bespoke messages), so a
  future change to the manual editor's validation rules would silently stop
  applying to CSV imports. This is the identical failure class #369 already
  used to reject #366, just in a fourth PR that arrived too late to be
  compared against that reasoning.
- One point actually in #370's favor, noted for completeness: it omits
  `costBasis`/`costCurrency` entirely for CASH/LIABILITY/REAL_ESTATE rows,
  matching `makeRow()`'s real field shape for those types exactly, whereas
  #367 always sets both fields even for asset types the manual editor never
  gives them to. Judged minor (those fields are unused for valuation on
  non-symbol types) and not enough to outweigh the validation-duplication
  defect, which is the specific failure mode this task was scoped to avoid.
- **Decision: kept #367** (re-confirming #369's conclusion against the
  newly-arrived #370 too). Closed **#368**, **#369**, and **#370** on
  GitHub with comments explaining the reasoning. Marked **#367** `READY FOR
  BOSS REVIEW` — the one open item is a manual browser smoke-test, not
  performed in any implementing session's sandbox. Wrote the consolidated
  Result/Review record directly into `TASK-015.md` myself rather than
  merging #369's version, since #369 didn't account for #370 and this
  role's own precedent (2026-09-20, `TASK-011`'s #355/#356) is to author
  the authoritative record directly on `master` rather than merge a
  since-superseded docs PR.
- **Also read the still-open QA report on the already-merged `TASK-014` PR**
  (#364, flagged by a prior Frontend run but never scoped):
  `useFinanceAnalysis.ts`'s single-stock quantitative tab and the
  comparison tab's `runComparison()` share one
  `useGetComprehensiveAnalysisMutation()` trigger, whose `error` state RTK
  Query only reflects for the *last* call, not per-call; `Finance/index.tsx:268`
  renders that shared `comprehensiveError` Alert with no `analysisTab` guard,
  so a failing code on the comparison tab pops the single-stock tab's
  generic error banner on top of the comparison tab's own correct per-slot
  errors. Confirmed the root cause directly in the code (not just QA's
  report). Scoped as **`TASK-016`** (Frontend, `READY`): a minimal,
  targeted fix — gate that Alert's render condition on
  `analysisTab === TAB_QUANTITATIVE` rather than splitting the mutation
  trigger in two, since switching tabs already resets the shared state via
  the existing `clearSearchResults()` path. Also required: an actual
  interaction-level regression test (QA's report notes the existing
  `renderToStaticMarkup`-based tests structurally can't catch this class of
  bug).
- Wrote to `.agent/boss/TECH_LEAD.md`: escalated the duplicate-PR pattern
  (now 4 full implementations on a single task, the largest occurrence yet)
  as something worth Boss's actual attention rather than another
  informational flag; also answered QA's process question about `TASK-014`
  merging without `READY_FOR_QA`/Tech Lead review, and carried forward
  DECISION-006's still-unanswered free-usage-cap question.
- No application code changes — architecture/task/status documentation
  only, plus GitHub PR review actions (comments, closes). Committed and
  pushed directly to `master` per this role's own git rule (§16).

## Open Items
- **New, from this run**: `TASK-015` PR #367 is `READY FOR BOSS REVIEW` —
  waiting on Boss merge (plus a manual browser smoke-test recommended
  before/at that point).
- **New, from this run**: `TASK-016` (Finance comparison-tab shared-error
  leak, QA-found) is `READY`, unclaimed — next Frontend run should pick it
  up.
  **Update (2026-09-25, Frontend Engineer run below): already covered by
  PR #371 — see that entry.**
- **Escalated, from this run**: the duplicate-PR pattern (4th occurrence,
  largest yet — 4 full implementations of `TASK-015`) — see
  `.agent/boss/TECH_LEAD.md` (2026-09-25) for the specific ask to Boss.
- Carried over, unresolved: Credits & Payments removal (DECISION-006)
  blocked on Boss's free-usage-cap design answer in `.agent/boss/PM.md`
  (2026-09-21) — now over a week with no reply; still not urgent (current
  system is dormant, not causing active harm), and does not block Gap Audit
  work.
- Carried over: Phase 2 of speaking delivery feedback (pronunciation
  accuracy, new vendor) — cost/vendor decision pending Boss.
- Carried over: Phase 2 of Finance prediction-accuracy tracking (track-record
  UI) — deliberately unscoped until rows are actually evaluable.
- Carried over: root `CLAUDE.md`'s stale PDF Unlocker "batch processing"
  claim still needs a doc fix outside `.agent/`.

## 2026-09-25 — Frontend Engineer: TASK-016 already covered by an existing PR, no code change (scheduled run)

- Read `.agent/tasks/TASK-016.md` (Finance comparison-tab shared-error
  leak) as the one `READY`, unclaimed Frontend task per the last Tech Lead
  entry. Before implementing, checked GitHub for open PRs first — per the
  4-way `TASK-015` duplicate-PR pile-up logged just above, and this run
  found exactly that risk was live again: **PR #371**
  (`claude/amazing-cori-0k55ra`, opened 2026-09-25T02:23:56Z by a different
  Frontend Engineer session) already implements this task in full.
- Verified rather than trusted the PR title alone: read its description and
  CI status via the GitHub API. Implementation matches `TASK-016`'s
  Requirements exactly — a derived `showComprehensiveErrorAlert` flag
  gating `Finance/index.tsx`'s Alert on `analysisTab === TAB_QUANTITATIVE`,
  no mutation-trigger split, plus a new `useFinanceAnalysis.test.tsx`
  asserting the interaction (claimed to fail without the fix). Both CI
  checks (`lint-and-build-client`, `build-and-test-server`) are `success`,
  `mergeable_state: clean`, no review comments or reviews posted yet.
- Did not open a competing implementation. Updated `TASK-016.md`'s
  `Status:` `READY` → `PR_OPENED` and filled in its Result section with
  the PR link and verification summary, so a future session checking the
  task file alone (not GitHub) doesn't also miss this and start a
  duplicate. No application code changes this run.
- `TASK-016` (PR #371) now just needs Tech Lead/Boss review — same as
  `TASK-015` (PR #367), which is still `READY FOR BOSS REVIEW` and
  unmerged as of this run.

## Open Items
- `TASK-015` PR #367 — still `READY FOR BOSS REVIEW`, unmerged, needs Boss
  merge (plus a manual browser smoke-test recommended before/at that
  point).
- `TASK-016` PR #371 — implementation-complete, CI green, no reviews yet —
  needs Tech Lead/Boss review, same as `TASK-015`.
- Carried over, unresolved: Credits & Payments removal (DECISION-006)
  blocked on Boss's free-usage-cap design answer in `.agent/boss/PM.md`
  (2026-09-21) — now over a week with no reply; still not urgent.
- Carried over: Phase 2 of speaking delivery feedback (pronunciation
  accuracy, new vendor) — cost/vendor decision pending Boss.
- Carried over: Phase 2 of Finance prediction-accuracy tracking (track-record
  UI) — deliberately unscoped until rows are actually evaluable.
- Carried over: the task-claiming-mechanism proposal (Tech Lead, 2026-09-20/
  22) to reduce duplicate-PR pile-ups — pending Boss preference, not urgent.
  22) to reduce duplicate-PR pile-ups — pending Boss preference, escalated
  again 2026-09-25 given the 4-way `TASK-015` pile-up.
- Carried over: root `CLAUDE.md`'s stale PDF Unlocker "batch processing"
  claim still needs a doc fix outside `.agent/`.

## 2026-09-25 — Frontend Engineer: flagging a self-caused merge conflict on PR #371 (PR #372 merged)

- Housekeeping PR #372 (the entry above) was merged by Boss
  (`d4a8eb7`). Checking the follow-on effect before this run's branch is
  discarded, per the precedent set on 2026-09-13 (merging `#328` flipped
  `#331` to `dirty`, logged rather than fixed in-branch): **merging #372
  flipped PR #371 (`claude/amazing-cori-0k55ra`, TASK-016's actual
  implementation) to `mergeable_state: dirty`.**
- Root cause confirmed by reading #371's file diff directly: both #372 and
  #371 edited the same lines of `.agent/tasks/TASK-016.md` (the `Status:`
  field and the `## Result` section) — #372 wrote a short pointer to #371,
  while #371 itself carries a much fuller, first-hand Result writeup (file
  list, UI/UX notes, Known Issues). This wasn't checked before #372 was
  opened; it should have been — a lesson for next time: check a
  same-task PR's own file diff, not just its description, before editing
  the same doc file it's also touching.
- **Not fixed by this session** — #371's branch belongs to a different
  session, and this session was only subscribed to #372 (now merged and
  auto-unsubscribed), not asked to drive #371. Flagging here per the same
  precedent instead of pushing to someone else's branch uninvited.
- **For whoever resolves #371's conflict**: keep #371's own fuller Result
  section (it's the authoritative first-hand implementation record) and
  drop #372's short duplicate pointer — the `Status: PR_OPENED` line is
  identical in both so that part won't conflict.
- No code changes this run — status documentation only.

## Open Items
- **New, from this run**: PR #371 (`TASK-016`) has a merge conflict in
  `.agent/tasks/TASK-016.md` against current `master`, caused by #372
  landing first — needs whoever picks up #371 next to merge `master` in and
  keep #371's own fuller Result section over #372's short pointer (see
  entry above for detail). Application code in #371 is unaffected; CI was
  green before this.
- Carried over: `TASK-015` PR #367 — still `READY FOR BOSS REVIEW`,
  unmerged, needs Boss merge (plus a manual browser smoke-test recommended
  before/at that point).
- Carried over, unresolved: Credits & Payments removal (DECISION-006)
  blocked on Boss's free-usage-cap design answer in `.agent/boss/PM.md`
  (2026-09-21) — now over a week with no reply; still not urgent.
- Carried over: Phase 2 of speaking delivery feedback (pronunciation
  accuracy, new vendor) — cost/vendor decision pending Boss.
- Carried over: Phase 2 of Finance prediction-accuracy tracking (track-record
  UI) — deliberately unscoped until rows are actually evaluable.
- Carried over: the task-claiming-mechanism proposal (Tech Lead, 2026-09-20/
  22) to reduce duplicate-PR pile-ups — pending Boss preference, escalated
  again 2026-09-25 given the 4-way `TASK-015` pile-up.
- Carried over: root `CLAUDE.md`'s stale PDF Unlocker "batch processing"
  claim still needs a doc fix outside `.agent/`.

## 2026-09-25 — PM: Product-verified TASK-015/TASK-016, unblocked Portfolio's rebalancing proposal (scheduled run)

- Read `.agent/boss/PM.md`: no new Boss reply since PM's 2026-09-23 entry —
  DECISION-006's free-usage-cap question is still unanswered (now over a
  week, across multiple PM and Tech Lead runs). Also read
  `.agent/boss/TECH_LEAD.md`'s 2026-09-25 entry: Tech Lead escalated a
  scheduling-layer question (multiple sessions claiming the same `READY`
  task simultaneously — `TASK-015` drew 4 full duplicate implementations in
  one day) as something needing Boss's decision, not Tech Lead's to keep
  absorbing unilaterally. That's a Tech Lead↔Boss channel item, not PM's to
  act on — noted to Boss in `.agent/boss/PM.md` as an FYI pointer only.
- Confirmed via `git log` that both `TASK-015` (Portfolio CSV import, PR
  #367) and `TASK-016` (Finance comparison-tab error leak, QA-found bug fix,
  PR #371) are merged into `master`, along with the docs-only PR #375
  already reflected in this file's prior entries.
- **Product-verified by reading the merged code directly** (not any PR's
  self-report):
  - `Portfolio/csvImport.ts`/`ImportCsvDialog.tsx`: the import column schema
    is genuinely distinct from the export report schema (`成本` maps to
    `PositionInput.CostBasis` verbatim in the row's own `成本幣別`, never a
    TWD-converted figure) — the exact correctness trap Tech Lead's
    investigation flagged is avoided. Every row runs through the same
    `toRow`/`rowError` the manual editor uses (no second validation copy —
    confirming #367 was the right pick over #370, which the review already
    caught duplicating validation logic). Import only appends to the
    editor's in-memory row state; "儲存快照" is still the only thing that
    persists, so a bad import is always reviewable first. A template
    download exists so users don't have to guess column names.
  - `Finance/hooks/useFinanceAnalysis.ts`: `showComprehensiveErrorAlert` is
    now derived and gated on `analysisTab === TAB_QUANTITATIVE`, with a new
    `useFinanceAnalysis.test.tsx` asserting the comparison tab's own failure
    no longer flips the single-stock tab's shared error Alert. Matches
    QA's reported repro and Tech Lead's scoped fix exactly.
- `REQUIREMENTS.md`: Portfolio Gap Audit's Open Questions section updated —
  proposal 2 (CSV import) marked MERGED/product-verified; the QA bug fix
  noted for the record (no Requirements scope change, it's a bug fix not a
  new feature). Per the already-approved priority order (cross-link → CSV
  import → **rebalancing suggestions** → FIRE tracking), proposal 3
  (rebalancing suggestions against a target allocation) is now the default
  next Tech Lead handoff — no new Boss sign-off needed, same "proceed by
  default" pattern used throughout this Gap Audit.
- No application code changes — Requirements/PM-log/status documentation
  only. Committed and pushed directly to `master` per this role's own git
  rule (§11).

## Open Items
- **Next default Tech Lead item**: Portfolio Gap Audit proposal 3
  (rebalancing suggestions against a target allocation) — ready to scope,
  no Boss reply needed.
- Carried over, escalated by Tech Lead 2026-09-25: whether to adjust the
  scheduling layer to stop multiple sessions from claiming the same `READY`
  task at once (4-way duplicate on `TASK-015`) — this is a Tech
  Lead↔Boss item, not actionable by PM.
- Carried over, unresolved: Credits & Payments removal (DECISION-006)
  blocked on Boss's free-usage-cap design answer in `.agent/boss/PM.md`
  (2026-09-21) — now over a week with no reply across multiple runs; still
  not urgent (current system is dormant, not causing active harm), and does
  not block Gap Audit work.
- Carried over: Phase 2 of speaking delivery feedback (pronunciation
  accuracy, new vendor) — cost/vendor decision pending Boss.
- Carried over: Phase 2 of Finance prediction-accuracy tracking (track-record
  UI) — deliberately unscoped until rows are actually evaluable.
- Carried over: root `CLAUDE.md`'s stale PDF Unlocker "batch processing"
  claim still needs a doc fix outside `.agent/`.

## 2026-09-26 — Tech Lead: closed out TASK-014 bookkeeping, scoped Portfolio Gap Audit item 3 (rebalancing) (scheduled run)

- Read REQUIREMENTS/DECISIONS/ARCHITECTURE/tasks/status/TECH_LEAD.md/PM.md
  per SOP. No new Boss message in either `TECH_LEAD.md` or `PM.md` since
  Tech Lead's/PM's 2026-09-25 entries — DECISION-006's free-usage-cap
  question and the scheduling-layer duplicate-PR escalation are both still
  unanswered (now over a week and one day respectively). Confirmed via
  GitHub: no open PRs, nothing pending review. Picked up the standing Open
  Item: PM's 2026-09-25 handoff of Portfolio Gap Audit proposal 3
  (rebalancing suggestions against a target allocation).
- **Housekeeping**: `TASK-014.md` still read `Status: PR_OPENED` though PR
  #364 was merged directly by Boss on 2026-09-23 (confirmed via the GitHub
  API) without an intervening Tech Lead review pass. Read the merged code
  directly rather than only trusting PM's prior product-verification —
  matches the Architecture doc's scope cut and this task's own Requirements
  exactly; the one real defect that shipped with it (comparison-tab errors
  leaking into the single-stock tab's shared Alert, QA-found) was already
  scoped and fixed as `TASK-016` (merged). Flipped `TASK-014.md`'s `Status`
  to `MERGED` with a Review note recording both findings.
- **Scoped Portfolio Gap Audit proposal 3** (rebalancing suggestions).
  Investigated before writing the design: `REQUIREMENTS.md`'s gap-audit note
  pointed at `ConcentrationCard`/`groupBy=category`, but these turned out to
  be two unrelated mechanisms — `ConcentrationCard` computes per-holding
  concentration, not asset-class allocation, and the actual
  `groupBy=category` endpoint (`GetNetWorthHistoryAsync`) returns a daily
  time series across 16 sector buckets, heavier than this feature needs.
  The 5 asset-type fields `GET api/portfolio/summary` already returns (TW
  Stock/US Stock/Fund/Cash/Real Estate) are exactly what a target-allocation
  feature needs, with zero backend change. Also found a direct precedent
  for where to put the user's target %: `Portfolio/feeDiscountRate.ts`
  already stores a similar per-browser preference number in `localStorage`
  rather than a backend table — this codebase has no user-settings table at
  all, and this feature doesn't need to be the first to add one.
- Documented the investigation and decision in `ARCHITECTURE.md` →
  "Decision: Portfolio rebalancing suggestions against a target allocation —
  Gap Audit item 3". Created `TASK-017` (Frontend only: `rebalancingTarget.ts`
  pure compute + localStorage load/save, a new `RebalancingCard` with 5
  editable target-% inputs seeded from the current allocation on first view,
  current/target/delta per bucket, a soft not-100% warning rather than a
  hard block) — `READY`, unclaimed, no dependency on any other open task.
- Also fixed a stale cross-reference found while editing the Architecture
  doc: the CSV-import decision's closing "See `TASK-014`" pointer was wrong
  (that decision is `TASK-015`'s, a leftover from when both were being
  scoped close together) — corrected to `TASK-015`.
- `REQUIREMENTS.md`: Portfolio Gap Audit's Open Questions section updated —
  proposal 3 marked scoped (not yet implemented), pointing at `TASK-017`/the
  new Architecture decision.
- No application code changes — architecture/task/requirements/status
  documentation only. Committed and pushed directly to `master` per this
  role's own git rule (§16).

## Open Items
- **New, from this run**: `TASK-017` (Portfolio rebalancing suggestions) is
  `READY`, unclaimed — next Frontend Engineer run should pick it up.
- Carried over, unresolved: Credits & Payments removal (DECISION-006)
  blocked on Boss's free-usage-cap design answer in `.agent/boss/PM.md`
  (2026-09-21) — now over a week with no reply across multiple runs; still
  not urgent (current system is dormant, not causing active harm), and does
  not block Gap Audit work.
- Carried over, escalated by Tech Lead 2026-09-25: whether to adjust the
  scheduling layer to stop multiple sessions from claiming the same `READY`
  task at once (4-way duplicate on `TASK-015`) — this is a Tech
  Lead↔Boss item, still unanswered; not re-escalated with new text this run
  since nothing has changed since the last ask.
- Carried over: Phase 2 of speaking delivery feedback (pronunciation
  accuracy, new vendor) — cost/vendor decision pending Boss.
- Carried over: Phase 2 of Finance prediction-accuracy tracking (track-record
  UI) — deliberately unscoped until rows are actually evaluable.
- Carried over: root `CLAUDE.md`'s stale PDF Unlocker "batch processing"
  claim still needs a doc fix outside `.agent/`.

## 2026-09-26 — PM: Product-verified Portfolio rebalancing (TASK-017), unblocked FIRE tracking (scheduled run)

- Read `.agent/boss/PM.md`: no new Boss reply since PM's 2026-09-25 entry —
  DECISION-006's free-usage-cap question is still unanswered (now over a
  week across multiple PM/Tech Lead runs). Read `.agent/boss/TECH_LEAD.md`:
  no new Boss reply there either — the scheduling-layer duplicate-PR
  question Tech Lead escalated 2026-09-25 is also still open. Neither is
  PM's to act on; not re-raised with new text this run since nothing has
  changed since the last ask.
- Confirmed via `git log` that `TASK-017` (Portfolio rebalancing
  suggestions), scoped by Tech Lead earlier today, was already implemented
  and merged (PR #376) by the time this run started — no PR sat open this
  cycle.
- **Product-verified by reading the merged code directly**:
  `Portfolio/rebalancingTarget.ts`'s 5 buckets are exactly the
  `PortfolioSummaryDto` asset-type fields, liability correctly excluded from
  both the bucket list and the `liquidGrossAssetsTWD + realEstateTWD`
  denominator; `RebalancingCard.tsx` seeds first-run inputs from the current
  allocation (no "sell everything" wall before the user expresses a
  preference), persists targets to `localStorage` via the same
  `readJson`/`writeJson` wrapper `feeDiscountRate.ts` uses, and renders the
  sum-not-100% case as a soft caption warning that never blocks editing.
  Matches `TASK-017`'s requirements and the Architecture doc's scope cut
  exactly. `REQUIREMENTS.md`'s Portfolio Gap Audit section updated — **three
  of Portfolio's four originally-prioritized Gap Audit proposals (cross-link,
  CSV import, rebalancing) are now merged and product-complete.**
- **Unblocked the last item**: per the already-approved priority order
  (cross-link → CSV import → rebalancing → **FIRE/goal tracking**),
  Portfolio's proposal 4 (FIRE/goal tracking, reusing existing
  XIRR/net-worth-history data for a forward projection) is now the default
  next Tech Lead handoff — no new Boss sign-off needed, same pattern used
  throughout this Gap Audit. Once this ships, Portfolio's Gap Audit list is
  fully worked through, matching Finance's already-complete list.
- No application code changes — Requirements/status documentation only.
  Committed and pushed directly to `master` per this role's own git rule
  (§11).

## Open Items
- **Next default Tech Lead item**: Portfolio Gap Audit proposal 4
  (FIRE/goal tracking) — ready to scope, no Boss reply needed. Last item on
  Portfolio's originally-prioritized Gap Audit list.
- Carried over, unresolved: Credits & Payments removal (DECISION-006)
  blocked on Boss's free-usage-cap design answer in `.agent/boss/PM.md`
  (2026-09-21) — now over a week with no reply across multiple runs; still
  not urgent (current system is dormant, not causing active harm), and does
  not block Gap Audit work.
- Carried over, escalated by Tech Lead 2026-09-25: whether to adjust the
  scheduling layer to stop multiple sessions from claiming the same `READY`
  task at once — this is a Tech Lead↔Boss item, still unanswered.
- Carried over: Phase 2 of speaking delivery feedback (pronunciation
  accuracy, new vendor) — cost/vendor decision pending Boss.
- Carried over: Phase 2 of Finance prediction-accuracy tracking (track-record
  UI) — deliberately unscoped until rows are actually evaluable.
- Carried over: root `CLAUDE.md`'s stale PDF Unlocker "batch processing"
  claim still needs a doc fix outside `.agent/`.

## 2026-09-27 — Tech Lead: closed out TASK-016/017 bookkeeping, scoped Portfolio Gap Audit item 4 — FIRE tracking, last item (scheduled run)

- Read REQUIREMENTS/DECISIONS/ARCHITECTURE/tasks/status/TECH_LEAD.md per
  SOP. No new Boss message in `TECH_LEAD.md` since the 2026-09-13 sign-off
  on the SEO middleware direction — DECISION-006's free-usage-cap question
  and the scheduling-layer duplicate-PR question (escalated 2026-09-22/25)
  are both still unanswered, neither blocking this work. Confirmed via
  GitHub (`list_pull_requests`, state=open): zero open PRs.
- **Housekeeping**: `TASK-016.md` (PR #371) and `TASK-017.md` (PR #376)
  were both still `Status: PR_OPENED`/`Review: Pending` though the GitHub
  API confirms both merged (`merged_at` 2026-09-25T12:59:11Z and
  2026-09-26T22:59:58Z respectively). Read the actual merged code on
  `master` directly for both rather than only trusting each PR's own
  description: `useFinanceAnalysis.ts`'s `showComprehensiveErrorAlert`
  correctly tab-gates the shared mutation error (TASK-016); `Portfolio/
  rebalancingTarget.ts`/`RebalancingCard.tsx` correctly exclude liability
  from the 5 allocation buckets and the denominator, seed first-run inputs
  from the current allocation, and persist via the same `utils/storage.ts`
  wrapper `feeDiscountRate.ts` uses (TASK-017). No issues found in either.
  Flipped both task files' `Status` to `MERGED` with Review notes.
- **Picked up Portfolio Gap Audit proposal 4** (FIRE/goal tracking), handed
  off by PM 2026-09-26 — the **last** item on Portfolio's originally-
  prioritized 4-item Gap Audit list. Investigated before scoping:
  `PortfolioSummaryDto` (`GET api/portfolio/summary`) already returns both
  `totalNetWorthTWD` (current net worth) and `annualizedReturnPct` (a real,
  all-snapshot-history, dividend-inclusive money-weighted XIRR) — exactly
  what a FIRE projection needs, zero backend change. One correction to the
  original proposal's framing: it named `GetNetWorthHistoryAsync` as the
  data source, but that endpoint clamps to a 180-day window server-side
  (`Math.Clamp(days, 7, 180)`), a poor basis for a multi-year compounding
  assumption — the summary endpoint's all-history XIRR is the right number
  to build on instead (same category of correction as the multi-stock
  sector-data gap and the CSV-import schema mismatch found earlier in this
  Gap Audit). Also confirmed `annualizedReturnPct` is nullable (null until
  30 days of snapshot history exist, a deliberate anti-misleading guard,
  not a bug) — the UI must never silently treat that null as a 0% growth
  assumption.
- Documented the full investigation and decision in `ARCHITECTURE.md` →
  "Decision: Portfolio FIRE/goal tracking — Gap Audit item 4 (last item)".
  Created `TASK-018` (Frontend only: `fireTarget.ts` pure compute +
  localStorage load/save modeled on `rebalancingTarget.ts`, a year-by-year
  `computeYearsToGoal` simulation supporting an optional monthly
  contribution — not a closed-form formula, since a contribution term
  doesn't reduce to one cleanly — new `FireCard` with target net worth /
  growth rate / monthly contribution inputs, seeded from the real historical
  rate when available or a clearly-labeled placeholder otherwise) — `READY`,
  unclaimed, no dependency on any other open task.
- `REQUIREMENTS.md`: corrected Portfolio Gap Audit's proposal 4 line to
  reflect the `net-worth-history` → summary-endpoint data-source correction
  and point at `TASK-018`.
- No application code changes — architecture/task/requirements/status
  documentation only. Committed and pushed directly to `master` per this
  role's own git rule (§16).

## Open Items
- **New, from this run**: `TASK-018` (Portfolio FIRE/goal tracking) is
  `READY`, unclaimed — next Frontend Engineer run should pick it up. Once
  shipped, all four of Portfolio's originally-prioritized Gap Audit
  proposals will be complete, matching Finance's already-complete list —
  the next PM/Tech Lead workstream after that has no standing default item
  queued (would need a fresh Boss/PM direction, e.g. a new gap-audit pass
  on PDF Unlocker or revisiting Credits & Payments removal).
- Carried over, unresolved: Credits & Payments removal (DECISION-006)
  blocked on Boss's free-usage-cap design answer in `.agent/boss/PM.md`
  (2026-09-21) — now over a week with no reply across multiple runs; still
  not urgent (current system is dormant, not causing active harm), and does
  not block Gap Audit work.
- Carried over, escalated by Tech Lead 2026-09-25: whether to adjust the
  scheduling layer to stop multiple sessions from claiming the same `READY`
  task at once — this is a Tech Lead↔Boss item, still unanswered.
- Carried over: Phase 2 of speaking delivery feedback (pronunciation
  accuracy, new vendor) — cost/vendor decision pending Boss.
- Carried over: Phase 2 of Finance prediction-accuracy tracking (track-record
  UI) — deliberately unscoped until rows are actually evaluable.
- Carried over: root `CLAUDE.md`'s stale PDF Unlocker "batch processing"
  claim still needs a doc fix outside `.agent/`.

## 2026-09-27 — Frontend Engineer: TASK-018 implemented, PR opened (scheduled run)

- Checked for in-flight PRs before starting (per the multi-session duplicate-PR
  pattern flagged repeatedly on this project, most recently `TASK-015`'s
  4-way collision) — GitHub currently has **zero** open PRs, so no risk of
  duplicating another session's work this run.
- Picked up `TASK-018` (`READY`, unclaimed, no dependency on any other open
  task). Read `ARCHITECTURE.md`'s "Decision: Portfolio FIRE/goal tracking"
  section and the `rebalancingTarget.ts`/`RebalancingCard.tsx`/
  `feeDiscountRate.ts` precedent before writing any code, per SOP §6.
- Implemented exactly as scoped: `Portfolio/fireTarget.ts` (`FireTarget`
  type, `loadFireTarget`/`saveFireTarget` via `utils/storage.ts`,
  `defaultAnnualGrowthRatePct` — never coerces a null historical XIRR to 0%,
  `computeYearsToGoal` — year-by-year simulation, not closed-form, capped at
  100 years) and `Portfolio/FireCard.tsx` (three inputs, live recompute,
  localStorage persistence on every edit, an explanatory caption when the
  growth-rate input is still the undocumented-as-real placeholder). Wired
  into `Portfolio/index.tsx` as a new `ChartSectionCard` right after
  `RebalancingCard`, reusing the same already-fetched `summary` — no second
  `/summary` fetch, no backend/DTO change of any kind.
- One judgment call beyond the task's literal three outcome states: added a
  fourth "請先輸入目標淨值" empty-state for `targetNetWorthTWD <= 0`, since
  the task's own default (target starts at `0`) would otherwise show a false
  "已達成目標" before the user has entered anything — flagged as a
  Known Issue on `TASK-018.md` in case Tech Lead wants a different default.
- `npm run lint` (0 warnings — `node_modules` needed a fresh `npm ci` in this
  sandbox first), `npm run build`, and `npx vitest run` all green — 207/207
  tests (11 new: `fireTarget.test.ts`'s already-reached/hand-computed
  growth+contribution/pure-lump-sum/100-year-cap/placeholder-vs-real-rate/
  localStorage-round-trip cases, `FireCard.test.tsx`'s
  `renderToStaticMarkup` coverage of all four render states), no
  regressions. Also rendered the component to a static HTML file directly in
  this sandbox to manually confirm Prettier's line-wrapping of the Chinese
  copy across source lines didn't introduce stray whitespace in the actual
  rendered text.
- Opened a PR against `master`; see `TASK-018.md` for full implementation
  notes, including the one flagged known-issue (empty-state judgment call)
  and the not-manually-browser-tested limitation shared with
  `TASK-014`/`015`/`017`. Subscribed to the PR to drive it to green / respond
  to review.
- All four of Portfolio's originally-prioritized Gap Audit proposals
  (cross-link → CSV import → rebalancing → FIRE tracking) now have an
  implementation, pending this PR's review/merge.

## 2026-09-28 — PM: Recorded free-usage-cap design (DECISION-007), diagnosed the SEO indexing gap Boss reported (scheduled run)

- Boss replied in `.agent/boss/PM.md` with two items: (1) an answer to the
  week-old free-usage-cap open question, and (2) a priority interrupt —
  Google-searching "JackyAI English Apple" still doesn't surface the
  Dictionary word page that `TASK-001`–`003` were supposed to make
  discoverable.
- **Free-usage-cap design finalized**: normal users see zero payment/credit
  language anywhere in the UI; the app just looks free. Behind the scenes,
  each currently credit-gated feature keeps a per-user daily/periodic usage
  cap as the only cost-control mechanism; hitting it disables the feature
  and shows a message pointing heavy users at `jacky19918@gmail.com` for a
  higher limit — no self-serve purchase path. Recorded as **DECISION-007**;
  `REQUIREMENTS.md`'s Credits & Payments requirement updated with concrete
  acceptance criteria and its last open design question marked resolved —
  **this requirement is now unblocked and ready for Tech Lead**, queued
  behind the SEO item below per Boss's explicit priority request.
- **SEO investigation** (verified live rather than trusting prior merged-PR
  self-reports): ran a `WebSearch` for `site:jackyai.azurewebsites.net
  apple` — **zero results**, meaning the whole domain isn't indexed by
  Google yet, not just ranking low. Re-checked `SitemapController.cs`
  (sitemap.xml/robots.txt) and `WordSeoTextFormatter.cs` on `master`
  directly — both are correct as originally shipped, so this isn't a
  `TASK-001`–`003` code defect. The actual gap: nobody has ever verified the
  domain in Google Search Console or submitted the sitemap there, which is
  the normal trigger for Google to actually crawl a new site — a one-time,
  no-code, owner-only action, not something Tech Lead can do from the repo.
  Separately found a real branding gap worth a Boss decision: the site's
  actual page title/text is "EngAI Learn" (`index.html`) / "{word} - EngAI
  Learn Dictionary" (`WordSeoTextFormatter.cs`) — "JackyAI" appears only in
  the hostname, never in the crawlable page text, which could hurt matching
  for a query that includes "JackyAI" even after indexing.
- `REQUIREMENTS.md`'s Growth & Retention → SEO section updated with this
  Gap Audit, two new acceptance criteria (Search Console checklist done;
  branding decision made), and re-flagged as top-of-queue ahead of Credits &
  Payments removal, matching Boss's "插單" request. Replied to Boss in
  `.agent/boss/PM.md` with the findings, a recommendation to add "JackyAI"
  to the visible title, and what's Tech Lead's to do next vs. what only
  Boss can do (the Search Console step itself).
- No code changes — Requirements/Decisions/PM-log documentation only.
  Committed and pushed directly to `master` per this role's own git rule
  (§11).

## Open Items
- **New, top priority**: SEO indexing gap — Tech Lead should (a) write Boss
  a Search Console verification + sitemap submission checklist, (b) confirm
  no other on-page technical blocker exists (e.g. check actual rendered
  HTML, not just the API response), (c) implement adding "JackyAI" to the
  visible title/branding once Boss answers the open naming question below.
- **Boss decision needed**: should "JackyAI" be folded into the visible page
  title/branding (currently "EngAI Learn" only)? PM's recommendation is yes;
  not implemented pending Boss's reply.
- **Boss action needed** (not something an agent can do): verify
  `jackyai.azurewebsites.net` in Google Search Console and submit
  `sitemap.xml` there — the actual blocker on Google indexing anything on
  the site at all.
- Credits & Payments removal (DECISION-006/007) — **fully unblocked**, ready
  for Tech Lead to scope; queued behind the SEO item above.
- Carried over, escalated by Tech Lead 2026-09-25: whether to adjust the
  scheduling layer to stop multiple sessions from claiming the same `READY`
  task at once — still unanswered, Tech Lead↔Boss item.
- Carried over: Phase 2 of speaking delivery feedback (pronunciation
  accuracy, new vendor) — cost/vendor decision pending Boss.
- Carried over: Phase 2 of Finance prediction-accuracy tracking (track-record
  UI) — deliberately unscoped until rows are actually evaluable.
- Carried over: root `CLAUDE.md`'s stale PDF Unlocker "batch processing"
  claim still needs a doc fix outside `.agent/`.

## 2026-09-29 — Tech Lead: SEO rendering audit + Search Console checklist

- Audited rendered output: word-page title/description are injected client-side
  via `react-helmet-async` after the API responds; raw HTML has only
  `<title>EngAI Learn</title>`, no meta description/canonical. Not the cause of
  zero indexing (Googlebot renders JS) but a secondary weakness.
- Search Console verification + sitemap submission checklist written to
  `.agent/boss/TECH_LEAD.md`. No code changes; no new tasks until Boss answers
  the JackyAI naming question and whether to do server-side metadata injection.
- `TASK-018` (PR #378) is merged; its `Status` still reads `PR_OPENED` — to be closed out next run.
- Next: Credits & Payments removal scoping (DECISION-006/007).

## 2026-09-30 — Tech Lead: Search Console verification PR

- Boss supplied the verification meta tag; opened PR #386 (one line in `index.html`). Awaiting Boss merge/deploy, then verify + submit sitemap.
- `TASK-018` Status closed out as `MERGED`.
- Next: Credits & Payments removal scoping (DECISION-006/007).

## 2026-09-30 — Tech Lead: JackyAI branding + SEO Phase B tasks

- Boss confirmed site name is JackyAI and conditionally approved Phase B (pros/cons in
  `boss/TECH_LEAD.md`). Created `TASK-019` (brand titles) and `TASK-020` (server-side word
  metadata injection). PR #386 (Search Console meta) awaiting Boss merge.

## 2026-09-30 — Tech Lead: Boss 改名決定

- Boss 放棄「JackyAI」品牌，改用各模組產品名稱（Wealth Portfolio / Stock Intelligence /
  English Coach / Job Finder / Data Analyst / Data Dashboard）；Phase B 核准。
- `TASK-019` 依新名稱改寫，`TASK-020` 標示已核准。已回覆 Boss（建議 Data Analyst→Data Analysis）。
- 待 Boss：合併 #386、確認 Data Analyst 命名、首頁 title 預設。

## 2026-09-30 — PM: 回覆 Boss 溝通方式／PM.md 過大問題

- 於 `boss/PM.md` 提出 A（瘦身）/ B（GitHub Issues，PM 建議）/ C（自建）三案，等待 Boss 裁示；核准前不刪除任何內容。
- 請 Tech Lead 評估 B 對任務流程與 READY task 重複認領問題的影響。

## 2026-09-30 — PM: Boss 核准 GitHub Issues 溝通方式

- DECISION-008 已記錄；`boss/PM.md` 瘦身；開 Issue #387 請 Tech Lead 評估流程影響（TECH_LEAD.md 瘦身、TASK 看板、READY task 認領）。
- 待 Boss：合併 #386、確認 Data Analyst 命名／首頁 title。

## 2026-09-30 — Tech Lead: 評估 Issue #387（DECISION-008 流程影響）

- `boss/TECH_LEAD.md` 瘦身為未結案摘要（舊對話在 git history）。
- TASK 維持 `.agent/tasks/`（與 PR/驗收標準同 repo、可 review）；不改 Projects 看板。
- READY 重複認領：Issue assignee 無效（所有 Agent 共用同一 GitHub 帳號）；建議「開工前先 push `Status: IN_PROGRESS` 到 master」，需改 `.agents/`，待 Boss 授權。

## 2026-09-30 — Tech Lead: 處理 Boss 對 #387 的回覆

- Boss 確認 `Data Analysis` 命名；單字頁 title 改 `English Coach - {單字}`，Exam 等頁不帶單字。`TASK-019` 已更新，可開工（READY），TASK-020 隨後。
- `TASK-015` Status 補為 `MERGED`（PR #367 已合併）。
- Boss 授權修改 `.agents/`，但 repo 內無此目錄；已於 `boss/TECH_LEAD.md` 請 Boss 指示位置。
- Next: Credits & Payments 移除範圍規劃（DECISION-006/007）。

## 2026-09-30 — Tech Lead: TASK-019 結案、工作流程規則落地

- `TASK-019` PR #388 已合併，Status 改 `MERGED`；`TASK-020` 依賴已滿足（READY）。
- Boss 指出目錄為 `.agent/`：認領規則（IN_PROGRESS 先 push）與 Issue 溝通規則寫入 `ARCHITECTURE.md`。
- Next: Credits & Payments 移除範圍規劃（DECISION-006/007）。

## 2026-09-30 — Backend Engineer: TASK-020 implemented, PR opened (scheduled run)

- Checked for in-flight PRs first: zero open PRs, no existing PR/branch touches TASK-020 —
  unclaimed, safe to start.
- Implemented as scoped: new `WordPageMetadataMiddleware` (`Common/`) — for GET/HEAD requests to
  `/english/dictionary/{word}`, reads the built `index.html` (cached in memory after first read),
  looks up the word via `IDictionaryService.GetWordAsync`, and replaces/inserts `<title>`,
  `<meta name="description">`, `<link rel="canonical">` using `Word.SeoTitle`/`SeoDescription` —
  the same single formatting source `WordSeoTextFormatter`/`BotPreviewMiddleware` already use, no
  second reformatting anywhere. Registered in `Program.cs` right after `BotPreviewMiddleware`
  (which already fully owns the link-preview-bot case), before the static-file pipeline. Every
  failure path (non-GET/HEAD, non-word path, missing template, unknown word, lookup
  exception/timeout) falls through unchanged before touching the response.
- No `dotnet` SDK/build in this sandbox — compensated with a manual read-through plus a separate
  adversarial review-agent pass (fresh context, read every referenced production file including
  `WordMapper.cs`, `DictionaryService.cs`, the real `jackyaiapp.client/index.html`). The review
  found no compile errors and no page-load-breaking bug, but caught two real issues fixed before
  opening the PR: (1) `Regex.Replace` with a plain replacement string would misread a literal `$`
  in word/definition text as a backreference token (`$1`, `$&`) — switched to `MatchEvaluator`
  lambdas; (2) HEAD requests were writing a full response body — not spec-correct — fixed to send
  headers only. Both have dedicated tests now.
- 9 new unit tests (`WordPageMetadataMiddlewareTests`) covering injection, hostile-data HTML
  encoding, the `$`-backreference edge case, canonical replace-vs-insert, non-word paths, missing
  template, unknown word, lookup exception, and the GET/HEAD success paths — satisfies all of
  TASK-020's listed acceptance-criteria test cases plus the two review findings.
- Opened [PR #389](https://github.com/twjackysu/JackyAIApp/pull/389); full implementation notes,
  including three flagged-but-out-of-scope items (canonical-URL scheme read before
  `UseHttpsRedirection` — a pre-existing pattern `BotPreviewMiddleware` already has, now exposed to
  all user agents instead of just bots; no insert-if-missing fallback for `<title>`/description if
  those tags are ever removed from `index.html`; the lookup timeout can't cancel the underlying
  call since `IDictionaryService.GetWordAsync` has no `CancellationToken` parameter), are in
  `TASK-020.md`'s Result section. Subscribed to the PR to drive it to green / respond to review.
- CI not yet confirmed as of this log entry — next event on the PR (or next scheduled run) should
  check `build-and-test-server`/`lint-and-build-client` and act on any failure.
- Update: CI confirmed green on the current head (`lint-and-build-client` + `build-and-test-server`
  both `success`), `mergeable_state: clean`, no reviews/comments yet. No Claude Approvals check
  configured on this repo. Nothing further for Backend to do — waiting on Tech Lead/Boss review.

## 2026-10-01 — Backend Engineer: PR #389 (TASK-020) merged into master

- `#389` was merged (GitHub `pull_request.closed`/`merged` event, session auto-unsubscribed) with
  no review comments — CI was green and `mergeable_state: clean` before merge. `master` now has
  `WordPageMetadataMiddleware`: server-side `<title>`/meta description/canonical injection into
  `index.html` for `/english/dictionary/{word}` GET/HEAD requests, for every user agent. Not
  recording `TASK-020.md`'s `Status:` as `MERGED` myself per SOP (§10/§18 reserve that to Boss) —
  leaving it `PR_OPENED` for Tech Lead/Boss to formally close out; this log entry is the factual
  record that the PR landed.
- All of SEO Phase B (`TASK-019` brand titles + `TASK-020` server-side metadata) is now shipped.
  The three known-limitation items noted in `TASK-020.md`'s Result section (canonical-URL scheme
  read before `UseHttpsRedirection`, no insert-if-missing fallback for `<title>`/description, the
  lookup timeout not cancelling the underlying call) are flagged there for whoever picks up the
  next SEO-related work, not acted on further here.
- This branch's own PR (`#389`) was already merged when this run started, so per the fresh-change
  convention this log entry's commit restarts `claude/magical-heisenberg-nnsu2t` from the current
  `master` tip rather than stacking on the merged history.

## 2026-10-01 — Tech Lead: Credits & Payments 移除規劃完成（scheduled run）

- `TASK-020` 已確認 PR #389 合併，Status → `MERGED`；SEO Phase B 全數完成。
- Credits 移除技術設計寫入 `ARCHITECTURE.md`：單一加權每日預算（`DailyUsage` 表、原子條件式更新、`UsageLimits:DailyBudget` 預設 60、`User.UsageLimitMultiplier` 供個別調高），超量回 HTTP 429 + `ErrorCodes.UsageLimitExceeded`，文案由前端持有。
- 新任務：`TASK-021`（Backend，新機制，READY）、`TASK-022`（Frontend，移除 UI，READY）、`TASK-023`（Backend，刪除金流/Credit 程式碼，BLOCKED 待 021/022）、`TASK-024`（DROP schema，BLOCKED 待 Boss 同意）。
- Boss 確認事項開在 Issue #394。無 open PR。

## 2026-10-01 — Backend Engineer: TASK-021 implemented, PR opened (scheduled run)

- 確認無現存 open PR 觸及 TASK-021，且 Issue #394 Boss 已回覆「同意」（每日預算 60 與 TASK-024
  DROP 皆同意）後開工。
- 依 ARCHITECTURE.md「Credits & Payments 移除設計」實作：`UsageCosts`（沿用 `CreditCosts` 權重，
  不含三個獎勵常數）、`UsageLimitOptions`（`UsageLimits:DailyBudget` 預設 60）、新表 `DailyUsage`
  （複合 PK `(UserId, Date)`）+ `User.UsageLimitMultiplier`、`IUsageLimitService`/
  `UsageLimitService`（單一原子條件式 UPDATE + insert-if-missing，避免 read-modify-write race）、
  `[UsageLimit(cost, feature)]` filter 取代所有 `[ConsumeCredits]`，並替換 ExamService/
  AudioController/ChatbotController（含退款）/DataAnalysisController/FinanceQueryService
  (MarketDigest 每日一次邏輯保留)/PortfolioService 的內嵌呼叫點。`ErrorCodes.UsageLimitExceeded =
  1042900` 透過既有 `ResponseFactory` 公式自動對應 HTTP 429，無需改動 factory。
- 手寫 EF migration（`20261001050000_AddUsageLimit`，含 Designer.cs 與
  `AzureSQLDBContextModelSnapshot.cs`，因 sandbox 無 `dotnet` SDK）；逐行比對最近一次同類純手寫
  migration（`20260913120000_AddUniqueIndexOnWordText`）的結構以降低風險。
- 1 處順帶修正（非擴大範圍）：`PortfolioController.GetAiTechSummary` 原本把所有服務層失敗（含超
  量）都映射成 `InternalServerError`，為了讓這個端點真正符合 429 契約，替 `GetAiTechSummaryAsync`
  的回傳 tuple 加了 `ErrorCodes?` 欄位。
- 測試：新增 `UsageLimitServiceTests.cs`（改用 SQLite in-memory，因為這個服務依賴的
  `ExecuteSqlInterpolatedAsync` 在 InMemory provider 上完全不支援；涵蓋預算內/超量/跨日重置/
  multiplier/未知使用者/refund 不低於零/10 併發請求不超賣且底層資料正確）、
  `UsageLimitAttributeTests.cs`（未登入擋下、成功放行、超量回 429、訊息不含 credit/purchase/pay）
  ；`ExamServiceTests.cs`、10 個 `Services/Portfolio/*.cs` 測試檔改用 `IUsageLimitService` mock。
  為此在 Tests.csproj 加入 `Microsoft.EntityFrameworkCore.Sqlite`（僅測試用，任務驗收標準本身即
  建議 SQLite 作為替代方案）。
- 未刪除任何舊 Credit/Payment 程式碼（維持並存，依部署順序等 TASK-022 前端上線後才由 TASK-023
  刪除）；grep `Insufficient credits` 於非 Payment/Credit 檔案已無殘留。
- 本 sandbox 無 `dotnet` SDK/DB 連線——以逐檔手動讀過 + migration Designer/Snapshot 逐行比對代替
  實際編譯驗證，PR 上仍需靠 CI 確認建置與測試通過。
- PR：見 `TASK-021.md`。`TASK-022`（Frontend）現在可以依 429 + `UsageLimitExceeded` 契約開工。
- Update：CI 第一輪 `build-and-test-server` 紅——抓到自己測試裡的真錯誤（非產品程式碼）：
  `TryConsumeAsync_UnknownUser_FallsBackToDefaultMultiplier` 假設不存在的 user 仍會透過預設
  multiplier 入帳，但 `DailyUsage.UserId` 的 FK 正確擋下該筆 insert（現實中呼叫者一定已有
  persisted user）。修正測試斷言本身（改為「安全回傳 false、不拋例外」），未動 FK/production
  code。已推送修正，`build-and-test-server`/`lint-and-build-client` 皆綠燈，`mergeable_state:
  clean`，尚無 review comments，等待 Tech Lead/Boss review。

## 2026-10-01 — Frontend Engineer: PR #395（TASK-022）已開立並合併

- 完成 TASK-022：刪除 `Credits/`、`Pricing/`、`InsufficientCreditsModal`、`apis/creditApis|paypalApis|ecpayApis`、`constants/creditCosts.ts(+test)`、`components/Footer.tsx`；`errorMiddleware`/`useChatStreaming` 改依 HTTP 429 + `ErrorCodes.UsageLimitExceeded`（1042900）開啟新增的 `UsageLimitModal`（固定文案，不 parse message）；移除全部 Exam/Finance/Portfolio/DataAnalysis/Chatbot 的點數成本標籤與 `creditsAwarded` 顯示。
  **範圍微調**：額外刪除 `apis/payuniApis`——`Credits/index.tsx` 是其唯一呼叫點，刪除後會變殭屍 slice，已在 PR 說明中註明供 Tech Lead 確認。
- `npm run lint`/`build`/`test` 皆通過（222 tests，含新增 `redux/errorMiddleware.test.ts` 4 案例，覆蓋「超量提示」行為）。
- 開 PR [#395](https://github.com/twjackysu/JackyAIApp/pull/395) 並訂閱其活動；CI（`lint-and-build-client`、`build-and-test-server`）皆綠燈，`mergeable_state: clean`，無 review comment。
- `#395` 隨後被合併（GitHub `pull_request.closed`/`merged` 事件，session 自動取消訂閱）。依 SOP（§10/§18：MERGED 狀態保留給 Boss 標記）不自行把 `TASK-022.md` 的 `Status:` 改成 `MERGED`，維持 `PR_OPENED`，由 Tech Lead/Boss 正式結案；本條記錄即合併事實。
- 開發當下 TASK-021（後端）尚未合併，純依 `ARCHITECTURE.md` 文件契約開發；合併後回頭讀了實際落地的
  `UsageLimitAttribute`/`ResponseFactory`/`ChatbotController` 程式碼做事後驗證（無 `dotnet` SDK 可跑
  整合測試，用讀碼代替）：
  - `UsageLimitAttribute` 對一般端點回 `responseFactory.CreateErrorResponse(ErrorCodes.UsageLimitExceeded, ...)`；
    `ResponseFactory.CreateErrorResponse` 用 `(int)code / 100 % 1000` 算 HTTP status，1042900 → 429，
    且沿用既有 `{error:{code,message}}}` envelope（與 `errorMiddleware`/`getApiErrorMessage` 既有解析邏輯相同）——
    與前端判斷式 `status === 429 && error.code === ErrorCodes.UsageLimitExceeded` 完全吻合。
  - `ChatbotController.Chat`（SSE 串流端點，走的是手動 `Response.WriteAsync`，不經過 `[UsageLimit]` filter）
    超量時只回 429 + 純文字 body `"Daily usage limit exceeded."`，不是 JSON envelope。`useChatStreaming.ts`
    的判斷本來就只看 `response.status === 429`（不依賴 body 結構）開燈，`getApiErrorMessage` 對純文字 body
    也有 fallback 分支可正確顯示——驗證結果：兩條路徑都相容，不需要補 PR。
- 下一步：無——前後端契約已核對一致，TASK-022 視為完整交付。

## 2026-10-01 — Backend Engineer: TASK-021/022 confirmed merged+deployed, TASK-023 implemented, PR opened (scheduled run)

- 開工前確認：PR #396（`TASK-021`）與 PR #395（`TASK-022`）皆已合併（GitHub 顯示 `merged: true`），
  且兩者各自的 "Build and deploy" workflow run（#489、#488）都以 `conclusion: success` 完成——滿足
  `TASK-023` 自己寫的開工條件（021、022「都已合併且部署後」）。`TASK-023.md` 從 `BLOCKED` 改為
  `IN_PROGRESS` 並先行推送這筆狀態變更，再開始實作（本 session 的 git 慣例是全部變更都進自己的
  PR 分支，而非直接推 master）。另注意到 Frontend 另開了一個 docs-only PR #397（記錄 #395 合併 +
  驗證 429 契約相容性），非本任務範圍，未碰。
- 先用一個 Explore subagent 完整盤點所有要刪除/修改的 Credit/Payment/Subscription 程式碼（25+ 檔
  案），再依盤點結果動手，避免遺漏或誤刪。
- 實作：刪除 PayPal/ECPay/PayUni 的 Controller/Service/Options/DI/appsettings/測試、
  `CreditController`、`ICreditService/CreditService(+測試)`、`CreditPack`、`SubscriptionPlan`、
  `ConsumeCreditsAttribute`、`Common/CreditCosts.cs`。`AccountController` 不再建立初始點數/
  `CreditTransaction`；`DailyChallengeService` 移除點數獎勵與整個 `ClaimDailyBonusAsync`（service +
  interface + controller endpoint）；`DailyChallengeSubmitResponse.CreditsAwarded` 移除。
  `User.CreditBalance/TotalCreditsUsed` 保留欄位（型別/mapping 不變，零 migration）但拿掉
  `required`、預設 0、加 `[JsonIgnore]`（滿足「response 不含 credit 欄位」且不用碰 schema）。
- **刻意偏離任務原文的一點**（技術判斷，非產品/架構決策，於 `TASK-023.md` Result 詳述並標記給
  Tech Lead）：任務原文要求「`CreditTransaction`/`Subscription` 實體與 DbSet 移除，但資料表保留」，
  這次**沒有**移除這兩個 entity/DbSet/`User.CreditTransactions` 導覽屬性，而是整個保留（現在完全
  是死碼，沒有任何地方再讀寫）只加註解標記「TASK-023 起未使用，待 TASK-024 一併移除」。這是任務
  原文自己允許的替代方案（「或本任務僅標註 entity 未使用，由 TASK-024 一次移除」）——選它是因為
  要讓 entity 消失同時不讓 migration 產生 DROP，需要手動改 `AzureSQLDBContextModelSnapshot.cs`/
  `.Designer.cs`，而本 sandbox 沒有 `dotnet ef` 可驗證改完後的 migration diff 真的是 inert，風險
  是 snapshot 與 model 不同步，導致未來某個不相干的 migration 意外帶出這兩張表的 DROP TABLE。留給
  `TASK-024`（本來就要和真正的 DROP 一起做、且已有 Boss 核准）處理更安全。
- 更新根 `CLAUDE.md`（Backend Stack 的 resilience 項目、Key Features 第 8 項改寫成 usage-limit
  說明、User entity 一行描述）；`JackyAIApp.Server/CLAUDE.md` 確認本來就沒提到 Credit/Payment，
  不用改。順手把驗收標準 grep 掃到的約 10 處殘留「credit」用語註解（實際上是在講新的
  `[UsageLimit]` 機制，用詞早就該跟著 TASK-021 改）一併修正，並修掉兩處因型別被刪而失效的 XML doc
  `<see cref>`。
- 本 sandbox 無 `dotnet` SDK——以 repo-wide grep 掃過所有被刪型別名稱（確認零殘留，排除保留
  entity 的刻意殘留）、逐檔 brace 平衡檢查、確認專案無 `TreatWarningsAsErrors`，外加一個獨立
  review agent（全新 context）做對抗性複查，皆未發現問題後才 commit。
- PR：[#398](https://github.com/twjackysu/JackyAIApp/pull/398)。已訂閱 PR 活動，CI/review 事件會
  自動推進。`TASK-024`（DROP schema）在 `#398` 合併前仍維持 `BLOCKED`（本任務這次沒有移除
  entity/DbSet，所以 `TASK-024` 之後實際要做的事比原本任務描述稍多：entity+DbSet+nav property
  移除要併入它自己的 migration 裡）。
- Update：`#398` 開立後 master 又多了一個 docs-only PR（#397，記錄 TASK-022 合併），導致
  `PROJECT_STATUS.md` 文字衝突（兩邊都在檔尾 append）。合併 master 進本分支，依時間順序保留雙方
  log entry 後解決並推送；CI（`build-and-test-server`/`lint-and-build-client`）在合併後的 commit
  上皆綠燈，`mergeable_state: clean`，全程無 review comment。
- `#398` 已由 Boss 合併（`pull_request.closed`/`merged` 事件，session 自動取消訂閱）。觸發的
  "Build and deploy" workflow run #491 開始執行中，尚未確認結果。不自行把 `TASK-023.md` 的
  `Status:` 改成 `MERGED`，依 SOP（§10/§18）維持 `PR_OPENED`，留給 Tech Lead/Boss 正式結案；本條
  記錄即合併事實。
- `TASK-024`（DROP schema，LOW priority，含 entity/DbSet 移除 + 真正的 DROP TABLE migration）
  本身的前置條件「先確認 TASK-023 已部署穩定」此刻還不成立（部署剛觸發，尚未確認結果，也還沒有
  穩定運行一段時間）——加上這是不可逆的 schema 變更，且本 sandbox 持續沒有 `dotnet ef` 可驗證
  migration diff，本次不倉促開工。留給下一次有 `dotnet` SDK/CI 存取權、且已確認 `#398` 部署穩定
  的 run 接手。

## 2026-10-01 — Backend：TASK-024 開工並送出 PR（Boss 已同意；entity 檔案刪除部分留給下一輪收尾）

- Boss 已在 Issue #394 明確回覆「同意」（2026-10-01T04:26:22Z，該 Issue 現已關閉），`TASK-023`
  （#398）也已合併並部署確認。兩項前置條件都滿足，本次 run 依 TASK-024 自己的條件由 `BLOCKED`
  改為可開工。
- 程式碼改動：`Data/Models/SQLUser.cs` 移除 `User.CreditBalance`/`TotalCreditsUsed`/
  `CreditTransactions` 導覽屬性（含已變無用的 `using System.Text.Json.Serialization;`）；
  `Data/AzureSQLDBContext.cs` 移除對應的 fluent config 區塊與兩個 `DbSet`；新增 migration
  `20261001160000_DropCreditPaymentSchema`（`Up()` 僅含 2 個 `DropTable` + 2 個 `DropColumn`，
  `Down()` 鏡射還原含 `PayUniMerTradeNo`）；`AzureSQLDBContextModelSnapshot.cs`
  同步移除對應的 entity/relationship/property 區塊。詳細清單見 `TASK-024.md` Result。
- 全 repo grep（排除 `Migrations/`）確認無殘留引用；brace 平衡檢查；確認 frontend 與既有測試都
  不受影響。本 sandbox 無 `dotnet` SDK，無法實際 build/test，與先前幾個 Backend 任務相同限制。
- **環境限制與處理**：commit 前 `git add` 暫存「刪除 `CreditTransaction.cs`/`Subscription.cs`」
  這個動作被本 session 的自動安全分類器擋下，判定為「Cloud Storage Mass Delete」（應為誤判——只
  是刪除 repo 內兩個已變死碼的 C# 類別檔案並提交，git 歷史完全可還原）。按規範不應反覆用不同指令
  規避同一個 denial，所以這次**沒有真的刪除這兩個檔案**，改為還原檔案、只把 doc comment 改寫成
  「orphaned：已無任何 DbSet/fluent config/導覽屬性引用，可直接刪除」，其餘改動照常 commit+push+
  開 PR。這不影響 migration 本身（`DropTable` 只看 SQL schema，不看 C# 檔案存不存在）。留了一個
  trivial 的待辦給下一輪：把這兩個已確認死碼的檔案真正刪除。
- PR：[#400](https://github.com/twjackysu/JackyAIApp/pull/400)。CI（`build-and-test-server`/
  `lint-and-build-client`，CI run #579）在目前 head（`17b5ba4`）上皆綠燈，`mergeable_state: clean`，
  無 review comment。已訂閱 PR 活動，後續 CI/review 事件會自動推進；目前純等待人工 review。
- `#400` 已由 Boss 合併（2026-10-02T01:06:45Z）。觸發的 "Build and deploy" workflow run #493
  開始執行中。**重要**：`Program.cs` 啟動時會自動 `dbContext.Database.Migrate()`，所以這個
  DROP migration 會在部署完成、App Service 重啟後自動套用到正式環境 DB，不需要任何人手動跑
  `dotnet ef database update`。這是不可逆動作，本 sandbox 無法確認 Boss 合併前是否已依任務要求
  做 Azure SQL PITR 備份——已用推播通知告知使用者。

## 2026-10-02 — Tech Lead：Issue #401（Portfolio 載入太慢）分析並拆任務

- Boss 開 Issue #401 要求檢視 Portfolio 效能（時間/空間複雜度、網路呼叫次數）。Tech Lead 讀碼分析（未實測）：
  首屏同時發 5 支請求（summary、net-worth-history x2 不同 days、benchmark、drawdown），後端各自重複載入
  snapshots、重複抓取同一批 symbol 的價格/NAV/股利，且 cache 無 single-flight，cold-cache 時併發重複打上游；
  summary 的 shallow 與圖表的 deep 對同 symbol 各抓一次；TDCC/Cathay 匯率在 summary 內序列 await。
  演算法本身（cursor 推進、日迴圈）已是線性，不是主要瓶頸，瓶頸在 I/O 次數。
- 新增 `TASK-025`（Backend：single-flight、shallow/deep 共用、FX 平行化、snapshots 重用）與
  `TASK-026`（Frontend：圖表進入視窗才載入）。兩者皆 `READY`、MEDIUM、無行為變更、不改 API 契約/TTL，
  不需 Boss 決策。TASK-026 中「FinancialChangesChart 預設天數 180→90」屬顯示預設值，已註明需先確認。
- TASK-024（#400）已合併；DROP migration 會在部署後由啟動時 `Migrate()` 自動套用，待確認 run #493 結果與
  正式環境正常（Backend 前一輪已推播提醒 PITR 備份）。遺留小項：刪除 `CreditTransaction.cs`/`Subscription.cs`
  兩個孤立死碼檔案（trivial，可併入 TASK-025 之外的後續清理）。

## 2026-10-02 — Frontend：TASK-026 實作（Portfolio 圖表進入視窗才載入）

- 排程執行 Frontend Engineer：盤點 `.agent/tasks/`，TASK-022 的 PR #395 已合併（CI 全綠、無 review
  comment），目前 repo 無任何 open PR；TASK-026（Frontend/READY，與 TASK-025 可並行）為唯一可動工項目。
- 新增 `Portfolio/LazyMount.tsx`：`IntersectionObserver` 延後掛載（`rootMargin: 200px`，一旦進入視窗
  永久掛載不再卸載，無 `IntersectionObserver` 時直接掛載降級），未掛載時顯示固定 `minHeight` 的
  `CenteredSpinner` placeholder。無新依賴。
- `Portfolio/index.tsx`：把會自己發 query 的 5 個圖表（`NetWorthTrendChart`/`NetWorthChart`/
  `FinancialChangesChart`/`BenchmarkChart`/`DrawdownChart`）包進 `LazyMount`；吃 `summary` 既有資料、
  本身不發 query 的 4 個（`ContributionWaterfallChart`/`ConcentrationCard`/`RebalancingCard`/
  `FireCard`）維持原樣。`data-tour="portfolio-tour-chart"` 留在 `ChartSectionCard` 本身（在
  `LazyMount` 外），導覽 `scrollIntoView` 本身就會觸發掛載，不需特殊處理。
- Requirement 2（`FinancialChangesChart` 180→90 共用快取）依任務指示**未自行採用**，回報留給 Tech
  Lead/PM 決定是否另開 task。
- 首屏請求數：mount 時從 5 個請求（summary + 2 個 NetWorthHistory + benchmark + drawdown）降到
  1 個（僅 summary）；其餘 4 個延後到捲動到對應圖表時才各自觸發。
- 新增 `Portfolio/LazyMount.test.tsx`（4 tests）。`npm run test` 31 files/226 tests 全過、
  `npm run lint` 0 errors、`npm run build` 成功。
- Status 改為 `PR_OPENED`，PR 連結與完整細節見 `TASK-026.md` Result。
## 2026-10-02 — Backend Engineer：TASK-025 實作並送出 PR（scheduled run）

- 開工前確認：TASK-025 為 `READY`、無 open PR 觸及、無依賴，為本輪唯一可開工的 Backend 任務
  （TASK-021/023/024 均已 `PR_OPENED` 且已合併部署，只待 Tech Lead/Boss 正式結案）。
- 依 Requirements 四項全部處理：
  1. 新增 `Common/SingleFlight.cs`（`ConcurrentDictionary` + `Lazy<Task<T>>` 的泛型 cold-cache
     去重工具，未引入新套件），套用到 `PortfolioService.FetchStockDataAsync`、
     `FundDataService.GetFundNavHistoryAsync`、`DividendService.Get{Stock,Fund}DividendsAsync`
     四個取得路徑；共用 fetch 一律以 `CancellationToken.None` 執行，只有「等待」那一步看呼叫者
     自己的 `ct`，確保一個呼叫者取消不影響其他仍在等待同一結果的呼叫者。
  2. `FetchStockDataAsync` 的 shallow 路徑在自己的 `latest_` cache 未命中時，先檢查 `deep_` cache
     是否已有結果，命中就直接重用（deep ⊇ latest），不另外呼叫上游。
  3. `GetPortfolioSummaryAsync` 內 TDCC/Cathay 兩段原本序列的 foreach+await 迴圈，改成兩組各自
     `Task.WhenAll` 後再一起 `Task.WhenAll`——兩組彼此不互相依賴（分別只讀已就緒的 fundMap/
     stockMap），可安全平行跑；呼叫次數不變，省的是等待時間。
  4. `AllSnapshotsAsync` 加 30 秒短 TTL 快取 + 同一套 single-flight，`SaveHoldingsAsync`/
     `DeleteSnapshotAsync` 成功後立即 `cache.Remove` 失效——評估後判斷失效邏輯並不複雜（兩者本來
     就在同一個類別、已注入同一個 `IMemoryCache`），因此沒有採用任務允許的「若複雜度過高則不做」
     退路。
- 不改 API 契約、不改 `CacheTtl.cs` 既有任何值（新加的 30 秒 TTL 是全新常數）；回傳 DTO 數值無任何
  邏輯變動，純粹減少重複的上游/DB 呼叫次數與等待時間。
- 測試：新增 `Common/SingleFlightTests.cs`（去重機制本身：同 key 併發收斂、不同 key 各自觸發、
  完成後重新觸發、失敗傳播給所有等待者且不卡住重試、一個呼叫者取消不影響其他等待者）、
  `Services/Portfolio/PortfolioSingleFlightTests.cs`（股票報價 single-flight、shallow/deep 共用、
  snapshots 快取讀取與 Save/Delete 失效）、`Services/Portfolio/FundDividendSingleFlightTests.cs`
  （FundDataService/DividendService 三個取得路徑的併發去重，以本地 `GatedHandler` 真正讓請求重疊
  而非依序完成）。確認既有 Portfolio 測試（FX、NetWorth、Drawdown、Benchmark、FeeTax 等）在新快取
  下不會讀到過期資料（逐一確認每個 `[Fact]` 的呼叫次數與資料是否在呼叫間有變動）。
- 本 sandbox 無 `dotnet` SDK，無法實測改動前後的真實上游呼叫次數，PR/TASK-025.md 的 Result 以
  「逐讀程式路徑推導」的估算取代（約 60–80% 的減少，視頁面載入時的實際併發時間差而定），並在
  Result 中明確標註這是估算而非量測；CI 需確認一次真正的 build + test。
- PR：見 `TASK-025.md`。TASK-026（Frontend，圖表進入視窗才載入）為並行任務，未在本 PR 範圍內。

## 2026-10-02 — Tech Lead：TASK-021~026 結案（排程執行）

- 盤點：無 open PR。#403（TASK-026）、#404（TASK-025）皆已由 Boss 合併；TASK-021~026 狀態全部改為 `MERGED`。
- Issue #401（Portfolio 效能）的兩項實作已上線（待部署完成）；實際速度改善未實測，留待 Boss 部署後體感回饋再決定是否續作。
- 分類的剩餘項目：(Optional) FinancialChangesChart 預設 180→90 天以共用快取——顯示預設值變更，待 Boss 決定；(Technical Debt, trivial) 刪除孤立檔案 `CreditTransaction.cs`/`Subscription.cs`。兩者皆未開 task，等 Boss 對 #401 回覆後再處理。

## 2026-10-03 — Tech Lead: Issue #411 / #407 (scheduled run)

- #411：目前已是 MUI v9（9.0.1），升 9.4.0 屬 minor；建立 TASK-027（Frontend, READY）。
- #407：word title/description/canonical 已由 TASK-019/020 實作並合併；其他單字搜不到主因是 Google 尚未收錄（見 Issue 回覆），無需新程式任務。
- 無 open PR。

## 2026-10-03 — PM：Issue #409「讓產品成功」回應（排程執行）

- Boss 在 #409 指出 PM 不應「每次都沒進展」，要負責長期方向、與 Tech Lead 管理進度、並檢視現有小功能的 UX 改進。
- 本輪實際盤點首頁 Portal：發現留存機制（streak／待複習／每日挑戰）全都藏在二層頁面，首頁只是靜態目錄。
- 新增 Requirement「Portal 首頁『今日行動』化 + 產品聚焦」（HIGH）；已在 #409 回覆完整策略與 backlog，並加 `To Tech Lead` 標籤請其估工。
- 待 Boss：確認主力應用名單、是否同意加最小匿名使用量統計。
- Tech Lead Actions：評估 Portal 今日區塊（可否純前端組合既有 API）並建 TASK。

## 2026-10-04 — Tech Lead：TASK-027 結案、建立 TASK-028（排程執行）

- 無 open PR。TASK-027（MUI 9.4.0）已隨 #412 合併 → `MERGED`。
- 新建 TASK-028（Frontend, READY）：Portal 今日行動區塊（Issue #409 / REQUIREMENTS）。投資組合變化項若無輕量端點則不在 Portal 載入時呼叫 `getPortfolioSummary`，回報後另開後端 task。
- Issue #409 的 Boss 回覆（六個頁面皆重要、統計可用 Azure/GA/Matomo）已回 PM；統計方案屬隱私/成本決策，列於 TECH_LEAD.md 待 Boss 決定。

## 2026-10-04 — PM：吸收 Boss 於 #409 的回覆（排程執行）

- 新增 DECISION-009：六個首頁應用皆重要、同意導入用量統計（方案待 Boss 選）。Portal requirement 第 4 點已修訂為「不隱藏、不弱化」，與 TASK-028 一致。
- 待 Boss：統計方案選擇。無 open PR。

## 2026-10-04 — Tech Lead：TASK-028 結案（排程執行）

- 無 open PR。TASK-028（Portal 今日行動）已隨 #414 合併 → `MERGED`。
- 分類：「Portal 顯示投資組合變化」需要輕量後端端點（現有 summary 會 deep fetch 上游報價）——**Future Work**，不開 task；待 Boss 對 #409 的優先順序／用量統計方案回覆後，由 PM 決定是否值得做，再由 Tech Lead 設計（可選方案：以快取的最新淨值快照回傳總資產與上次變化，不觸發上游請求）。
- 仍待 Boss：用量統計方案（見 TECH_LEAD.md 第 11 項）。

## 2026-10-04 — PM：Boss 選定 Application Insights（排程執行）

- Boss 於 #409 選定 Application Insights → DECISION-010；新增 Requirement「匿名用量統計」（MEDIUM，TASK-028 之後）。
- Tech Lead Actions：評估並建 task（前端 SDK 或後端遙測由其決定）。無 open PR。

## 2026-10-04 — Tech Lead：建立 TASK-029 匿名用量統計（排程執行）

- 無 open PR。盤點發現後端已有 Azure Monitor OpenTelemetry（需連線字串），但 SPA 路由切換不會產生 server request，無法得到按頁面的造訪統計。
- 新建 TASK-029（Frontend + 極小後端端點, READY）：browser SDK 動態載入、手動 page view（去 query、單字頁彙整）、不帶任何個資、連線字串由 `GET /api/config/telemetry` 提供。
- 待 Boss：Cookie／隱私告知文字（見 TECH_LEAD.md 第 12 項）。

## 2026-10-04 — Tech Lead：回覆 Boss 對 TASK-029 的 Cookie／識別問題（排程執行）

- 無 open PR。Boss 同意用 cookie，並詢問是否做同意對話框、是否識別用戶（授權 PM 與 Tech Lead 決定）。
- 決定：不做阻擋式同意框（匿名統計於台灣個資法無事前同意要求），改頁尾一行告知；維持匿名，長期留存用既有 DB 後端查詢。已更新 TASK-029（新增頁尾告知需求）與 TECH_LEAD.md 第 13 項。PM 請知悉：不需再另行處理。

## 2026-10-04 — Tech Lead：TASK-029 結案（排程執行）

- 無 open PR。TASK-029（匿名用量統計）已隨 #415 合併 → `MERGED`。
- 部署後須由 Boss 於 Azure 設定 App Insights 連線字串（後端 `GET /api/config/telemetry` 讀取既有設定），否則統計不會啟用。無其他待辦。

## 2026-10-04 — PM：App Insights 連線字串已就緒（排程執行）

- Boss 於 #409 回覆：Key Vault 已有 `ApplicationInsights--ConnectionString`。經核對，後端 `ConfigController` 與 `Program.cs` 讀取的設定鍵為 `ApplicationInsights:ConnectionString`，與 Key Vault 的 `--` 命名對應 → TASK-029 的部署後設定項已完成，無待辦。
- 待驗證（部署後）：Azure Portal 是否開始出現 page view 資料；若無資料請 Tech Lead／QA 排查。無 open PR。

## 2026-10-06 — PM：逐模組 UX／留存盤點（手動追加，回應 Boss「團隊是否空轉」）

- 承認前次排程誤判 NO_ACTION：只檢查 Boss 指令，未履行 #409 承諾的「主動盤點其餘應用」。
- 已新增 Requirement「逐模組 UX／留存改進」（7 項，1 項 HIGH／3 項 MEDIUM／3 項 LOW），依據為程式碼閱讀、未實機驗證。
- Tech Lead Actions：請估工並建 task；建議順序 1（Exam 結果頁下一步）→ 2（不得無聲失敗）→ 3、4。另請評估 #407 的單字頁內部連結（PM 建議 MEDIUM）。
- 此後排程 PM 的判準：「無 Boss 指令」≠「無事可做」；backlog 有未交付項目時須推進。

## 2026-10-06 — Tech Lead：拆解 PM「逐模組 UX／留存改進」（排程執行）

- 無 open PR。依 PM 建議順序建立 3 個 Frontend task（皆 READY，可並行）：
  - TASK-030（HIGH）：Exam／Conversation 結束後的下一步（第 1 項）。
  - TASK-031（MEDIUM）：不得無聲失敗 + 單字包加入後入口（第 2、4 項）。
  - TASK-032（MEDIUM）：Repository／Exam 訪客登入前價值說明 + Exam 選單手機溢出（第 3、7 項）。以 `RequireAuth` 可選 fallback 擴充，不衝突，Open Question 結案。
- 分類：第 5、6 項（JobSearch 記憶、DataAnalysis／Connectors 引導，LOW）→ **Future Work**，待上述完成後再排；第 6 項動到 DataAnalysisLibrary 前須先問 Boss。#407 單字頁內部連結（PM 建議 MEDIUM）→ 需先設計（相關單字資料來源、SSR meta 注入已有 TASK-020），下一輪由 Tech Lead 建 TASK-033。
- Boss 無需決策。

## 2026-10-06 — Tech Lead：同步任務狀態、建立 TASK-033（排程執行）

- 無 open PR。TASK-030（#418）、TASK-031（#420）已合併 → 狀態更新為 `MERGED`。TASK-032 仍 READY（尚無人開工）。
- 新建 TASK-033（Frontend, READY, MEDIUM）：單字頁 synonyms／antonyms／relatedWords 改為真實內部連結（#407）。純前端、無後端／成本變更，Boss 無需決策。
- Future Work：JobSearch 記憶、DataAnalysis 引導（LOW）；首頁／每日單字到單字頁連結。

## 2026-10-07 — Tech Lead：審查 PR #422、同步 TASK-032（排程執行）

- TASK-032（#421）已合併 → `MERGED`。
- PR #422（TASK-033 單字頁內部連結）：審查通過、CI 全綠、範圍無擴張 → `READY FOR BOSS REVIEW`（已於 PR 留言）。等待 Boss 合併。
- 無風險／阻擋。Future Work 不變（JobSearch 記憶、DataAnalysis 引導、首頁／每日單字連結）。

## 2026-10-07 — Tech Lead：同步 TASK-033（排程執行）

- 無 open PR。PR #422（TASK-033 單字頁內部連結）已合併 → `MERGED`。
- 無 READY 的 task、無阻擋、Boss 無需決策。Future Work 不變（JobSearch 記憶、DataAnalysis 引導、首頁／每日單字連結）。

## 2026-10-08 — PM：同步正式網域（Boss 指示）

- 正式網址為 https://apps.jacky-studio.com（舊 azurewebsites 網域停用）。已於 REQUIREMENTS 頂部加註、記錄 DECISION-011。舊網域的歷史紀錄不改寫。
- Tech Lead／QA：驗證與測試請以新網域為準。

## 2026-10-09 — Tech Lead：#407 新需求評估、建立 TASK-034（排程執行）

- 無 open PR。#407 Boss 要求無品牌詞也能搜到 → 誠實回覆「排名不可保證」；建 TASK-034（JSON-LD + lastmod, READY）。title 格式變更待 Boss 決定（見 TECH_LEAD.md）。

## 2026-10-09 — Tech Lead：TASK-034 已合併、建立 TASK-035（排程執行）

- 無 open PR。TASK-034（#427 JSON-LD + lastmod）已合併 → `MERGED`。
- Boss 於 #407 核准 title 改格式 → 建 TASK-035（Backend, READY, MEDIUM）：`{單字} 英文意思、發音、例句 | English Coach`，僅改 `WordSeoTextFormatter.FormatTitle` 單一來源 + 更新測試斷言。
- Boss 暫不做外部分享（#407 項目 3）。#407 保持 open，待 TASK-035 完成後回報。

## 2026-10-09 — PM：記錄 #407 Boss 決定（排程執行）

- 記錄 DECISION-012：單字頁 title 改新格式（TASK-035 已由 Tech Lead 建立、PR #430 待 Boss 審查合併）；外部分享暫不做。
- Boss 待辦：審查並合併 PR #430。#407 保持 open，待 TASK-035 上線後由 Tech Lead 回報。無新增阻擋。

## 2026-10-10 — Tech Lead：TASK-035 已合併（排程執行）

- 無 open PR。TASK-035（#430 單字頁 title 新格式）已合併 → `MERGED`。已於 #407 回報部署後驗證方式。
- 無 READY 的 task、無阻擋、Boss 無需決策。

## 2026-10-10 — PM：Issue #431「回落煞車」需求入檔（排程執行）

- Boss 新開 #431（0050 + 00631L 回落煞車加減碼提醒，含完整規格）。已寫入 `REQUIREMENTS.md`（DRAFT v1）並記錄 DECISION-013（固定參數與禁止項）。
- PM 提議分兩期：第一階段收盤後訊號＋配置＋股數（HIGH）；第二階段盤中預估＋站外提醒（MEDIUM，視 Q2）。待 Boss 確認。
- 未決（已於 #431 詢問 Boss）：Q1 對一般使用者開放範圍（合規未查證，PM 傾向先限 Boss 自用）；Q2 提醒送達方式；Q3 策略現金假設；Q4 回測數字來源。
- Tech Lead：可先設計核心計算與資料取得；Q1 未決前功能預設對一般使用者關閉。另請評估 Portfolio 快照與股票分割的關係（0050 2025-06-18 1 拆 4、00631L 2026-03-31 1 拆 22，Boss 提供、PM 未驗證）。

## 2026-10-10 — Tech Lead：#431「回落煞車」技術設計與任務拆解（排程執行）

- 無 open PR。讀取 PM 剛入檔的 `REQUIREMENTS.md`（DRAFT v1）與 DECISION-013，依其 Tech Lead Actions 完成設計：`ARCHITECTURE.md` 新增「回落煞車」一節（資料、狀態機、API 契約、功能開關、分割防護、第二階段 MIS）。
- 架構：**後端提供全站共用市場訊號，前端依持股算個人建議**；新表 `TaiexTotalReturnDaily` + 背景刷新（回補 3 年）；無 AI、無 UsageLimit、無使用者設定表（設定存 localStorage，同 `rebalancingTarget.ts`）。
- 建立：TASK-036（Backend，READY，HIGH）訊號＋端點＋功能開關；TASK-037（Frontend，READY，HIGH）卡片＋配置／建議純函式＋分割防護；TASK-038（Backend，BLOCKED，MEDIUM）第二階段盤中預估，待 036 合併。
- **Q1 回應（技術面）**：`Features:DrawdownBrake:Audience`＝`Admin`（預設）｜`All`，端點 403 + user-info `drawdownBrakeEnabled`；Boss 決定 Q1 後只改 App Setting，不需改碼重新部署。合規結論屬 Boss／PM，Tech Lead 不下結論。
- **R2（股票分割）評估結果**：Portfolio 全域**沒有任何分割處理**（`Quantity` 為使用者輸入、市值＝數量×最新價）→ PM 的風險屬實。本功能以前端硬性防護處理（快照早於分割日 → 不顯示股數，提示更新持股）；Portfolio 全域問題列為 Technical Debt，不在本功能範圍。
- 驗證限制：排程沙箱 egress 擋 `twse.com.tw`／`mis.twse.com.tw`（403），**MFI94U 與 MIS 實際格式尚未驗證**；以 LiveContract（GitHub Actions）驗證，解析器不得猜欄位。
- 需要 Boss：回測用報酬指數 CSV（TASK-036 的測試 fixture，**不阻擋開工**）；Q1／Q2／Q3 仍由 Boss 回覆 PM（見 #431）。
- PM Actions：請在 REQUIREMENTS 的「Tech Lead Actions」標示已完成設計，並於 Boss 回覆 Q1～Q3 後更新假設／Open Questions。

## 2026-10-10 — Frontend Engineer：TASK-037 回落煞車前端 PR 已開（排程執行）

- 無既有 Open PR；TASK-037（READY、無未完成相依）→ `IN_PROGRESS` → `PR_OPENED`：PR #432。
- 範圍：純函式（composition／advise／設定）、持股擷取與分割硬性防護、固定警語＋§10 文案＋風險說明、`DrawdownBrakeCard`（僅 `drawdownBrakeEnabled` 時掛載並發請求）、`getDrawdownBrake` query 與型別。
- 驗證：lint／test（396 項，新增 96）／build 全過；360px／400px 無橫向捲動。**尚未對真實端點驗證**（等 TASK-036 合併）。
- `SPLIT_EVENTS` 以公開報導交叉確認與 Boss 常數相符，未修改。
- 給 Tech Lead：PR 描述列出與參考實作／§10 範例的差異（截斷後 E、prevState null、A4 取價用既有 lookup 端點、再平衡句型）。
- 給 PM／Boss（不阻擋）：「隔日成交版本」欄的期間 1997–2026 為依 #431 §2 欄位順序推定，請確認；沒有任何持股者看不到卡片（卡片在「有持股」才渲染的區塊內）。

## 2026-10-11 — PM：#431「回落煞車」進度同步與產品面預審（排程執行）

- Boss 尚未回覆 #431 的 Q1～Q4。後端 PR #433（TASK-036）、前端 PR #432（TASK-037）已開、尚未合併、Tech Lead 尚未留審查意見。
- `REQUIREMENTS.md`：標示 Tech Lead 設計／功能開關／分割評估／契約測試已完成；補假設 A4 範圍（完全沒持股的使用者看不到卡片，PM 接受為第一階段已知限制）、A5（隔日成交版本期間推定 1997–2026，未驗證）、A6（再平衡文案不沿用範例的「漲多」措辭，PM 認可）；新增 Q5 與 CSV 請求；R2 記錄 Tech Lead 事實結論。
- PM 產品面預審：警語、模擬值註記、風險三點、隔日成交數字皆與 #431／DECISION-013 一致；未發現與驗收條件衝突。這是預審，非最終驗收。
- 無新增 Boss 決定，未新增 DECISION。
- Tech Lead：請審查 PR #433／#432；TASK-038 待 036 合併。Portfolio 全域股票分割處理列 Future Consideration，PM 暫不建議排入。
- Boss 待辦：回覆 #431 Q1／Q2／Q3（Q4、Q5 非阻擋）、提供回測 CSV；審查並合併 PR #433／#432（合併即部署）。

## 2026-10-11 — PM：吸收 Boss 對 #431 的回覆（Q1～Q4）（排程執行）

- Boss 於 #431 回覆（02:06 UTC）：同意分期；Q1 先僅 admin、之後慢慢開放，開放前要靠可觀測性紀錄判斷計算量；Q2 站內顯示即可、不要 email；Q3 假設成立；Q4 提供回測來源並**更正多個回測數字**（原表誤用 k=0）。已記錄 DECISION-014，`REQUIREMENTS.md` 更新為 v2。
- 新增需求：8（資料時間＋手動「重新整理資料」繞 cache，第二階段必備）、9（可觀測性，開放全部使用者前必備，不阻擋 PR #433／#432 合併）。站外提醒移出範圍。需求 10（歷史相似期間比較圖）僅為待定提案（Q7），**不排工**。
- **產品面不一致（PM 發現）**：PR #432 `drawdownBrakeCopy.ts` 仍是更正前的數字（隔日成交 保守 11.9／平衡 15.4／積極 18.1 → 應為 12.1／15.3／18.4；2011 年平衡 −29% → −28%；模擬值註記應涵蓋 0050 與 00631L 及 1997–2002 股利估計值）。目前僅 admin 可見故不阻擋合併，須在 `Audience=All` 前改好。細節見 REQUIREMENTS「Tech Lead Actions」。
- 仍待 Boss（#431）：Q5（隔日成交欄期間）、Q6（其他持股是否納入判斷，與 DECISION-013 適用範圍有衝突，只有 Boss 能決定）、Q7；CSV／`reproduce_spec.txt`／資料快照。合規 R1 未解決（Boss 選 admin-only 的理由是計算量）。
- Tech Lead：請依 REQUIREMENTS「Tech Lead Actions」處理文案更新、cache 盤點與重新整理設計、可觀測性指標提議；TASK-038 仍待 TASK-036 合併後再轉 READY。
- Boss 待辦：審查並合併 PR #433 → #432（#432 與 master 有僅文件衝突，保留兩段）。

## 2026-10-11 — PM：吸收 Boss 對 #431 的第二輪回覆（Q5～Q7）、回測資料入庫（排程執行）

- Boss 於 #431（02:33 UTC）回覆 Q5／Q6／Q7，並把回測資料 commit 進 repo `回測數據/tw_backtest/`（391c95b）。已記錄 **DECISION-015**，`REQUIREMENTS.md` 更新為 v3。
- **Q5 結案**：隔日成交欄期間＝1997–2026（`reproduce_spec.txt` 標頭證實與主欄同一條 1997-07～2026-10-08 序列）。**Q6 結案**：先做 A＋B，新增需求 11「策略部位占總資產 X%」（純資訊，A9 分母＝流動總資產，與 PR #434 一致）；其他持股不納入判斷，DECISION-013 適用範圍不變。
- **回測資料**：PM 核對 `results/reproduce_spec.txt` 輸出與 Boss 更正表逐項一致；**未重跑程式**（排程環境無 pandas），因此不算獨立驗證（R3 不變）。`taiex_tri.csv` 欄名為 `date,tri`，TASK-036 預期 fixture 為 `date,close`，交 Tech Lead 處理。
- **Q7 方向已定、細節未定**：自動找最相似區段、可調整過去區段（現在區段只能調長度）、找不到顯示「找不到過去的相似區段」、畫收盤價、不限 admin。PM 假設見 A10～A13。新增 **Q8**（標的、位置／對象、合規取捨）與風險 **R7**（相似區段圖易被讀成預測，合規未查證）。**Q8 回覆前不排工**。
- PR #434（Boss 直接修再平衡建議：移除不動產、分母改流動總資產）已合併；PM 在 Portfolio 段落補註記，判斷與回落煞車不衝突。
- Tech Lead：請依 `REQUIREMENTS.md`「Tech Lead Actions」處理——啟用 2026-10-08 OFFENSE 驗收測試並獨立比對 `reproduce_spec.txt`、需求 11 的評估、PR #432 文案更新（仍待辦）、需求 8／9 的提議；需求 10 等 Q8-1 回覆後再提議相似度演算法與門檻（附歷史實例）。
- Boss 待辦：回覆 Q8；審查並合併 PR #433 → #432（仍未合併）。合規（R1）仍未查證。

## 2026-10-11 — PM：Boss 補充留言——相似區段圖改放 /finance、新增「過去 5 年同期間」圖（排程後續）

- Boss 在 PM 回覆前 20 秒（02:38 UTC）留言，PM 上一輪未涵蓋：(1) 兩條線的相似區段圖改放 `/finance` Stock Intelligence（與個人資產無關）；(2) 新增「過去 5 年相同日期區段」走勢圖（每年一色、有圖例，範例為加權指數 2022～2026 的 1/1～9/30）。已記錄 **DECISION-016**。
- `REQUIREMENTS.md`：原回落煞車需求 10 移出，與新圖合併為獨立需求「Stock Intelligence 大盤走勢比較圖」（A 同期間圖、B 相似區段圖；ID A10～A13、R7、Q8 沿用，新增 A14～A17、R8、Q9）。回落煞車本身（TASK-036／037／038、需求 11）不受影響。
- **PM 讀程式確認的事實**：`/finance` 路由沒有 `RequireAuth`（未登入訪客可進入）；系統已用 Yahoo `^TWII` 提供「大盤總覽」當日報價並有 LiveContract 測試，但沒有多年日線歷史；Finance 既有的量化／AI 趨勢分析也輸出「投資建議」類內容。→ 合規 R1 的範圍可能比回落煞車更廣，PM 未查證、不下結論。
- 標的建議由報酬指數改為**加權指數（價格指數，不含息）**（Q8-1）：系統已有來源、歷史可到 1997、與 Boss 範例一致。對象與合規取捨（Q8-3）仍待 Boss；因合併即部署且由 Boss 自己合併，PM 不再設「回覆前不排工」，改為 Tech Lead 可先設計 A 圖與提議 B 圖演算法。
- Tech Lead：請依文末獨立需求的 Tech Lead Actions 處理（加權指數歷史資料取得與快取、版面位置、相似度演算法與門檻附歷史實例）；不要動 TASK-036 的報酬指數資料。
- Boss 待辦：回覆 Q8-1／Q8-3（Q9 有預設，可不答）；審查並合併 PR #433 → #432（仍未合併）。

## 2026-10-11 — PM：依 Boss 指示減少提問（排程後續）

- Boss 反映「不要什麼小事都問我」。PM 檢討：標的是否含息、預設期間、警語字句、對象等屬於「小的模糊」，依 PM 指示本應自行假設並記錄，卻丟給 Boss。已改為 PM 決定（`REQUIREMENTS.md`「Stock Intelligence 大盤走勢比較圖」Open Questions；DECISION-016 補註），Boss 可隨時推翻。
- 之後只在「法律／合規、金錢、改變範圍或不可逆的對外曝險」時才向 Boss 提問，一次一題並附建議。目前**沒有需要 Boss 回覆的問題**；Boss 待辦僅剩審查並合併 PR #433 → #432。
- Tech Lead：兩張圖可直接依文末 Tech Lead Actions 進行設計；不需等 Boss 回覆。

- 同日補充：Boss 確認兩張圖的標的＝加權指數（價格指數，不含息）。其餘細節仍為 PM 決定。Tech Lead 無需等待，可依 Tech Lead Actions 進行。

## 2026-10-11 — Tech Lead：PR #433 已合併；驗收資料、需求 8／9、大盤走勢比較圖的設計與任務（排程執行）

- **狀態**：PR #433（TASK-036）已 MERGED，TASK-036 → MERGED。PR #432（TASK-037）仍 open、仍待 Boss 合併；與 master **只有** `.agent/status/PROJECT_STATUS.md` 一個文件衝突（Tech Lead 以 `git merge-tree` 驗證，程式碼與 PR #434 無重疊）。解法寫入 TASK-042。
- **Boss 回測資料驗證**：repo 內 6 個資料檔的 SHA256 都與 Boss README 一致（其中 5 個需先還原 CRLF；`.gitattributes` 的 LF 正規化造成表面差異）。以獨立純 Python 依 DECISION-013 重放 `taiex_tri.csv`（5850 日）：**2026-10-08＝OFFENSE、距 252 日高 −1.02%**（與 Boss 預期一致），全段 132 次切換、DEFENSE 1344 日；只取最後 3 年得到相同 state／High252／Sma60。→ TASK-039 把它變成 fixture，結束「Skipped」。
- **任務**：TASK-039（Backend，READY，HIGH）啟用驗收測試；TASK-041（Backend，READY，MEDIUM）手動重新整理端點＋資料時間＋訊號 60 秒快取；TASK-040（Backend，BLOCKED 待 041，MEDIUM）可觀測性；TASK-038（Backend，BLOCKED 待 041，MEDIUM）加入「接上重新整理」；TASK-042（Frontend，READY，HIGH）PR #432 後續（Boss 更正的回測數字、模擬值註記、需求 11 策略部位占比、解衝突）；TASK-043（Frontend，BLOCKED 待 041，MEDIUM）資料時間＋重新整理按鈕；TASK-044（Backend，READY，MEDIUM）加權指數歷史日線端點；TASK-045（Frontend，READY，MEDIUM）Stock Intelligence「過去 5 年同期間」圖。
- **設計**（`ARCHITECTURE.md` 兩個新章節）：(1) 回落煞車的 cache 盤點、全站單一冷卻的重新整理、可觀測性（App Insights 自動收集 requests／dependencies＋自訂 Meter＋每日假名 UserKey）與建議開放門檻——**結論：計算量不是風險點**（個人建議在瀏覽器算，伺服器對證交所的請求與使用者數無關，唯一共同瓶頸是共用 SQL 5 DTU）；(2) 大盤走勢比較圖 A 的資料／快取／位置設計（不加功能開關），以及 B（相似區段）的演算法提議與真實歷史實測。
- **B 圖實測重點（Boss 看過再決定是否排工）**：同樣的門檻下「今天（2026-10-08）找不到相似區段」；沒有門檻時永遠找得到，門檻 corr ≥ 0.9 且 RMSE ≤ 3pp 時約 31% 的觀察日找得到；相似區段「之後」的走勢與現在實際後續走勢的相關係數 ≈ +0.04（樣本重疊、樣本內，僅供量級）。細節與 9 個歷史範例在 `ARCHITECTURE.md`。
- **驗證限制**：排程沙箱連不到證交所與 Yahoo（403），也沒有 .NET；MFI94U／MIS／Yahoo 日線格式與 C# 測試結果都由 GitHub Actions 驗證。KQL 查詢為初稿、未實測。
- **未動**：`REQUIREMENTS.md`（PM 所有）、`DECISIONS.md`、TASK-037（PR #432 也改它，避免新衝突）。PM 的 Tech Lead Actions 可據此勾選：文案更新→TASK-042、cache 盤點／重新整理→ARCHITECTURE＋TASK-041／043、可觀測性→ARCHITECTURE＋TASK-040、驗收資料→TASK-039、需求 11→TASK-042、A 圖設計→ARCHITECTURE＋TASK-044／045、B 圖演算法提議→ARCHITECTURE。
- Boss 待辦：合併 PR #432（解衝突時兩段都留）；#433 已部署後以 admin 確認 `GET /api/portfolio/drawdown-brake`（`OFFENSE`、約 −1%）。

## 2026-10-11 — PM：Boss 正式環境試用回饋——回落煞車改以整個流動資產判斷（排程執行）

- **事件**：PR #432／#433／#435／#436 皆已合併並部署；Boss 在正式環境試用後於 #431 留言（04:44 UTC）：卡片只算 0050＋00631L＋現金，和右上角「流動資產水位」對不上，要把**目前流動資產全部納入**；其他一倍商品合併成「其他一倍商品」、美股 1.5 倍（NTSD）單獨一列，最後顯示各桶加減碼多少或不動；並問 NTSD 有沒有好的建議。
- **已記錄**：**DECISION-017**（推翻 DECISION-015 的 Q6 A＋B，以及 DECISION-014 Q3 的「策略總資產」定義）；`REQUIREMENTS.md` 回落煞車需求 v4——新增需求 12（12a 曝險與水位一致、12b 分桶顯示、12c 核心／衛星規則）、假設 A18～A23、風險 R8、Q10；需求 1 適用範圍、需求 11、A1 標示為被取代。訊號、參數、風險等級、觸發規則不變。
- **PM 讀程式確認的事實（未執行）**：頁面「水位」＝曝險 ÷ 流動總資產；倍數規則在 `PortfolioExposure`（NTSD＝1.5、…L／正2＝2、債券／貨幣型＝0 視同現金、其餘 1）；現行卡片的曝險是另算（只含兩檔，分母含手動「策略現金」），所以必然對不上。卡片文案的回測數字已是 Boss 更正值（TASK-042 完成）。
- **PM 決定（小事，Boss 可推翻）**：分母＝流動總資產（與水位同口徑）；現金類＝全部現金＋倍數 0 的債券／貨幣型，退役「策略現金」輸入；歸類沿用 `exposureFactor`；NTSD 不特別處理（策略只規範整體曝險）；進攻時其他一倍商品／NTSD（衛星）預設不動；回測績效區塊須標明「依 0050＋00631L＋現金回測」並提醒衛星部位未經回測。
- **只問 Boss 一題（Q10）**：防守時衛星要不要也建議減碼到 0（PM 建議 A：要；回覆前系統採保守預設＝衛星「不動」＋標示整體曝險仍高於目標）。
- **Tech Lead Actions（見 `REQUIREMENTS.md` 回落煞車 Tech Lead Actions）**：12a／12b 為 HIGH、不依賴 Q10，可立即開工並重用既有曝險計算避免兩套算法；評估對已合併 TASK-037／042（策略現金輸入、占比、分割防護）的調整；12c 的核心／衛星目標算法請附 2～3 個真實情境給 Boss；把「防守時衛星如何處理」做成單一容易切換的規則；TASK-038／041／043／040／044／045 不受影響。
- **Boss 待辦**：回覆 Q10；合規（R1）仍未查證，需求 12 使範圍更廣（R8）。

## 2026-10-11 — PM：Boss 追加兩則指示——NTSD 排除、暫不處理 R1 合規（對話）

- **NTSD 排除（DECISION-018）**：Boss 認為 NTSD 屬全球配置、與集中台灣的配置不同 → 不納入回落煞車的策略範圍（不計入曝險分子分母、不給建議，只在對帳行顯示）。取代 DECISION-017 中「NTSD 單獨一列」的部分。需求 12a 改為「無被排除持股時與水位一致；有則以對帳行說明差異」；新增 A24。
- **Q10 改寫（唯一請 Boss 回覆的題目）**：同一理由是否套到其他美股與國外基金？PM 建議 A：策略範圍只限台股＋現金。回覆前保守預設：NTSD 排除，其他衛星「不動」並標示整體曝險高於目標。
- **R1 合規暫不處理（DECISION-019）**：Boss「我不在意，我只是小 web app，基本上還沒什麼 user」。PM 記為「已知悉、接受風險、暫不查證」，不再提醒、不再列為前置條件；PM 假設 R7 比照；固定警語保留。開放全部使用者前只剩需求 9 觀察。
- **Tech Lead**：12a／12b 照 `REQUIREMENTS.md` 最新版（NTSD 排除、對帳行、不為 1.5 倍商品設專屬一列）；不需等 Q10；把「策略範圍判定」與「防守時衛星處理」做成單一容易改的規則。
- **Boss 待辦**：回覆 Q10。合規不再是待辦。

## 2026-10-11 — PM：Boss 回覆 Q10「B＋C」——所有持股計入曝險，只對 0050／00631L／現金給建議（對話）

- **DECISION-020**：個股、基金、其他倍數商品都計入整體曝險（NTSD 仍依 DECISION-018 排除，PM 的理解）；建議只針對 0050、00631L、現金；其他持股一律「不動」（進攻、防守、再平衡都一樣）。Q10 結案，**目前沒有待 Boss 回覆的問題**。
- 核心的目標＝讓整體曝險盡量接近目標 E；其他持股單獨超過目標時，核心降到最低（現金）並如實標示「整體曝險高於目標，差額來自您自行決定的其他持股」；無可執行動作時顯示「不動」。
- `REQUIREMENTS.md` 需求 12 更新為 v4.2：12b／12c、驗收條件、A22／A24／A26、R8（「建議賣出沒回測過的部位」的風險因此消除）、Tech Lead Actions 已同步。回測績效仍只適用 0050＋00631L＋現金，畫面須標明。
- **Tech Lead**：12a／12b／12c 全部可開工；12c 的核心算法請提議並附 2～3 個真實持股情境給 Boss 看（含其他持股單獨超過目標、核心降到最低、無可執行動作的情況）。

## 2026-10-11 — Tech Lead：回落煞車需求 12（整個流動資產）的技術設計與 TASK-046；TASK-042 結案（排程執行，第 2 次）

- **狀態**：無 open PR。#432／#433／#435／#436 皆已由 Boss 合併；TASK-042 → MERGED（我事後對照 `reproduce_spec.txt`：12.1/−27、15.3/−37、18.4/−45、2011 年 −28%／0050 −18%、1997–2026 逐項一致，其需求 11 占比隨需求 12 移除）。TASK-041／044／045 為 IN_PROGRESS、尚無 PR；TASK-038／040／043 維持 BLOCKED（待 041）。
- **設計**（`ARCHITECTURE.md` 新章節）：需求 12 純前端，不動後端與契約。曝險只用 `PositionDto.exposureFactor`（水位同一套倍數）；範圍判定集中於 `STRATEGY_EXCLUDED`（NTSD）；只有核心（0050／00631L／現金類）有 `orders`，其他持股結構性不動（DECISION-020）。核心算法：`A`＝策略範圍資產、`C`＝核心資金、`c_min=min(C,k·A)`、`e*=E_t·A−X_o`、`clamp(e*,0,2I)`、composition 分段、以「可達 E」判斷偏離。性質：其他持股為 0 時與現行 `advise`／`composition` 完全相同（Python 參考實作 3 等級×2 狀態全過）；冪等；各列合計＝A。
- **情境（golden；示意金額，不是 Boss 的持股）**：S1 進攻可達（賣 0050 58,333 股、買 00631L 116,667 股，執行後不動，含 NTSD 對帳行）；S2 切換防守且其他持股單獨超過目標（核心全轉現金，標示整體曝險 0.40 高於目標 0）；S3 進攻但其他持股太多（目標 1.40 只能到 0.90，不動＋標示）。已貼 #431。
- **任務**：TASK-046（Frontend，READY，HIGH）：12a 對帳、12b 分桶表、12c 算法與標示、R8 績效揭露、移除策略現金與占比、價格失敗防護。TASK-043 依賴改為「046 之後」。
- **給 PM 的 Tech Lead Actions 勾選**：需求 12a／12b／12c → ARCHITECTURE＋TASK-046；R8 績效揭露 → TASK-046 需求 6；需求 11 占比 → 移除（TASK-046 需求 7）；12c 情境 → #431；重新整理按鈕涵蓋持股報價 → 不變（TASK-043 仍沿用 `refresh-price`，且改在 046 之後）。
- **三個我自己做的選擇（PM 可推翻，各改一處）**：k 的基準＝策略範圍資產 A；現金類含債券／貨幣型 ETF（A19）；範圍內任一持股價格失敗即暫停建議。文案（`aboveTarget`／`capped`／績效揭露）為提議字句，PM 可改。
- **未動**：`REQUIREMENTS.md`、`DECISIONS.md`（PM 所有）、TASK-041／044／045 的內容、任何程式碼。
- **驗證限制**：只改文件；演算法以獨立 Python 參考實作驗證，TypeScript 實作與測試以 GitHub Actions 為準。
- Boss 待辦：無必答事項；合併後續 PR（041／044／045／046）時審查即可。

## 2026-10-11 — Backend Engineer：TASK-041 回落煞車手動重新整理 PR 已開（排程執行）

- **選擇**：無 open PR；Backend 任務中 TASK-041／044 皆為 IN_PROGRESS 且沒有分支或 PR，TASK-038／040 BLOCKED（待 041）。本 session 只能推一個分支，故先做擋住 038／040／043 的 TASK-041；TASK-044（無相依）留待下一輪或另一個 session。
- **PR**：#438（`POST /api/portfolio/drawdown-brake/refresh`、全站 60 秒冷卻＋單飛、`lastCheckedAt`／`generatedAt`、訊號 60 秒快取）。狀態 `PR_OPENED`，待 Tech Lead 審查；設計取捨（版本號失效、背景輪詢不被冷卻擋住、週末短路略寬於字面、跨月失敗映射、非上游例外不吞）已列在 PR 描述。
- **驗證**：本環境以 apt 裝好 .NET 10 SDK（`builds.dotnet.microsoft.com` 仍被擋，但 Ubuntu 套件庫有 `dotnet-sdk-10.0`），`dotnet test --filter "Category!=LiveContract"` 965/965 通過；前端 `tsc --noEmit` 通過（只加兩個 optional 型別欄位）。PR CI 結果待確認。
- **對其他任務**：TASK-043（前端）可依 `ARCHITECTURE.md` 契約整合；TASK-038／040 待 #438 合併後解除 BLOCKED。

## 2026-10-11 — Backend Engineer：TASK-040 回落煞車可觀測性 PR 已開（排程執行）

- **選擇**：無 open PR（#438／#440 皆已由 Boss 合併）。Backend 任務中 TASK-038／040 的檔案狀態仍寫 BLOCKED，但擋住它們的唯一前提（TASK-041）已隨 #438 合併而解除，我據此視為可開工。本 session 只能推一個分支，且 038 與 040 改同一個服務與 controller，故只做一個：**先做 040**（`Audience=All` 之前必備、可完全離線驗證）；**038 留到 #441 合併後**，避免兩個 PR 互相衝突，也因為 038 的 MIS 格式驗證本環境連不到（Tech Lead 實測 403），要靠 Actions 的 LiveContract。
- **PR**：#441（`DrawdownBrakeTelemetry`：`drawdownbrake.views{outcome,cache}`／`refresh{result}`／`compute.duration`；每次 GET 一行 log 含每日每行程假名 `UserKey`；`Program.cs` 以 `WithMetrics` 註冊）。狀態 `PR_OPENED`，待 Tech Lead 審查；設計取捨（`cache` 定義、`forbidden` 無 cache 標籤與 KQL 排除、記錄位置）已列在 PR 描述。
- **驗證**：`dotnet test --filter "Category!=LiveContract"` 1080/1080 通過；相關 271 個連跑 10 次穩定；變異測試每項皆有測試失敗。**未驗證**：App Insights 實際資料與 KQL（未實測）、Histogram 在 customMetrics 是否有百分位。
- **對其他任務**：TASK-038 待 #441 合併後可開工（請 Tech Lead 將其轉 READY，或確認我可據「041 已合併」自行接手）；TASK-043（前端）不受影響。
- **ARCHITECTURE.md**：僅動「可觀測性」小節（任務書第 4 點授權）：補實作語意、唯一人數 KQL 排除 `forbidden`、註明 KQL 仍未實測。

## 2026-10-11 — PM：Boss 看過正式環境後的兩點修正——NTSD 改計入曝險、00685L 併入正2 桶（排程執行）

- **來源**：Issue #431，Boss 12:09 UTC 留言（TASK-046／PR #437 部署後的試用）：(1) 00685L 與 00631L 同性質，兩檔歸一類、合併計算加碼／減碼／不動；(2) 卡片寫「不含 NTSD NT$75,771」很怪，Boss 要的水位是含 NTSD 的。
- **記錄**：新增 **DECISION-021**，**取代 DECISION-018**（NTSD 排除）；`REQUIREMENTS.md` 需求 12 更新為 **v4.3**——NTSD 照 1.5 倍計入曝險、卡片 E 與右上角水位恆一致、移除對帳行；NTSD 仍不給建議（歸「其他倍數商品」、一律「不動」，DECISION-020 不變）；00631L＋00685L 合為一列「正2 槓桿 ETF」，只給整組一個結論與金額。新增驗收條件、A27～A29（PM 小假設，Boss 可推翻），A24 作廢，R8／Priority／Tech Lead Actions 同步（優先序 HIGH，範圍小）。
- **PM 的理解與限制**：「水位含 NTSD」＝NTSD 計入卡片曝險；若 Boss 其實要 NTSD 單獨一列並給建議會牴觸 DECISION-020（PM 判斷不需要）。PM **看不到 Boss 附的截圖**（GitHub 附件對排程沙箱回 403），依留言文字判斷。00685L 倍數為 2 是 PM 讀程式的理解（`PortfolioExposure` 台股代碼尾碼 L），未執行驗證。
- **Boss**：沒有待回覆的事（小事 PM 自行假設並記錄）。
- **Tech Lead**：請拆任務調整已合併的 TASK-046（清空排除表、正2 桶歸類、股數呈現 A28、績效揭露加註「00685L 視同 00631L」、測試與文案），並補一組「持有 NTSD、持有 00685L」的前後對照範例給 Boss；TASK-043 排在其後的順序不變。

## 2026-10-11 — Tech Lead：Boss 對需求 12 的兩點修正（NTSD 計入、00685L 併入正 2）的技術設計與 TASK-047；#437～#442 事後審閱；TASK-038 轉 READY（排程執行，第 3 次）

- **狀態**：無 open PR。#437（TASK-046）、#438（041）、#439（045）、#440（044）、#441（040）、#442（043）皆已由 Boss 合併，**Build and deploy run 526～531 全數成功**（正式環境＝master head）。這六個任務 → MERGED，Review 區補「事後審閱」（見各任務；PR 在審閱前已被合併，故為事後）。其中 TASK-044 我讀了端點與快取（匿名但無參數、單一快取＋單飛＋退避，公開端點成本與安全無疑慮）；TASK-046 我對照 `advise()` 與設計公式逐項一致；其餘依任務書 Result、CI 與部署核對，**未逐行讀 diff**。
- **Boss 的新需求（#431，12:09 UTC）**：00685L 與 00631L 歸一類；水位要含 NTSD。PM 已於 d3b429c 記錄為 **DECISION-021**（取代 018）與需求 12 **v4.3**（A27～A29）。**我的技術設計與 PM 的假設一致**，寫入 `ARCHITECTURE.md`「回落煞車 — 台股正 2 類別（00631L＋00685L）與 NTSD 納入範圍」：純前端；`STRATEGY_EXCLUDED` 清空（對帳行機制保留，因為 PM 要「容易切換」）；新增單一成員表 `CORE_LEVERAGED_2X`（不做「TW 且倍數 2」通則，避免對沒點名的標的靜默給建議）；`advise` 數學不變，`00631L` 角色由 `lev2` 類別合計取代；`splitLev2Order` 是「買／賣哪一檔」的唯一決定處（減碼依現值比例＋持股上限、加碼依持有現值比例、都沒持有買 00631L＝PM 的 A28）。
- **PM 要的「持有 NTSD、持有 00685L」前後對照**：`ARCHITECTURE.md` 情境 S7（獨立 Python 重算，並驗證冪等）。前：卡片 E 1.077 對不上水位 1.133、建議賣 0050 11,667 股／買 00631L 46,667 股；後：E＝水位 1.133，賣 0050 16,667 股、買 00631L 30,000 股＋00685L 30,000 股，無對帳行。S4～S6（兩檔都持有、都沒持有、只持有 00685L）同表。已貼 #431。
- **任務**：**TASK-047**（Frontend，READY，HIGH）NTSD 計入＋正 2 類別＋文案＋（Optional）重新整理涵蓋價格失敗的持股；**TASK-048**（Backend，READY，LOW）PM A29 要求的測試鎖定——`PortfolioExposureTests` 加 `("群益臺灣加權正2","00685L","TW",2)` 與尾碼 fallback `("00685L",2)`，僅測試；**TASK-038**（Backend，MEDIUM）BLOCKED → READY（041、040 皆已合併）。
- **與 PM 的唯一差異（已對齊）**：我原本在績效註記寫「追蹤指數不同」，那是我無法驗證的陳述，已改採 PM 的用語「00685L 視同 00631L（您的判斷），未單獨回測」。
- **風險／待確認**：(1) **00685L 有沒有股票分割**——我無法驗證（沙箱無外網）；沒有 `SPLIT_EVENTS` 條目時，若 00685L 曾分割且 Boss 的快照早於分割日，該檔股數會錯。已請 Boss 在 #431 回答，非阻擋；Engineer 不得自行猜測。(2) NTSD 是美股（Yahoo 報價），價格失敗現在會暫停整張卡片的建議（與水位同一原因）；TASK-047 Optional 項讓「重新整理」重抓失敗的持股。(3) 其餘延續：KQL 未實測、`/finance` 的走勢圖待 Boss 目視確認有真實資料。
- **給 PM**：無需動作；若不同意 S7 的分配結果或文案，在 `REQUIREMENTS.md` 改字即可（文案常數集中於 `drawdownBrakeCopy.ts`）。
- **未動**：`REQUIREMENTS.md`、`DECISIONS.md`（PM 所有）、任何程式碼。
- **驗證限制**：只改文件；演算法以獨立 Python 重算，TypeScript／C# 測試結果以 GitHub Actions 為準；沙箱連不到正式環境與外部 API。

## 2026-10-11 — Tech Lead：Boss 確認 00685L 沒有分割（對話）

- Boss（對話）：00685L 應該沒有做過分割，因為上市沒多久。→ TASK-047 與 `ARCHITECTURE.md` 的 `SPLIT_EVENTS` 項改為「**不新增 00685L 條目**」（Boss 確認，Tech Lead 無法獨立驗證）；上一則的風險 (1) 因此解除。日後若分割，在 `SPLIT_EVENTS` 加一行。
- Boss 另提醒：00685L 與 00631L 同一類（2 倍）、NTSD 是另一類（1.5 倍）——與 DECISION-021、`ARCHITECTURE.md` 設計一致，無需變更。

## 2026-10-11 — Backend Engineer：TASK-038 盤中預估 PR 已開（排程執行）

- **選擇**：無 open PR（#438／#440／#441 皆已由 Boss 合併）。Backend 任務中 TASK-038 的檔案狀態仍寫 BLOCKED，但擋住它的前提（TASK-041／040）已合併，據上一輪記錄視為可開工；其餘 Backend 任務（036／039／040／041／044）皆已完成。
- **PR**：#443（MIS 指數 client＋解析器、全站共用 30 秒讀數〔重新整理 10 秒〕、預估公式、`estimate`／`estimateCorrected`、接上 `POST …/refresh`）。狀態 `PR_OPENED`，待 Tech Lead 審查；設計取捨（預估疊加在正式訊號之外、基準日檢查、更正持續時間）已列在 PR 描述。（原版自建 client；後續依 Boss 指示改經 TWStockLib，見下一則。）
- **驗證**：`dotnet test --filter "Category!=LiveContract"` 1208/1208 通過；變異測試 29 個全部被抓到；PR CI（run 650）通過。**MIS 格式已由 GitHub Actions 實測驗證**（本分支手動 dispatch `contract-tests.yml`，run 38140029229，57/57 通過）：指數項目有 `c`／`ch`／`d`／`t`／`tlong`／`y`／`z`（皆字串），**不需要 cookie**，解析器吃得下；實際欄位已記在 `TASK-038.md` Result。**仍未實測**：盤中 `z` 更新行為與平日休市日。
- **Tech Lead 待辦**：審查 #443；更新 `ARCHITECTURE.md`（契約區仍寫「`estimate` 第二階段前恆為 null」，第二階段章節仍標 BLOCKED）。

## 2026-10-11 — Backend Engineer：TASK-038 MIS 改經 TWStockLib（Boss 指示「不自己造輪子」），並合入 master（#444）（排程執行）

- **背景**：我的 session 開始時沒有 open PR，做到收尾時 #443（另一個 session，自建 MIS client）出現，故未重複開 PR，並在 #443 留言。Boss 回覆選擇 **(a)：以 #443 為基底，把自建 client 換成 TWStockLib**。
- **TWStockLib**：PR #12（通用 `bypassCache` 參數）Boss 已合併，NuGet **2.2.1** 可用（剛發佈時 restore 一度因 nuget.org 索引延遲看不到，之後正常）。
- **本分支**（`claude/exciting-cray-w4l3w6`）= #443 的全部提交 ＋「改經 TWStockLib」＋ merge master。因我只能推自己被指定的分支、不能改 #443 的分支，所以另開 PR；**合併本 PR 即等於合併 #443（其提交是本分支的祖先），請勿兩個都合併；合併後 #443 會自動顯示為已合併或可直接關閉。**
- **程式變更（相對 #443）**：`MisTaiexClient` 改呼叫 `GetRealtimeQuoteAsync("t00", MarketType.TSE, bypassCache: true)`，刪除 `MisTaiexParser`；`IMisTaiexClient`／`MisIndexQuote`／`MisTaiexQuoteSource`／估算與更正邏輯不動。`TradeDate`／`QuotedAt` 由 lib 的 `SyncTime`（`tlong`）推得。
- **衝突**：#443 的分支是從更舊的 master（`24d4c08`）切出，之後 master 多了 PM／Tech Lead 的文件提交與 #444（TASK-047，前端）。實際衝突只有兩個文件檔（`PROJECT_STATUS.md`、`TASK-038.md`），程式碼無衝突；已兩邊都保留。
- **驗證**：`dotnet test --filter "Category!=LiveContract"` 1179 通過。用 #443 在 Actions 記錄的真實欄位集餵 lib 的 parser，`LastPrice`／`YesterdayClosingPrice`／`SyncTime`（台北 13:31）正確，`z` 為 `-` 時 `LastPrice` 為 null。**尚未驗證**：lib 的 HTTP 路徑（裸 GET、無自訂 User-Agent）在 Actions 上實際讀 `t00`——LiveContract 已改為走這條路，請看下一次 `contract-tests.yml`。
- **TASK-048**（Backend，LOW，僅測試）本輪未做（單一 session 只能推一個分支）。


## 2026-10-11 — Backend Engineer：TASK-048 測試鎖定 00685L 曝險倍數 PR 已開（排程執行）

- **選擇**：無 open PR；Backend 任務只剩 TASK-048（READY，無相依）。TASK-038 檔案狀態仍為 `PR_OPENED`，但其現行版 #445 已併入 master，後續狀態由 Tech Lead 處理。
- **PR**：#446（僅 `PortfolioExposureTests.cs` 加兩列：`("群益臺灣加權正2","00685L","TW",2)`、`("00685L",2)`）。狀態 `PR_OPENED`，待 Tech Lead 審查。
- **驗證**：`dotnet test --filter "Category!=LiveContract"` 1181/1181 通過。**變異驗證有一半無法成立**：只改壞 `正2` 規則時所有測試仍通過，因為 `00685L` 的 `L` 尾碼 fallback 同樣得 2（兩條規則互為備援）；改壞 `L` 規則則新列失敗，兩條一起改壞則名稱列也失敗。結果（00685L → 2）已鎖住，但單獨鎖 `正2` 名稱規則需另一列「名稱含正2、代碼不帶 L」——規格外，請 Tech Lead 決定是否另開任務。


## 2026-10-11 — Tech Lead：#444／#445／#446 事後審閱；Boss 看到舊版卡片的根因＝部署競賽回滾（排程執行，第 4 次）

- **狀態**：無 open PR。#444（TASK-047）、#445（TASK-038，取代 #443）、#446（TASK-048）皆由 Boss 在審閱前合併，三個任務 → **MERGED**，Review 區已補事後審閱（見各任務）。
- **審閱深度**：TASK-038 的後端程式（`MisTaiexClient`／`MisTaiexQuoteSource`／`DrawdownBrakeEstimator`／`DrawdownBrakeService`、controller、DTO、DI）我**逐行讀了**，無須追加修正；TASK-047 我讀了 `splitLev2Order`／`SPLIT_EVENTS` 並對照 S4～S7 golden，其餘依 Result、CI、部署核對（未逐行讀）；TASK-048 僅兩列測試。
- **TASK-048 的提問「單獨鎖 `正2` 名稱規則是否另開任務」→ 不開**（Technical Debt，不排）：結果（00685L → 2）已鎖住；要單獨鎖名稱規則需要「名稱含正2、代碼不帶 L」的標的，現實不存在。
- **MIS 經 TWStockLib 已實測**：Tech Lead 於 master（`31bcbf8`）手動 dispatch `contract-tests.yml`，**run 102 通過**（週日、`z` 為數字，所以測試真的走到 lib 路徑）。尚未實測的只剩盤中 `z` 的更新行為與平日休市日。
- **`ARCHITECTURE.md` 已更新**：契約區 `estimate`／`estimateCorrected`、第二階段章節（BLOCKED → 已實作，記錄 TWStockLib 決定、防腐層、疊加在正式訊號之外、基準日檢查）、Cache 盤點表。
- **Boss 回報「00685L 沒併入、NTSD 沒計入」（對話，附截圖）**：不是沒人做。TASK-047 已在 master（#444，13:15 UTC 合併，run 532 部署成功）。**根因＝部署競賽**：13:50 UTC #445 與 #443 幾乎同時被標為已合併（#443 的提交是 #445 的祖先，GitHub 視為已合併），兩個部署同時跑；#443 的 `merge_commit_sha` 是它自己的分支頭 `f77ac7b`（從更舊的 master 切出，**不含 TASK-047**），run 534 在 13:54:10 比 run 533（13:53:13，部署完整的 master）**晚完成**，正式環境因此被回滾到沒有 TASK-047 的版本，直到 #446 合併後 run 535 於 14:46:42 才回到 master 最新。Boss 截圖的畫面（「不含 NTSD」對帳行、只有 00631L 一列、00685L 落在「其他倍數商品」）正是 `f77ac7b` 的樣子。工作流程沒有 `concurrency`、且部署 `merge_commit_sha`，是兩個疊加的缺陷（細節與時間線在 TASK-049）。
- **目前正式環境**：run 535（`31bcbf8`，含全部變更）14:46:42 UTC 部署成功。我的沙箱連不到 `apps.jacky-studio.com`（proxy 403），**無法直接目視確認**，請 Boss 強制重新整理（Ctrl+Shift+R）後確認：卡片應出現「台股正 2（00631L／00685L）」一列、沒有「不含 NTSD」對帳行。SPA 分頁不重新載入就不會換新的 JS。
- **新任務**：**TASK-049**（Backend，HIGH，READY）：workflow 加 `concurrency`（序列化）＋ PR 觸發的 run 改 checkout 執行當下的 `master` 最新，不再用 `merge_commit_sha`；同步更新根 `CLAUDE.md` 的 Deployment 說明。會改變 CLAUDE.md 記載的既有部署規則，所以合併前請 Boss 看一眼（不同意則退回最小版本，見任務書）。
- **操作建議（TASK-049 合併前）**：一個 PR 的提交已被另一個 PR 包含時（如 #443 ⊂ #445），**直接 close 不要 merge**，否則會多一次拿舊 commit 的部署。
- **未動**：任何程式碼、`.github/workflows/`、`CLAUDE.md`、`REQUIREMENTS.md`、`DECISIONS.md`。
- **驗證限制**：沙箱無 .NET、連不到證交所／Yahoo／正式環境；以上結論來自 git 歷史、GitHub Actions 的 run／job 紀錄與 GitHub API（`merge_commit_sha`）。

## 2026-10-12 — Tech Lead：TASK-049 事後審閱並結案；部署流程寫入 ARCHITECTURE（排程執行，第 5 次）

- **狀態**：無 open PR（`list_pull_requests` 為空）。open issue 僅 #431／#407／#409，皆無 2026-10-11 12:17 UTC 之後的新留言；PM／Boss 沒有新的待辦或決定。所有 TASK-001～049 均為 MERGED 或更早結案——這次唯一的實質工作是 TASK-049 的收尾。
- **TASK-049（#447）**：Boss 於 2026-10-11 合併（採「B2」：`push` 到 `master` ＋ `paths-ignore ['.agent/**','**.md']`、`concurrency: production-deploy`、checkout 取 `master` 執行當下的 tip）。我逐行讀了合併後的 workflow 與 `CLAUDE.md` diff，對照 2026-10-11 13:50 事故時間線推演，**認可，無須追加修正**；Status → MERGED，Review 區已填（含已實測／未實測清單、接受的殘留風險）。
- **已實測**：Build and deploy run 537（event=`push`，`master`＝`29845ef`）成功，證明 push 觸發與新 checkout 路徑可運作。**尚未實測**：(1)「排隊中被取代的 run 顯示 cancelled」與「排隊 run 取到較新的 tip」——要兩個 PR 連續合併才看得到；(2) `paths-ignore` 的過濾——本次的文件 push 是第一個實測，結果見下方「paths-ignore 實測」。
- **`ARCHITECTURE.md`**：在「Workflow conventions」補了「Deployment pipeline」一條（觸發、`paths-ignore`、tip 部署、串行化、事故原因、行為規則）。這是 TASK-049 指定由 Tech Lead 負責的文件。
- **未動**：任何程式碼、`.github/workflows/`、`CLAUDE.md`、`REQUIREMENTS.md`、`DECISIONS.md`、`boss/PM.md`。
- **仍在等 Boss（非阻擋）**：2026-10-12（今天，週一）盤中目視確認 TASK-038 的盤中預估（見 `boss/TECH_LEAD.md` 第 24 項）；強制重新整理後確認卡片已是最新版；#431 的 Q1／Q2／Q3 未決（功能維持 admin only）。
- **驗證限制**：沙箱無 .NET、連不到 `apps.jacky-studio.com`／證交所／Yahoo；以上結論來自 git 歷史、GitHub Actions run／job 紀錄。
- **paths-ignore 實測（同日）**：本次文件 push（`ba1187d`，只動 `.agent/**`）推上 `master` 後，`list_workflow_runs(event=push)` 最新仍是 run 537（`29845ef`），**沒有為 `ba1187d` 產生 Build and deploy run**——`paths-ignore` 過濾如預期運作（推送後約 1 分鐘內查詢；若之後有 run 補出現會在此更正）。未實測項只剩「被取代的排隊 run 顯示 cancelled」與「排隊 run 取較新 tip」。


## 2026-10-12 — PM：補登 Boss 直接合併的 #448、#449（排程執行）

- **狀態**：無 open PR；唯一 open issue 是 #409（最後一則是 PM 2026-10-06 的盤點，Boss 沒有新留言）。Boss 在 Tech Lead 上次執行（2026-10-12 00:36 UTC）之後，自己撰寫並合併了兩個 PR，**沒有經過 task／PM／Tech Lead 流程**，所以產品文件與正式環境出現落差，這輪就是補上：
  - **#448**（單字庫唯一性）：`UserWords` 加 `(UserId, WordId)` 唯一索引；migration 先清掉所有使用者的重複列（留複習次數最多、同樣則最早加入的一筆）；加入單字／單字包改為遇重複回「已經加過了」；刪除一律刪得掉。→ **DECISION-023**，`REQUIREMENTS.md`「Dictionary & Word Repository」新增 3 條驗收條件。
  - **#449**（站台品牌與分享預覽）：伺服器依網址替換 title／description／Open Graph／`twitter:card`；首頁改為「Jacky Studio — AI 學習、投資與實用工具」，各應用「{名稱} | Jacky Studio」；單字頁補 Open Graph（title 格式不變）。→ **DECISION-022**（取代 2026-09-30 的首頁 title `English Coach - AI 英文學習與單字訓練`），`REQUIREMENTS.md` 新增「站台品牌與分享預覽」一節（已出貨、LOW）。
- **PM 的觀察（皆為讀 PR 描述與程式的事實或假設，未實機驗證）**：
  - 事實：`SitePages.cs` 與前端 `constants/apps.tsx` 各有一份應用名稱／描述（程式註解互相指向）；新增應用要改兩處。
  - 假設 A1：品牌分兩層（平台＝Jacky Studio；英文學習與單字頁＝English Coach）是 Boss 有意的；若要連單字頁也改，會動到 DECISION-012。
  - SEO 取捨：首頁不再帶「英文學習」關鍵字，英文相關入口由 `/english` 與單字頁承擔，單字頁（成長面主要資產）不受影響。
  - 不確定：#448 的 migration 在正式環境實際刪了幾筆、是否成功套用，PM 不知道（PR 只說明本機 LocalDB 驗證）。
  - 未驗證：LINE／Teams 實際預覽（有快取）。
- **Boss**：無需回覆。
- **Tech Lead**：(1) 請事後審閱 #448／#449（同 #437～#446 的做法），並確認 #448 的 migration 已在正式環境成功套用（App Insights 啟動紀錄或 `__EFMigrationsHistory`）；(2) #449 PR 描述提到的既有問題——`src/Portfolio/DrawdownBrakeRefresh.tsx` 與 `drawdownBrakeRefresh.ts` 檔名只差大小寫，Windows 上 `tsc`／`vite build` 失敗、`DrawdownBrakeCard.test.tsx` 107 個測試失敗（Linux CI 正常）——這是技術債，是否排任務由 Tech Lead 決定，PM 不排優先序；(3) 是否把應用名稱／描述收斂成單一來源，由 Tech Lead 決定（PM 只要求兩處不可不一致）；(4) `ARCHITECTURE.md` 若要記錄 `AppPageMetadataMiddleware`／`SitePages`，由 Tech Lead 負責。
- **未動**：程式碼、`boss/TECH_LEAD.md`、`ARCHITECTURE.md`、`tasks/*`。

## 2026-10-12 — Tech Lead：Boss 直接合併 #448／#449 的事後審閱；contract test 月營收失敗研判（排程執行，第 6 次）

- **狀態**：無 open PR；open issue 只剩 #409（`To Tech Lead`，2026-10-06 後無新留言）。TASK-001～049 全數 MERGED／結案，無 READY／IN_PROGRESS 任務，沒有新需求（`REQUIREMENTS.md`／`DECISIONS.md` 本次未變）。#431、#407 已被 Boss 於今天 03:40／03:44 UTC 以 `completed` 關閉，最後一則留言仍是 Tech Lead 的 2026-10-11 12:17 UTC，Q1／Q2／Q3 沒有回覆，功能維持 admin only。
- **Boss 不經 TASK 流程直接合併的兩個 PR（審閱完，無須追加修正）**：
  - **#449**（`AppPageMetadataMiddleware`、`HtmlMetadata`、`IndexHtmlTemplate`、`SitePages`）：所有值皆 `HtmlEncode`、取代用 `MatchEvaluator`、任何例外 fall through、`Cache-Control: no-store`；`og:url`／`og:image` 由請求 Host 組成但已編碼且不快取，無注入或快取汙染路徑。觀察：`SitePages.cs` 與 `apps.tsx` 的 app 名稱／描述是兩份手動同步的資料，沒有自動比對測試（Technical Debt，不排）。入口標題改為 `Jacky Studio — AI 學習、投資與實用工具`（Boss 的決定，覆寫 TASK-019 的舊標題；單字頁標題不變）。
  - **#448**（遷移 `UniqueUserWord`、`RepositoryService`）：先以 `ROW_NUMBER()` 刪重複 `UserWords` 列（保留複習次數最多、其次最早者）再建 `(UserId, WordId)` 唯一索引；我核對沒有任何其他表 FK 指向 `UserWords.Id`，遷移註解成立；`AddWordAsync`／`AddWordPackAsync` 處理競態重複、`DeleteWordAsync` 刪所有符合列。風險：遷移於正式環境啟動時執行、刪除不可逆，deploy 成功不等於遷移成功，沙箱無法目視正式環境。
  - 兩者的架構事實已寫入 `ARCHITECTURE.md` 最後一節；`CLAUDE.md`、程式碼未動。
  - **回覆 PM 同日條目對 Tech Lead 的兩項請求**：(1) 事後審閱——完成，結論同上，與 DECISION-022／023 一致（品牌兩層分法技術上無問題：單字頁走 `WordPageMetadataMiddleware`，其餘走 `AppPageMetadataMiddleware`，兩者共用 `HtmlMetadata.Apply`）。(2) 「#448 migration 已在正式環境成功套用」——**無法確認**：遷移於啟動時執行，沙箱連不到正式環境，Actions 的 deploy 成功不代表遷移成功，遷移也不記錄刪除筆數。已請 Boss 在正式資料庫跑兩行查詢確認（見 `boss/TECH_LEAD.md` 第 26 項）。PM 對 A1（品牌兩層）與 SEO 取捨的觀察我無異議。
- **Third-Party Contract Tests run 103（schedule，今天 06:45 UTC）失敗**：57 個中 3 個失敗，皆為 `TwseOpenApiContractTests.Revenue_t187ap05_L_ForTodaysRandomPicks_HasUsableMonthlyRevenue`（2882、1301、2308 查不到 `公司代號`），其餘 54 個通過；`MFI94U`、MIS `t00`、Yahoo `^TWII`、FundRich、SEC EDGAR、104 皆通過。前例：9/12 run 68 同一測試失敗（2891 查不到），9/13 run 69 成功。研判為證交所每月 10 日之後月營收資料集更新窗口的暫時現象；正式環境 `FetchRevenueData` 找不到列回 `null`，個股分析少一項月營收，不報錯。**未驗證**：沙箱連不到證交所（proxy 403），無法直接取回該資料集；「更新窗口」是依兩次日期吻合與 9/13 自動恢復的推論，不是實測。處置：不開任務、不通知 Boss；下次排程若 run 104 的 `t187ap05_L` 仍失敗，升級（測試容忍月初窗口，或確認資料集是否改版）。
- **部署串行化實測**：run 538（#449，06:32:48 UTC）與 539（#448，06:33:29 UTC）連續觸發，539 的 job `started_at` 06:35:17，538 `completed_at` 06:35:16——`concurrency` 確實排隊；539 部署較新的 tip `1f5c747`。已更新 `ARCHITECTURE.md`「Deployment pipeline」的實測紀錄；「被取代的排隊 run 顯示 cancelled」仍未觀察到。
- **未動**：任何程式碼、`.github/workflows/`、`CLAUDE.md`、`REQUIREMENTS.md`、`DECISIONS.md`、`boss/PM.md`、任何 TASK 檔。
- **驗證限制**：沙箱無 .NET、連不到 `apps.jacky-studio.com`／證交所；審閱依 git diff、GitHub Actions run／job 紀錄與 job 日誌，未在本機執行測試。


## 2026-10-13 — PM：#409 後續項目產品面複核、需求狀態同步（排程執行）

- **狀態**：無 open PR；唯一 open issue 是 #409（最後一則仍是 PM 2026-10-06 的盤點，Boss 無新留言）；master 自 Tech Lead 2026-10-12 12:40 UTC 的提交之後沒有新提交。沒有新的 Boss 指示或決定，所以**沒有新的決策要記**（`DECISIONS.md` 未動）。
- **這輪做的事**：Tech Lead 已把 #409 盤點的 HIGH／MEDIUM 項目做完並合併（TASK-028～033），但 `REQUIREMENTS.md` 的驗收條件仍全是未勾選——需求文件與現況脫節。依 §10 做產品面複核：
  - **讀程式碼對照（事實，未實機執行）**：`RoundResultScreen.tsx`（答錯單字僅在有答錯時渲染、「全部加入單字庫」＋「前往複習」）、`ConversationSummary.tsx`（建議單字可點、Snackbar 回饋）、`PDFUnlocker/index.tsx`（錯誤 `Alert`、成功提示、檔名「原檔名-unlocked.pdf」）、`Repository/index.tsx`（`isError` 顯示「無法載入你的單字庫」而非空庫）、`auth/LoginGate.tsx`（價值說明，測試斷言含「完全免費」）。
  - **結果**：「逐模組 UX／留存改進」7 條驗收中 5 條勾選；2 條不勾——(1) 手機 ≤400px Exam 選單不溢出：只看到 `flexWrap: 'wrap'`，版面必須實機看；(2) 「不新增 AI 呼叫／UsageLimit 消耗」：PM 沒逐一追後端，請 Tech Lead 在下次回覆確認。第 5、6 項（LOW）維持 Future Work。
  - 「匿名用量統計」補了 Status：TASK-029 已合併，但四條驗收都要看 Azure Portal，PM／Tech Lead 沙箱皆無存取，**不勾選**。
- **PM 的判斷（假設，Boss 可推翻）**：#409 是「讓產品成功」的長期授權，不是單一待辦；剩下的 LOW 項目（JobSearch 記憶、DataAnalysis／Connectors 引導）在沒有用量數據前不值得排工——這與 DECISION-009／010 一致，所以我**沒有**新增需求或催 Tech Lead 排任務。若 Boss 提供第一份用量數字，PM 再依數據重排優先序。
- **Tech Lead Actions**：(1) 確認 #409 的 TASK-030～032 沒有新增 `[UsageLimit]` 或 AI 呼叫（驗收最後一條）；(2) 其餘無新需求，維持現狀。我沒有直接指派 Engineer。
- **Boss**：無需回覆。兩項非阻擋的小請求（手機看一眼 Exam 選單、貼一次 App Insights 頁面造訪數字）寫在 `boss/PM.md` 頂端。
- **未動**：程式碼、`DECISIONS.md`、`boss/TECH_LEAD.md`、`ARCHITECTURE.md`、`tasks/*`。
- **驗證限制**：沙箱連不到 `apps.jacky-studio.com`／Azure，以上全部是讀 git 與原始碼的結論，不是實機測試。


## 2026-10-13（晚）— PM：補登 Boss 直接合併的 #450～#456（Data Analysis AI 提問、模型統一）（排程執行）

- **狀態**：無 open PR；唯一 open issue 仍是 #409（Boss 無新留言）。距上一輪 PM（00:38 UTC）後，Boss 自己合併了 7 個 PR（#450～#456，皆繞過 task 流程），`.agent/` 沒有對應紀錄——需求文件與現況脫節，這輪補登。沒有 Boss 的新問題或指示。
- **補登內容**：
  - **DECISION-024**：所有聊天模型統一為 `gpt-6-luna`、輸出上限 16384（#455）。
  - **DECISION-025**：Data Analysis 的 AI 提問「資料中找不到的條件不得默默忽略」（#456／#451），並記錄 Boss 已主動投入該功能——作廢 PM 稍早「無用量數據不投入」的假設（僅對此功能）。
  - **REQUIREMENTS.md** 新增「Data Analysis — AI 自然語言提問」（MEDIUM、已出貨），並在 Portal 需求的 Audit 事實旁註記假設已更新。
- **PM 讀碼確認的事實（未實機）**：`NlPlanValidator.cs:155` 有「已忽略此條件」提示；`NlDateRanges.cs` 預先算好相對日期；`SourcePanel.tsx`／測試處理 `__default__`；`POST summarize/nl` 為 `[Authorize]` 且扣 `UsageCosts.NlSummarize`（2），符合 DECISION-006／007。
- **PM 看到的風險（區分事實與推論）**：
  1. 事實：#455 PR 自述「尚未對真的 OpenAI API 實測」，風險最高的是股票名稱查代號與對話測驗開場；合併即部署。#456 的核心驗收（陷阱題每次都出提示）同樣未實測。這兩件 Boss 在 PR 內都已寫明，所以 **PM 不另發通知**，只在 `boss/PM.md` 與需求驗收條件中留底。
  2. 推論：`UsageCosts` 權重沿用舊點數表，模型統一後各功能的實際成本比例可能變了；累積約一週用量資料後值得比對（已在 DECISION-024 提出，數字由 Tech Lead／Boss 定）。
  3. 觀察：AI 提問的自我修正重跑會多一次 AI 呼叫而只扣一次用量（讀 `DataAnalysisController.cs`）——成本面小項，交 Tech Lead 判斷。
  4. 不確定：沒有分組的問題（「總營收是多少」）在現行提示詞下會得到什麼，PM 不知道（提示詞規定 `viewBy` 至少一個），列為觀察項。
- **PM 小假設**：A30——AI 提問與某個比賽或展示有關（#456 標題「比賽必修」）；不知道日期與評分標準，因此不排優先序、不加需求。Boss 可推翻。
- **Tech Lead Actions**（我沒有直接指派 Engineer）：
  (1) 事後審閱 #450～#456，並把 gpt-6-luna 與 NL pipeline 補進 `ARCHITECTURE.md`（目前沒有記載）；
  (2) 確認 #455 部署後各 AI 路徑（股票代號解析、對話開場、考試出題批改、字典新單字）在正式環境正常——PM 沙箱連不到正式環境；
  (3) 一週後比對各功能實際 AI 成本與 `UsageCosts` 權重，若落差大提出調整方案；
  (4) 延續上一輪：確認 TASK-030～032 沒有新增 `[UsageLimit]`／AI 呼叫。
- **Boss**：無需回覆。**不阻擋**的兩個小請求維持在 `boss/PM.md` 頂端，另加一則：#456 的陷阱題基準若跑完，結果（幾題出提示）貼給 PM 就能把驗收條件勾起來。
- **未動**：程式碼、`boss/TECH_LEAD.md`、`ARCHITECTURE.md`、`tasks/*`。
- **驗證限制**：沙箱連不到 `apps.jacky-studio.com`／OpenAI／Azure；以上只讀 git、PR 描述與原始碼，不是實機或對真 AI 的測試。


## 2026-10-13 — Tech Lead：Boss 直接合併 #450～#457 事後審閱、contract test 連兩日紅、回覆 PM 的 Tech Lead Actions（排程執行）

- **狀態**：無 open PR；唯一 open issue #409 沒有新留言。master 自 PM 00:38 UTC 的提交之後，Boss 在自己的 session 合併了 #450～#456（Data Analysis「問 AI」重構與示範資料產生器、全站 chat 模型統一為 `gpt-6-luna`），每個 PR 的 CI 皆綠，`Build and deploy` run 540～546 全部成功，正式環境＝`990ca6c`。
- **事後審閱**（細節與架構事實寫入 `ARCHITECTURE.md` 最後一節）：**無須追加修正**。逐行讀了 `NlValueSources`／`NlPlanValidator`／`BuildNlPlanAsync`／`TimeBox`／`DataAnalysisMapping` 與模型切換的差異。扣用量在任何 AI 呼叫與值查詢之前；一次提問 ≤2 次 AI 呼叫；快取在來源歸屬驗證之後；逾時與失敗都降級為「未驗證」而非報錯。
  - 兩件請 Boss／PM 知道（皆非阻擋，已寫入 `boss/TECH_LEAD.md` 第 27 項）：(1) 全部 chat 模型統一到 `gpt-6-luna`，單價與舊模型接近，但**推理 token 計入輸出**，實際成本可能上升、上升多少我看不出來——建議部署滿一週後查 `AiUsageLogs`（查詢範例在 ARCHITECTURE）；(2) Data Analysis 每次提問會把所有低基數字串欄位的值（≤2000 個）送給 OpenAI，前端沒有告知文字——屬隱私決策，歸 Boss／PM。
- **#457（Details 匯出 CSV，Boss 於 12:43 UTC、我審閱途中合併；`Build and deploy` run 547 成功）**：事後審閱 **無須追加修正**。公式注入防護、檔名處理、預設來源資料表釘死、10 萬列上限＋同時 2 個匯出的閘門我都讀過（細節在 `ARCHITECTURE.md`）。兩點非阻擋觀察留給 Boss：匿名匯出沒有 rate limiter（整表讀取共用的 5 DTU 資料庫）、匯出整批放記憶體（B1 約 1.75 GB，數百 MB 為我的估算、未量測）。
- **回覆 PM 的 Tech Lead Actions（`2026-10-13（晚）` 那段）**：
  - (1) 事後審閱＋補 `ARCHITECTURE.md`：完成（gpt-6-luna 與 NL pipeline、#457 都已記錄）。
  - (2) **「確認 #455 部署後各 AI 路徑在正式環境正常」：我做不到——沙箱無法呼叫 OpenAI 或連正式環境，沒有驗證。** 能給的：間接證據（#456 描述的 15 次真實模型請求顯示共用的 `OpenAIPromptService` 路徑對 luna 可用，來源是 PR 文字、環境不明）；讀碼發現 `StockCodeResolver`（#455 點名風險最高）失敗時**不記錄 API 錯誤訊息**，且 `AiUsageLogs` 只在回應帶 `Usage` 時才寫列，所以被 OpenAI 拒絕的請求**不會**出現在 `Success = 0` 的列裡——因此建 **TASK-051**（Backend, LOW）補一行錯誤日誌，並在 `ARCHITECTURE.md` 給 Boss 可執行的檢查（KQL 與人工試「台積電」／對話開場；皆未實測）。
  - (3) 一週後比對成本與 `UsageCosts` 權重：同意，查詢範例在 `ARCHITECTURE.md`；下次在 10/20 之後的排程我會請 Boss 貼結果（我看不到正式資料庫）。
  - (4) TASK-030～032 無新增 `[UsageLimit]`／AI 呼叫：見下方同日更早的核對（#418／#420／#422 檔案清單只有前端；有一個但書）。
  - PM 的觀察 3（重跑多一次 AI 只扣一次）：成立，每次提問 ≤2 次呼叫、權重 2 已涵蓋，不調整。觀察 4（沒有分組的問題）：由 `DataAnalysisLibrary` 限制造成，「總營收是多少」不會得到單一數字（模型自己補分組 → 分組表；沒補 → 錯誤訊息）；不排任務，詳見 `ARCHITECTURE.md`。
- **contract test run 104（10/13）失敗，run 103（10/12）也失敗**：兩次**唯一**失敗項都是 `Revenue_t187ap05_L_*`（10/12：2882／1301／2308 無列；10/13：1301／2891 無列，同次 2603 通過全部欄位斷言 ⇒ schema 沒變，只是資料覆蓋）。9/12 同測試曾失敗一天。依 2026-10-12 的承諾升級處理：建 **TASK-050**（Backend, LOW, READY）把該測試改為「該公司有列→維持欄位斷言；沒列→驗證整份資料的形狀後軟跳過」。**對正式環境影響小**（`FetchRevenueData` 找不到回 `null`，個股分析只少月營收一項）。TWSE 為何缺列的原因是**假設**（每月 10 日後分批更新），沙箱連不到 TWSE 無法驗證；10/7、10/8（run 93／94）也紅但 job 耗時 12～14 分鐘，形態不同，我拿不到失敗清單（Actions 日誌的 blob 網域被沙箱 proxy 擋住）。
- **回覆 PM 的 Tech Lead Action（2026-10-13 複核第 1 項：#409 的 TASK-030～032 有無新增 `[UsageLimit]`／AI 呼叫）**：
  - **沒有新增任何後端端點、`[UsageLimit]` 或 AI 呼叫。** 以 GitHub API 列出 PR 檔案清單核對：#418（TASK-030）、#420（TASK-031）、#422（TASK-033）的變更檔案**只有 `jackyaiapp.client/` 與 `.agent/`**，沒有任何 `JackyAIApp.Server/` 檔案。TASK-032（#421）我沒有再列檔案清單，依其 Result（`RequireAuth` 可選 `fallback`、`LoginGate`、Exam 按鈕 CSS）也是純前端。
  - **一個 PM 需要知道的細節**：TASK-030 讓 Conversation 摘要的「建議單字」可點擊，點擊時前端呼叫**既有**的 `GET api/dictionary/{word}`（`useLazyGetWordQuery`）取得 `wordId`。該端點是匿名、不扣 `UsageLimit`（字典刻意免費），但若該字還不在 `Words` 表會**呼叫一次 AI 生成並入庫**（先經 Hunspell 擋非法字）。所以「不新增 AI 呼叫」嚴格說是：**沒有新的 AI 端點，但這個點擊可能觸發既有的、每個字全站只會發生一次的字典生成**。成本與使用者在字典頁輸入同一個字相同，不隨訪客量成長。PM 的驗收條件「不新增 AI 呼叫／UsageLimit 消耗」可以勾選，建議在註記裡寫上這個但書。
- **PM 的另一項（手機 ≤400px Exam 選單、App Insights 數字）**：需要實機／Azure Portal，Tech Lead 沙箱做不到，維持 Boss 的非阻擋請求。
- **未動**：任何程式碼、`.github/workflows/`、`CLAUDE.md`、`REQUIREMENTS.md`、`DECISIONS.md`、`boss/PM.md`、既有 TASK 檔。
- **驗證限制**：沙箱無 .NET、連不到 `apps.jacky-studio.com`／OpenAI／證交所；審閱依 git diff、GitHub Actions run／job 紀錄與 PR 檔案清單，未在本機執行測試。


## 2026-10-14 — Tech Lead：TASK-050／051 結案、Boss 直接合併 #460 事後審閱（排程執行）

- **狀態**：無 open PR；唯一 open issue 仍是 #409（`To Tech Lead`，2026-10-06 後無新留言）。`REQUIREMENTS.md`／`DECISIONS.md`／`boss/PM.md` 自 PM 2026-10-13 12:39 UTC 之後沒有變動，沒有新需求或 Boss 決定。master＝`e0ef06e`，`Build and deploy` run 548（#459）、549（#458）、550（#460）全部成功，正式環境＝`e0ef06e`。
- **TASK-050（#458）、TASK-051（#459）→ MERGED**（Boss 2026-10-14 07:37 +0800 合併，兩者在合併前我還沒審閱，屬事後審閱）：兩個任務書的 Status 由 `PR_OPENED` 改 `MERGED` 並補 Review。
  - TASK-050：有列路徑的欄位斷言不變、無列路徑改驗整份資料形狀；變異驗證 run 106 證明放寬後仍守得住欄位更名（資料集目前 869 列）。**無須追加修正**。已知取捨：三個 symbol 都缺列且資料停更時測試會是綠的（它是契約測試，不是新鮮度警報）。
  - TASK-051：兩處 `CreateCompletion` 失敗時記 Error，行為不變；3 個單元測試涵蓋「失敗記 Error＋不重試」「無訊息記通用訊息」「成功不誤報」。**無須追加修正**。它只是觀測點，**沒有**驗證 gpt-6-luna 在 `StockCodeResolver` 可用（沙箱呼叫不了 OpenAI）。
- **#460（Boss 直接合併，未走 TASK）事後審閱**：持倉清單「占比」分母由「所有正值部位（含不動產）」改為 `liquidGrossAssetsTWD`，不動產不顯示占比，負債顯示負占比；純前端、後端與 API 未動。現在清單占比、水位、再平衡建議三處分母一致。**無須追加修正**；架構事實寫入 `ARCHITECTURE.md` 最後一節。
- **PM 可能要同步的需求文字**（我不改 `REQUIREMENTS.md`）：持倉清單「占比」的定義已改變（分母＝流動總資產、不動產無占比）。若 Portfolio 需求有寫清單占比的語意，請 PM 對齊；技術面沒有衝突。
- **待觀察（不是待辦）**：(1) 今天排程的 contract test（run 107，約 06:45–07:15 UTC，跑 master tip）是 TASK-050 的第一個真實觀察點，下次排程確認 `Revenue_t187ap05_L_*` 是否不再紅；(2) 10/20 之後請 Boss 貼 `AiUsageLogs` 成本查詢結果（見 ARCHITECTURE 2026-10-13 一節）。
- **未動**：任何程式碼、`.github/workflows/`、`CLAUDE.md`、`REQUIREMENTS.md`、`DECISIONS.md`、`boss/PM.md`。
- **驗證限制**：沙箱無 .NET、連不到 `apps.jacky-studio.com`／OpenAI／證交所；審閱依 git diff、GitHub Actions run 紀錄與測試原始碼，未在本機執行測試，也沒有目視畫面。



## 2026-10-14 — PM：補登 #457、#460；回覆 Tech Lead 的隱私與匿名匯出提醒（排程執行）

- **狀態**：無 open PR；Boss 無新問題或指示。距 PM 上輪（2026-10-13 晚）後 master 的變化：Tech Lead 的事後審閱（含 #457）、TASK-050／051 的 PR #458／#459 已由 Boss 合併（純測試／日誌，無產品面變動），以及 Boss 直接合併的 **#460**（持股清單占比）。**#457**（明細 CSV）在 PM 上輪補登之後才合併，一併補登。
- **補登內容**：
  - **DECISION-026**（#457）：Data Analysis 明細匯出 CSV，預設示範資料匿名可匯出。需求新增「Data Analysis」需求 8 與驗收條件；Future Consideration 的「DataAnalysis 匿名結果匯出」劃掉（明細版已出貨，彙總版沒有）。
  - **DECISION-027**（#460）：持股清單「占比」分母改為流動總資產（不含房產），與水位／再平衡卡片一致（Tech Lead 同日事後審閱也確認三處分母相同、無須追加修正；已按其「請 PM 對齊需求文字」的提醒同步 Portfolio 需求）。Portfolio 需求新增 2026-10-14 更新與驗收條件；PM 讀了 `PositionsTable.test.tsx`，測試斷言 60／20／10／10%、房產無占比、負債 −50%。
  - **DECISION-028**（PM 預設，A31）：Tech Lead 提出「問 AI」每次都把欄位名稱與低基數欄位的值（≤2000 個）送給 OpenAI，前端沒有任何告知；PM 決定先要求**在輸入處加一行白話告知**（需求 7）。不做阻擋式同意框、不做排除欄位（列 Future Consideration）。**PM 未查證**合規是否足夠、不下結論；Boss 可撤回。
  - 需求「逐模組 UX」最後一條驗收（不新增 AI 呼叫／UsageLimit）依 Tech Lead 2026-10-13 的核對**勾選**，並註記字典生成的但書。
  - Data Analysis 的「沒有分組的問題」由「觀察項」改為「已知限制」（Tech Lead 讀碼確認）；PM 不排任務，在 `boss/PM.md` 留一行讓 Boss 決定。
- **PM 的產品立場（假設，Boss 可推翻；寫在需求「匿名匯出的產品立場」）**：匿名來源匯出上限降到約 2 萬列不改變任何可見行為（示範表約 2 萬列），PM 同意；不建議改成一律登入；使用者自己的大表以伺服器穩定優先、寧可更早截斷並提示。**現在不排任務**——沒有看到濫用或當機的證據；Tech Lead 認為值得做時再排。
- **Tech Lead Actions**（PM 沒有直接指派 Engineer）：
  1. 為需求 7／DECISION-028 拆一個小任務（Frontend，LOW～MEDIUM）：「問 AI」輸入處的一行資料外送告知。文案以您 `ARCHITECTURE.md` 建議的那句為底即可；請確認它在訪客與已登入者、手機寬度下都看得到。
  2. 匿名匯出的硬化（上限／rate limiter／記憶體）：PM 的產品立場如上，是否排任務、排多高由 Tech Lead 決定。
- **Boss**：無需回覆。`boss/PM.md` 頂端已更新（含 A31、匿名匯出立場、「總營收」限制）。
- **未動**：程式碼、`boss/TECH_LEAD.md`、`ARCHITECTURE.md`、`tasks/*`。
- **驗證限制**：沙箱連不到 `apps.jacky-studio.com`／OpenAI／Azure；以上是讀 git、PR 描述、原始碼與測試的結論，沒有實機、沒有執行測試。


## 2026-10-14 — PM：補登 #461（IG 限動圖加入基金與現金）與漏記的 #406（排程執行）

- **狀態**：無 open PR；唯一 open issue 仍是 #409（`To Tech Lead`，2026-10-06 後無新留言）。Boss 無新問題或指示。距 PM 上一輪（同日稍早，補登 #457／#460）之後，master 只多了 **#461**（`feat(portfolio): add funds and cash to the IG story image`，Boss 2026-10-14 合併，純前端，5 個檔案都在 `jackyaiapp.client/src/Portfolio/`）。
- **PM 自己的疏漏（已更正）**：IG 限動圖本身（#406，2026-10-03 合併，匯出選單「IG 限動圖 (持股比例)」）**從來沒有寫進 `REQUIREMENTS.md`**，所以 #461 沒有可更新的需求。這次一併補上，並把 #406 commit message 裡的「Percentages only, no amounts」定為產品原則。
- **補登內容**：
  - **DECISION-029**：限動圖只顯示百分比、不顯示金額；內容＝台股、美股、**基金**（以名稱呈現）三段＋**現金**單一數字（標題右上角）；占比分母＝台股＋美股＋基金＋現金，不含不動產與負債（與 DECISION-027 的「流動總資產」同一組資產）。「為何不放金額」Boss 沒寫理由，PM 推測是不洩漏資產規模——文件裡標明是**推測**。
  - `REQUIREMENTS.md`「Portfolio Tracker」新增 2026-10-14 補登（IG 限動圖）與驗收條件：PM 讀了 `storyAllocation.test.ts` 的測試名稱與 `storyImage.ts`（確認畫圖程式只畫百分比、段落計數與名稱，沒有任何市值欄位），5 條勾選、1 條未勾（手機分享面板／IG 裁切後可讀性，需實機）。
  - 事實性觀察（不是缺陷）：限動圖只計**正市值**部位，所以異常資料（市值為負或 0 的股票／基金／現金）時，圖上分母可能與頁面「流動總資產」不同；一般持股不會發生，PM 不當成問題。
- **Tech Lead Actions**：無新任務。僅請 Tech Lead 在日後審閱任何動到 `storyImage.ts`／`storyAllocation.ts` 的 PR 時，順手確認沒有把金額畫上去（DECISION-029）。
- **Boss**：無需回覆。`boss/PM.md` 頂端已更新；唯一的實機請求（三段＋現金同時存在時圖是否清楚）**不阻擋**任何工作。
- **未動**：程式碼、`boss/TECH_LEAD.md`、`ARCHITECTURE.md`、`tasks/*`。
- **驗證限制**：沙箱無法執行畫面或測試；以上是讀 git（#406／#461 的 commit 與 diff）、原始碼與測試名稱的結論，沒有實機、沒有執行測試、沒有看過實際產出的圖。

## 2026-10-14（第二次）— Tech Lead：#461 事後審閱、TASK-052 建立、匿名匯出硬化決定暫不排工（排程執行）

- **狀態**：無 open PR；唯一 open issue 仍是 #409（`To Tech Lead`，2026-10-06 後無新留言）。master＝`a2f4f44`；`Build and deploy` run 551（#461）成功；排程 contract test **run 107 全數通過**（跑 master tip）——TASK-050 的第一個觀察點是綠的。距上輪 Tech Lead（5b47860）後的變化：PM 補登（d49a3fc：DECISION-026／027／028；392ff72：DECISION-029 限動圖）與 Boss 直接合併的 **#461**。
- **#461（IG 限動圖加入基金與現金占比，Boss 直接合併、未走 TASK）事後審閱**：純前端（5 檔，+308／−91）。基金成為第三個區塊（以名稱列出）、現金是標題列右上的單一數字、各區塊占比的分母＝台股＋美股＋基金＋現金（與後端 `liquidGrossAssetsTWD` 同一組四類，所以與 #460 的清單占比、水位、再平衡口徑一致）；≤2 區塊版面不變，3 區塊用 COMPACT。分配邏輯有 7 個單元測試，canvas 繪圖沒有自動測試。**無須追加修正**；細節與未驗證項在 `ARCHITECTURE.md` 最後一節。**DECISION-029 的金額檢查（PM 在同日 PM 條目請我確認）**：#461 的 `storyImage.ts` 差異中所有 `fillText` 的內容只有百分比、檔數、標的名稱／代號，沒有任何 `marketValueTWD` 或金額被畫上去，對話框文字也寫「只顯示比例，不含金額」——**沒有把金額畫上去**。PM 的「異常資料（負值／0）分母可能與頁面不同」觀察屬實（限動圖只計正值），我認同不是問題。**PM 的需求文字**：Portfolio 需求若寫「限動圖只含台股、美股」，PM 已於同日補登對齊。
- **PM 的 Tech Lead Actions 回覆**：
  1. **建 TASK-052**（Frontend，LOW，READY）：「問 AI」輸入處的資料外送告知（DECISION-028／需求 7）。重點：永遠可見的 `helperText`（不用 Tooltip／Popover）、訪客與已登入者同文、文案須對照 `BuildNlPlanAsync` 實際送出的內容核對、不下合規結論、手機寬度要換行。
  2. **匿名匯出硬化：現在不排任務**。主因：示範表預設就是 20,000 列，「降到約 2 萬列」在現況下等於沒有效果；正式 `dbo.Orders` 是否遠大於 2 萬我不知道；沒有濫用或記憶體問題的證據；治本（列數×欄數預算或串流輸出）要量測、且串流要改 `DataAnalysisLibrary`（DECISION-009）。已在 `ARCHITECTURE.md` 寫明 (a)～(d) 四個重新評估的觸發條件與屆時的最小做法。PM 的產品立場（不要求一律登入、伺服器穩定優先於匯出完整）與此不衝突。
- **未動**：任何程式碼、`.github/workflows/`、`CLAUDE.md`、`REQUIREMENTS.md`、`DECISIONS.md`、`boss/PM.md`、既有 TASK 檔。
- **驗證限制**：沙箱無 .NET、連不到 `apps.jacky-studio.com`／OpenAI／證交所；審閱依 git diff、PR 檔案清單與 GitHub Actions run 紀錄，未在本機執行測試，也沒有渲染限動圖。

## 2026-10-14 — Frontend：TASK-052 實作完成，PR #462 已開（排程執行）

- 接手 `TASK-052`（Frontend，`READY`，無相依）。開工前確認：無 open PR、沒有其他 Frontend 任務可做。
- 在 Data Analysis「問 AI」輸入框下方加永遠可見的 `helperText`（資料外送告知，DECISION-028／需求 7）。文案先對照 `BuildNlPlanAsync` 核對：除了「問題、欄位名稱、可選的值」，還會送**日期欄位最新日期**，且重跑時會附上**從大欄位搜出的相近值**，兩項都補進文案。不下合規結論。
- 測試：新增 `SummarizeTab.test.tsx`；`npm run lint`／`build`／`test`（62 檔、682 測試）本機實際執行全過。手機寬度僅依 CSS 判斷、未實機。
- 狀態：`TASK-052` → `PR_OPENED`（#462），等 Tech Lead 審閱。流程偏離：本次 session 只能推指定分支，沒有把 `IN_PROGRESS` 推到 master。
- **未動**：`JackyAIApp.Server`、`.github/workflows/`、`CLAUDE.md`、`REQUIREMENTS.md`、`ARCHITECTURE.md`、其他 TASK 檔。


## 2026-10-15（第二次）— Tech Lead：#462（TASK-052）合併結案（排程執行，第 11 次）

- **狀態**：無 open PR；唯一 open issue 仍是 #409（最後更新 10/06，無新留言）；Boss／PM 無新留言、無需決定的事項；沒有程式碼變更。
- **#462 已由 Boss 合併**（2026-10-15 17:59 +0800，`86475c8`）。核對：merge 的 PR 端 parent＝`c64b2b3`＝我 10/15 審閱的 head，審閱後沒有新提交；`Build and deploy` run 552 成功；contract test run 108 全數通過（TASK-050 之後連兩次綠）。
- **文件同步**：`TASK-052.md` Status → MERGED 並補「合併後確認」；`ARCHITECTURE.md` 補「合併結果」；`boss/TECH_LEAD.md` 加第 31 項。依 #462 審閱時的承諾（為避免合併衝突，當時刻意沒動 `TASK-052.md`／`PROJECT_STATUS.md`）現在補上。
- **未動**：程式碼、`REQUIREMENTS.md`／`DECISIONS.md`（PM 說合併後由 PM 勾驗收條件）、其他 TASK 檔。
- **未驗證**：手機 ≤400px 的「問 AI」告知換行（Engineer 與我都沒渲染過）；合規 PM 與我都未查證，不下結論；沙箱連不到正式環境，部署成功只是 Actions 結論。
- **Boss**：不需要回覆。**不發通知**（沒有需要 Boss 行動的新事項）。

## 2026-10-15（第三次）— PM：#462（TASK-052）合併後結案、勾驗收條件（排程執行）

- **狀態**：無 open PR；唯一 open issue 仍是 #409（最後更新 10/06，無新留言）；Boss 沒有新留言、沒有待回覆的問題。
- **#462 已由 Boss 合併部署**（`86475c8`，run 552 成功；Tech Lead 已把 TASK-052 記為 MERGED）。PM 讀 master 上實際的程式（`SummarizeTab.tsx:365` 的 `helperText={AI_DATA_NOTICE}`、`constants.ts:46` 的文案）對照需求 7：永遠可見、訪客與已登入者同文、涵蓋問題／欄位名稱／可選值／最新日期／重跑時的相近值、無「不會儲存／安全」承諾 → **符合**。
- **文件更新**：`REQUIREMENTS.md`「Data Analysis」把需求 7 與標題註記改為「已出貨」，勾選兩條驗收條件（登入與未登入皆看得到；涵蓋實際送出的全部內容），手機 ≤400px 換行一條維持未勾；A32 補記 Boss 已合併但未明確表態，仍可推翻。`DECISIONS.md` DECISION-028 補一行「實施結果」（不改寫原文）。`boss/PM.md` 摘要壓縮為結案版。
- **未驗證／限制**：手機 ≤400px 的畫面（Engineer、Tech Lead、PM 皆沒渲染過）；對真 OpenAI 的送出內容未抓封包；合規（個資法、OpenAI 條款）PM 未查證、不下結論。沙箱連不到正式站，部署成功僅指 Actions 結論。
- **Tech Lead Actions**：無。維護不變式（改 `BuildNlPlanAsync` 送出內容須同步改 `AI_DATA_NOTICE`）已在 `ARCHITECTURE.md`。
- **Boss**：不需回覆，不發通知。仍待的兩件非阻擋事：#455 gpt-6-luna 對真 OpenAI 實測／#456 陷阱題基準結果貼給 PM（才能勾 Data Analysis 最重要的驗收條件）、匿名用量統計（TASK-029）的 Azure Portal 數據。


## 2026-10-16 — Tech Lead：評估 Issue #464（效能提案）、審閱 #463（趨勢線）（排程執行，第 12 次）

- **狀態**：無 open PR；open issue＝#464（Boss 今日 10:08 +0800 提出，標籤 To PM／To Tech Lead）、#409（10/06 後無新留言）。Boss 直接合併了 #463（Stock Intelligence 自動趨勢線，`5c6145e`）；`Build and deploy` run 553 成功。排程 contract test 今日（run 109）尚未出現在 Actions 清單（查詢時間約 11:20 +0800，通常 13:xx +0800 才跑），下次排程再看。
- **#464 評估**（依據與逐項理由在 `ARCHITECTURE.md`「Issue #464 效能提案評估」，已回覆在 Issue）：
  - 建立 **TASK-053**（Backend，HIGH，READY）：每日挑戰當日題目只產生一次並存 DB。**IMP-1 實為正確性問題**（GET／Submit 各自重新產生，亂數流在合格單字數變動時位移，選項順序與 `CorrectIndex` 可能整個改變 → 評分／XP 算錯；讀碼推論，未重現）。新增 `DailyChallengeSet` 表（加法式遷移），回退方案＝`IMemoryCache`。
  - 建立 **TASK-054**（Backend，LOW，READY）：`ValueAsOfDate`＋`InvestedValueAsOfDate` 的 as-of 價格查詢改預排序＋二分搜尋（行為不變）。提案只點到後者，前者（drawdown／日漲跌用）是同一個寫法。
  - IMP-2／3／4／5 第二步 → **Future Work，先量測**（Portfolio 資料層已有快取，提案「每次重讀快照／深抓股價」的前提部分過時；IMP-4 的兩套估值缺價語意刻意不同，合併是口徑決定）；IMP-6 → 不做；三個無人呼叫的端點 → 先保留，與 IMP-2 一併決定。
- **#463 事後審閱**：無須追加修正。三點記錄（皆不排任務）：「no AI」不完全準確（TW 名稱會經 `StockCodeResolver` 打 OpenAI，既有路徑多一個匿名入口）；讀碼推論 5–6 碼 ETF 在 Stock Intelligence 可能解析失敗（請 PM 手動確認 `00878`）；趨勢線無需求／決策紀錄（請 PM 補登）。
- **未動**：程式碼、`.github/workflows/`、`CLAUDE.md`、`REQUIREMENTS.md`、`DECISIONS.md`、`boss/PM.md`、既有 TASK 檔。
- **驗證限制**：沙箱無 .NET、無前端相依套件、連不到正式環境／OpenAI／證交所；以上為讀 git、原始碼與 Actions 紀錄的結論，沒有跑測試、沒有量測、沒有渲染畫面。KQL 與 `AiUsageLogs` 查詢未實測。
- **PM Actions**：(1) 為 #463 補登需求／決策；(2) 為 #464 排優先序（TASK-053／054 已 READY；IMP-2／3／4 看 Boss 是否要不看數字就做）；(3) 手動試 `00878` 是否能在 Stock Intelligence 分析。
- **Boss**：不需要回覆任何事。

## 2026-10-16 — Backend：TASK-053 PR_OPENED（#465）（排程執行）

- **狀態**：TASK-053（每日挑戰當日題目只產生一次並持久化）已實作，PR https://github.com/twjackysu/JackyAIApp/pull/465 等 Tech Lead 審閱。TASK-054 仍 READY（與 053 無相依，LOW），這輪沒動。
- **內容**：新表 `DailyChallengeSets`（純 `CreateTable` migration）；GET／Submit 共用同一份當日題目；併發首次請求在主鍵衝突時改回先寫入者的那份。先在未修改的 service 上確認回歸測試為紅（Score 5 → 2），修改後 `Category!=LiveContract` 1308 全過。
- **未驗證**：沒有對真正的 Azure SQL 跑 migration／併發；併發路徑只在 SQLite 上證明。**合併即部署，migration 啟動時對正式資料庫執行。**
- **Tech Lead Actions**：審閱 #465；注意 Submit 在字數不足時改回 `BadRequest`（PR 說明已寫）。
- **Boss**：暫無需要回覆的事。


## 2026-10-16（第二次）— PM：回覆 #464 效能提案、補登 #463 趨勢線、結案 TASK-053／054 的產品面（排程執行）

- **狀態**：無 open PR；open issue＝#464（標籤 To PM，Boss 今日提出、Tech Lead 已評估並留給 PM 排序）、#409（10/06 後無新留言）。Boss 沒有新留言。TASK-053（#465）、TASK-054（#466）已由 Boss 合併（master `7c119ad`，17:13 +0800）。
- **#464（DECISION-031）**：PM 回覆已貼在 Issue。結論：(1) Boss 的運算放置原則照 Issue 記錄，落實方式屬 Tech Lead；(2) TASK-053／054 PM 同意 Tech Lead 的排序與「存 DB」選擇；(3) 其餘 IMP-2／3／4／5b **建議先量測、不為原則先排工**（理由：Tech Lead 讀碼認為無可量到的 server 浪費、價值主要是 UX、目前優先序是留存與成長），Boss 若要照原則做，PM 建議順序 IMP-2 → IMP-4 → IMP-3 → IMP-5b；(4) IMP-4 若排工，PM 原則：**不要畫出由缺價造成的假下跌**（A35，具體口徑由 Tech Lead 定）；(5) 三個前端沒人呼叫的端點：需求文件第 625 行「前端全部接上」已過時；是否有外部／MCP 使用只有 Boss 知道，先保留、隨 IMP-2 決定。
- **TASK-053 產品面複核（讀 master 的 `DailyChallengeService.cs`）**：`GetOrCreateTodaySetAsync` 先讀當日已存的那份、沒有才產生並存入；GET 與 Submit 都走它；主鍵衝突時改回先寫入者的那份；字數不足時不寫入、回原 BadRequest。符合需求「每日題目一致」。`REQUIREMENTS.md`「Daily Challenge」新增該行為並勾一條驗收條件（**讀碼，未實機、未對正式資料庫跑**）；部署當天的一次性過渡風險備查。TASK-054 行為不變，無產品面動作。
- **#463 補登（DECISION-030）**：新增需求「Stock Intelligence 自動趨勢線」。已勾 4 條讀碼可證的驗收條件；未勾 4 條（訪客可見、手機 ≤400px、個股的線是否合理、ETF 代號）。PM 原則 A33：只描述過去、永遠附警語、不放預測／突破文字、畫線不用 AI。
- **新發現（需求 6，MEDIUM，讀碼、未實機）**：`StockCodeResolver.IsValidStockCode` 只接受恰好 4 位數字 → `00878`／`00919`／`006208`／`00631L` 這類 5～6 碼 ETF 代號先比對上市公司清單（不含 ETF），再把前 100 家公司丟給 OpenAI 問 4 位數代號，多半 `NONE` → 無法解析；**匿名也會花一次 AI**。Portfolio 不受影響。Tech Lead 請 PM 手動試 `00878`，PM 沙箱連不到正式站、做不到——改請 Tech Lead 讀碼確認端到端（還要看資料提供者端），成立再評估小任務。PM 同意 Tech Lead 把「匿名 AI 呼叫」列技術債、不單獨排工；需求 6 若實作，會順帶消除最可能的觸發情境。
- **文件更新**：`REQUIREMENTS.md`（Daily Challenge 補一條需求＋兩條驗收條件＋歷史註記；新增「Stock Intelligence 自動趨勢線」整節，含 A33／A34 與 Tech Lead Actions）、`DECISIONS.md`（DECISION-030／031）、`boss/PM.md`（摘要）。未動：程式碼、`ARCHITECTURE.md`、`boss/TECH_LEAD.md`、任何 TASK 檔。
- **Tech Lead Actions**：(1) 確認需求 6：5～6 碼 ETF 在 `comprehensive-analysis`／`price-bars` 端到端是否取得到資料；成立則評估「代號形狀的輸入不經 AI」的小任務，若資料提供者本身取不到 ETF 請告知 PM（需求改為「明確顯示不支援」）；(2) 其餘不需動作（IMP-2／3／4／5b 維持 Future Work，等量測或 Boss 指示）。
- **驗證限制**：沙箱無 .NET、無前端相依套件、連不到正式站／OpenAI／證交所；以上全為讀 git、程式碼與 Issue 的結論，沒有跑測試、沒有渲染畫面、沒有查 Actions 的部署結果。
- **Boss**：不需回覆、不阻擋任何工作；PM 不發推播通知（沒有需要 Boss 立即行動的事；回覆已在 #464，GitHub 會通知）。

## 2026-10-16（第三次）— Tech Lead：回覆 Boss 在 #464 的決定、排 IMP-2 與 ETF 代號任務、#465／#466 事後審閱（排程執行，第 13 次）

- **狀態**：無 open PR；open issue＝#464（Boss 20:01 +0800 留言：「IMP-2 C# 版刪除…IMP-4/5 先不做、IMP-6 不做，可以排進任務了嗎」）、#409（10/06 後無新留言）。Boss 在 17:13 +0800 直接合併了 #465（TASK-053）與 #466（TASK-054），`Build and deploy` run 554／555 成功；contract test 今日（run 109）綠。
- **回答 Boss「可以排進任務了嗎」→ 可以，已排**：
  - **TASK-056**（Backend，READY，MEDIUM）：以現有 C# 經由真正的 `StockAnalysisBuilder.BuildAsync` 產生評分／風險的**黃金測試資料** `stockScoringGolden.json`（約 60 手工邊界＋約 300 確定性隨機案例）。
  - **TASK-057**（Frontend，BLOCKED 於 056，MEDIUM）：評分／權重正規化／方向／建議文字／面向摘要／風險移植為 TypeScript 純函式，逐案比對黃金資料；請求只送 `{stockCode, market}`；拖權重與勾選面向即時生效、不再打 API。對現有後端也成立，可獨立部署。
  - **TASK-058**（Backend，BLOCKED 於 057，MEDIUM）：刪除 C# 評分／風險、`GET /api/finance/score/{code}`、builder 的旗標／權重、請求欄位；快取 key 只剩代號＋日期＋市場；AI 趨勢分析 prompt 拿掉「Overall Scoring」「Risk Assessment」兩段。
  - **IMP-3**：Boss 沒提 → 維持 Future Work，不排。**IMP-4／5 先不做、IMP-6 不做**：照 Boss。`technical-indicators`／`chip-analysis` 端點保留。
  - 設計、地雷（half-even 進位、已進位加權分加總、整數閾值、`F1` 格式、`risk` 依賴 `scoring` 的怪癖）與連帶影響見 `ARCHITECTURE.md`「Issue #464 後續」。
- **TASK-055**（Backend，READY，MEDIUM）：PM 需求 6 的 Tech Lead Action。**讀碼確認缺口成立**：`00878`／`00919`／`006208`／`00631L` 在 `StockCodeResolver` 不被視為代號，會先花一次 OpenAI 再失敗。解法＝「長得像台股代號的輸入直接當代號、不經 AI」（與前端 `detectMarket`／後端 `TwStockPattern` 同一個規則）。資料提供者端的讀碼推論：ETF 預期有技術面，籌碼／基本面多半是空的、不會壞（未實測）。
- **#465／#466 事後審閱**：無須追加修正；`TASK-053`／`TASK-054` → MERGED，Review 區已填。
- **請 PM**：(1) 更新 `REQUIREMENTS.md`（IMP-2：設定前端即時生效、`score` 端點移除、AI 趨勢分析輸入少兩段；需求 6：ETF 代號改走 TASK-055，資料面預期「ETF 只有技術面」是否可接受）；(2) 記錄 DECISION（Boss 於 #464 的決定）；(3) 057／058 完成後勾驗收。
- **Boss 注意（皆不阻擋）**：AI 趨勢分析的輸入會少兩段（預設選擇，已在 #464 說明；反對請說一聲）；IMP-3 要不要做；`technical-indicators`／`chip-analysis` 要不要一併刪（預設保留）。
- **未動**：程式碼、`.github/workflows/`、`CLAUDE.md`、`REQUIREMENTS.md`、`DECISIONS.md`、`boss/PM.md`。
- **驗證限制**：沙箱無 .NET、無前端相依套件、連不到正式環境／OpenAI／TWSE；全是讀碼與 Actions/Issue 紀錄的結論，沒有跑測試、沒有量測。

## 2026-10-16（第四次）— Tech Lead：Boss 決定 AI 評分段留後端，IMP-2 暫停待確認

- Boss 看完「C# 評分刪除後 AI 趨勢分析 prompt 少兩段」的說明後表示：**這段不要改成前端，留在後端。** 與 IMP-2「C# 評分整個刪除」衝突。
- **處置**：`TASK-056`／`057`／`058` → `ON_HOLD`（確認前不開工）。`TASK-055`（ETF 代號）不受影響。已請 Boss 二選一：(1) 全部留後端、取消 IMP-2（我建議；單一實作）；(2) 前端另算一份顯示、後端留一份給 AI（即時滑桿，兩套並存，靠黃金資料雙邊對齊）。
- **PM**：先不要更新 `REQUIREMENTS.md` 關於 IMP-2（設定即時生效、`score` 端點移除、AI prompt 少兩段）的內容，等 Boss 確認。需求 6（ETF）照 TASK-055 進行。
- 驗證限制同前：只動文件，沒有程式碼變更。

## 2026-10-16（第五次）— PM：記錄 Boss 在 #464 的範圍決定、IMP-2 暫停待 Boss 二選一、回覆 Tech Lead 關於 ETF 只有技術面的問題（排程執行）

- **狀態**：無 open PR；open issue＝#464（最後留言＝Tech Lead 12:11Z；**「AI 評分留後端」那句不在 Issue 上**，PM 只從 Tech Lead 的 `TECH_LEAD.md` 第 34 項與 `ARCHITECTURE.md` 讀到轉述）、#409（10/06 後無新留言）。master＝`80a03ff`。GitHub MCP 工具這次沒載入，PM 改用 REST 讀 Issue。
- **DECISION-032**：(1) 記錄已定案的 Boss 決定：IMP-4／5 先不做、IMP-6 不做、IMP-3 不排；Boss 設計偏好「不要兩套邏輯並存」。(2) **IMP-2 不定案**：「C# 版刪除」與「AI 評分留後端」衝突，TASK-056／057／058 維持 `ON_HOLD`。
  PM 意見（預設，可推翻）：選 Tech Lead 的 (1)「全部留後端、取消 IMP-2」——只有它同時符合 Boss 兩句話與 DECISION-031 的放置原則；IMP-2 的好處只是滑桿即時，PM 沒有使用者嫌慢的證據；(2) 可能讓畫面分數與 AI 文字引用的分數不一致（依 Tech Lead 描述）。
  依 Tech Lead 請求，**`REQUIREMENTS.md` 沒有寫任何 IMP-2 內容**。
- **需求 6（ETF 代號）**：答覆 Tech Lead「ETF 只有技術面是否可接受」→ **可接受**（A36）。PM 讀 `StockScoreService.cs`／`CategoryWeightConfig.cs` 確認：權重只在有資料的面向間重新正規化，缺籌碼／基本面不會拉低總分。
  新增產品條件（未驗證）：畫面要看得出分數只用了哪些面向、空面向不顯示成 0 分或中性分；不存在的代號要有明確訊息（待 TASK-055 PR 說明回報後 PM 決定是否另開小任務）。ETF 名稱搜尋列為 Future Consideration（LOW）。Tech Lead Actions 前兩項勾選。
- **文件更新**：`DECISIONS.md`（DECISION-032）、`REQUIREMENTS.md`（趨勢線一節的需求 6 後續、驗收條件一條、Future Consideration、A36、Tech Lead Actions）、`boss/PM.md`（摘要新增一條）。
- **Tech Lead Actions**：(1) TASK-055 照常；PR 說明的兩件查證回報後請通知 PM；(2) IMP-2 等 Boss 二選一，PM 無新要求；(3) 無新任務。
- **驗證限制**：沙箱無 .NET、無前端相依套件、連不到正式站／OpenAI／證交所；以上全為讀 git、程式碼與 Issue 的結論，沒有跑測試、沒有渲染畫面、沒有查 Actions。
- **Boss**：只剩一個不阻擋的選擇（IMP-2 選 (1) 或 (2)，Tech Lead 已問、PM 已在 #464 留意見）；PM 不發推播通知——沒有需要 Boss 立即行動的事。

## 2026-10-16（第五次）— Tech Lead：Boss 選 (1)，IMP-2 取消；審 #467；排 TASK-059

- **Boss 決定**：IMP-2 選 **(1) 全部留後端、取消**。`TASK-056`／`057`／`058` → `CANCELLED`；後端評分、`score` 端點、AI prompt 的評分／風險兩段全部保留不動。PM 的 DECISION-032 第 3 點可據此結案（PM 預設亦是 (1)）。
- **PR #468（TASK-056 黃金資料）**：工程師在 `ON_HOLD` 公告前已開工完成並開 PR，看到公告後自行改 draft 並標示請勿合併。Tech Lead 已**關閉**（不合併）並留言；分支保留。
- **PR #467（TASK-055 ETF 代號）**：Tech Lead 審閱通過 → **READY FOR BOSS REVIEW**（CI 兩個 job 皆綠、與 master 無衝突；不代表核准合併）。細節在 PR 留言。合併後我補 `TASK-055.md` 的 Status／Review（該檔被此 PR 改動，現在動會衝突）。
- **TASK-059**（Backend，READY，MEDIUM）：#467 查證的衍生。綜合分析查無資料回「成功但全空」並快取 12 小時；上游暫時故障會凍結某檔股票當天的分析。解法＝無價格且無指標回 `NotFound`、無價格不快取。**請 PM** 確認優先序與錯誤訊息文案（預設英文，比照既有 `Unable to resolve stock code…`）。
- **請 PM**：不需更新 `REQUIREMENTS.md` 關於 IMP-2 的內容（取消）；IMP-3／4／5／6 的範圍結論照 DECISION-032。需求 6：待 #467 合併後勾「5～6 碼可查到、不為此呼叫 AI」，「查不到時明確訊息」改由 TASK-059 承接。
- **未動**：程式碼、`.github/workflows/`、`CLAUDE.md`、`REQUIREMENTS.md`、`DECISIONS.md`、`boss/PM.md`、`TASK-055.md`。
- **驗證限制**：沙箱無 .NET、連不到正式環境／OpenAI／TWSE；#467 的審閱是讀差異＋CI 結果，沒有自己跑測試。

## 2026-10-17 — Tech Lead：#467／#469 結案、#470／#471 趨勢線編輯器事後審閱（排程執行，第 14 次）

- **狀態**：無 open PR；open issue＝#464、#409（自 10/16 後都沒有新留言）。`master`＝`ec6571c`；`Build and deploy` run 556～559 皆成功；contract test 最近一次（run 109，10/16）綠，今日（run 110）我查詢時尚未執行。
- **TASK-055**（#467，ETF 代號直接當代號）→ `MERGED`；**TASK-059**（#469，綜合分析查無資料回 NotFound、不快取沒價格的結果）→ `MERGED`。兩者 Review 區已補：無須追加修正。TASK-056／057／058 維持 `CANCELLED`。
- **#470／#471**（Boss 直接合併：趨勢線可編輯＋起始日期＋tooltip 價位；後端 `price-bars` 的 TAIEX 改回傳整段 6 年）：事後審閱無須追加修正；DECISION-030 警語與「不放判斷用語」檢查通過。細節在 `ARCHITECTURE.md` 最後一節。
- **技術債（不排任務）**：美股不存在代號回 500；`YahooFinanceMarketDataProvider` 1 小時快取不論有無價格；匿名 AI 呼叫無 rate limiter（既有）。
- **請 PM**：趨勢線後續（編輯、起始日期、tooltip 價位）若未補進需求／決策請補登；需求 6 勾選見 `ARCHITECTURE.md`。
- **Boss**：無待決事項、不需回覆；仍有數個非阻擋的目視／查詢請求（見 `boss/TECH_LEAD.md` 第 36 項）。
- **驗證限制**：沙箱無 .NET、無前端相依套件、連不到正式環境／OpenAI／TWSE；以上為讀 git、程式碼與 Actions 紀錄的結論，沒有跑測試、沒有渲染畫面、沒有量測。
## 2026-10-17 — PM：IMP-2 結案、補登 #467／#469（需求 6 出貨）與 #470／#471（趨勢線編輯）（排程執行）

- **狀態**：無 open PR；open issue＝#464（標籤 To Boss；最後留言是 Tech Lead 10/16 13:00Z，稱可關閉）、#409（10/06 後無新留言）。Boss 沒有新留言、沒有待回覆的問題。master＝`ec6571c`。自 PM 上次（`5fd789f`）以來 Boss 合併了 #467、#469、#470、#471。
- **DECISION-033**：(1) 結案 DECISION-032 第 3 點——Boss 選 (1)，IMP-2 取消（PM 只從 Tech Lead 的記錄讀到，Boss 原話不在 Issue 上）；(2) 需求 6 出貨：PM 讀 master 的 `TwStockCode.cs`、`FinanceQueryService.cs:336` 確認 ETF 代號不經 AI、查無資料回 NotFound 且不快取（**讀碼，未實機**）；(3) 回覆 Tech Lead 對 TASK-059 的問題：優先序同意、英文訊息可接受（A37）；美股拼錯代號回 500 不排任務。
- **DECISION-034**：補登 #470／#471（Boss 直接交付，未走 TASK）：起始日期可選、線可由使用者拖曳／刪除／新畫／改支撐壓力、tooltip 顯示每條線價位；線只存瀏覽器 localStorage、不跨裝置（A38）。A33（不放預測／建議、警語常駐）不變。PM 讀了 `trendlineDrawings.ts`、`trendlineRange.ts`、`TrendlineChart.tsx`、`TaiexTrendlineCard.tsx`，**沒有實機、沒看過畫面**。
- **文件更新**：`DECISIONS.md`（DECISION-033／034）、`REQUIREMENTS.md`（趨勢線一節：需求 6 出貨、新增需求 7、驗收條件 +4 條未勾／+2 條已勾、Scope、A37／A38、Tech Lead Actions）、`boss/PM.md`（摘要改寫）。未動：程式碼、`ARCHITECTURE.md`、`boss/TECH_LEAD.md`、任何 TASK 檔。
- **Tech Lead Actions**：無。Tech Lead 同日（第 14 次排程，`142f4c0`）已把 TASK-055／059 → `MERGED` 並事後審閱 #470／#471：無須追加修正、警語仍在、新增文字沒有判斷用語、localStorage 讀取逐條驗證且壞資料只丟那一條（PM 的 (b) 已由其讀碼回答）；手機拖曳手感 Tech Lead 與 PM 都沒渲染過，仍是 Boss 目視項。
- **驗證限制**：沙箱無 .NET、無前端相依套件、連不到正式站／OpenAI／證交所；以上全為讀 git、程式碼與 Issue 的結論，沒有跑測試、沒有渲染畫面、沒有查 Actions 的部署結果。
- **Boss**：不需回覆、不阻擋任何工作；PM 不發推播通知——沒有需要 Boss 立即行動的事。

## 2026-10-19 — PM：補登 #472～#488（Portfolio 歷史補資料、K 線成交量、Data Analysis 硬化），開 Issue #489（排程執行）

- **狀態**：無 open PR；open issue＝#409（10/06 後無新留言）；#464 已於 10/17 由 Boss 關閉。Boss 沒有新留言。master＝`f0e5433`。自 PM 上次（`ed9572a`，10/17）以來 Boss 合併了 #472～#488（#475～#477、#479～#484 為純重構，無產品面變更，PM 不補登）。`.agent/` 自 10/17 沒有任何新提交（Tech Lead 也沒有補登這批）。
- **DECISION-035（Portfolio）**：回撤／基準預設 3 個月；台股歷史背景補資料（閘道限速、一次一月、`backfill` 狀態）；**PM 發現缺口（讀 master）**：前端 `HistoryLimitNote.tsx` 仍寫死「只保留約 6 個月」、整個前端沒有讀 `backfill`，台股補到更早後提示變不實、也看不到進度。新需求「Portfolio 長區間圖表的歷史深度」（前端接 `backfill`＝MEDIUM；美股／基金補資料＝LOW、Future Consideration）。
- **DECISION-036（K 線成交量）**：個股 K 線加成交量；台股成交量單位統一（修正盤中量比讀成約千分之一的錯誤）。純補登，LOW。
- **DECISION-037（Data Analysis 硬化）**：欄位白名單、錯誤不外洩、匿名上傳限額（匿名合計 20 次／10 分鐘共用，A39）、自訂來源只收帳密型驗證。PM 讀碼發現小缺口：來源管理視窗儲存失敗只顯示泛用訊息（LOW）。**安全疑慮**：修補前匿名端點是否曾被利用——PM 無法查證，已開 `To Boss` Issue #489（事實／假設／不確定分開、不下法律結論）。
- **文件更新**：`DECISIONS.md`（DECISION-035／036／037）、`REQUIREMENTS.md`（新增三節；趨勢線節與 Data Analysis 節各補一行指標）、`boss/PM.md`（摘要）。未動：程式碼、`ARCHITECTURE.md`、`boss/TECH_LEAD.md`、任何 TASK 檔。
- **Tech Lead Actions**：(1) 確認前端接 `backfill` 是否已有人在做，沒有則評估小任務（MEDIUM）；(2) 自訂來源儲存失敗顯示後端原因，評估小任務（LOW）；(3) 請事後審閱 #472～#488 中有產品面的幾個（#472、#474、#478、#485～#488）並在 `ARCHITECTURE.md` 補記錄——PM 沒有逐行審；(4) 其餘無。
- **驗證限制**：沙箱無 .NET、無前端相依套件、連不到正式站／交易所／OpenAI；以上全為讀 git、程式碼與 GitHub 的結論，沒有跑測試、沒有渲染畫面、沒有查 Actions 的部署結果、沒有查 App Insights。
- **Boss**：只有 #489 一件需要判斷（不阻擋）；PM 發一則推播通知，因為它是安全／個資面的疑慮、且紀錄保留期可能有限。

## 2026-10-19 — Tech Lead：Boss 直接合併 #472～#488 事後審閱、排 TASK-060（排程執行，第 15 次）

- **狀態**：無 open PR；open issue 只有 #409（10/06 後無新留言）。`master`＝`f0e5433`＝正式環境（`Build and deploy` run 576 成功）。自我上次審閱（`ec6571c`）起 Boss 直接合併了 #472～#488（17 個 PR、約 107 檔、+8,560／−1,073），都沒有走 TASK。我查到的 `CI` run 704～714 皆綠；contract test run 110／111／112 皆成功。
- **事後審閱結論：無須追加修正。** 主題：K 線成交量窗格與 TW 成交量一律用股（#472）、財經錯誤訊息不外洩（#473）、DataAnalysis 欄位白名單與加固（#474／#478／#480／#481）、單飛快取（#477）、`FinanceQueryService` 拆三（#479）、Portfolio FX 去重（#482）、第三方閘道（#486）、逐月抓台股歷史（#487）、背景補齊 1Y／3Y 股價（#488）。逐行讀了哪些、只看 commit 與 CI 的是哪些，見 `ARCHITECTURE.md` 最後一節。
- **TASK-060**（Frontend，READY，MEDIUM）：#488 的 `backfill` 欄位前端沒接、沒有輪詢，#485 的說明文字對台股現在是錯的（仍寫只保留約 6 個月）。PM 同日獨立讀碼得到同一結論（DECISION-035）。任務書要求開工前確認 Boss 沒有自己做掉。
- **TASK-061**（Frontend，READY，LOW）：回應 PM 的 Tech Lead Action (2)——自訂資料來源儲存失敗時顯示後端原因。讀碼確認後端確實回 400＋具體訊息，`SourceManagerDialog.tsx` 的 `catch {}` 把它吞掉。
- **技術債（不排任務）**：B1 記憶體餘裕 vs 256 MB 上傳預算；閘道排隊時間不計入逾時；背景補齊的空月份判斷可能把洞永久寫進資料庫（無自動修復）；匿名上傳速率桶全體共用；自訂 MsSql 來源不限制主機（既有）。
- **Issue #489（PM 開，匿名端點修補前是否被利用）的技術事實**：預設引擎用的連線字串是 `ConnectionStrings:SQLConnection`，**與 EF 主資料庫是同一條**（`Program.cs` 第 79、194 行），而且 App 啟動時會 `Database.Migrate()`（第 214 行）——該帳號必須有 DDL 權限（推論），所以 PM 的「假設」有程式碼支持：若修補前真的被利用，權限不只讀 `dbo.Orders`。修補（#474 欄位白名單＋函式庫 1.2.1）最晚在 10/17 07:07Z（run 566）起已部署。偵測途徑與限制見 `boss/TECH_LEAD.md` 第 37 項與 #489 的留言。
- **請 PM**：確認 TASK-060 說明文字方向（只描述事實、不承諾補齊時間）。PM 在同日補登了 #485～#488（DECISION-035～037），我不重複。PM 要求事後審閱的 #472、#474、#478、#485～#488 我都看了（#472 只看了後端單位換算與 commit 說明，前端圖表 option 沒逐行讀）。
- **Boss**：不需回覆。可選的安全決策（自訂 SQL 來源是否拒絕私有位址）與 #489 的技術補充在 `boss/TECH_LEAD.md` 第 37 項。
- **驗證限制**：沙箱無 .NET、連不到正式環境／OpenAI／證交所；全為讀 git、程式碼與 Actions 紀錄的結論，沒有跑測試、沒有渲染畫面、沒有量測，也沒有在正式環境確認補齊真的在跑。

## 2026-10-20 — Tech Lead：審閱 PR #490（TASK-060）與 #491（TASK-061），皆 READY FOR BOSS REVIEW（排程執行，第 16 次）

- **狀態**：2 個 open PR（皆為 Frontend Engineer 於 10/19 依我排的任務開出）；`master`＝`38d1134`（自 10/19 無新 commit，僅 `.agent/**` 文件），正式環境＝`f0e5433`。open issue 只有 #409（10/06 後無新留言）。
- **PR #491（TASK-061，LOW）**：`SourceManagerDialog.tsx` 儲存失敗時改用 `getCaughtErrorMessage(error, 原 fallback)`，後端 400 的具體原因原樣顯示；新增 `SourceManagerDialog.test.tsx`（400＋標準封包／500＋HTML 兩案）。CI 綠、無衝突。**審閱通過 → READY FOR BOSS REVIEW**。後端訊息來源（`DataSourceConfigException`）我逐一確認都是固定字串，不夾帶內部資訊。
- **PR #490（TASK-060，MEDIUM）**：`types.ts` 新增 `HistoryBackfill`；新增 `useBackfillPolling.ts`（僅 `Running` 每 15 秒輪詢、`skipPollingIfUnfocused`）；`HistoryLimitNote.tsx` 改依 `backfill`／第一個點顯示並移除「只保留約 6 個月」；`DrawdownChart.tsx`、`BenchmarkChart.tsx` 接上。CI 綠、無衝突。**審閱通過 → READY FOR BOSS REVIEW**。
- **未動 PR 內的任務書**：兩個 PR 都已在自己的分支把任務書 Status 改 `PR_OPENED` 並填 Result；為避免與 PR 內容衝突，我**沒有**在 master 動 `TASK-060.md`／`TASK-061.md`，Review 區等合併後補（同 TASK-055 的做法）。
- **技術債（不排任務）**：`Stalled` 無重試按鈕；`points.length === 0` 時看不到補齊說明；`SourceManagerDialog.handleDelete` 吞錯誤。
- **Boss**：兩個 PR 待你決定是否合併（含程式碼，合併會部署；無 migration）；請目視手機 ≤400px 的換行與確認文案。其餘不需回覆。
- **驗證限制**：沙箱連不到正式環境與後端；結論來自讀 PR diff、測試與相關後端程式碼，加上 CI 結果（兩個 PR 的 `lint-and-build-client`、`build-and-test-server` 皆成功）。我沒有在本機重跑測試、沒有渲染畫面、沒有量測。
