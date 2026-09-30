# TASK-019

## Title
SEO/Branding: 依 Boss 2026-09-30 決定改用各模組產品名稱（取代「JackyAI」品牌方案）

## Owner
Frontend (server 端字串為小改動，同一 owner)

## Status
PR_OPENED

## Priority
HIGH

## Objective
各模組顯示名稱與 SEO 副標改為 Boss 決定的新名稱；字典頁 title/description 不再出現「EngAI Learn」。

## Requirements
名稱對照（Boss 決定，不可自行改動；2026-09-30 Boss 已確認 Data Analysis）：

| 現在 | 新名稱 | SEO 副標 |
|---|---|---|
| My Portfolio | Wealth Portfolio | 投資組合與資產追蹤 |
| Stock Intelligence & Market Analysis | Stock Intelligence | AI 股票分析與市場情報 |
| English Learning | English Coach | AI 英文學習與單字訓練 |
| 職缺搜尋 (Job Search) | Job Finder | 職缺搜尋與工作媒合 |
| 資料分析 Demo (Data Analysis) | Data Analysis | AI 資料分析與自然語言查詢 |
| 儀表板 (Dashboards) | Data Dashboard | 資料視覺化與互動儀表板 |

- `constants/apps.tsx`：`name` 改新名稱、`description` 改為（或納入）SEO 副標。
- 頁面標題同步：`Portfolio/index.tsx`、`Finance/index.tsx`、`JobSearch/index.tsx`、
  `Dashboard/DashboardList.tsx`（標題文字；內文用語如「我的儀表板」可維持，不需硬改）。
- `index.html`：`<title>` 改為 `English Coach - AI 英文學習與單字訓練`（站台首頁以已被收錄的
  英文模組為主），並加 meta description。**不得動 PR #386 的 verification meta。**
- `WordSeoTextFormatter.cs`：單字頁 title 改為 Boss 指定格式 `English Coach - {word}`
  （例：`English Coach - Apple`；不再有 `EngAI Learn Dictionary` 後綴），Helmet 與
  `BotPreviewMiddleware` 共用。Exam 等非單字頁的 title 不帶單字，只用模組名稱／`English Coach`。
- 更新受影響的既有測試。

## Acceptance Criteria
- title/meta/OG 與 UI 上不再出現 `EngAI Learn`、`My Portfolio`、舊 Finance 標題。
- 名稱與上表一致；既有測試更新並通過；無其他行為變更。
- 路由 URL 不變（不改 path，避免 SEO/書籤失效）。

## Technical Notes
- grep `EngAI Learn` 與上表舊名稱（client + server）；註解內的「市場儀表板」等不需改。
- Boss 已採納 `Data Analysis`（2026-09-30），無待確認項目。
- 單字頁 title 採 Boss 指定的「品牌在前」格式；若日後要以單字為 SEO 首位，需 Boss 再決定。

## Dependencies
PR #386 合併（同檔 `index.html`，避免衝突）。

## Result
- `constants/apps.tsx`：`PORTFOLIO`/`FINANCE`/`ENGLISH_LEARNING`/`JOB_SEARCH`/`DATA_ANALYSIS`/
  `DASHBOARDS` 的 `name` 改為新名稱，`description` 改為對照表 SEO 副標（此欄位驅動 Portal
  catalog 卡片文字，`App.tsx` 也用 `currentApp.name` 當全站預設 `<title>`，故改這裡等於同時
  修正各模組所有子頁籤的分頁標題）。
- 頁面內文標題同步：`Portfolio/index.tsx`（My Portfolio → Wealth Portfolio）、
  `Finance/index.tsx`（Stock Intelligence & Market Analysis → Stock Intelligence）、
  `JobSearch/index.tsx`（職缺搜尋 → Job Finder）。`Dashboard/DashboardList.tsx` 檢查後未動——
  頁面只有「我的儀表板」內文用語，不含舊品牌字串，符合任務註記可維持不改。
- `index.html`：`<title>` 改為 `English Coach - AI 英文學習與單字訓練`，新增對應 meta
  description；PR #386 的 `google-site-verification` meta 未動。
- `WordSeoTextFormatter.cs`：`FormatTitle` 由 `{word} - EngAI Learn Dictionary` 改為
  `English Coach - {word}`，Helmet（`Dictionary/index.tsx` 用 `seoTitle` 原樣渲染）與
  `BotPreviewMiddleware`（走同一 DTO 欄位）共用同一結果，不需各自改動。Exam 等非單字頁本來
  就走 `App.tsx` 的 `currentApp.name`（現為 `English Coach`），未帶單字。
- 更新測試：`WordSeoTextFormatterTests.cs`（`FormatTitle` 斷言）、
  `BotPreviewMiddlewareTests.cs`（fixture 字串與斷言的 title/og:title）。

## Tests
- `npm run lint`：pass。
- `npm run build`（tsc + vite build）：pass。
- `npm run test`（vitest）：30 files / 239 tests pass。
- 後端 `dotnet test`：此 remote session 環境未安裝 .NET SDK（`dotnet` 指令不存在），**無法執行**；
  已人工檢視 `WordSeoTextFormatter.cs` 改動（僅改一個字串常數與其插值順序）與對應測試斷言，
  改動範圍小、風險低，但請 Tech Lead 或 CI 跑一次 `dotnet test` 確認。

## PR
https://github.com/twjackysu/JackyAIApp/pull/TBD

## Review
