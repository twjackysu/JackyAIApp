# JackyAIApp — Product Roadmap

_Owner: JackyOpenClaw 🦞 | Audience: CEO / daily bot_  
_Last updated: 2026-04-04_

---

## 戰略框架

這個 app 已有完整功能骨架（英文學習 + 股票分析 + 付費系統），但還沒真正「起飛」。
作為 CEO，每一個開發決策都要回答：**這會讓更多人付錢嗎？為什麼？**

### 核心指標 (North Star Metrics)
| Metric | 現狀估計 | Q2 目標 | Q3 目標 |
|--------|---------|---------|---------|
| DAU (Daily Active Users) | ~2-5 | 50 | 200 |
| 7-day retention | ~10% | 35% | 50% |
| Paying users (累計) | ~0 | 10 | 40 |
| MRR | ~$0 | $50 | $200 |

### 增長漏斗分析
```
Awareness   → SEO幾乎為零（SPA無法被索引），無病毒傳播機制
First Visit → 落地在Finance頁，沒有landing page，用戶不知道這是什麼
Activation  → 沒有onboarding，用戶自己摸索
Retention   → 沒有推播通知，用戶忘了回來
Revenue     → Credit gates存在，但從「credits用完」到「付款」的路徑不順
Referral    → 無推薦機制
```

### 每個功能的投資邏輯
開發前必須能回答：
- **誰會用？** 具體描述目標用戶行為
- **為什麼現在？** 在漏斗的哪個位置卡住了？
- **成功指標？** 怎麼知道有沒有效？

---

## Q2 2026 Roadmap (April — June)

### Month 1 (April): Retention — 留住已有用戶

現有用戶最大的問題是「忘了回來」。先解決這個，才有意義拉新用戶進來。

#### Sprint 1: Smart Upsell & Credit Experience
- [ ] **Insufficient credits modal**: 點 AI 功能時 credits 不足 → 彈出 modal，顯示目前餘額、功能費用、直接跳到購買頁的 CTA（現在是直接 error，體驗很差）
- [ ] **Credits running low banner**: 餘額 < 50 credits → navbar 顯示橙色警示 + "Buy Credits" 按鈕（讓用戶在用完前就知道）
- [ ] **Credit cost preview**: 在每個 AI 功能按鈕旁顯示小 chip 說明費用，例如 "5 💎" (目前用戶不知道要花多少，點了才發現不夠)

**為什麼先做這個：** 付費轉換漏斗最後一步要順暢。Credits 用完 → 購買的路徑現在有 UX 斷點。

#### Sprint 2: Streak Freeze & Daily Habit Loop
- [ ] **Streak Freeze**: `/daily` 頁面底部加「保護連續天數」功能，花 10 credits 可以 freeze streak 一天（為有錢但沒時間的用戶提供付費理由）
- [ ] **Daily Check-in Bonus**: 登入首頁顯示「今日已領取 10 credits」確認訊息，讓用戶知道每天登入有獎勵（目前後端有這邏輯但前端沒有明顯展示）
- [ ] **Streak milestone celebration**: 達到 7 / 30 / 100 天時，全螢幕 confetti 動畫 + "分享你的成就" 按鈕（連結社群平台）

**為什麼：** 讓習慣養成有儀式感，Streak Freeze 創造直接收入。

---

### Month 2 (May): Acquisition — 讓新用戶找到你

#### Sprint 3: Landing Page & SEO Foundation
- [ ] **Landing page (`/`)**: 把預設路由從 Finance 改為專屬 landing page，包含：
  - Hero: 大標題 "用 AI 學英文 × 看懂股市，每天 5 分鐘" + Sign in CTA
  - Feature showcase: 3 個 key feature cards（AI 英文對話、股票量化分析、每日挑戰）
  - Social proof section: "X 個用戶正在學習中" + 示例每日挑戰（可互動，免登入）
  - Pricing preview card
- [ ] **SEO meta tags**: react-helmet 加入各頁面的 title / description / OpenGraph，讓分享連結有預覽圖
- [ ] **Sitemap + robots.txt**: 讓 Google 可以索引靜態路由

**為什麼：** 沒有 landing page 就是把所有流量都推走。這是 acquisition 最大槓桿。

