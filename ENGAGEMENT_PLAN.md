# JackyAIApp — Daily English Engagement & Monetization Plan

_Created: 2026-03-13 | Owner: JackyOpenClaw 🦞_
_Goal: Make users WANT to come back every day to practice English → naturally convert to paying users_

## 核心理念

付費轉換的前提是 **用戶黏著度**。沒有人會為一個「用一次就忘」的工具付錢。
策略：先用遊戲化 + 習慣養成讓人每天回來 → 免費額度用完後自然付費。

---

## Problem Analysis

### Why users DON'T come back daily right now
1. **No habit loop** — 沒有「每日任務」的概念，打開 app 不知道該做什麼
2. **No progress visibility** — 學了多少字、進步了多少，完全看不到
3. **No social pressure** — 沒人知道你有沒有練，缺乏動力
4. **No reward** — 練完了沒有任何正向回饋
5. **Word of the Day is passive** — 每日一字只是展示，沒有互動

### What makes language apps sticky (Duolingo 模式分析)
1. **Streak** — 連續天數，斷了會心痛
2. **Daily goal** — 每天一個小目標（5分鐘就好）
3. **XP / Points** — 做了就有數字增長
4. **Leaderboard** — 跟別人比
5. **Push notification** — 提醒你回來
6. **Spaced repetition** — 科學複習間隔

---

## Implementation Plan

### Phase 0: Daily Challenge System (Week 1) 🎯 First Priority

**核心功能：每日英文挑戰**

每天自動生成一組 5 題混合挑戰（不需要 Repository 裡有字）：
- 1x 每日一字 填空題
- 1x 翻譯題（中→英）
- 1x 聽力題（聽音頻選單字）
- 2x 詞彙題（從 WordOfTheDay 歷史 + 常用字庫出題）

**為什麼這個最重要：**
- 降低門檻：不需要先存字就能用
- 有明確目標：「今天 5 題做完就好」
- 快速完成：3-5 分鐘搞定

**Tech:**
- Backend: `DailyChallengeController` — 每天根據日期 seed 生成固定題組（同一天同樣的題）
- Frontend: `/daily` 路由，卡片式一題一題滑
- DB: `DailyChallenge` table（UserId, Date, Score, CompletedAt）

### Phase 1: Streak & XP System (Week 1-2) 🔥

**Streak 連續天數：**
- 完成每日挑戰 = 該天 streak +1
- 連續 7 天 → 🔥 火焰圖示
- 連續 30 天 → 💎 鑽石圖示
- **Streak Freeze**: 用 credits 可以凍結 streak（一次 10 credits）→ 付費動力！

**XP 經驗值：**
- 每日挑戰全對 = 50 XP
- 完成一次 Conversation Test = 30 XP
- 查一個字 = 5 XP
- 做一次 Cloze/Translation Test = 20 XP

**Level 等級：**
- 0-100 XP = Beginner 🌱
- 100-500 XP = Learner 📖
- 500-2000 XP = Explorer 🧭
- 2000-5000 XP = Advanced 🎯
- 5000+ XP = Master 👑

**Tech:**
- DB: 在 User table 加 `CurrentStreak`, `LongestStreak`, `LastActivityDate`, `TotalXP`, `Level`
- API: `GET /api/user/stats` → 回傳所有數據
- Frontend: Dashboard widget 顯示 streak 火焰 + XP bar

### Phase 2: Smart Review (Spaced Repetition) (Week 2) 🧠

**基於遺忘曲線的複習系統：**
- 每個存到 Repository 的字都有 `NextReviewDate` 和 `Interval`
- 第一次複習：1 天後
- 答對：interval × 2.5
- 答錯：reset to 1 天
- 每天打開 app → 「你有 N 個字該複習了」

**Tech:**
- DB: `UserWord` table 加 `NextReviewDate`, `ReviewInterval`, `ReviewCount`, `EaseFactor`
- API: `GET /api/repository/due-reviews` → 今天需要複習的字
- Frontend: `/review` 頁面，flashcard 翻轉式複習

### Phase 3: Weekly Report & Progress (Week 2-3) 📊

**每週學習報告：**
- 本週學了幾個新字
- 做了幾題
- Streak 持續幾天
- XP 增長圖表
- 「你比 X% 的使用者更努力」（即使只有少數用戶也可以算）

**Tech:**
- API: `GET /api/user/weekly-report`
- Frontend: 漂亮的圖表（用 Recharts）
- Optional: 每週日發 email 報告（需要 user 同意）

### Phase 4: Daily Free Credits + Credit Gates (Week 3) 💰

**把現有 credit system 跟 engagement 結合：**
- 每日登入 → 10 免費 credits
- 完成每日挑戰 → 額外 5 credits bonus
- 7 天 streak → 50 credits bonus
- 這些免費 credits 讓用戶嘗到甜頭 → 當免費的用完就會想買

**Credit 消耗門：**
- AI Conversation Test → 3 credits/session
- AI 股票分析 → 5 credits/analysis
- Dictionary AI 查詢 → 1 credit/word
- Exam 生成 → 2 credits/exam
- 每日挑戰 = FREE（永遠免費，這是留人的核心）
- 複習 = FREE（已存的字複習不收費）

**重點：每日挑戰和複習永遠免費。** 付費的是「進階」功能（AI 對話、深度分析）。

### Phase 5: Social & Notifications (Week 3-4) 📱

