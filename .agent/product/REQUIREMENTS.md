# JackyAIApp — Product Requirements

> **正式環境網域（2026-10-08 更新）**：正式網址為 **https://apps.jacky-studio.com**
> （Cloudflare 代理；Linux App Service）。舊的 `jackyai.azurewebsites.net` 已停用，
> 下文提及舊網域的內容為歷史紀錄，Search Console 驗證／sitemap 請以新網域為準。
> 見 DECISION-011。


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
- [ ] **(New, 2026-10-12, DECISION-023, shipped in PR #448)** A word appears **at most
      once** in a user's Repository. Adding the same word again — including a double-click
      or two simultaneous requests, or tapping the same word pack twice — answers "already
      added", never an error, and leaves exactly one entry. A word pack adds only the words
      not yet in the Repository.
- [ ] **(New, 2026-10-12)** Deleting a Repository word always succeeds for a word that
      exists (and reports "not found" for one that doesn't); opening a word's detail page and
      recording an exam result against it never fail because of an earlier duplicate.
- [ ] **(New, 2026-10-12)** Observation, not yet verified in production by PM: after the
      migration cleaned existing duplicates (one kept per user+word: most reviews, then
      earliest added), each affected user's remaining entry keeps its review progress.

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
- **每日題目一致（PM 2026-10-16 補登，TASK-053／PR #465 已出貨）**：同一個 UTC 日期，全站每個人看到、
  並被評分的是**同一份題目**（同樣的題目、選項與順序）。使用者送出答案時，評分一定對照「他看到的那份」，
  不會因為這一天內字典多了或少了一個字而改成另一份。分數與 XP 因此不會靜默算錯。

### Acceptance Criteria
- [ ] Daily Challenge never calls the AI/credit system.
- [ ] Missing a day breaks the streak per whatever streak rule is currently
      implemented (documented here as-is; changing the rule is a product decision,
      not a bug).
- [x] 同一天內，字典新增一個單字之後，再次開啟每日挑戰，題目、選項順序與原本相同；
      該使用者先前拿到題目後再送出全部正確答案，得分為滿分（PM 2026-10-16 讀 `DailyChallengeService.cs`
      的 `GetOrCreateTodaySetAsync` 與 TASK-053 的測試描述確認；**未實機、未對正式資料庫跑**）。
- [ ] 部署當天（2026-10-16）在部署前已取得題目、部署後才送出的使用者，可能對到「部署後第一次存下的那份」而與原本看到的不同
      （Tech Lead 在 TASK-053 已列為一次性過渡風險；PM 沒有證據有人受影響，不處理，列此備查）。

> **Daily Challenge 歷史註記（PM 2026-10-16）**：這個問題是 Boss 在 Issue #464 請 Agent 盤點效能時被發現的（IMP-1），
> Tech Lead 判定其實是**正確性問題**而非單純效能。PM 的產品面看法：每日挑戰是 DECISION-003 以來的留存主力，
> 分數／XP 算錯會直接傷害信任，所以 TASK-053 排 HIGH 是對的。

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
- **Update to Proposal 3 (2026-10-11, Boss, PR #434 merged — recorded by PM
  for consistency; the 2026-09-26 entry above is kept as history)**: the
  rebalancing card no longer treats real estate as a bucket. Buckets are now
  4 (TW Stock / US Stock / Fund / Cash) and the denominator is
  `liquidGrossAssetsTWD` (the same number as the page's 流動資產 headline;
  excludes real estate and liabilities). A target saved earlier with a
  `realEstate` bucket is converted once on load (liquid buckets keep their
  proportions and old total). Reason given by Boss: a house can't be bought
  or sold in the amounts a rebalance suggests. PM view: consistent with the
  product's intent; no requirement conflict. The 回落煞車 card's
  "占總資產 X%" (需求 11, A9) uses the same denominator.
- **Update to the holdings list 占比 (2026-10-14, Boss, PR #460 merged — recorded
  by PM; DECISION-027)**: the 占比 column of the holdings list (each group's
  header share and each holding's share) is now `市值 ÷ 流動總資產`
  (`summary.liquidGrossAssetsTWD` = TW stock + US stock + fund + cash, before
  subtracting debt) — the same denominator as 水位, 再平衡建議 and the 回落煞車
  card. Before, the denominator was every positive position **including real
  estate**, so a house shrank every other share (the PR's example: TW stocks
  worth 600 of 1,000 liquid assets showed 15% next to a 3,000 house). Real
  estate shows no share ("—"); liability shows a **negative** share of liquid
  assets; an info popover on the column header states the formula. PM read the
  diff (`positionShare.ts`, `PositionsTable.tsx`) and its tests; not run on the
  real page. Acceptance:
  - [x] A holdings list with TW stock 600 / US stock 200 / fund 100 / cash 100
        (liquid 1,000) plus a 3,000 house shows 60% / 20% / 10% / 10% — the
        house does not shrink them. (`PositionsTable.test.tsx` asserts exactly
        these group and row shares; PM read it, did not run it.)
  - [x] Real estate rows and the real-estate group show no share.
  - [x] A liability shows a negative share of liquid assets (test: a 500 debt
        shows −50.0%).
  - [ ] The sum of the four liquid group shares reads ≈100% on the real page.
        (Tech Lead's post-merge review, 2026-10-14, read the code and agrees
        from the formula, with no objection and no further fix needed; nobody
        has looked at real data — the sandbox can't reach production.)
  - **PM view**: consistent with Boss's #434 direction (a house can't be traded
    in the amounts a list share implies) — one denominator across the whole
    Portfolio page. Fact from the diff, not a defect: the per-holding 占比
    column is still hidden below the `sm` breakpoint (phones, unchanged); the
    group header share is not.

- **IG 限動圖（持股比例）— 補登（2026-10-14，Boss，PR #406 於 2026-10-03、#461
  於 2026-10-14 合併；DECISION-029）**。這個功能 #406 出貨時沒有記進需求，是 PM
  的疏漏，現補上：
  - **是什麼**：Portfolio 匯出選單的「IG 限動圖 (持股比例)」，在瀏覽器端畫一張
    1080×1920 PNG（無伺服器、無 AI 成本），手機優先走分享面板、否則下載。
    **只顯示百分比，不顯示任何金額**（#406 的設計，DECISION-029 把它定為產品原則）。
  - **#461 的變更**：原本只有台股、美股兩段；現在多了 **基金** 一段（用基金名稱，
    不用代號——代號對讀者沒意義），**現金** 只顯示一個數字（標題右上角，不列明細，
    裁掉下半部圖時仍看得到）。各段標題的占比改為「該段 ÷（台股＋美股＋基金＋現金）」；
    每一段內部的持股各自加總 100%。三段並存時改用緊湊版面（每段最多 5 列，其餘併入
    「其他」）；一至兩段維持原版面；只有一段但有現金時，那一段也會顯示占比（因為不再是 100%）。
  - **PM 讀 diff 的觀察（事實，不是缺陷）**：圖上的分母＝台股＋美股＋基金＋現金，
    與 DECISION-027 的「流動總資產」同一組資產；不動產、負債不出現在圖上。差別：
    限動圖只算**正市值**的部位，所以某檔部位市值為負或 0 時，圖上分母與頁面上的
    流動總資產可能不同——一般持股不會發生，PM 不當成問題。
  - Acceptance（讀 `storyAllocation.test.ts` 的測試名稱與 diff 確認有對應；**PM 沒有
    執行測試、沒有看過實際產出的圖**）：
    - [x] 圖上不出現任何金額，只有百分比（#406 設計；`storyImage.ts` 只畫 `pct`／`share`／`cashPct`）。
    - [x] 基金自成一段、以名稱呈現（測試「lists funds as their own section, named by fund name rather than code」）。
    - [x] 現金是單一數字、且納入每一段占比的分母（測試「adds cash as one figure and to every share's denominator」）。
    - [x] 不動產、負債、非正市值的部位不出現，同代號合併（測試「ignores real estate, liability and non-positive positions, and merges duplicate symbols」）。
    - [x] 三段並存時每段列數減少（測試「lists fewer rows per section when three sections share the image」）。
    - [ ] 三段＋現金同時存在時，圖在手機分享面板與 IG 裁切後仍清楚可讀。（需要實機；
          請 Boss 方便時看一眼，**不阻擋任何工作**。）
  - Out of Scope（現況）：不顯示金額、不顯示報酬率、不含不動產／負債、無自訂樣式或
    分享到 IG 以外平台的專屬版型。

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
  （2026-10-13 更新：Boss 當天合併 #450～#456 投入 Data Analysis 的 AI 提問，此假設對該功能不再適用，見本檔末「Data Analysis — AI 自然語言提問」與 DECISION-025；Slideshow／Job Finder 仍適用。）
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

### Status
**已實作並合併（TASK-029／PR #415）；驗收條件尚未逐條確認（2026-10-13 PM）。** 四條驗收都需要看 Azure Portal 或正式環境，PM 沙箱做不到，
所以**不勾選**。尚缺的是「第一份實際數據」：Tech Lead 與 PM 的沙箱都沒有 App Insights 存取，只有 Boss 能在 Azure Portal 看到；
上線約一週後的各頁面造訪／不重複使用者數，是功能去留與下一步排序的依據（非阻擋，建議 Boss 方便時貼一次數字，見 `boss/PM.md`）。

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

### Status
**第 1～4、7 項已交付並合併（TASK-030／031／032，PR #418／#420 等）；PM 於 2026-10-13 以讀程式碼方式做產品面複核，驗收條件除最後一條外皆有對應實作（未實機執行）。**
第 5、6 項（JobSearch 記憶、DataAnalysis／Connectors 引導，LOW）仍為 Future Work，不排程，等用量數據再決定（DECISION-010）。
複核依據見下方各條驗收的註記；PM 不能代替實機驗證，Boss 或 QA 若發現落差請回報。

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
- [x] 完成一輪 Cloze 或 Translation 後，結果頁列出答錯單字，且有一鍵加入複習與前往 `/review` 的入口；全對時不顯示空區塊。（PM 讀 `RoundResultScreen.tsx`：`wrongWords.length > 0` 才渲染、「全部加入單字庫」＋「前往複習」；未實機）
- [x] Conversation 摘要中的建議單字可點擊加入單字庫，並有成功／失敗回饋。（`ConversationSummary.tsx`：Chip `onClick`＋Snackbar；未實機）
- [x] PDF 解鎖輸入錯誤密碼時，畫面顯示明確錯誤訊息；成功時顯示完成提示。（`PDFUnlocker/index.tsx`：`Alert severity="error"`、下載檔名改為「原檔名-unlocked.pdf」，測試涵蓋；未實機）
- [x] 模擬單字查詢失敗時，Repository 顯示錯誤而非「沒有單字」警示。（`Repository/index.tsx`：`isError` 時顯示「無法載入你的單字庫」＋原因；未實機）
- [x] 未登入訪客開啟 Repository 或 Exam，先看到價值說明與登入按鈕，而非空白頁。（`auth/LoginGate.tsx`，測試斷言含「完全免費」；未實機）
- [ ] 手機寬度（≤ 400px）Exam 選單無橫向捲動。（**PM 無法以讀程式碼確認**：`Exam/index.tsx` 有 `flexWrap: 'wrap'`，但版面是否溢出必須在實機或瀏覽器縮窄視窗才看得出來——請 Boss 手機看一眼，或交 QA。）
- [x] 以上項目不新增 AI 呼叫或 UsageLimit 消耗。（Tech Lead 2026-10-13 以 PR 檔案清單核對：#418／#420／#422 只動前端、沒有新增後端端點或 `[UsageLimit]`；TASK-032 依其 Result 也是純前端。**但書**：Conversation 摘要的建議單字被點擊時會呼叫既有的匿名字典查詢，若該字尚未入庫會觸發一次字典 AI 生成——每個字全站只發生一次、設計上就是免費，成本不隨訪客量成長。）

### Priority
第 1 項 HIGH；第 2–4 項 MEDIUM；其餘 LOW（不全部標 HIGH）。

### Scope
#### In Scope
- 上述 7 項的使用者可見行為

#### Out of Scope
- 推播／Email、分享機制（DECISION-004）、新增金流或點數概念（DECISION-006）、下架任何應用（需先有 App Insights 數據，並由 Boss 決定）

#### Future Consideration
- Portal 顯示投資組合變化（需輕量後端端點，見 PROJECT_STATUS 2026-10-04）
- JobSearch 儲存職缺／提醒；~~DataAnalysis 匿名結果匯出~~（2026-10-13 Boss 以 #457 交付「明細 CSV 匯出」，預設來源匿名可用；見「Data Analysis — AI 自然語言提問」需求 8 與 DECISION-026）
- #407 SEO：單字頁內部連結（相關單字、每日單字）以加速收錄，Tech Lead 已提出、待 PM 排序——PM 建議列 MEDIUM，成長面唯一的低成本槓桿

### Constraints
- 修改 DataAnalysisLibrary 前須先與 Boss 確認範圍（DECISION-009）。

### Open Questions
- 無需 Boss 決定。第 3 項若 Tech Lead 認為與既有 RequireAuth 衝突，請回報。


## Requirement: 0050 + 00631L「回落煞車」加減碼提醒（Issue #431）

### Status
**v4.3（2026-10-11，Boss 看過正式環境後的兩點修正，DECISION-021）**：(1) **00685L 與 00631L 同性質，兩檔歸成同一個「正2 槓桿 ETF」桶，合併計算加碼／減碼／不動**；
(2) **NTSD 納入曝險**（Boss：「我其實水位是想要含 NTSD 的」）——**推翻 DECISION-018 的排除**：NTSD 照系統的 1.5 倍計入分子與分母，卡片曝險與右上角「水位」恢復為同一個數字，對帳行不再需要；
NTSD 仍**不給建議**（屬「其他持股」，一律「不動」，DECISION-020 不變）。PM 的理解與小決定見 A27～A29（Boss 可推翻）。**目前沒有待 Boss 回覆的問題。**
下方 v4.1／v4.2 與需求 12 內文中「NTSD 排除、對帳行」的敘述**已被 v4.3 取代**（原文保留供追溯，**以 v4.3 為準**）。
**v4.2（2026-10-11，Boss 回覆 Q10：「B＋C」，DECISION-020）**：**所有持股（個股、基金、其他倍數商品）都計入整體曝險，但建議只針對 0050、00631L 和現金；其他持股一律「不動」，由使用者自己決定。**
Q10 結案，**目前沒有待 Boss 回覆的問題**。NTSD 仍依 DECISION-018 排除（PM 的理解，見 A24）。
**v4.1（2026-10-11，Boss 後續兩則指示）**：(1) **NTSD 屬全球配置，與「集中在台灣」的配置不同 → 排除於策略範圍**（DECISION-018），因此「卡片曝險與水位一致」改為「與水位對得起來」（有對帳行，見 12a）；
(2) **暫不處理 R1 合規**（Boss：小型 web app、幾乎還沒有使用者；DECISION-019），開放全部使用者前只剩需求 9 的觀察。Q10 因 (1) 改寫為「策略範圍與防守處理」。
**v4（2026-10-11，Boss 正式環境試用回饋，04:44 UTC）**：Boss 要求把**整個流動資產**納入曝險判斷，使卡片曝險與頁面右上角「水位」一致（DECISION-017），
**推翻 Q6（A＋B）**。新增需求 12、風險 R8、Q10、假設 A18～A26；**需求 1 的適用範圍、需求 11、假設 A1 因此被取代**（原文保留供追溯，**以需求 12 為準**）。
訊號、參數、風險等級、觸發規則不變。Boss 要的「各桶加減碼」中，唯一需要 Boss 決定的防守時其他部位怎麼處理（Q10）已於 v4.2 結案；其餘由 PM 決定、Boss 可推翻。
**v3（2026-10-11，再吸收 Boss 對 Q5～Q7 的回覆；v2 為 Q1～Q4）**。訊號、配置、提醒規則為 Boss 指定且已固定（DECISION-013）；
Q1～Q4 的回覆與分期已記錄為 DECISION-014；Q5／Q6 結案與 Q7 的方向已記錄為 DECISION-015。
**v3 變更摘要**：Q5 結案（隔日成交欄期間＝1997–2026）；Q6 結案（先做 A＋B，新增需求 11「策略部位占總資產比例」）；
需求 10（歷史相似區段比較圖）與 Boss 新增的「過去 5 年同期間走勢圖」**已移出本需求**，改為文末獨立需求「Stock Intelligence 大盤走勢比較圖」（Boss 02:38 UTC 決定放 `/finance`、與個人資產無關，DECISION-016）；
回測資料已由 Boss 放進 repo `回測數據/tw_backtest/`（見「回測來源」）。
**進度（2026-10-11，v4 更新）**：後端 PR #433（TASK-036）、前端 PR #432（TASK-037）、驗收測試 PR #435（TASK-039）、卡片文案更正與策略部位占比 PR #436（TASK-042）**皆已由 Boss 合併**；
Boss 已在正式環境試用，並因此提出需求 12。TASK-038（第二階段）、041／040／043（重新整理、可觀測性）、044／045（同期間圖）進行中或待開工，以 `.agent/tasks/` 為準。功能開關為「預設僅 admin 可見」。
PM 產品面複核（讀 `drawdownBrakeCopy.ts`，未執行）：卡片文案的回測數字已是 Boss 更正值（隔日成交：保守 12.1／−27、平衡 15.3／−37、積極 18.4／−45；2011 年平衡 −28%、0050 −18%），
資料限制註記已涵蓋「2014 年以前的 0050 與 00631L 為模擬、1997–2002 年股利為估計」。原「PR #432 文案是更正前數字」的不一致已解除。
**Boss 決定（Q1）：目前僅 admin 可見，之後再慢慢開放；是否開放全部使用者，要先看「可觀測性」數據（見需求 9）。**
Boss 的理由是擔心計算量，不是合規；**合規（R1）仍未查證、仍是開放給一般使用者前的獨立前置條件**。**【2026-10-11 更新：Boss 決定暫不處理 R1，見 DECISION-019；開放前僅剩需求 9 的觀察。】**
**以下數字與規則以 Boss 2026-10-11 更正為準（見「Boss 更正的回測數字」）；規則原文與參考實作以 Issue #431 為準；本文與 #431 不一致時以 #431 為準，
但 #431 內文的 §2 績效表已被 Boss 在留言中更正，內文尚未編輯 → 以本文更正值為準。**

### Goal
讓採用「0050 + 00631L + 現金」策略的台股投資人，依固定、可重現的大盤訊號（回落煞車）與自選風險等級，
知道「現在該加碼、減碼還是不動」以及具體調整股數，不必自己每天算均線與回落幅度。
屬 Wealth Portfolio（主力應用）的延伸，目的為提供每日回來查看的理由（DECISION-003 留存）。

### User Story
As a 使用 0050 + 00631L + 現金策略的台股投資人,
I want 系統依大盤訊號與我目前的持股，告訴我要加碼、減碼或不動，並給出調整股數,
so that 我能照紀律執行策略，而不是憑情緒或自己手算。

### Requirements
**1. 適用範圍（固定）**
- 僅限台股大盤（證交所「發行量加權股價報酬指數」）、0050、00631L；不得套用個股或其他槓桿 ETF。
- 計算範圍僅限使用者的「策略部位」：0050 + 00631L + 使用者指定給此策略的現金（見假設 A1）。
  **【v4（DECISION-017）：此條的「計算範圍」被需求 12 取代——改為整個流動資產；「適用的標的」仍限大盤訊號與回測過的 0050／00631L 為核心，見需求 12】**

**2. 大盤訊號（固定參數，不得調整）**
- `DD_TRIGGER = 0.08`、`REENTER_SMA = 60`、`LOOKBACK_HIGH = 252`。
- 每日：`up = close > sma60`（資料不足 60 日為 False）；`down = close/high252 − 1 < −0.08`。
- `up` → OFFENSE（同日 up 與 down 皆成立以 up 為準）；否則 `down` → DEFENSE；兩者皆否 → 維持前一日狀態；初始 OFFENSE。
- 每次都以完整歷史（至少 2 年）從頭重算，不得只依賴上一筆已存狀態。

**3. 風險等級（使用者選擇，預設「平衡」）**
| 等級 | 進攻 E | 防守 E | 最低現金 k |
|---|---|---|---|
| 保守 | 1.0 | 0 | 0.2 |
| **平衡（預設）** | 1.4 | 0 | 0.2 |
| 積極 | 1.8 | 0 | 0.1 |
- E = 0050 權重 + 2 × 00631L 權重。進階選項：防守 E 可改 0.3。
- 不得提供：防守 E ≥ 0.5、不煞車的固定槓桿（DECISION-013）。

**4. 目標配置**
- `inv = 1 − k`；`E ≤ inv` → 0050 = E、00631L = 0、現金 = 1 − E；否則 E 截斷為 `2·inv`，0050 = `2·inv − E`、00631L = `E − inv`、現金 = k。
- 發生截斷時，介面提示「現金比例過高，無法達到此曝險」，並以截斷後的 E 計算。

**5. 提醒與調整建議**
- 狀態切換（OFFENSE ↔ DEFENSE）→ 一定產生調整建議到新目標配置；否則 `|E_actual − E_target| > 0.10` → 再平衡建議；其餘 → 「不動」。
- 分類：「減碼」＝進攻轉防守或 `E_actual > E_target + 0.10`；「加碼」＝防守轉進攻或 `E_actual < E_target − 0.10`；其餘「不動」。
- 具體建議：各標的目標市值 − 目前市值，除以最新收盤價，四捨五入到整數股（零股可）；先賣後買。
- 使用者切換日未調整時，之後只要偏離仍 > 0.10，提醒持續出現，直到持股與目標一致。
- 介面文案範例（減碼／加碼／不動／再平衡）以 #431 §10 為準。

**6. 執行時機與資料狀態**
- 休市日不執行；資料未更新時沿用前一日狀態並標示「資料未更新」。
- （第二階段）13:15–13:20 以盤中預估值先算「預估訊號」並提醒，使收盤集合競價（13:25–13:30）可下單；
  收盤後以正式數據再確認，不一致時以收盤後為準並通知「訊號已更正」。
- **提醒只在站內顯示（Boss 2026-10-11 Q2）**：不做 Email；也不做推播（Boss 只要求站內）。第二階段原提議的「站外提醒」因此移出範圍。
  代價（PM 已向 Boss 說明、Boss 接受）：13:15 的盤中提醒只對「當時正開著網站」的使用者有效。

**7. 揭露與限制（固定）**
- 所有相關畫面常駐顯示：「依歷史回測規則產生的訊號，過去績效不代表未來，不構成投資建議。」（不可關閉）；並註記模擬／估計資料（見下一條與 A7）。
- 資料限制註記（Boss 2026-10-11 更正）：**2014 年以前的 0050 與 00631L 是用報酬指數模擬的；1997–2002 年的股利是估計值**。
  （#431 §10 原附註只寫「2014 年以前的 00631L 績效為模擬值」，範圍較窄；見 A7。）
- 風險等級說明須含三點（#431 §10）：盤整年可能比 0050 虧更多（例：**2011 年平衡等級 −28%，0050 是 −18%**——Boss 2026-10-11 由 −29% 更正）；
  00631L 曾單日跌 20%、煞車於收盤才判斷擋不住急跌；平均每年約觸發 3 次進出、約 29% 時間防守。
- 顯示任何回測績效數字時，須同時顯示其期間與最大回撤；不得只突顯單一最高報酬欄位（R3，Boss 2026-10-11 同意）。
- 顯示績效須與提醒時點一致：第一階段只在收盤後提醒，等同「隔日成交」，須使用「隔日成交版本」數字（**平衡 15.3% / −37%**，Boss 更正值），
  而非收盤集合競價版本（**17.2% / −32%**）（R4，Boss 2026-10-11 同意）。
- 顯示所依據的持股資料日期；持股快照過舊時顯示「持股資料可能已過期，請先更新持股」。過期天數門檻 7 天（Boss 於 Q3 確認 2026-10-11）。

**8. 資料新鮮度與手動重新整理（Boss 2026-10-11 新增）**
- 背景（Boss）：系統有 cache，當日指數／股價可能被 cache 住，13:15 不一定拿到最新值。
- 此功能的畫面須顯示「資料時間」（訊號所依據的指數日期／盤中預估的取得時間、價格的取得時間），讓使用者看得出資料有多新。
- 畫面提供「重新整理資料」操作：使用者按下後，此功能所用的資料（報酬指數／盤中預估／最新價格）須重新向來源取得，不使用已過期的 cache；
  完成後「資料時間」更新；來源暫時取不到時，保留原資料並標示「資料未更新」，不得顯示成功更新。
- 「重新整理」不得改變固定規則與參數，只影響資料新鮮度。
- 需防濫用／保護來源（證交所請求間隔 ≥ 2 秒，#431 §6）：同一使用者短時間重複按下須有節流（冷卻時間由 Tech Lead 提議）。
- 第二階段（13:15 盤中預估）**必須**具備此機制；第一階段也有價值（例如收盤後指數公布時間不確定，「資料未更新」時可自行重試），
  由 Tech Lead 評估是否以不阻擋 PR #433／#432 合併的後續小任務補上。

**9. 可觀測性——決定是否開放全部使用者的依據（Boss 2026-10-11 新增）**
- 背景（Boss）：擔心此功能計算量過大，開放前需要以紀錄（log／指標）判斷能否開放給所有人。
- Boss 須能在不讀程式碼的情況下查到至少：此功能每日被查看／呼叫次數與不重複使用者數、後端計算與回應時間、
  向證交所（及其他來源）的請求次數與失敗次數、cache 命中與手動重新整理次數、「資料未更新」出現次數。
  具體指標、紀錄管道（DECISION-010 的 Application Insights 是既有選項）與開放門檻的建議值由 Tech Lead 提議，Boss 決定。
- 此需求**不阻擋 PR #433／#432 合併**，但須在把 `Features:DrawdownBrake:Audience` 改為 `All` 之前完成並累積足夠觀察期。
- 觀測資料不得包含使用者的持股、股數或金額等個人財務內容（隱私；PM 的產品面要求，實作由 Tech Lead 決定）。

**10. 歷史相似區段比較圖——【已移出本需求】** Boss 於 2026-10-11 02:38 UTC 更正：這功能與個人資產無關，改放 `/finance` 的 Stock Intelligence（DECISION-016）。
完整規格（Boss 方向、假設 A10～A13、風險 R7、Q8）已移至下方獨立需求「**Stock Intelligence 大盤走勢比較圖**」（文末），ID 沿用。回落煞車卡片內**不做**這張圖。

**11. 策略部位占總資產比例（Boss 2026-10-11，Q6 選 B）**
- 回落煞車卡片顯示「此策略部位占總資產 X%」，**純資訊**，不影響訊號、目標配置、提醒與建議股數。
- 策略部位＝0050 市值＋00631L 市值＋策略現金（A1）；「總資產」的定義見 A9。
- Boss 的說明：網站本來就顯示目前曝險，其他持股的曝險＝總曝險扣掉 0050 與 00631L 的曝險，Boss 自行判讀；此功能的目標只是讓 0050＋00631L 的績效有效成長。
  因此**不**把其他持股納入加減碼判斷（Q6 的 C 不做；DECISION-013 的適用範圍不變）。
- 分母為 0 或資料不足時不顯示此行（不顯示 NaN／無限大）。
- **【v4：被需求 12 取代】** 策略範圍改為整個流動資產後，「策略部位占總資產 X%」不再有意義。已實作的這一行是否改為「核心（0050＋00631L）占流動資產 X%」或移除，由 Tech Lead 視成本決定（PM 預設：不需要）。

**12. 以整個流動資產判斷曝險、分桶顯示加減碼（Boss 2026-10-11 04:44 UTC，試用後新增；DECISION-017；取代需求 1 的計算範圍、需求 11、A1、Q6）**
- 背景（Boss）：在正式環境試用後，卡片只算 0050＋00631L＋現金的曝險，和頁面右上角「流動資產水位」又有差距；應把目前流動資產全部納進來算。
  其他一倍商品歸成一個「其他一倍商品」；美股 1.5 倍商品（如 NTSD）要加減碼比較麻煩，問「你們有好的建議嗎」；最後顯示 0050、00631L、現金、其他一倍商品、NTSD 各要加減碼多少或不動。
- **PM 讀程式確認的事實（未執行）**：
  - 頁面「水位」＝市場曝險 ÷ 流動總資產（`exposureOfLiquidAssetsPct`，分母不扣負債）；市場曝險＝各持股市值 × 系統的曝險倍數（`exposureFactor`）。
  - 倍數規則已存在於後端 `PortfolioExposure`：一般股票／基金 1；名稱含「正2／槓桿」或台股代碼尾碼 L → 2；債券／貨幣型 → 0（視同現金）；美股 NTSD → 1.5
    （程式註記：每 $1 含 $0.90 美股＋$0.60 MSCI EAFE 期貨，皆為股票曝險；PM 未驗證基金現況）；其他美股槓桿／反向依名稱或個別代碼。
  - 現行卡片的曝險是另算的：只含 0050＋00631L，分母＝0050＋00631L＋使用者手動輸入的「策略現金」（`strategyCashTWD`），因此與「水位」必然對不上。
- **12a 曝險與「水位」對得起來（可立即做）**：卡片的「目前有效曝險 E」改為整個流動資產：
  `E_actual = Σ(持股市值 × 曝險倍數) ÷ 策略範圍資產`，策略範圍資產＝流動總資產（分母沿用 A9）。
  **【v4.3】現在沒有任何被排除的持股（NTSD 已納入，DECISION-021），因此卡片的 E 與頁面右上角「水位」永遠是同一個數字**（水位 % ÷ 100，差異僅限四捨五入）。
  ~~有被排除持股時須顯示對帳行~~（v4.1／DECISION-018 的設計）：**v4.3 起不再需要**，畫面不應再出現「不含 NTSD Y」「策略範圍 Z」這類字樣；
  若 Tech Lead 為日後保留「排除機制」，預設排除清單為空，且「沒有被排除持股時不出現對帳行」的規則不變（A21、A24）。
  訊號、風險等級的目標 E、最低現金 k、觸發規則（狀態切換；|E_actual − E_target| > 0.10）都不變（DECISION-013）。
- **12b 分桶顯示（可立即做）**：卡片列出下列各列，每列顯示目前市值、目前曝險貢獻、目標、建議（加碼／減碼／不動＋金額；有股數概念者含股數，沿用「先賣後買」與整數股）：
  1. 0050　2. **正2 槓桿 ETF（00631L＋00685L，v4.3）**　3. 其他一倍商品（其餘曝險倍數為 1 的商品：其他台股、美股、基金等，合併一列；名稱為 PM 暫定，Boss 說「其他原型一倍商品之類的」，可改。含個股、美股與基金；**此列只顯示市值與曝險貢獻，建議一律「不動」**，DECISION-020）
  4. 現金類（現金，加上曝險倍數 0 的債券／貨幣型——系統本來就視同現金）
  另：若持有其他倍數的商品（例如 **NTSD（1.5 倍）**、美股 3 倍、反向），單獨一列「其他倍數商品」，處理同第 3 列（只顯示、一律「不動」）。**【v4.3】NTSD 計入此列與整體曝險**（DECISION-021），不再只出現在對帳行。
  各列市值合計＝策略範圍資產（＝流動總資產）。
  **正2 槓桿 ETF 桶（v4.3，Boss：「00685L 也是跟 00631L 同性質，兩個歸一類一起算要加、減碼、不動就好」）**：
  - 00631L 與 00685L 合為**一列**，目前市值＝兩檔市值合計；**加碼／減碼／不動只給整組的一個結論與一個金額**（不分別給兩檔各自的買賣方向），金額口徑與其他列相同。曝險倍數兩檔皆為 2。
  - 原本 composition／等級表中「00631L」的角色，改由這個桶承擔；訊號、目標 E、最低現金 k、觸發規則、固定參數**一律不變**（DECISION-013）。
  - 股數怎麼呈現（PM 假設 A28，Boss 可改）：只持有其中一檔 → 股數以該檔計；兩檔都持有 → 加碼／減碼依兩檔目前市值比例分攤，各自整數股；兩檔都沒有而需加碼 → 買 00631L（回測標的）。
    先賣後買、整數股、分割防護（A8）的規則沿用，套用到每一檔。
- **12c 各桶的目標與建議（Boss 2026-10-11 決定「B＋C」，DECISION-020）**：
  - 策略只規範「**整體**有效曝險 E」，回測也只有這件事。**所有持股（個股、基金、其他倍數商品，v4.3 起含 NTSD）都計入整體曝險**（C），
    但**建議只針對 0050、正2 槓桿 ETF（00631L＋00685L）、現金（合稱「核心」）**；其他一倍商品、其他倍數商品（含 NTSD；合稱「其他持股」）**一律「不動」，由使用者自己決定**（B）——進攻、防守、再平衡都一樣，
    不會出現「賣出／買進某檔個股或基金」的建議，策略也不替它們訂目標比例。
  - 核心的目標：讓**整體**曝險盡量接近目標 E，也就是核心要補足（或削減）的是「目標 E − 其他持股已貢獻的曝險」；
    沿用 #431 §3 的 composition 與最低現金 k，在核心可用資金（0050＋00631L＋現金類）上套用；目標在保留最低現金下不可行時截斷並提示，同原規則（算法由 Tech Lead 提議）。
  - **其他持股單獨就已達到或超過目標 E 時**（例如防守時目標 0、但持有大量個股／基金）：核心降到最低（現金類），**畫面如實標示**「整體曝險 X 倍，高於目標 Y 倍；差額來自您自行決定的其他持股」，不假裝已達到防守。
  - **核心已無可調整、卻因其他持股而偏離容許帶（> 0.10）時**：顯示「不動」並附上述說明，不得出現無法執行的建議（延伸 A3）。
  - 沒有 0050／00631L 的使用者：核心的買進建議照 composition 算（同 A4）。
- **NTSD（v4.3：納入曝險、不給建議，DECISION-021；取代 v4.1 的「排除」DECISION-018）**：
  Boss 在看過正式環境後說：「NTSD 算是不同的（不屬正2 那一類），我看目前的實作寫『不含 NTSD NT$75,771』好像怪怪的，我其實水位是想要含 NTSD 的」→
  **NTSD 照系統的 1.5 倍計入整體曝險的分子與分母**，卡片 E 與右上角「水位」一致，不再有「不含 NTSD」的對帳行；
  NTSD **不併入正2 桶、不給任何加減碼建議**，歸在「其他倍數商品」列，一律「不動（由您自行決定）」（DECISION-020 本來就如此）。
  這個調整的結果是：NTSD 與其他個股、基金一視同仁——計入曝險、不給建議。PM 先前排除 NTSD 的顧慮（用台股訊號對全球配置下指令）已被「其他持股一律不動」解決，所以排除不再有實益。
  事實依據（程式註記，PM 未驗證基金現況）：NTSD 是 WisdomTree Efficient U.S. Plus International Equity Fund，每 $1 含 $0.90 美股＋$0.60 MSCI EAFE 期貨，確為全球股票曝險，與台股大盤訊號無直接對應——計入整體曝險會讓核心的目標偏低（核心要補的曝險＝目標 E − 其他持股已貢獻的曝險），這是 Boss 要的「水位一致」的自然結果。
  （歷史，已被取代）v4.1：Boss 曾說「NTSD 屬於全球配置，跟全部集中在台灣配置不太一樣」→ 排除於策略範圍、只在對帳行顯示市值（DECISION-018）。
- **績效揭露（R8）**：卡片上的回測績效（保守／平衡／積極各欄）是「0050＋00631L＋現金」的回測，**不是整個流動資產的績效**。
  績效區塊須標明「依 0050＋00631L＋現金回測」，不得暗示適用整個流動資產；**【v4.3】另須註明「00685L 視同 00631L（您的判斷），未單獨回測」**（PM 未驗證兩檔追蹤標的的差異，採 Boss 的判斷）；對其他持股須提醒「其他持股未經回測，僅計入整體曝險、不給建議，由您自行決定」（字句由 PM 決定，Boss 可改）。固定警語（DECISION-013）不變。
- **取代與影響**：「策略現金」的手動輸入不再需要（現金類＝流動資產中的實際現金，A19）；需求 11 見上；已合併的卡片（PR #432／#436）須調整，由 Tech Lead 評估。
  持有 0050／00631L 時的「快照早於分割日就不顯示買賣股數」防護（A8）維持不變。

### Acceptance Criteria
- [ ] 以 2003-01 ~ 2026-10-08 報酬指數重算，2026-10-08 收盤狀態為 OFFENSE，距 252 日高約 −1%（Boss 提供預期值，PM 未獨立驗證）。
- [ ] 目標配置：E=1.4,k=0.2 → 0050 20% / 00631L 60% / 現金 20%；E=1.0,k=0.2 → 60% / 20% / 20%；E=1.8,k=0.2 → 截斷為 1.6，0% / 80% / 20% 並顯示提示；E=0 → 現金 100%。
- [ ] 狀態機：同日 up 與 down 皆成立 → OFFENSE；兩者皆否 → 維持前一日；sma 資料不足 60 日 → up 為 False；休市日／資料未更新 → 沿用前日並標示。
- [ ] 狀態沒變且偏離 ≤ 0.10 → 「不動」；狀態切換 → 一定有調整建議；偏離 > 0.10 → 再平衡建議。
- [ ] 建議列出各標的買賣股數（整數），賣出排在買進之前；所有建議股數皆為 0 時顯示「不動」（PM 假設 A3）。
- [ ] 預設風險等級為「平衡」；選單中沒有防守 E ≥ 0.5 或固定槓桿選項。
- [ ] 固定警語在此功能所有畫面常駐可見；資料限制註記存在，且涵蓋「2014 年以前的 0050／00631L 為模擬值、1997–2002 年股利為估計值」（需求 7、A7）。
- [ ] 畫面上的回測數字（隔日成交版本：保守 12.1% / −27%、平衡 15.3% / −37%、積極 18.4% / −45%；風險說明「2011 年平衡 −28%、0050 −18%」）與下方「Boss 更正的回測數字」逐項一致，並同時標示期間與最大回撤。
- [ ] 畫面顯示持股資料日期；快照超過 7 天時出現過期提示。
- [ ] 畫面顯示資料時間；按「重新整理資料」後資料時間更新，來源取不到時顯示「資料未更新」而非假裝成功；短時間連按有節流（需求 8；第二階段必備）。
- [ ] 以 admin 使用此功能後，Boss 能查到需求 9 列出的指標，且紀錄中沒有持股、股數、金額（需求 9；開放給一般使用者之前必備）。
- [ ] 此功能不呼叫 AI、不消耗 UsageLimit。
- [ ] 手機寬度（≤ 400px）版面無橫向捲動。
- [ ] 一般使用者看不到此功能（Boss 2026-10-11 決定：目前僅 admin 可見，之後再慢慢開放；`Audience=All` 須待需求 9 的觀察結果後，由 Boss 明確指示；R1 合規 Boss 決定暫不處理，見 DECISION-019）。
- [ ] （第二階段）預估訊號與收盤正式訊號不一致時，使用者看到「訊號已更正」（站內顯示）。
- [ ] 卡片顯示「此策略部位占總資產 X%」，分母與 A9 定義一致；改變其他（非 0050／00631L）持股的金額，不改變訊號、目標配置、提醒與建議股數；分母為 0 時不顯示此行（需求 11）。
- [ ] 畫面上標示隔日成交版本績效時，期間寫「1997–2026」（Q5 結案；實際序列起點為 1997-07，終點為資料截止日）。
（原「需求 10 草案驗收」已隨需求 10 移至文末的獨立需求。）

**需求 12 驗收（v4；取代上方「策略部位占總資產 X%」那一項與以「策略現金」為前提的條目）**
- [ ] **（v4.3 取代原「持有 NTSD 時不計入、有對帳行」條目）** 卡片的「目前有效曝險」與頁面右上角「水位」一致（水位 % ÷ 100，差異僅限四捨五入），**包含持有 NTSD 的使用者**；NTSD 以 1.5 倍計入分子與分母；畫面上沒有「不含 NTSD」的對帳行或類似字樣（12a、DECISION-021）。
- [ ] **（v4.3 取代原「NTSD 不出現在任何一列」條目）** 卡片分桶列出 0050、**正2 槓桿 ETF（00631L＋00685L）**、其他一倍商品、現金類（有其他倍數商品——**含 NTSD**——時另列「其他倍數商品」）；各列市值合計＝流動總資產；每列有建議（加碼／減碼／不動）與金額；NTSD 出現在「其他倍數商品」列、**一律「不動」、不產生任何建議**（12b、DECISION-020／021）。
- [ ] **（v4.3 新增）** 只持有 00685L（不持有 00631L）的使用者，正2 桶的市值、曝險與建議金額都正確計入；兩檔都持有時只顯示**一個**整組結論與金額（不出現一檔加碼、另一檔減碼的相反建議）；股數依 A28 呈現且為整數、先賣後買；兩檔都持有時，兩檔金額相加即為正2 桶的目前市值。
- [ ] **（v4.3 新增）** 00685L 的曝險倍數為 2（與 00631L 相同），改變 00685L 的金額會改變目前曝險與核心的建議，方式與改變 00631L 相同；績效區塊註明「00685L 視同 00631L，未單獨回測」（R8）。
- [ ] 訊號、風險等級的目標 E、最低現金、觸發規則與固定參數，與需求 2～4 一致，沒有因這次改動而變動（DECISION-013）。
- [ ] 進攻且 |E_actual − E_target| ≤ 0.10 時，所有列顯示「不動」；其他持股（其他一倍商品、其他倍數商品）在進攻、防守、再平衡**任何情況下**都顯示「不動」、不出現買賣建議（12c、DECISION-020）。
- [ ] 持有其他個股或基金時，它們計入整體曝險（與水位同口徑；v4.3 起 NTSD 也計入），改變其金額會改變目前曝險與核心的建議；但它們本身不產生任何建議（12c）。
- [ ] 防守時，若其他持股的曝險單獨就達到或超過目標：核心降到最低（現金類），畫面標示「整體曝險 X 倍，高於目標 Y 倍；差額來自您自行決定的其他持股」；若因此沒有可執行的動作，顯示「不動」而非無法執行的建議（12c）。
- [ ] 回測績效區塊標明「依 0050＋00631L＋現金回測」；其他持股有「未經回測、僅計入曝險、不給建議」的提醒；固定警語仍常駐（R8）。
- [ ] 不再需要輸入「策略現金」；現金類取自流動資產（A19）。
- [ ] 改變其他一倍商品的金額，會改變目前曝險與建議（與舊需求 11「其他持股不影響建議」的行為相反）；**v4.3：改變 NTSD 的金額也會改變目前曝險與核心的建議**（與其他持股一致），但 NTSD 本身仍不產生建議。
- [ ] 沒有任何其他一倍商品／其他倍數商品（含 NTSD）的使用者，行為與 v3 的差別只在「分母與現金」，不出現空列、空對帳行或 NaN（A21）。

### Priority
- 第一階段（收盤後正式訊號 + 目標配置 + 提醒與股數，站內顯示）：**HIGH**（Boss 明確提出、規格完整、屬主力應用 Wealth Portfolio）。PR #433／#432 已完成待合併。
- 第二階段（盤中預估訊號 + 「訊號已更正」，皆站內顯示）+ 手動重新整理（需求 8）：**MEDIUM**。Boss 已同意分期；站外提醒已移出範圍。
- 可觀測性（需求 9）：**MEDIUM**；不阻擋第一階段合併，但是開放全部使用者的前置條件。
- 策略部位占總資產比例（需求 11）：**LOW**（純資訊、Boss 已定案；體量小，可併入下一次碰到回落煞車卡片的任務，由 Tech Lead 決定排程）。**v4：已被需求 12 取代**，不再獨立排程。
- 整個流動資產的曝險判斷（需求 12）：12a（曝險與水位一致）＋12b（分桶顯示）**HIGH**——Boss 實際使用後直接提出、現行卡片數字與他每天看的水位對不上，是他自己用不下去的原因，且不依賴 Q10；
  12c 核心目標與「其他持股一律不動」的規則 **MEDIUM**——Boss 已決定（B＋C，DECISION-020），與 12a／12b 一併做；不再待決。
- **v4.3 調整（DECISION-021）：NTSD 納入曝險＋00685L 併入正2 桶 → HIGH**——TASK-046 已合併並部署，Boss 在正式環境看到「不含 NTSD NT$75,771」的對帳行與他要的水位不一致（又一次數字對不上），
  且他持有 00685L 卻沒有被歸進正2 桶，是同一類「用不下去」的問題；範圍小（分桶歸類與一條排除規則），建議盡快做。
- 歷史相似區段比較圖（原需求 10）：已移出，優先序見文末獨立需求。
- 對一般使用者開放：**暫不開放（Boss 決定）**；前置條件＝需求 9 的觀察結果（Boss 2026-10-11 決定暫不處理 R1 合規，不再列為前置條件，DECISION-019）。
- 分期已由 Boss 同意（DECISION-014）。

### Scope
#### In Scope
- 訊號計算、三級風險等級、目標配置、加減碼提醒與股數、固定警語、資料未更新處理、資料時間與手動重新整理、可觀測性（開放前）、策略部位占總資產比例（資訊顯示；v4 已被需求 12 取代）。
- **（v4，需求 12）整個流動資產納入曝險判斷、分桶顯示各桶加減碼或不動**。
#### Out of Scope
- 個股或其他槓桿 ETF；自動下單／券商串接；使用者自訂訊號參數；回測引擎；AI 生成內容；
  Email／推播等站外提醒（Boss 2026-10-11 Q2：站內顯示就好、不要 email）；
  Boss 明確禁止項目（防守 E ≥ 0.5、固定槓桿、突破新高減碼、深跌提前加碼、階梯式加碼，見 DECISION-013）；
  ~~把其他持股納入加減碼判斷（Boss 2026-10-11 Q6：先做 A＋B 就好，C 不做；DECISION-015）~~——**v4 已被 Boss 推翻**（DECISION-017，需求 12）：整個流動資產納入判斷，現列為 In Scope。
  仍 Out of Scope：替其他持股訂「目標比例」、建議買賣其他持股（個股、基金，Boss 決定一律不動，DECISION-020）；其他標的的回測。
#### Future Consideration
- 歷史訊號圖／回測展示頁；其他標的（須 Boss 先重新回測並決定）。歷史相似區段比較圖已移出本需求（見文末獨立需求）。
- ~~若 Boss 日後想把其他持股納入判斷（Q6 的 C）：須 Boss 先定義其他持股如何換算成有效曝險 E~~——**v4：Boss 已提出（DECISION-017）**；換算沿用系統現有的 `exposureFactor`（與頁面「水位」同一套倍數），不需要 Boss 另行定義。該結果仍沒有回測支持（R8）。
- Portfolio 全域股票分割處理（目前為技術債）：影響所有在分割日前建立快照的 0050／00631L 持有人之淨值走勢與報酬率。PM 暫不建議排入（Q1 未決、實際受影響者主要是 Boss 本人），除非 Boss 認為有其他使用者受影響；屆時另立需求。
- 完全沒有持股的使用者進入此功能的入口（見 A4 補充）：Q1 若開放給一般使用者再評估。

### Constraints
- 成本紀律（DECISION-003）：不使用 AI、不新增付費服務。
- Boss 指定資料規則：0050／00631L 價格不得用 Yahoo Finance（分割調整錯誤）；證交所請求間隔 ≥ 2 秒；訊號只用報酬指數。其餘資料來源與實作細節見 #431 §6，由 Tech Lead 決定。
- 不得修改 `.agents/` 或由 Agent 自行調整固定參數。

### Boss 更正的回測數字（2026-10-11，取代 #431 內文 §2 的績效表）
Boss 重新驗證時發現原表誤用「不保留現金（k=0）」的結果，更正如下（依規格配置 k=0.2 / 0.1 重算）。**PM 未獨立驗證**；#431 內文尚未編輯，本表為準。

| 等級 | 回測年化 / 最大回撤（1997–2026） | 隔日成交 | 實際資料 2014-11~2026-10 |
|---|---|---|---|
| 保守（1.0 / 0, k=0.2） | 13.5% / −24% | 12.1% / −27% | 19.9% / −19% |
| 平衡（1.4 / 0, k=0.2） | 17.2% / −32% | 15.3% / −37% | 26.0% / −26% |
| 積極（1.8 / 0, k=0.1） | 20.9% / −40% | 18.4% / −45% | 32.7% / −33% |
| 0050 買進持有 | 9.6% / −65% | — | 22.2% / −34% |

其他更正：平衡等級＋防守 E=0.3 → **16.1% / −36%**（原 16.6%）；固定 2 倍不煞車最大回撤 → **−93%**（原 −92%）；2011 年平衡等級 **−28%**、0050 −18%（原 −29%）。
驗收測試期望值不變（Boss 以腳本確認：2026-10-08 為 OFFENSE、距 252 日高 −1.0%；四個 `composition` 案例正確）。

**回測來源（Q4 已回覆）**：Boss 本機資料夾 `E:\Users\jacky\Desktop\tw_backtest`（Agent 無法存取）。重現指令 `python reproduce_spec.py`，參考輸出 `results/reproduce_spec.txt`（資料截至 2026-10-08；Python 3.13.5、pandas 3.0.6、numpy 2.3.5）；
核心程式 `bt.py`（訊號、報酬指數序列、績效指標）、`lev.py`（0050／00631L 模擬、配置、再平衡）、`calib.py`（00631L 耗損校正，2014-11~2026-10 實測每年落後 2.9%）；資料快照 SHA256 與模型假設見其 `README.md`。
資料限制：2014 年以前的 0050 與 00631L 為報酬指數模擬；1997–2002 年股利為估計值；「實際資料」欄使用證交所原始價格並已處理配息與兩次分割。
Boss 建議：獨立實作狀態機與 `composition`，用同一份資料快照比對 `reproduce_spec.txt`。

**2026-10-11 更新：資料已進 repo**（commit 391c95b，路徑 `回測數據/tw_backtest/`）：`README.md`、`reproduce_spec.py`、`results/reproduce_spec.txt`、`data/taiex_tri.csv`（證交所 MFI94U 報酬指數，2003-01-02～2026-10-08，5,850 筆）、`data/TWII.csv`、0050／00631L 證交所原始價格等。
- **PM 核對（事實）**：`results/reproduce_spec.txt` 輸出的表格與「Boss 更正的回測數字」逐項一致（保守／平衡／積極三列的 FULL、隔日成交、實際資料三欄，0050 買進持有，防守 E=0.3 的 16.1%／−36%，固定 2 倍 −93%，2011 年 −28% vs −18%，每年約 3 次進出、約 29% 時間防守）；
  驗收值 2026-10-08＝OFFENSE、距 252 日高 −1.0% 與 `composition` 五個案例也與驗收條件一致。
- **PM 沒有做的事**：沒有重跑 `reproduce_spec.py`（排程環境未安裝 pandas），所以這只是「輸出檔與文件互相一致」，不是對回測方法或數字正確性的獨立驗證（R3 不變）。
- 同一份輸出標頭註明：`FULL＝1997-07..最新的合成序列；lag2＝隔天收盤成交`，即「隔日成交」欄與「回測年化／最大回撤」欄用的是**同一條 1997-07～2026-10-08 序列**（Q5 的事實依據）。
- 欄名提醒（Tech Lead 處理）：`taiex_tri.csv` 的欄位是 `date,tri`，TASK-036 預期的測試 fixture 為 `date,close`。

### 假設（A1、A3、7 天門檻已由 Boss 於 2026-10-11 確認；其餘為 PM 補充）
- A1：策略總資產 V = 使用者在此功能指定的「0050 市值 + 00631L 市值 + 策略現金」。0050／00631L 取自 Portfolio 最新快照；策略現金由使用者在此功能內指定金額或勾選現金項目，不等於 Portfolio 的全部現金。**（Boss 2026-10-11 確認 yes；另提出「若可能把其他持股一併納入判斷更好」，Q6 已結案：先做 A＋B，不把其他持股納入判斷，此定義不變。）**
  **【v4 被取代：Boss 試用後改為整個流動資產納入判斷（DECISION-017、需求 12、A18～A19）；「策略現金」的手動輸入不再需要。】**
- A2：使用者的風險等級、策略現金設定會被記住。
- A3：狀態切換但四捨五入後所有建議股數為 0 時，顯示「不動」而非「加碼／減碼」。**（Boss 2026-10-11 確認）**
- A4：沒有任何 0050／00631L 持股、只有策略現金的使用者，仍能看到目標配置與建議買進股數。
  - 2026-10-11 PM 補充：此假設限於「Portfolio 內有其他持股、但沒有 0050／00631L」的使用者。**完全沒有任何持股（Portfolio 空狀態）的使用者，第一階段看不到此卡片**（PR #432 回報，卡片掛在有持股才顯示的區塊）。PM 接受為第一階段已知限制：Q1 未決前唯一使用者是 Boss（已有持股），不影響驗收；若 Q1 開放給一般使用者，再評估是否補入口。
- A5（**已結案，2026-10-11**）：「隔日成交版本」績效欄與「回測年化／最大回撤」欄屬同一回測期間。Boss 回覆「如果從 1997 開始不會造成困擾的話可以從 1997」，
  且 `results/reproduce_spec.txt` 標頭寫明兩欄用同一條 1997-07～2026-10-08 序列（只差成交時點）→ 畫面寫「1997–2026」即可，不造成誤導；實際序列起點是 1997-07，需要精確時可寫「1997-07 起」。
- A6：再平衡提醒（狀態未切換但曝險偏離 > 0.10）的文案不沿用 #431 §10 範例的「漲多／升到」措辭，改為只陳述實際曝險與目標的差距——PM 認可（範例的因果敘述在「切換日後沒照做」時會不正確；其餘減碼／加碼／不動三種文案與範例一致）。
- A7（2026-10-11，PM 假設）：Boss 說明「2014 年以前的 0050 和 00631L 是用報酬指數模擬的；1997–2002 年股利是估計值」，比 #431 §10 固定附註（只寫 00631L）範圍更廣。
  PM 假設畫面上的資料限制註記應改為與此一致（揭露寧可完整）；固定警語本身（DECISION-013）不變。Boss 若要維持原附註字句請告知。
- A8（2026-10-11，PM 假設）：Boss 說「股票分割風險先不用擔心，假設使用者會自己更新到對的股數」→ 不再為分割追加工作（Portfolio 全域分割處理維持技術債）；
  但 PR #432 已實作的「快照早於分割日就不顯示買賣股數」防護成本低、保護使用者不看到差數倍的建議股數，PM 假設**保留**，Boss 若要移除請告知。
- A9（2026-10-11，PM 假設，需求 11）：「總資產」的分母＝Portfolio 的「流動總資產」（`liquidGrossAssetsTWD`：台股＋美股＋基金＋現金，不含不動產與負債），
  與頁面上方「流動資產」標題、以及 Boss 剛合併的 PR #434（再平衡建議改以流動總資產為分母）同一個數字，避免同一頁出現兩種「總資產」。Boss 若要含不動產或扣負債請告知。
- A10～A13（2026-10-11，PM 假設，原需求 10）：已隨相似區段圖移至文末獨立需求「Stock Intelligence 大盤走勢比較圖」，ID 沿用。
- A18（2026-10-11，PM 假設，需求 12）：整體曝險的分母＝流動總資產（沿用 A9，不扣負債），使卡片 E 與頁面「水位」是同一個數字（Boss 說的是「水位」，不是「槓桿」）。
  若 Boss 有負債，「槓桿」（÷ 流動淨值）會與卡片 E 不同；這是定義不同而非錯誤，畫面標示「分母為流動總資產」即可。Boss 若要改以流動淨值為分母請告知。
- A19（PM 假設，需求 12）：「現金類」＝流動資產中全部現金（含外幣現金）加上曝險倍數 0 的債券／貨幣型，與頁面「水位」的口徑一致；不再有「策略現金」的獨立輸入。
  代價（PM 提醒）：Boss 若把部分現金視為「不動用」（例如緊急預備金），它也會被算進分母與現金類；要排除請告知（屆時需要「排除」機制，範圍較大，PM 不預設做）。
- A20（PM 假設，需求 12；v4.3 修訂）：以系統現有的 `exposureFactor` 歸類——倍數 0 → 現金類；1 → 其他一倍商品（0050 除外）；**2 的 00631L 與 00685L → 正2 槓桿 ETF 桶（v4.3，DECISION-021）**；
  其餘倍數（含 **NTSD（1.5 倍，v4.3 起計入而非排除）**、其他台股槓桿 ETF、美股 3 倍、反向）→「其他倍數商品」。倍數若分類不準（例如新基金名稱沒寫明），畫面與「水位」一樣受影響，這是既有限制。
- A21（PM 假設，需求 12）：沒有的桶不顯示（不顯示空列／0 股／NaN）；整個流動資產為 0 時不顯示建議。
- A22（PM 假設，需求 12；DECISION-020 修訂）：其他持股只顯示市值與曝險貢獻，**不給建議金額或股數**；核心的建議，0050／00631L 附整數股數與金額，現金類只顯示金額變動。
- ~~A24~~（2026-10-11，PM 假設，需求 12；DECISION-018；**v4.3 被 DECISION-021 取代：NTSD 改為納入曝險，不再排除，此條僅供追溯**）：NTSD 的排除＝不計入曝險分子與分母、不產生建議，只在對帳行顯示市值；以「美股 NTSD」辨識（與 `PortfolioExposure` 已有的 NTSD 條目同一判斷，細節由 Tech Lead 決定）。
  **只排除 Boss 點名的 NTSD**；其他個股、基金（含國外、名稱未必看得出投資地區者）照常計入曝險、不給建議（DECISION-020）。
  PM 的理解：Boss 的「B＋C」沿用 C 的「NTSD 除外」，所以 NTSD 仍排除。若 Boss 希望 NTSD 也計入曝險（反正不動；計入後曝險與水位完全一致，對帳行即可省），請告知，只需改一處。
  對帳行的措辭由 PM 暫定、Boss 可改。
- A26（2026-10-11，PM 假設，需求 12；DECISION-020）：其他持股一列的建議欄位顯示「不動（由您自行決定）」；「其他一倍商品」「其他倍數商品」兩列沿用，僅顯示。Boss 若想合併成單一列請告知。
- A27（2026-10-11，PM 假設，需求 12；DECISION-021）：Boss 說「我其實水位是想要含 NTSD 的」，PM 理解為**把 NTSD 計入卡片的整體曝險（分子與分母），使卡片 E 與右上角水位恆為同一個數字**；
  「NTSD 算是不同的」＝它不併入 00631L／00685L 的正2 桶，也不給加減碼建議（歸「其他倍數商品」、一律「不動」，DECISION-020）。若 Boss 其實希望 NTSD 單獨一列並給加減碼建議，請告知（PM 目前判斷不需要，且會牴觸 DECISION-020）。
  這是小的理解修正，PM 自行決定、不另外詢問；Boss 可推翻。
- A28（PM 假設，需求 12b；DECISION-021）：正2 桶只給整組一個結論與金額；股數＝只持有一檔就以該檔計；兩檔都持有就依兩檔目前市值比例分攤（加碼與減碼皆然），各自整數股；兩檔都沒有而需加碼則買 00631L（回測標的）。
  理由：不改變使用者已有的偏好、規則單純可預期。細節（含零股、分割防護、四捨五入造成的誤差）由 Tech Lead 提議，Boss 可改成其他分配方式。
- A29（PM 假設，需求 12；DECISION-021）：00685L 的曝險倍數為 2（PM 讀程式的理解：`PortfolioExposure` 對台股代碼尾碼 L 判為 2 倍，未執行驗證；請 Tech Lead 以測試鎖定）；
  若實際判成別的倍數，須修正使其與 00631L 同為 2，不是在卡片另寫特例（沿用「不得在前端另寫倍數規則」的原則）。
  PM 不評估 00685L 與 00631L 的追蹤標的差異是否影響回測適用性——採 Boss 的判斷「同性質」，並在績效區塊註明（R8）。
- A25（2026-10-11，PM 假設）：Boss 說「先不用管 R1 合規」（DECISION-019）；PM 假設同類的 R7（相似區段圖／同期間圖被讀成預測）比照處理，Boss 可推翻。
  固定警語（DECISION-013 的「不構成投資建議」）是 Boss 自己的規格，**不因此移除**。
- A23（PM 假設，需求 12）：「其他一倍商品」「現金類」「其他倍數商品」等列名為暫定，Boss 可改；功能仍僅 admin 可見，不影響其他使用者。

### Product Risks（事實／假設／不確定分開列；PM 不下法律結論）
- **R1 合規（不確定，需 Boss 決定）**：事實——功能會對登入使用者輸出個人化「買進／賣出 N 股」建議，Boss 已規劃固定警語。不確定——這在台灣是否構成證券投資顧問業務、是否需要執照、警語是否足夠，PM 未查證也無資格下結論。見 Q1。
  - **2026-10-11（最新）：Boss 決定暫不處理 R1（「我不在意，我只是小 web app，基本上還沒什麼 user」，DECISION-019）。PM 記為「Boss 已知悉、接受風險、暫不查證」；PM 不把它當成已查證或已排除，但不再提醒、也不再列為開放前置條件。**
  - 2026-10-11（較早）：Boss 決定目前僅 admin 可見、之後慢慢開放，**理由是擔心計算量，不是合規**。PM 不把這視為合規已解決：R1 仍是 `Audience=All` 之前的獨立前置條件（與需求 9 的觀察結果並列），開放前 PM 會再向 Boss 提醒。
- **R2 持股資料過期／股票分割**：Boss 指出 0050 於 2025-06-18 做過 1 拆 4、00631L 於 2026-03-31 做過 1 拆 22（PM 未驗證）。若使用者的 Portfolio 快照在分割前建立，股數與市值會嚴重失真，建議股數也會跟著錯。PM 未查證 Portfolio 現有行為。緩解（行為面）：顯示持股資料日期＋過期提示；請 Tech Lead 評估。
  - 2026-10-11 Tech Lead 評估結果（事實）：Portfolio 全域**沒有任何股票分割處理**（股數為使用者輸入、市值＝股數×最新價），PM 的風險屬實。本功能以前端硬性防護處理——快照日早於分割日就不顯示買賣股數，改提示先更新持股（實作於 PR #432；兩個分割日期已由實作者以公開報導交叉確認與 Boss 提供值一致，PM 未獨立驗證）。Portfolio 其他處（淨值走勢、報酬率）的同一問題列為技術債，不在本功能範圍，見下方 Future Consideration。
- **R3 績效數字**：回測數字為 Boss 提供，PM 未獨立驗證；2014 年前 0050／00631L 為模擬值、1997–2002 年股利為估計值；「1997–2026 回測」（平衡 17.2%）與「實際資料 2014–2026」（26.0%）差距大，若只顯示後者容易誤導。Boss 2026-10-11 同意同時標示期間與最大回撤。
  - **事實（2026-10-11）**：Boss 剛更正了數字（原表誤用 k=0）。PR #432 的 `drawdownBrakeCopy.ts` 仍是更正前的數字，見 Tech Lead Actions。這也說明數字應只有單一來源、更新時逐項對照 Issue／本文。
- **R4 執行時機**：回測主結果假設收盤集合競價成交；「隔日成交」版本績效明顯較差（平衡 15.3% / −37% vs 17.2% / −32%）。若使用者只能收盤後看到提醒，等同隔日成交。Boss 2026-10-11 同意第一階段顯示隔日成交版本；提醒僅站內（Q2），13:15 盤中提醒只對當時開著網站的使用者有效。
- **R5 資料源依賴**：訊號依賴證交所端點；端點改版會讓訊號停擺。行為面已要求「資料未更新」標示。
- **R6 槓桿 ETF 風險**：已由 Boss 規格要求揭露（見需求 7）。
- **R7 相似區段圖／同期間圖被讀成預測**：已移至文末獨立需求「Stock Intelligence 大盤走勢比較圖」（ID 沿用，並因改放公開的 `/finance` 而更新）。DECISION-014 的「僅 admin」只針對回落煞車卡片。
- **R8 建議範圍超出回測範圍（2026-10-11 新增，需求 12）**：
  - 事實：Boss 的回測只涵蓋 0050＋00631L＋現金（DECISION-013 的適用範圍原本就限於此）；v4 把整個流動資產（其他台股、美股、基金、NTSD 等）納入曝險判斷。
  - 不確定（PM 無法判斷）：以台股報酬指數產生的訊號，用在美股／基金／NTSD 是否合適，沒有回測支持；防守時若建議賣出這些部位，還有匯兌、手續費、稅務、基金贖回時差等成本，PM 未評估。
  - 不確定（合規）：個人化建議的範圍由兩檔台股 ETF 擴大到美股與基金，R1 的範圍更廣；PM 未查證、不下結論（Boss 已決定暫不處理，DECISION-019）。
  - 緩解（行為面）：**其他持股一律「不動」、不給任何買賣建議（Boss 決定 B＋C，DECISION-020）**——先前擔心的「建議賣出沒回測過的美股／基金」因此不會發生；~~NTSD 已排除（DECISION-018）~~（v4.3：NTSD 改為計入曝險、不給建議，DECISION-021——同樣不會有買賣建議）；回測績效區塊標明範圍（含「00685L 視同 00631L，未單獨回測」）；其他持股標示「未經回測、僅計入曝險」；功能仍僅 admin 可見。剩下的風險：整體曝險含其他持股，核心的實際配置會偏離回測的組合，績效數字不適用（已要求標明）。
  - 另一個誤導風險：卡片上「平衡 15.3% / −37%」等數字是 0050＋00631L＋現金的回測，若放在「整個流動資產」的卡片上，使用者會以為是整個資產的歷史績效——須標明範圍（需求 12「績效揭露」）。

### Open Questions
已回覆（2026-10-11，Boss；見 DECISION-014）：
- ~~Q1~~ 誰能看到：**先僅 admin，之後慢慢開放**；開放全部人前要靠可觀測性紀錄判斷（需求 9）。合規（R1）未因此解決（2026-10-11：Boss 決定暫不處理，見 DECISION-019）。
- ~~Q2~~ 提醒送達：**站內顯示就好，不要 email**。
- ~~Q3~~ 假設：**yes**（A1、A3、7 天過期門檻）。附帶新請求：若可能，把其他持股一併納入判斷 → 見 Q6。
- ~~Q4~~ 回測來源：已提供（見「Boss 更正的回測數字」），並附更正值。

已回覆（2026-10-11，Boss 於 #431 第二輪回覆；見 DECISION-015）：
- ~~Q5~~ 隔日成交欄期間：**1997 起可以**；`reproduce_spec.txt` 標頭證實與主欄同一序列（A5）。
- ~~Q6~~ 其他持股：**先做 A＋B 就好**（只看 0050＋00631L＋策略現金，另顯示策略部位占總資產 %）；C 不做（需求 11、A9）。
  **→ 2026-10-11 04:44 UTC 被 Boss 推翻**：試用後改為整個流動資產納入判斷（DECISION-017、需求 12）；換算沿用系統現有的 `exposureFactor`，不需 Boss 另外定義。
- ~~Q7~~ 相似期間：方向已定（自動找、可手動調過去區段、現在區段只能調長度、找不到就顯示、畫收盤價、不限 admin），**細節見 Q8**。
- ~~回測 CSV／`reproduce_spec.txt`／資料快照~~：Boss 已 commit 至 `回測數據/tw_backtest/`（見「回測來源」）。
- ~~「是否重用再平衡建議區塊」~~：Boss 同意不混用，回落煞車維持獨立卡片。

仍未決：
- ~~Q10~~（需求 12c）**已結案（2026-10-11，Boss 回覆「B＋C」；DECISION-020）**：個股、基金都計入整體曝險，但建議只針對 0050、00631L 和現金，其他持股一律「不動」（由使用者自己決定）。NTSD 原先排除（DECISION-018），**v4.3 起改為計入曝險、仍不給建議（DECISION-021，A27）**。
- **Q8（相似區段圖／同期間圖）**：已移至文末獨立需求「Stock Intelligence 大盤走勢比較圖」，並依 Boss 02:38 UTC 的更正改寫（位置已定：`/finance` Stock Intelligence）。與回落煞車本身無關。

### Tech Lead Actions
- [x] 評估並拆分第一階段：已完成（2026-10-10，TASK-036 後端／TASK-037 前端／TASK-038 第二階段 BLOCKED；PR #433、#432 審查中）。
- [x] 功能開關：已實作為預設僅 admin 可見（`Features:DrawdownBrake:Audience`）；Q1 決定後以設定開放，不需改碼。
- [x] 評估 Portfolio 快照股數與股票分割的關係（R2）：已完成，結果見 R2。
- [x] 依 LiveContract 模式為證交所端點加契約測試：後端 PR #433 已含 MFI94U 與 holidaySchedule（GitHub Actions 驗證通過，格式已確認）。
- [ ] 第二階段：驗證證交所 MIS 盤中端點實際回傳格式（#431 §5 指出需先驗證；TASK-038 待 TASK-036 合併後開工）。
- [x] 請 Tech Lead 審查 PR #433／#432 後交 Boss 合併：Tech Lead 已於 2026-10-11 審查通過，待 Boss 合併（合併即部署）。PM 產品面預審結果見下。
- [x] **（已完成：PR #436 / TASK-042 已合併，PM 複核一致）（產品面不一致，2026-10-11 PM 發現）更新 PR #432 `drawdownBrakeCopy.ts`（及其測試）以符合 Boss 更正值**：
  - `BACKTEST_NEXT_DAY`：保守 11.9→**12.1**（回撤 −27 不變）、平衡 15.4→**15.3**（−37 不變）、積極 18.1→**18.4**（−45 不變）；檔內註解的收盤集合競價版本也改為 13.5/−24、17.2/−32、20.9/−40。
  - `RISK_POINTS[0]`：「2011 年平衡等級 −29%」→ **−28%**（0050 −18% 不變）。
  - `SIMULATED_NOTE`：依 A7 改為涵蓋「2014 年以前的 0050 與 00631L 為報酬指數模擬值；1997–2002 年股利為估計值」。
  - 因目前僅 admin 可見，**不阻擋 #433／#432 合併**；但必須在 `Audience=All` 之前完成。可併入 PR #432（尚未合併）或另開小任務，由 Tech Lead 決定。
- [ ] 需求 8（資料時間＋手動重新整理）：請盤點目前此功能與 Portfolio 報價實際有哪些 cache（Boss 記得有 cache，PM 未查證）、重新整理的節流方式與對證交所請求間隔（≥ 2 秒）的衝突；第二階段必備，第一階段是否以後續小任務補由 Tech Lead 評估（不阻擋合併）。
- [ ] 需求 9（可觀測性）：請提議具體指標、紀錄管道與「可開放全部使用者」的量化門檻供 Boss 決定；Boss 擔心的是計算量，請一併說明此功能的負載由什麼決定（全站共用的大盤訊號 vs 隨使用者數成長的部分，PM 理解設計是前者為主，未驗證）。不得記錄持股／股數／金額。
- [ ] **（2026-10-11，資料已到位）** Boss 已把回測資料放進 repo `回測數據/tw_backtest/`：
  - 以 `data/taiex_tri.csv`（欄位 `date,tri`；TASK-036 預期 fixture 為 `date,close`，請決定如何對應）啟用後端「2026-10-08 應為 OFFENSE、距高點約 −1%」驗收測試，結束 Skipped 狀態；
  - 用同一份快照，以獨立實作的狀態機與 `composition` 比對 `results/reproduce_spec.txt`（Boss 建議；Tech Lead 先前的 Python golden 重放可併入）；
  - 該資料夾（含 ~3.5 萬行 CSV 與 .py）放在 repo 根目錄，請確認不影響建置、測試、部署與 CI（PM 不評估）；測試是否直接引用此路徑或另存 fixture 由 Tech Lead 決定。
- [x] **需求 11（Q6＝B）**：已由 PR #436 實作；**v4（DECISION-017）被需求 12 取代**，是否保留見下方「需求 11 已實作的占比」一項。
- [ ] 原需求 10（相似區段圖）已移出：不要把它做進回落煞車卡片，也不需要擴充 TASK-036 的報酬指數資料；詳見文末獨立需求的 Tech Lead Actions。
- [ ] （資訊）Boss 已合併 PR #434（再平衡建議改為 4 個項目、分母＝流動總資產，不含不動產）。回落煞車與再平衡建議是兩張獨立卡片，PM 判斷不衝突；請 Tech Lead 確認 PR #432 與 #434 合併後無版面或文案重疊。
- [x] 更新卡片文案為 Boss 更正值與 A7 註記：已由 PR #436（TASK-042）完成並合併；PM 讀 `drawdownBrakeCopy.ts` 複核一致（未執行）。
- [ ] **需求 12 v4.3 調整（2026-10-11，Boss 12:09 UTC 看過正式環境後留言；DECISION-021，HIGH，可立即開工；後續任務請由 Tech Lead 拆分）**：
  (1) **NTSD 改為計入整體曝險**（分子與分母，照系統 1.5 倍），卡片 E 與右上角水位恆為同一個數字，移除「不含 NTSD」對帳行；NTSD 歸「其他倍數商品」、一律「不動」。
  已合併的 TASK-046 以單一 `STRATEGY_EXCLUDED` 表決定排除，預期只需清空該表並更新測試／文案（PM 只讀 `ARCHITECTURE.md`／任務書，未讀程式）；是否保留排除機制由 Tech Lead 決定（預設清單為空）。
  (2) **00685L 併入正2 桶**：00631L＋00685L 合為一列，市值合計、曝險倍數 2、一個整組結論與金額；股數呈現見 A28。請確認 `exposureFactor` 對 00685L 為 2（A29），並以測試鎖定。
  (3) 驗收：見需求 12 驗收（v4.3 條目）；請在三個情境（`ARCHITECTURE.md` 的 golden）之外，補一組「持有 NTSD、持有 00685L」的真實比例範例，把改版前後的數字攤給 Boss 看一次。
  (4) 績效揭露加註「00685L 視同 00631L，未單獨回測」。
  **PM 無法看到 Boss 留言附的截圖**（GitHub 附件對排程沙箱回 403），以上依留言文字判斷；若實際畫面與此理解有出入，請 Tech Lead 在 PR 描述或 Issue 回報。
- [ ] **需求 12a／12b（2026-10-11，Boss 試用回饋，HIGH，可立即開工）**：把卡片改為整個流動資產——分母沿用 A9／A18（流動總資產）、倍數沿用 `exposureFactor`，
  使「目前有效曝險」在沒有被排除持股時與頁面右上角「水位」是同一個數字（這是 PM 的產品面期望：實作時請重用 Portfolio 既有的曝險計算，避免再出現兩套算法；做法由 Tech Lead 決定）；
  ~~NTSD 排除於策略範圍（DECISION-018、A24）：不計入分子分母、不給建議，卡片加對帳行~~（**v4.3 被取代**：NTSD 改計入曝險、不給建議，無對帳行，DECISION-021）；
  分桶顯示（0050／00631L／其他一倍商品／現金類／其他倍數商品，見需求 12b、A20～A22、A26；**其他持股只顯示、一律「不動」**）；退役「策略現金」手動輸入（A19）。
  請一併評估對已合併的 TASK-037／042（策略現金輸入、需求 11 占比、分割防護 A8）的影響與調整方式，並拆成任務。
- [ ] **需求 12c（Boss 已決定 B＋C，DECISION-020，不再待決）**：請提議核心的目標與建議算法——整體曝險含所有持股（v4.3 起含 NTSD），只調整 0050／正2 槓桿 ETF（00631L＋00685L）／現金；
  需涵蓋：其他持股單獨超過目標、核心降到最低時的如實標示、截斷、最低現金 k 的基準、無可執行動作時顯示「不動」；並用 2～3 個真實持股情境（含 Boss 的）把結果攤給 Boss 看。PM 只定產品規則（見需求 12c）。
- [ ] **績效揭露（R8）**：回測績效區塊標明「依 0050＋00631L＋現金回測」；其他持股加「未經回測、僅計入曝險、不給建議」提醒；固定警語不變。
- [ ] 需求 11 已實作的「策略部位占總資產 X%」：視需求 12 保留（改為核心占比）或移除，由 Tech Lead 決定（PM 預設不需要）。
- [ ] 第二階段（TASK-038 盤中預估）與重新整理／可觀測性（需求 8／9）**不受影響**，照原排程；請確認 TASK-041／043 的重新整理按鈕涵蓋 12a 用到的持股報價（若同一條資料路徑，PM 假設自然涵蓋，未驗證）。

### PM 產品面預審（2026-10-11，依 PR 描述與 `drawdownBrakeCopy.ts` 的文案；**歷史紀錄：PR 現已合併、文案已更正，見上方 [x] 項與 v4 進度；需求 12 讓部分預審條目被取代**）
- 固定警語字串與 DECISION-013 一致；「2014 年以前的 00631L 績效為模擬值」註記存在；風險三點與 #431 §10 一致。
- 隔日成交版本數字（保守 11.9/−27、平衡 15.4/−37、積極 18.1/−45）當時與 #431 §2 表格一致；**但 Boss 於 2026-10-11 更正了該表（保守 12.1/−27、平衡 15.3/−37、積極 18.4/−45；2011 年平衡 −28%），上述預審結論對這些數字已不成立**，見上方 Tech Lead Actions。有同時標示期間（Q5 已結案：1997–2026）與最大回撤。
- 未發現與驗收條件衝突之處。尚待合併後在真實端點上走一遍的項目（PR #432 已自述）：`GET /api/portfolio/drawdown-brake` 與 `drawdownBrakeEnabled` 的整合；以及 Boss CSV 到位後的 2026-10-08 OFFENSE 驗收。


## Requirement: Stock Intelligence 大盤走勢比較圖（Issue #431 延伸）

### Status
**DRAFT v1（2026-10-11）**。來源：Boss 在 #431 的兩則留言——Q7 回覆（歷史相似區段比較圖），以及 02:38 UTC 的更正：
「兩條線那個功能還是放到 `/finance` 的 Stock Intelligence 比較好，因為那個和個人資產沒關係」，並新增「過去 5 年相同日期區段」走勢圖。
本需求原為回落煞車的需求 10，現獨立；**與回落煞車（訊號、持股、建議股數）完全無關**，見 DECISION-016。

### Goal
讓在 Stock Intelligence 看台股大盤的使用者，能用「過去幾年同一段期間」與「歷史上走勢相似的一段」來對照目前的大盤走勢，作為參考（不是預測）。
屬 Stock Intelligence（主力應用，DECISION-009）的延伸，給使用者回來看大盤的另一個理由（DECISION-003 留存）。

### User Story
As a 看台股大盤的使用者,
I want 把今年某段期間的大盤走勢，和過去幾年同一段期間、或歷史上走勢相似的一段疊在一起看,
so that 我能用歷史當參考，檢視這次和過去的差異。

### Requirements
**A. 過去 5 年同期間走勢圖（Boss 2026-10-11 02:38 UTC 新增）**
- 一張線圖，把「同一段月/日區間」在今年與前幾年的走勢疊在一起：**每一年一條線、不同顏色，圖例標示年份**，「僅供使用者參考」（Boss 原話）。
- Boss 範例（加權指數）：2022/1/1～9/30、2023/1/1～9/30、2024/1/1～9/30、2025/1/1～9/30、2026/1/1～9/30，共 5 條線。
- 預設期間與可調整方式、標的、是否含今年，見 A14～A16。
- 滑過圖上某個月/日時，每條線顯示該年對應日期的實際收盤價與日期。
- 圖上常駐警語（A17）；不放任何預測性文字或由過去年份推算的數字。
- 資料暫時取不到時，顯示「資料暫時無法取得」，不顯示部分或錯誤的資料。

**B. 歷史相似區段比較圖（原回落煞車需求 10；Q7 方向，DECISION-015）**
- 目的（Boss）：把「現在這一段走勢」和「歷史上最相似的一段」畫成兩條線檢視差異，並看到相似區段**之後**的收盤價怎麼走，
  「雖然過去不一定等於未來，但僅供參考」（Boss 原話）。
- Boss 已定方向：
  - 由**系統自動**找出歷史上最相似的區段，不是讓使用者從清單挑。
  - 使用者可手動調整「用來比對的過去區段」的時間範圍；「現在」這一段不能移動，使用者至多調整其長度。
  - 找不到相似的過去區段時，顯示「找不到過去的相似區段」。
  - 圖上畫的是**收盤價**（不是距高點的回落幅度）。
  - **不想只限 admin**。
  - 位置：`/finance` 的 Stock Intelligence（DECISION-016）。
- 「相似」的演算法、預設區段長度、可調整範圍、「相似到什麼程度才算找得到」的門檻、相似區段之後要顯示多長：**由 Tech Lead 提議，並用歷史實例讓 Boss 確認**；PM 不訂數字。
- PM 產品面限制（不改變 Boss 的決定，降低被讀成預測的風險）：圖上常駐警語；不顯示任何由相似區段推算的預測數字或結論（例如「預期漲 X%」「看漲／看跌」）；
  相似區段之後的走勢須清楚標示為「過去實際發生的走勢」。

**共同要求**
- 標的名稱須清楚標示（例如「加權指數（價格指數，不含息）」），避免與含息的報酬指數混淆（A15）。
- 圖表資料是全站共用的市場資料：使用者增加不應等比例增加對外部資料源的請求（成本紀律 DECISION-003；Boss 對計算量的擔心見 DECISION-014）。實作由 Tech Lead 決定。
- 不呼叫 AI、不消耗 UsageLimit。

### Acceptance Criteria
**A. 過去 5 年同期間圖**
- [ ] `/finance` 的 Stock Intelligence 內有此圖；5 條線（今年＋前 4 年）、顏色各不同、圖例標示年份。
- [ ] 預設期間為今年 1/1 到最近一個交易日；使用者可修改起訖月/日，修改後 5 條線同步更新（A14）。
- [ ] 各年以月/日對齊，起點＝100 標準化（A11）；滑過某月/日，每條線顯示該年對應日期的實際收盤價與日期；該年該日未開市時的呈現方式一致且不報錯。
- [ ] 今年尚未到的日期不畫線（不延伸、不預測）。
- [ ] 圖上常駐警語；畫面沒有預測性文字。
- [ ] 標的名稱清楚標示；資料取不到時顯示「資料暫時無法取得」，而非空白圖或部分資料。
- [ ] 同一圖在快取有效期間內重複載入，不會重複向外部資料源取資料（Tech Lead 說明快取期限）。
- [ ] 手機寬度（≤ 400px）版面無橫向捲動，圖例可讀。
- [ ] 此圖不呼叫 AI、不消耗 UsageLimit。

**B. 相似區段圖（草案；上線對象待 Q8-3，演算法待 Tech Lead 提議）**
- [ ] 找得到時：同一張圖顯示「現在」與「最相似的過去區段」兩條收盤價線（以各自起點＝100 標準化），滑過時可見各自的實際收盤價與日期。
- [ ] 找不到時：顯示「找不到過去的相似區段」，不顯示任何替代的猜測結果。
- [ ] 使用者調整「過去區段」的時間範圍後，圖與相似區段隨之更新；「現在」區段不可被移動，只可調整長度。
- [ ] 相似區段之後的走勢標示為「過去實際發生的走勢」；圖上常駐警語；畫面沒有預測性文字或由相似區段推算的數字。
- [ ] 此圖不影響、也不出現在回落煞車的訊號、目標配置與建議股數中（DECISION-013 的固定規則不變）。

### Priority
- A 過去 5 年同期間圖：**MEDIUM**（Boss 新提出、範圍小、資料系統已有來源、屬主力應用 Stock Intelligence；不含相似度演算法，風險與不確定性較低）。可由 Tech Lead 先行設計。
- B 相似區段圖：**MEDIUM**，排在 A 之後；演算法與門檻複雜，需先用歷史實例給 Boss 看過（A12）。Tech Lead 可先提議（預設標的為加權指數，A15）。
- 與回落煞車（HIGH／MEDIUM）無依賴關係，互不阻擋。

### Scope
#### In Scope
- 加權指數的「過去 5 年同期間」圖；加權指數的「歷史相似區段」比較圖；警語、標的標示、資料取不到的處理。
#### Out of Scope
- 個股或其他指數（第一版只做加權指數，見 A15）；預測、評分、買賣建議；AI 生成內容；站外通知；
  任何與使用者持股相關的內容；回落煞車的訊號與建議（兩者互不影響）。
#### Future Consideration
- 讓使用者選個股／其他指數做同期間比較（Boss 說「例如加權指數」，若原意是任何標的，請告知，見 Q9）；
  相似區段圖的更多自訂（相似度參數、前幾名）。

### Constraints
- 成本紀律（DECISION-003）：不使用 AI、不新增付費服務。
- 不得與回落煞車的固定規則混用（DECISION-013）。

### 假設（皆為 PM 補充，可推翻）
- A10：Boss 說「系統先自動找出最相近的兩個區段」，PM 解讀為「現在這段＋最相似的 1 個過去區段」＝兩條線（Boss 先前原話：「一條線代表某個期間，一條線代表現在」）。若 Boss 其實要顯示前兩名相似的過去區段（共三條線），請告知。
- A11：各段收盤價的價位差很大（例如加權指數 2022 年約 1.5 萬、2026 年約 5 萬點）無法直接疊圖，所以各條線都以各自區段起點＝100 標準化後顯示，仍是收盤價，只是縮放；滑過時顯示各自的實際收盤價與日期。（A、B 兩張圖共用。）
- A12：「相似」指區段內收盤價的走勢形狀相近（標準化後比較）。PM 不指定演算法、預設長度（僅建議 Tech Lead 先評估約 60 個交易日，**不是定案**）、可調範圍與「找得到」的門檻——要用歷史實例給 Boss 看過再定。
  若沒有門檻，系統永遠找得到「最像的一段」，與 Boss 要的「找不到就顯示找不到」不符，所以門檻是必要的。
- A13：Boss 想看「相似的收盤價未來可能怎麼走」，所以 B 圖須延伸顯示相似區段**之後**的走勢；延伸長度由 Tech Lead 提議。這是兩張圖中最容易被讀成預測的部分（R7）。
- A14：A 圖的預設期間＝今年 1/1 到最近一個交易日（年初至今），使用者可改起訖月/日。Boss 範例的「1/1～9/30」視為範例（9/30 是上季底，不是今天）。若 Boss 想要固定期間或別種預設，請告知。
- A15：標的＝**加權指數**（Boss 範例「例如加權指數的」；Boss 2026-10-11 已同意兩張圖都用加權指數，不含息）。第一版兩張圖都只做加權指數；個股／其他指數列 Future Consideration（Q9）。
  事實（PM 讀程式確認）：系統已經在 Finance「大盤總覽」以 Yahoo Finance 取得加權指數（`^TWII`，`MarketOverviewProvider`），並有 `^TWII` 的 LiveContract 測試——但目前只取當日報價；
  多年日線歷史的取得方式與穩定度由 Tech Lead 驗證（Boss 的回測資料 `回測數據/tw_backtest/data/TWII.csv` 同樣來自 Yahoo `^TWII`，涵蓋 1997 年起，可作參考，PM 未驗證其完整性）。
  加權指數是**價格指數**，不含息；若 Boss 想用含息的報酬指數（只有 2003 年起的官方資料），請告知。
- A16：「過去 5 年」含今年（今年＋前 4 年＝5 條線），與 Boss 範例（2022～2026）一致。
- A17：兩張圖共用常駐警語（PM 提議，Boss 可改字）：「過去走勢僅供參考，不代表未來表現，不構成投資建議。」回落煞車的固定警語（DECISION-013）是該功能專用，不沿用。

### Product Risks（事實／假設／不確定分開列；PM 不下法律結論）
- **R7 被讀成預測／合規**：
  - 事實：Boss 想看「相似的收盤價未來可能怎麼走」，並表示這功能不想只限 admin；Boss 將位置改到 `/finance`。
    PM 讀程式確認 `/finance` 路由沒有 `RequireAuth`——**未登入的訪客也能進入該頁**（頁內「今日市場 AI 摘要」分頁需登入，圖表則取決於實作）。所以這兩張圖放在那裡，預設就是公開可見。
  - 不確定：在台灣，對公眾顯示「相似歷史之後怎麼走」是否被視為投資建議、是否需要額外處理，PM 未查證也不下結論（與 R1 同類；Boss 2026-10-11 決定暫不處理 R1，PM 假設比照，A25）。
    另外，Finance 既有的「綜合量化分析」「AI 趨勢分析」也輸出看多／看空與「投資建議」類內容（程式中有 `title="投資建議"`），PM 同樣未查證它們是否在 R1 範圍內；**R1 的範圍可能比回落煞車更廣**，建議 Boss 查證時一併涵蓋。
  - 事實：相似區段只有少數歷史樣本，「過去相似」不代表「之後相似」，使用者容易高估其參考價值。
  - PM 的緩解（行為面，不推翻 Boss 的決定）：常駐警語、不放預測文字或推算數字、之後走勢明確標示為過去實際走勢。A 圖（同期間）不含「相似」判斷，被讀成預測的程度低於 B，但仍屬歷史走勢呈現，PM 不下結論。
- **R8 資料來源依賴**：加權指數歷史來自 Yahoo Finance 的非官方 chart 端點（系統既有做法，已有 LiveContract 測試監控）。端點變動會讓圖取不到資料；行為面已要求顯示「資料暫時無法取得」。

### Open Questions
已回覆／已由 PM 決定（2026-10-11，Boss 嫌小事問太多，這些改由 PM 自行決定並記錄；Boss 隨時可推翻，不必回覆）：
- ~~Q8-2 位置~~：Boss 已定——放 `/finance` 的 Stock Intelligence，不放 Portfolio（DECISION-016）。具體放哪個分頁／區塊由 Tech Lead 提議。
- ~~Q8-1 標的~~：**兩張圖都用加權指數（價格指數，不含息），Boss 已同意（2026-10-11）**，畫面標示「不含息」（A15）。理由：Boss 範例即加權指數、系統已有來源、歷史可到 1997。
- ~~Q8-3 對象~~：**PM 依 Boss 既有決定處理**——Boss 已明說「不想只限 admin」，且 `/finance` 本來就未登入也能進入，所以**照現況（訪客也看得到）**，不另設開關為前提；
  Tech Lead 是否加功能開關自行決定。合規仍未查證，記錄於 R7，不再向 Boss 追問；Boss 若要改成登入後才顯示或先 admin，說一聲即可。
- ~~Q9~~：**PM 決定**——預設期間＝年初至今且可自訂起訖（A14）；「過去 5 年」含今年（A16）；第一版只做加權指數（A15）；警語字句採 A17。

目前**沒有需要 Boss 回覆的問題**。

### Tech Lead Actions
- [ ] 設計 A 圖（過去 5 年同期間）：驗證加權指數多年日線歷史的取得方式與穩定度（現有 `MarketOverviewProvider` 只取當日報價）、快取策略（共用資料、使用者增加不應放大對外請求）、
  放在 Stock Intelligence 的哪個分頁／區塊（現有分頁：綜合量化分析、AI 趨勢分析、今日市場 AI 摘要、多股比較，PM 不決定放哪）。是否加功能開關（像 `Features:DrawdownBrake:Audience`）由 Tech Lead 決定，以保留 Q8-3 的彈性。
- [ ] 提議 B 圖（相似區段）的演算法、預設長度、可調範圍、「找得到」門檻、之後走勢長度，並**用幾個真實歷史例子**（含一個「找不到」的例子）給 Boss 過目——見 A12。可先行，預設標的為加權指數。
- [ ] 兩張圖共用同一份歷史資料與快取；**不要**動回落煞車的報酬指數資料（TASK-036）或把這兩張圖放進 Portfolio。
- [ ] 完成設計後回報工作量與風險；若發現資料來源的歷史長度不足以支撐 B 的相似搜尋，請告知 PM（可能影響 Boss 的預期）。

---

## Requirement: 站台品牌與分享預覽（Site Branding & Link Previews）— 已出貨（DECISION-022）

> 補登：Boss 直接合併 PR #449，未經 task 流程；本節把已出貨的行為記為需求，之後變更以此為基準。

### Goal
把網站連結貼到 LINE／Teams 等聊天工具時，預覽顯示的是「被分享的那個應用」，而不是千篇一律的英文學習標題。

### User Story
As a 想把某個工具（例如 PDF 解鎖、投資組合）分享給朋友的使用者，
I want 貼出網址後預覽顯示該工具的名稱與一句話說明，
so that 對方一眼看得出這是什麼、願意點進來。

### Requirements
- 首頁與未對應頁面：標題「Jacky Studio — AI 學習、投資與實用工具」＋站台描述。
- 每個應用頁：標題「{應用名稱} | Jacky Studio」＋該應用的描述（九個應用，名稱與描述與首頁卡片一致）。
- 單字頁維持 DECISION-012 的 title 格式，預覽的標題與描述是該單字的內容。
- 瀏覽器分頁標題與分享預覽一致。
- 不執行 JavaScript 的預覽機器人也要看到正確內容。

### Acceptance Criteria
- [ ] 貼 `/pdf`、`/portfolio`、`/finance` 等網址到 LINE 或 Teams，預覽顯示該應用的名稱與描述，而不是「English Coach」。（預覽有快取，已被預覽過的舊網址可能延遲更新。）
- [ ] 貼首頁網址，預覽顯示「Jacky Studio — AI 學習、投資與實用工具」。
- [ ] 貼單字頁網址，預覽顯示該單字的標題與說明。
- [ ] 瀏覽器分頁標題與上述預覽的標題一致。
- [ ] 預覽機制出任何問題時，頁面照常打得開（不因 metadata 失敗而空白）。

### Priority
LOW（已出貨；後續只做成長面確有需要的小改進）

### Scope
#### In Scope
- 已出貨的標題、描述、Open Graph 與 `twitter:card`。
#### Out of Scope
- 變更單字頁 title 格式（DECISION-012 不動）。
#### Future Consideration
- 每個應用專屬的預覽圖（目前全站共用 `JackyAIIcon.png`；需先有圖片素材）。
- 重新評估首頁是否需要帶「英文學習」關鍵字（PM 觀察：目前由 `/english` 與單字頁承擔）。

### Constraints
- 預覽有快取，不可承諾「分享後立刻更新」。
- 應用名稱與描述在前後端各有一份（`SitePages.cs` 與 `constants/apps.tsx`），新增應用時兩邊都要有；這是 PM 觀察到的維護風險，作法交 Tech Lead 決定。

### Open Questions
- 無需 Boss 回覆。PM 小假設：A1——品牌分兩層（平台＝Jacky Studio，英文學習與單字頁＝English Coach）是 Boss 有意的；若 Boss 想連單字頁也改成 Jacky Studio，請告知（會動到 DECISION-012）。



## Requirement: Data Analysis — AI 自然語言提問（Boss 直接交付，PR #450～#457，2026-10-13）— 已出貨

> **性質**：這是 PM 事後補登，不是新需求。Boss 在 2026-10-13 一天內自己合併了 8 個 PR（#450～#457；#457 為明細 CSV 匯出，2026-10-14 補登）。
> 需求 7（資料外送告知）是 PM 在 2026-10-14 依 Tech Lead 的提醒新增的；**PR #462（TASK-052）Boss 已於 2026-10-15 17:59（+0800）合併（`86475c8`），`Build and deploy` run 552 成功，已出貨**（PM 2026-10-15 對照 master 上的程式確認，見需求 7）。
> 之前 PM 的假設是「沒有用量數據前，Data Analysis 不值得投入」（見 Portal 需求 Audit 與 DECISION-010）；
> 這批 PR 表示 Boss 已決定投入，該假設對 **Data Analysis 的 AI 提問** 不再適用（DECISION-025）。
> 驗收條件寫的是 PR 已交付的行為；PM 只讀了程式碼與 PR 描述，**沒有實機、沒有對真的 AI 實測**。

### Goal
使用者用中文或英文問一句話（例如「越南各產品類別的營收」），就得到一張正確、可信的彙總表與圖，
而且**結果和使用者問的不一致時，畫面要明講**，不能讓人把「全部國家的營收」當成「越南的營收」。

### User Story
As a 已登入的使用者,
I want 用自然語言問資料、並知道 AI 是怎麼理解我的問題,
so that 我不必自己設定篩選、分組與度量，也不會被默默錯誤的結果誤導。

### 現況事實（PM 讀 `DataAnalysisController.cs`、`NlPlanValidator.cs`、`NlDateRanges.cs`、`UsageCosts.cs`；未實機）
- AI 提問端點 `POST api/DataAnalysis/summarize/nl` 需登入；每次提問消耗 `UsageCosts.NlSummarize`（2）的每日用量（DECISION-006／007）。
- 流程：先讀資料欄位的真實值（每欄最多 300 個、有時間上限）→ AI 產生查詢計畫 → 驗證欄位名與篩選值 → 有不存在的值就帶著提示重跑一次 → 執行並回傳「AI 解讀」＋結果＋提示。
- 日期：「最新／最近一個月／本月」＝資料最新日期所在月份的 1 號到最新日期（不是往回推 30 天）；「上個月」「本季」「上一季」「今年」「去年」同樣由程式預先算好，不讓 AI 自己算。結束日若只有日期，視為包含結束日整天。
- 使用者提到、但資料裡沒有對應值的條件，AI 須回報（`unmatched`）；畫面顯示黃色提示：「資料中找不到符合「X」的值，已忽略此條件，以下結果未套用此篩選。」
- 修正前的量化基準（Boss 於 PR #456 自述）：5 題「資料裡沒有的條件」陷阱題 × 3 次，**15 次全部**默默拿掉篩選、沒有任何提示。
- 明細頁只向資料庫拿當頁資料，筆數為精確計數（#452）；來源下拉選單選「預設」不再顯示空白（#450）。
- **送給 OpenAI 的資料（Tech Lead 2026-10-13 審閱 `NlSummarize`／`BuildNlPlanAsync` 所述，PM 另 grep 前端確認）**：每次提問都會把來源裡「相異值 ≤300 個」的字串欄位（最多 30 欄、合計 ≤2000 個值）連同欄位名稱放進 prompt 送給 OpenAI；以前只送 AI 先挑出的相關欄位。預設示範資料無妨；使用者連自己的資料庫／上傳檔案時，低基數欄位若是人名、部門、客戶代碼，這些值也會送出。`jackyaiapp.client/src/DataAnalysis/` 目前**沒有任何**「資料會送給 AI 服務」的告知文字（PR #462 合併前）。
  - **PM 更正（2026-10-15，Frontend 在 #462 對照 `BuildNlPlanAsync` 發現；PM 讀同一段程式確認）**：上面「欄位名稱＋≤2000 個值」**低估了實際送出的內容**，還有兩項：(1) 每個日期欄位的**最新日期**（最多 3 欄）及由它算出的相對區間；(2) 篩選值對不到資料而**重跑一次**時，會把從資料搜出的**相近真實值**（每次最多 20 個）附進去——**包含太大、第一次沒列進 prompt 的欄位**（例如客戶名稱）。也就是說，人名／客戶名這類高基數欄位的值，在重跑時也可能送出一小部分。PM 在 DECISION-028 與 `boss/PM.md` 的說法因此不完整；DECISION-028 的決定本身（要告知）不變，不改寫歷史，更正記在這裡。
- 明細頁的「匯出 CSV」（#457）：下載符合目前篩選與排序的**全部**列（不只當頁），上限 100,000 列；超過上限時以 `X-Exported-Count`／`X-Total-Count` 讓畫面提示被截斷；UTF-8 含 BOM（Excel 可開）；以 `=` `+` `-` `@` 開頭的文字會加前導單引號防試算表公式注入（數字不受影響）；資料庫來源同時最多 2 個匯出，其餘回 429。**預設來源（示範資料表 `dbo.Orders`）匿名即可匯出**，自訂來源須登入且只能匯出自己的來源。（Tech Lead 審閱；PM 未實機。）
- 另有開發用的示範資料產生器 `scripts/demo-data/`（7 份已攤平的企業資料，可上傳 CSV 或產生 SQL；可選擇取代正式環境 `dbo.Orders`）——屬工具，不是使用者功能。

### Requirements
1. 使用者提到的條件若在資料中找不到對應值，結果旁必須有明確提示說明該條件被忽略、結果未套用此篩選；**不得**只回傳未篩選的結果。
2. 條件都能對應時，不得出現多餘的「找不到」提示。
3. AI 的解讀說明要讓使用者看得出實際套用了哪些分組、度量與篩選（含日期區間）。
4. 相對日期說法（最新一個月、上個月、本季、今年、去年）以**資料最新日期**為準，結果可預期、可重現。
5. 提問須登入並消耗每日用量；超過額度沿用既有的 429＋「聯絡作者提高額度」提示（DECISION-007），不新增付費或點數概念。
6. 這個功能的資料量與等待時間不得讓頁面卡死：取不到欄位真實值時退化為不驗證、繼續回答，而不是報錯。
7. **（PM 2026-10-14 新增；PR #462 已於 2026-10-15 合併出貨）資料外送告知**：使用者在「問 AI」的輸入處就能看到一段白話說明，講明「為了理解問題，會把**哪些內容**傳送給 AI 服務（OpenAI）」，使用者不必去翻其他頁面。文案可由 Tech Lead／Frontend 微調，但須白話、不得暗示資料不會離開本站，也不寫「不會儲存」「安全」「已去識別化」這類本站無法保證的承諾。依據：DECISION-028。
   - **PM 2026-10-15 修訂（產品面）**：告知須涵蓋**實際送出的全部內容**，而不是固定的「欄位名稱與可選值」清單——需求 7 原文的清單比實際少兩項（最新日期、重跑時的相近值，見上方更正）。**告知與實際行為不一致，比告知得不夠簡短更糟**；所以原先寫的「一行」放寬為「一小段、永遠看得到」。日後只要「問 AI」送給 OpenAI 的內容有增減，告知文字須一併更新（怎麼確保由 Tech Lead 決定）。
   - **PM 對 #462 文案的產品意見**（Engineer 在 TASK-052 Result 問「第二句是否保留」）：**建議保留**（Tech Lead 同日審閱也建議兩句都留，PM 與 Tech Lead 一致）。理由：第二句（重跑時附上相近值）講的正是高基數欄位（人名、客戶名）也會有一小部分被送出，是這次告知最該讓使用者知道的一件事；刪掉會讓文案與實際行為不一致。代價只是手機寬度多幾行字（PM 估約 3～4 行，未實機）。決定權在 Boss，PM 不阻擋。日後 `BuildNlPlanAsync` 送出的內容有增減時須同步改文案——Tech Lead 已把它記為維護不變式（`ARCHITECTURE.md` TASK-052 條目）。
8. **明細 CSV 匯出（#457，已出貨）**：匯出的檔案內容必須與畫面上目前的篩選、排序一致；被上限截斷時使用者必須看得到提示；Excel 直接開啟不得亂碼，也不得因為資料內容而執行公式。

### Acceptance Criteria
- [ ] 對真實 AI 問 5 題陷阱題（資料裡沒有的國家／付款方式等）各 3 次，**每次**都出現「已忽略此條件」提示。（**未驗證**：PR #456 說明當天額度用完、尚未對真 AI 實測；Boss 計畫以 `run_benchmark.py --update G01,G02,G03,G04,G05` 驗證，該腳本不在 repo 內，PM 看不到。這是此功能最重要的一條。）
- [ ] 對真實 AI 問一般題（例如「美國各產品類別的營收」）不出現任何提示。（未驗證，同上。）
- [x] 提示文字與去重邏輯存在於程式與單元測試。（`NlPlanValidator.cs:155` 有該句；PR 描述稱離線測試 1264 個全過——PM 沒有重跑。）
- [x] 「最新一個月」等相對區間由程式預先計算（`NlDateRanges.cs`）。（讀碼；未實機。）
- [x] 來源下拉選「預設」時欄位顯示「預設」而非空白。（`SourcePanel.tsx` 用 `__default__`，`SourcePanel.test.tsx` 有對應測試；未實機。）
- [ ] 手機寬度（≤ 400px）下，提示 Alert 與結果表不造成橫向捲動。（PM 無法以讀碼確認。）
- [x] （需求 7，**PR #462 已合併部署**）「問 AI」輸入處有資料外送告知；訪客與已登入者看到的文字一致；不需點擊或 hover 就看得到。（PM 2026-10-15 讀 master：`SummarizeTab.tsx:365` 的 `helperText={AI_DATA_NOTICE}`，沒有 `isLoggedIn` 條件、不在 Tooltip／Popover 內；`SummarizeTab.test.tsx` 涵蓋登入／未登入；部署 run 552 成功。**讀碼，未在正式站實機看。**）
- [x] （需求 7）告知文字涵蓋實際送出的全部內容：問題、全部欄位名稱、可選的值、最新日期、重跑時的相近值，且不含「不會儲存／安全」類承諾。（PM 對照 `BuildNlPlanAsync` 與 master 上 `constants.ts:46` 的 `AI_DATA_NOTICE`：**一致**——文案含問題、欄位名稱、可選值、最新日期，第二句講重跑時附上資料中相近的值；無「不會儲存／安全」承諾。Tech Lead 獨立逐行核對，結論相同。**讀碼，未對真 OpenAI 抓封包**；日後 `BuildNlPlanAsync` 送出內容有增減須同步改文案，見需求 7 的維護不變式。）
- [ ] （需求 7）手機寬度（≤400px）下告知文字自動換行、不造成橫向捲動。（#462 自述僅依 CSS 判斷、**未實機**；請 Boss 順手用手機看一眼。）
- [x] 明細頁「匯出 CSV」存在，下載內容為符合目前篩選／排序的全部列，上限 10 萬列，被截斷時有提示。（#457 PR 描述自述以 12 萬列資料表驗證匯出 10 萬列、帶篩選時 27,424 列全出；`DataAnalysisExportTests`、`exportCsv.test.ts`、`DetailsTab.test.tsx` 涵蓋；PM 未重跑。）
- [x] 以公式字元開頭的文字欄位匯出後前面加單引號、數字不受影響。（Tech Lead 逐行讀 `GuardFormula` 確認運算子優先序；有單元測試。）
- [ ] 實際用 Excel 開啟匯出檔，中文不亂碼、含逗號與換行的欄位排版正確。（PM 無法驗證；程式面是 UTF-8 BOM＋RFC 4180。）

### Priority
MEDIUM（Boss 已實際投入；PM 沒有數據判斷它相對於英文學習與投資組合的優先序）

### Scope
#### In Scope
- 已登入使用者的 AI 提問行為、提示、日期解讀、用量扣除

#### Out of Scope
- 匿名使用 AI 提問；匯出彙總結果（目前只能匯出明細列）；讓使用者排除不想送給 AI 的欄位（見 Future Consideration）
- DataAnalysisLibrary 本身的變更範圍須另行與 Boss 確認（DECISION-009）
- 為 AI 提問另做計費或點數

#### Future Consideration
- 沒有分組的問題（例如「總營收是多少」）：**已知限制**（Tech Lead 2026-10-13 讀碼確認）——`DataAnalysisLibrary` 的彙總不接受沒有分組的請求，所以不會得到單一數字：模型自己補一個分組就得到分組表，沒補就是「AI could not interpret your query」。要支援單一彙總值須改 `DataAnalysisLibrary`（依 DECISION-009 須先與 Boss 確認）。PM 的判斷（假設）：「總營收是多少」是最自然的問法之一，若 A30（比賽／展示）成立，這可能是現場會被問到的題目；但沒有任何使用者或 Boss 回報撞到，所以**不排任務**，只在 `boss/PM.md` 留一行，由 Boss 決定。
- 讓使用者（或資料來源擁有者）勾選「這個欄位不要送給 AI」：能降低需求 7 的隱私顧慮，但會增加設定負擔、且改變「問 AI」的準確度（被排除欄位的值無法驗證）。PM 目前只要求告知（需求 7）；是否進一步做，等有真實使用者連自己的資料庫、或 Boss 指示再評估。
- 匿名匯出的硬化（Tech Lead 提出的取捨）：見下方「匿名匯出的產品立場」。
- 「最新一個月」在月初（例如資料最新日期是 10/01）只會涵蓋 1 天，使用者可能不預期——PM 的觀察，未驗證，也沒有證據使用者問過。
- 首次使用引導（原 LOW 第 6 項）：Boss 既然投入，之後可再評估，但不在這次範圍。

### Constraints
- AI 的「不遺漏」靠模型自己回報（PR #456 的「限制」段）：模型若省略條件卻沒回報，程式無法發現。所以驗收必須靠對真 AI 的量化基準，不能只靠單元測試。
- 每次提問可能有一次自我修正重跑（多一次 AI 呼叫）而只扣一次用量——成本面的觀察。**Tech Lead 2026-10-13 回覆：成立，每次提問 ≤2 次呼叫、權重 2 已涵蓋，不調整。**

### 匿名匯出的產品立場（PM 2026-10-14，假設，Boss 可推翻）
Tech Lead 在審閱 #457 時提出兩個取捨：(1) 預設來源可匿名匯出，且 App 內沒有任何 rate limiter，每次匯出是對共用 5 DTU 資料庫的整表讀取；(2) 匯出整批放記憶體，使用者自己的大表（10 萬列寬表）可能吃掉數百 MB（Tech Lead 的估算，未量測）。PM 只談產品面：
- 匿名匯出的價值是**讓人在不登入的情況下體驗示範資料**；示範資料表約 2 萬列，所以把匿名來源的匯出上限降到約 2 萬列**不會改變任何使用者看得到的行為**。若 Tech Lead 要硬化，PM 同意這個方向。
- 不建議改成「匯出一律要登入」：會讓不想登入的訪客（含可能的展示場合，A30）看不到這個功能的價值，而 PM 沒有證據顯示匿名匯出已被濫用。
- 對使用者自己的大表：**伺服器穩定優先於匯出完整**——寧可更早截斷並明確提示（需求 8 已要求提示），也不要讓行程記憶體爆掉。具體上限與做法交 Tech Lead。
- 以上都不是現在就要做的任務；PM 沒有看到實際濫用或當機的證據。Tech Lead 認為值得做時再排。

### Open Questions
- 無需 Boss 回覆。PM 小假設：**A32**（2026-10-15，可推翻）——#462 的告知是兩句話、手機上約 3～4 行，不是需求原寫的「一行」；PM 接受，理由是告知必須涵蓋實際送出的內容（見需求 7 修訂）。Boss 已合併（未提異議，但這不等於明確同意，故仍標「可推翻」）。Boss 若覺得太長，可請 Tech Lead 精簡措辭，但不要刪到與實際行為不符。
- 無需 Boss 回覆。PM 小假設：**A31**（2026-10-14）——資料外送告知（需求 7）只需一段文案，PM 先定為預設（成本近零、可逆）；是否足以滿足個資法或 OpenAI 的使用條款，PM **未查證**，也不下結論（比照 DECISION-019 對 R1 的處理：Boss 若要查證再說）。Boss 若不要這行字，說一聲即可撤回。
- 無需 Boss 回覆。PM 小假設：**A30**——Data Analysis 的 AI 提問是為了某個比賽或展示（PR #456 標題寫「比賽必修」）；PM 不知道日期與評分標準，因此不排優先序、不加需求。若 Boss 希望 PM 對齊評分標準或 demo 流程，請說一聲。

---

## Requirement: Stock Intelligence 自動趨勢線（Boss 直接交付，PR #463，2026-10-16）— 已出貨

> **性質**：PM 事後補登，不是新需求。Boss 在 2026-10-16 自己合併 PR #463（`5c6145e`，`Build and deploy` run 553 成功）。
> 此功能先前**沒有需求、沒有決策紀錄**（Tech Lead 審閱時指出），PM 補上（DECISION-030）。
> PM 只讀了程式碼與 PR／commit 說明，**沒有實機、沒有看過畫出來的線**（沙箱無法執行畫面、連不到正式站）。
> **2026-10-17 更新**：Boss 隨後合併 #467（需求 6，ETF 代號）、#469（查無資料）、#470／#471（起始日期可選、線可由使用者編輯；DECISION-034，見需求 7）。下方「現況事實」是 #463 當時的描述，與 #470／#471 不同之處以需求 7 為準。

### Goal
讓看台股的使用者在 Stock Intelligence 一眼看到「價格現在離最近的支撐／壓力有多遠」，不必自己拿尺在 K 線圖上畫線。
屬主力應用 Stock Intelligence（DECISION-009）的延伸，也是使用者再回來看大盤與個股的另一個理由（DECISION-003 留存）。

### User Story
As a 看台股大盤或個股的使用者,
I want 在 K 線圖上看到程式依固定規則自動畫出的支撐線與壓力線,
so that 我能快速知道目前價格相對於近期高低點的位置，並知道這些線是怎麼畫出來的。

### 現況事實（PM 讀 `TaiexTrendlineCard.tsx`、`StockTrendlineSection.tsx`、`TrendlineChart.tsx`、`FinanceController.cs`／`FinanceQueryService.cs` 的 `price-bars`；未實機）
- **兩個位置**：(1) `/finance` 的 Stock Intelligence 內「加權指數趨勢線」卡片（近兩年日 K，放在季節性圖之下）；
  (2)「綜合量化分析」結果內的「K 線與趨勢線」區塊（該檔個股近半年日 K）。
- 圖上有藍色支撐線、紫色壓力線，粗線＝今天仍有效的線；圖上方兩個標籤：「支撐 ≈ X（連 N 點，收盤距離 Y%）」「壓力 ≈ …」，
  沒有有效的線時顯示「目前沒有有效的線」。旁邊有 ⓘ 說明「線是怎麼畫的」（ZigZag 濾掉小於 2 倍 ATR 的波動、挑碰觸最多的線等，無人工挑日期）。
- 圖下方常駐一行：「資料至 {日期}，收盤 {價}。趨勢線僅供參考，不構成投資建議。」
- 資料取不到時：加權指數卡片顯示「加權指數資料暫時無法取得，請稍後再試。」；個股區塊顯示「這檔股票的歷史價格不足或暫時無法取得，無法畫趨勢線。」；載入中顯示骨架。
- 線由**瀏覽器**計算；後端 `GET api/finance/price-bars` 只回日 K 資料。**匿名可用**（與綜合量化分析同），不消耗 UsageLimit；個股資料來自綜合分析已快取的同一份，不增加每次造訪的對外請求。
- 門檻只用相對值（ATR 倍數），沒有寫死日期；commit 說明寫「門檻是用加權指數兩年資料調的，並用一份固定資料釘住 15 條人工審過的線」——**也就是只在加權指數上驗證過，個股的線品質沒有同等驗證**（PM 觀察，見 A33）。
- **commit 寫「no AI」不完全準確**（Tech Lead 2026-10-16 讀碼，PM 另讀 `StockCodeResolver` 確認）：台股個股若輸入的不是「恰好 4 位數字」也不是完整公司名，
  `StockCodeResolver` 會呼叫 OpenAI 嘗試解析（`comprehensive-analysis` 本來就有，`price-bars` 多一個匿名入口）；單次成本 Tech Lead 估 < US$0.001（未量）。
  **同一個行為也造成下面的「已知缺口」。**

### Requirements
1. 兩個位置各有一張日 K 圖＋自動支撐／壓力線（現況，已出貨）。
2. **永遠附警語**：「趨勢線僅供參考，不構成投資建議。」；圖上不放預測性文字、不放買賣建議、不放「突破」「看漲」這類判斷（A33）。
3. 說明線的畫法（ⓘ）；使用者可以知道「為什麼這條線在這裡」。
4. 資料不足或取不到時，明講原因，不顯示部分或錯誤的線（現況已做）。
5. 不呼叫 AI 來畫線、不消耗 UsageLimit（現況成立；唯一例外是上面的代號解析，見需求 6）。
6. **台股代號輸入（已知缺口，PM 2026-10-16 讀碼發現，未實機驗證）**：使用者輸入「看起來就是台股代號」的字串——含 ETF 的 5～6 碼，例如 `00878`、`00919`、`006208`、`00631L`——
   應直接當作代號查詢，**不應該要靠 AI 才能解析**；查不到時要顯示明確訊息，而不是無聲失敗。
   - 讀碼事實：`StockCodeResolver.IsValidStockCode` 只接受「恰好 4 位數字」。`00878` 不符合 → 先比對上市公司清單（不含 ETF）→ 找不到完整名稱 →
     **把前 100 家公司清單丟給 OpenAI 問一個 4 位數代號** → 多半回 `NONE` → 回傳空字串 → 「無法解析代號」。也就是說 5～6 碼 ETF **很可能查不到，而且每次查都花一次 OpenAI 的錢**（匿名也一樣）。
   - 不確定：LLM 對 `00878` 會不會硬湊出一個錯的 4 位數代號（PM 不知道，沒有測）；ETF 代號通過解析後，後面的資料提供者（綜合分析與 `price-bars`）能不能取到該 ETF 的日 K（PM 不知道）。
   - Portfolio 不受影響（它走 TWStockLib，不經這個解析器）。
   - **Tech Lead 後續（2026-10-16，讀碼，未實機）**：上述缺口讀碼成立；已建 **TASK-055**（READY，MEDIUM）：長得像台股代號（`^\d{4,6}[A-Za-z]?$`，與前端 `detectMarket` 同一規則）的輸入直接當代號、不經 AI；
     名稱輸入（`台積電`）與 AI 回傳值的驗證（仍只收 4 碼）一行不改。資料面推論：ETF 預期有技術面（K 線、技術指標），籌碼／基本面多半是空的、不會讓整個分析失敗。
   - **PM 回覆 Tech Lead「ETF 只有技術面是否可接受」→ 可接受**（A36，可推翻）。PM 讀 `StockScoreService`／`CategoryWeightConfig` 確認：權重只在「有資料的面向」之間重新正規化，
     所以缺籌碼／基本面**不會把 ETF 的總分拉低**。產品條件：(a) 畫面要讓使用者看得出「這個分數只用了哪些面向」，缺的面向不得顯示成 0 分或中性分（PM 未讀前端元件，Tech Lead 稱前端「以有指標的面向顯示」，待實機確認）；
     (b) 查不到的代號（例如 `99999`）要顯示明確訊息、不得無聲失敗——TASK-055 的 PR 說明會回報目前行為，PM 看過後再決定是否另開小任務。
   - **ETF「名稱」搜尋（例如輸入「元大高股息」）不在範圍**：公司清單不含 ETF，要支援得另有一份 ETF 清單。PM 同意 Tech Lead 排為 Future Consideration（LOW，沒有使用者要求的證據）。
   - **已出貨（2026-10-17 PM 讀 master；未實機）**：#467（TASK-055）——`TwStockCode.cs` 以 `^\d{4,6}[A-Za-z]?$` 判斷，符合的輸入直接當代號、不取公司清單、不呼叫 AI（17 個單元測試，含 OpenAI 零呼叫）。
     #469（TASK-059）——綜合分析在「沒有價格且沒有指標」時回 `NotFound`（`No market data found for '{code}'.`），有指標但無價格時照回、**不快取**，有價格才快取 12 小時。
     已知限制（TASK-059 工程師讀碼回報，PM 判斷不排任務，見 DECISION-033）：(a) 美股拼錯代號回 HTTP 500（非 404），量化分頁顯示固定中文「綜合分析失敗，請檢查股票代碼是否正確」；(b) 比較分頁顯示「找不到 {code}：{英文訊息}」，中英混雜；(c) Yahoo 回成功但價格為空時，provider 層快取 1 小時。
7. **起始日期可選、線可由使用者調整（DECISION-034；#470／#471，Boss 直接交付，已出貨）**：
   - 起始日期：預設近 1 年（加權指數與個股一致）；預設鈕 3 個月／半年／1 年／2 年／5 年／全部，或任選日期；加權指數最早可選到 6 年前。自動線只在選定起始日之後的 K 棒上找（換起始日會畫出不同的線）。起始日至少要留 40 根 K 棒。選擇存在這個瀏覽器（以代號為單位）。
   - 編輯：選一條線後可拖曳端點／整條移動、刪除、改成支撐或壓力、新畫（先選種類）、復原、「還原自動線」；鍵盤可用方向鍵微調、Delete 刪除。
   - 一旦改過，這張圖的線就是**使用者的版本**：存在這個瀏覽器（localStorage，以代號為單位），不會隨新資料自動重畫，**不跨裝置、不跟帳號**；畫面上已明講。「還原自動線」可回到自動版本。
   - 游標滑過圖面時 tooltip 顯示開／高／低／收與當天每條線的價位（過去的事實，不是預測）。
   - 需求 2（警語、不放預測／建議／突破文字）對使用者自己畫的線**同樣適用**；警語仍常駐於圖下方。
   - 沒有新增 AI 呼叫、沒有消耗 UsageLimit；後端唯一改動是 `price-bars` 對加權指數回傳整份快取的六年資料。

### Acceptance Criteria
- [x] 加權指數卡片與個股區塊都有日 K 圖與支撐／壓力標籤；沒有有效的線時顯示「目前沒有有效的線」（讀 `TrendlineChart.tsx`）。
- [x] 圖下方常駐「趨勢線僅供參考，不構成投資建議。」且圖上沒有預測性文字（讀 `TrendlineChart.tsx`；只要圖畫得出來，警語就一起畫出來）。
- [x] ⓘ 說明存在，且與實際規則一致（ZigZag、2 倍 ATR、連續兩日收盤穿越 0.25 ATR 或單日 1 ATR、離開 3 倍 ATR；PM 對照 commit 說明，未逐行對 `trendlines.ts`）。
- [x] 資料不足／取不到時顯示明確文字，不顯示空白圖（讀兩個元件）。
- [ ] 未登入的訪客在 `/finance` 看得到加權指數趨勢線卡片（PM 讀碼推論：路由沒有 `RequireAuth`、端點匿名；**未實機**）。
- [ ] 手機寬度（≤ 400px）圖與兩個標籤不橫向溢出、標籤可讀（**無人渲染過**）。
- [ ] 個股（不只加權指數）的線看起來合理：至少用 3～5 檔不同波動度的個股人工看過（**沒有人做過**；若 Boss 看到離譜的線，回報股票代號即可）。
- [x] 需求 6：輸入 `00878`、`00631L` 等 ETF 代號**不為此呼叫 AI**（#467 已合併；PM 讀 `TwStockCode.cs` 與其測試，**未實機**）。
- [ ] 需求 6：`00878`、`00631L` 等 ETF 代號在正式站實際查得到（技術面）。資料提供者端是 Tech Lead 讀碼推論，**未實測**；Boss 在 `/finance` 輸入一次 `00878` 即可確認（不阻擋）。
- [ ] 需求 6：ETF 的結果頁看得出「分數只用了哪些面向」，沒有資料的面向不顯示成 0 分或中性分（PM 未讀前端元件，**未驗證**，待實機看一次 `00878`）。
- [x] 需求 6：輸入不存在的 TW 代號（如 `99999`）得到明確的「找不到」而不是空白畫面，且不快取（#469 已合併；PM 讀 `FinanceQueryService.cs:336`，**未實機**）。美股拼錯代號是 500＋固定中文訊息，見上方已知限制。
- [ ] 需求 7：手機（≤ 400px）可選起始日期、選取一條線並刪除／拖曳，且在沒選線時仍可正常捲動頁面（PM 讀到 `TrendlineOverlay.tsx` 只在選線或畫線中才停用瀏覽器捲動；**無人實機操作過**）。
- [ ] 需求 7：改過的線在重新整理後仍在、按「還原自動線」可回到自動版本、清掉瀏覽器資料後回到自動版本且不報錯（`trendlineDrawings.ts` 與測試涵蓋；**未實機**）。
- [ ] 需求 7：改起始日後，個股（不只加權指數）的線仍合理（延續上面「個股的線」那條，**沒有人驗證過**）。

### Priority
- 趨勢線本身（含需求 7 的起始日期與編輯）：已出貨，**無新工作**。
- 需求 6（ETF 代號）：**MEDIUM**（已出貨）。理由（事實＋推測分開）：事實——Boss 自己的 Portfolio 與回落煞車就持有並討論 0050（4 碼，不受影響）、00631L、00685L（5～6 碼，會受影響）；
  推測——台灣散戶常用的 ETF 多是 5～6 碼（00878、00919、006208），所以 Stock Intelligence 的使用者很可能會輸入它們（PM 沒有用量數據，見 TASK-029）。
  不是 HIGH：ETF 的「基本面」分數本來就不太適用，PM 也不確定 ETF 在 Stock Intelligence 是否算「支援的標的」。

### Scope
#### In Scope
- 加權指數與個股的自動支撐／壓力線、警語、說明、資料不足時的訊息；需求 6 的代號輸入；需求 7 的起始日期與使用者編輯。
#### Out of Scope
- 由趨勢線產生買賣訊號、突破提醒、推播；把趨勢線接進回落煞車或 Portfolio（DECISION-013、DECISION-016 的分離原則不變）；
  演算法參數（ATR 倍數、門檻）開放給使用者調整（線的位置與期間可調，見需求 7）；美股以外的其他市場擴充；
  **使用者畫的線跨裝置／跨帳號同步**（目前只存在這個瀏覽器；沒有使用者要求的證據，DECISION-034）。
#### Future Consideration
- 在圖上疊 DECISION-016 的「過去 5 年同期間」或其他走勢。
- ETF 名稱搜尋（需另一份 ETF 清單；LOW）。
- 把後端錯誤訊息統一成中文（比較分頁中英混雜；LOW，DECISION-033）。
- ~~讓使用者調整期間~~ → 已由 #470／#471 完成（需求 7）。

### Constraints
- 成本紀律（DECISION-003、DECISION-006／007）：畫線不使用 AI。代號解析目前是匿名可觸發的 AI 呼叫，與「每個 AI 端點受用量限制」的原則不一致——
  Tech Lead 已評為「技術債，不排任務，異常再升級」；PM 同意**現在不為它單獨排工**，但需求 6 若被實作（代號形狀的輸入不再經 AI），會一併消除最可能的觸發情境。
- 合規：Finance 的支撐／壓力價位屬於「看起來像建議」的內容，與 R7 同類；Boss 已決定暫不處理（DECISION-019），PM 不再提醒，警語保留。

### 假設（皆為 PM 補充，可推翻）
- A33：趨勢線是「描述過去價格的輔助線」，不是買賣訊號。因此圖上不得出現預測或建議性文字（需求 2）。若 Boss 想加「突破／跌破」提示，那是新範圍，請先告知，PM 會重新評估 R7。
- A34：需求 6 的優先序（MEDIUM）依 Boss 持有 5～6 碼 ETF 的事實與「散戶常用 ETF」的推測；若 Boss 認為 Stock Intelligence 只做個股、不做 ETF，說一聲，PM 會把需求 6 改成「查 ETF 時明確顯示不支援」。
- A36（2026-10-16）：Stock Intelligence 對 ETF「只有技術面」可以接受（Tech Lead 讀碼推論籌碼／基本面多半為空；PM 讀碼確認空面向不會拉低總分）。
  理由：Boss 自己持有並討論的就是 ETF，能查到價格走勢與趨勢線已有價值，比「查不到還花一次 AI」好得多；ETF 的基本面分數本來就不適用。條件見需求 6。
- A37（2026-10-17）：「查無資料」的後端訊息維持英文（`No market data found for '{code}'.`），與既有 `Unable to resolve stock code…` 同風格；中英混雜不排任務（LOW）。Boss 若覺得要統一成中文，說一聲即可。
- A38（2026-10-17）：使用者自己畫／改的線屬使用者的工具，不受 A33「不放預測／建議」限制（A33 針對程式自動產生的內容）；但警語仍常駐。線只存在這個瀏覽器、不跨裝置同步，PM 接受（畫面已告知）。Boss 若要跨裝置，是新需求（需要後端儲存與帳號關聯）。

### Open Questions
- 無需 Boss 回覆。（PM 無法自己驗證 `00878`：沙箱連不到正式站與 OpenAI。Tech Lead 請 PM「手動試 `00878`」——PM 做不到，已改請 Tech Lead 以讀碼＋既有測試確認端到端行為，見下。Boss 有空在 `/finance` 輸入一次 `00878` 也行，**不阻擋**。）

### Tech Lead Actions
- [x] 確認需求 6 的讀碼結論是否成立：5～6 碼 ETF 代號（`00878`、`00919`、`006208`、`00631L`）在 `comprehensive-analysis` 與 `price-bars` 端到端是否能取到資料（解析器之外，資料提供者端也要看）。
  ——Tech Lead 2026-10-16：讀碼成立；資料提供者端預期 ETF 有技術面、籌碼／基本面為空（未實測）。
- [x] 若成立，評估一個**小任務**：代號形狀的輸入（含 5～6 碼、尾端英文字母）直接當代號、不經 AI；查不到時回明確訊息。PM 不指定實作。若 ETF 後面的資料提供者本身就取不到 ETF，請告知 PM，需求 6 改為「明確顯示不支援」。
  ——已建 TASK-055（READY）；ETF 資料提供者取得得到技術面，不需改為「不支援」。
- [x] TASK-055 的 PR 說明回報的兩件查證（不存在代號目前回什麼、ETF 在各資料提供者的預期資料）到後，PM 決定是否為「查不到的明確訊息」另開小任務。
  ——Tech Lead 已自行排 TASK-059（#469，已合併）；PM 同意，並回覆文案與優先序（DECISION-033）。
- [x] 其餘（匿名 AI 呼叫的技術債）維持 Tech Lead 現有判斷，不排任務。（#467 後，代號形狀的輸入已不經 AI，最可能的觸發情境已消除。）
- [x] `TASK-055.md`、`TASK-059.md` 的 Status 改為 `MERGED` 並填 Review——Tech Lead 2026-10-17 已做（`142f4c0`）。
- [x] Tech Lead 事後審閱 #470／#471（Boss 直接合併、未走 TASK）——2026-10-17 已做：無須追加修正；警語仍在、新增文字沒有「突破／買賣／預測」類用語；localStorage 讀取逐條驗證、壞資料只丟那一條（讀碼）。
- [ ] 仍沒人驗證（Boss 目視）：手機觸控下拖曳線與頁面捲動是否互相干擾（Tech Lead 與 PM 都沒渲染過）；編輯過的線在新資料進來後的行為與畫面文字是否一致（畫面文字已寫明「不會再隨新資料自動重畫」）。