#### Sprint 4: Referral System
- [ ] **Referral code generation**: 每個用戶有唯一 referral code，`/api/account/referral-code` endpoint
- [ ] **Referral reward**: 被推薦用戶首次登入 → 雙方各得 50 credits（超過免費的 200 積分不重置）
- [ ] **Referral UI**: Profile dropdown 加 "邀請朋友" 選項，顯示個人連結 + 複製按鈕 + 已成功邀請人數
- [ ] **Shareable achievement cards**: 在 Stats 頁新增 "分享" 按鈕，生成含 streak / level / weekly stats 的圖卡（可下載 / 直接分享）

**為什麼：** 有機增長最便宜。讓每個用戶帶 1.1 個新用戶就能指數增長。

---

### Month 3 (June): Product Depth — 提升付費意願

#### Sprint 5: Finance Feature Enhancement
- [ ] **Stock watchlist**: 無需分析，直接 "收藏" 股票 → GET `/api/finance/watchlist` 顯示自選股清單（基本報價）
- [ ] **US stock support**: 加入 Yahoo Finance API 支援美股（AAPL, NVDA 等），TAM 從台灣 2300 萬擴展到全球
- [ ] **Market sentiment dashboard**: 整合台股大盤指標（外資、融資、VIX）做成 dashboard 首頁，每次開 app 就能快速掌握市況
- [ ] **AI analysis history**: 儲存用戶歷史股票分析記錄，可以回顧 AI 過去預測（增加黏著度）

**為什麼：** 財經功能的用戶 ARPU 天花板更高（投資人願意付更多）。加 US stocks 打開全球市場。

#### Sprint 6: English AI Enhancement
- [ ] **Conversation scenario expansion**: 新增 "工作面試"、"商務會議"、"旅行英文" 等情境（目前 scenarios 有限）
- [ ] **AI personalized word recommendations**: 根據用戶錯題歷史，推薦相關詞彙 (`/api/repository/recommendations`)
- [ ] **Pronunciation scoring**: 用 Whisper 分析用戶發音，給出 0-100 分 + 改進建議（高感知價值功能）
- [ ] **Grammar correction inline**: Conversation test 中，實時在 UI 標示語法錯誤（而不只是在結果頁）

**為什麼：** AI English coach 是最高感知價值的功能，也是最容易說服人付費的理由。

---

## Backlog (Q3 and beyond)

- [ ] **PWA + Web Push Notifications**: 安裝到手機桌面 + "你的 streak 快斷了！" 推播
- [ ] **Email weekly report**: 每週日發學習摘要 email（需收集 email 地址）
- [ ] **Mobile app** (React Native): 如果 web 驗證成功，考慮 native app
- [ ] **B2B / Team plan**: 公司購買給員工學英文（$30/月/seat）
- [ ] **Content marketing**: AI 趨勢分析的文章可以 SEO → 帶流量進來

---

## How the Daily Bot Should Work

**每次執行流程：**

1. Clone 最新 main branch
2. 閱讀此 PRODUCT_ROADMAP.md
3. 找當月 Sprint 中第一個 `- [ ]` 未完成的任務
4. **認真評估這個任務：**
   - 閱讀相關的現有代碼（Controller、前端組件、API slice）
   - 思考最小可行實作方案
   - 確認不會破壞現有功能
5. 實作 + 測試（至少確保 TypeScript compile + .NET build 不報錯）
6. 把 PRODUCT_ROADMAP.md 中的 `- [ ]` 改為 `- [x]`，並在任務後補充完成備註
7. 一個 commit 提交所有變更（code + roadmap update）
8. Push + 開 PR

**任務評估原則：**
- 如果任務需要資料庫 migration，要確實寫 EF Core migration（`dotnet ef migrations add`)
- 如果前端要加 API call，要同時更新 RTK Query API slice
- 如果任務太大（估計超過 800 行改動），拆成第一個 PR 做 backend、留 frontend 給下次
- 如果遇到 blocker（例如需要 API key），標記為 `- [⚠️ blocked]` 並說明原因，改做下一個任務

---

_此文件是 Bot 的工作指令，同時也是產品路線圖。人和機器都應該能讀懂。_