**Leaderboard 排行榜：**
- 本週 XP 排行
- 最長 Streak 排行
- 匿名顯示（用戶可以選擇顯示名稱或 "Anonymous"）

**通知系統（Web Push）：**
- 「你的 🔥 streak 要斷了！今天還沒練習」（晚上 8 點如果還沒做）
- 「你有 5 個字該複習了」
- 「新的每日挑戰已經準備好了」

---

## Revenue Model Integration

```
User Journey:
1. 聽朋友說有個免費學英文的 app → 註冊（Google OAuth）
2. 每天做每日挑戰 → 養成習慣（免費）
3. 開始存字 → 用複習功能（免費）
4. 想試 AI Conversation → 用掉免費 credits
5. Credits 用完 → 想買更多 → 💰
6. Streak 快斷了 → 用 Streak Freeze → 需要 credits → 💰
```

### 預估單位經濟：
| 項目 | 免費用戶 | 付費用戶 |
|------|---------|---------|
| 每日挑戰 | ✅ 免費 | ✅ 免費 |
| 複習 | ✅ 免費 | ✅ 免費 |
| AI 對話 | 200 credits (~66 次) | 無限（買 credit） |
| Streak Freeze | ❌ | 10 credits/次 |
| 每日登入 bonus | 10 credits/天 | 10 credits/天 |

一個活躍免費用戶的 200 credits 大約用 2-3 週耗完 → 轉換窗口

---

## Priority Order & Timeline

| Week | Deliverable | Impact |
|------|------------|--------|
| Week 1 | Daily Challenge + Streak system | ⭐⭐⭐⭐⭐ 核心 |
| Week 2 | XP + Level + Spaced Repetition | ⭐⭐⭐⭐ 黏著度 |
| Week 3 | Credit gates + Daily bonus credits | ⭐⭐⭐⭐ 收入 |
| Week 4 | Weekly report + Leaderboard + Polish | ⭐⭐⭐ 社交壓力 |

---

## Tech Decisions

1. **Daily Challenge 不用 AI 生成** — 用現有字庫 + 規則生成（省 token 成本）
2. **Streak 用 server time** — 避免時區作弊
3. **Spaced Repetition 用 SM-2 演算法** — 經典、簡單、有效
4. **每日挑戰永遠免費** — 這是 retention 的核心，不能收費
5. **前端用 MUI 現有元件** — 不引入新 UI 框架

---

## Success Metrics（一個月後驗收）

1. **DAU (Daily Active Users)**: 目標 > 10
2. **7-day retention**: 目標 > 30%
3. **Average streak**: 目標 > 5 天
4. **Paying users**: 目標 > 3 人
5. **Monthly revenue**: 目標 > token 費用 × 1.2

---

_This plan is reviewed and updated on each development cycle._

---

## Progress Log

### 2026-03-13 (Session 1)
- [x] Created ENGAGEMENT_PLAN.md
- [x] Opened PR #50
- [x] **Phase 0: Daily Challenge System — COMPLETE**
  - Backend: `DailyChallengeController` with 3 endpoints (GET challenge, POST submit, GET stats)
  - 5 question types generated from Word DB (no AI calls = zero token cost)
  - Date-seeded deterministic generation (same questions for everyone per day)
  - Frontend: `/daily` route with QuestionCard, StatsBar, ResultScreen
  - RTK Query API slice with cache invalidation
  - Added "🔥 每日挑戰 (Daily)" tab to English Learning nav
- [x] **Phase 1: Streak & XP — Backend COMPLETE**
  - User model: CurrentStreak, LongestStreak, LastStreakDate, TotalXP
  - DailyChallengeResult entity with unique (UserId, Date) constraint
  - Streak logic: auto-increment, auto-reset, longest tracking
  - XP: perfect = 50, else score × 8
  - Level system: Beginner 🌱 → Master 👑
  - Stats endpoint with live streak validation
- 0 lint errors, TypeScript strict pass, .NET build pass
### 2026-03-13 (Session 2 — Heartbeat)
- [x] **Phase 2: Spaced Repetition (SM-2) — COMPLETE**
  - Backend: `ReviewController` with 3 endpoints (GET /due, POST /submit, GET /count)
  - SM-2 algorithm: ease factor (min 1.3, default 2.5), interval scheduling
  - Quality ratings 0-5: wrong resets to 1 day, correct multiplies interval by ease factor
  - UserWord model extended with 5 new fields for review tracking
  - XP: 3 per correct review word
  - Batch review: max 20 words per session
  - Frontend: `/review` route with flashcard UI (reveal → rate)
  - Result screen with retention rate
  - Added "🧠 複習 (Review)" tab
- 0 lint errors, 0 TS errors, 0 .NET warnings
### 2026-03-13 (Session 3 — Heartbeat)
- [x] **Phase 3: Credit Gates + Daily Bonus — COMPLETE**
  - CreditCosts.cs: centralized cost constants (Dictionary=1, Cloze=2, Translation=2, Conversation=3, Stock=5, TTS=1)
  - Credit gates on ExamController: cloze, translation, conversation start
  - Daily login bonus: POST /claim-daily-bonus → 10 credits/day
  - Challenge completion: +5 credits
  - Streak milestones: +50 credits at 7, 30, 100 days
  - Frontend: ResultScreen shows credits awarded, claimDailyBonus API
- 0 lint/TS/build errors
- **Next:** Phase 4 (Weekly report + leaderboard), credit gates on remaining AI endpoints (Dictionary, Finance, Audio)
