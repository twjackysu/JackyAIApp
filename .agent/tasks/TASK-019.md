# TASK-019

## Title
SEO/Branding: 依 Boss 2026-09-30 決定改用各模組產品名稱（取代「JackyAI」品牌方案）

## Owner
Frontend (server 端字串為小改動，同一 owner)

## Status
READY

## Priority
HIGH

## Objective
各模組顯示名稱與 SEO 副標改為 Boss 決定的新名稱；字典頁 title/description 不再出現「EngAI Learn」。

## Requirements
名稱對照（Boss 決定，不可自行改動；標記 * 者待 Boss 確認後才改）：

| 現在 | 新名稱 | SEO 副標 |
|---|---|---|
| My Portfolio | Wealth Portfolio | 投資組合與資產追蹤 |
| Stock Intelligence & Market Analysis | Stock Intelligence | AI 股票分析與市場情報 |
| English Learning | English Coach | AI 英文學習與單字訓練 |
| 職缺搜尋 (Job Search) | Job Finder | 職缺搜尋與工作媒合 |
| 資料分析 Demo (Data Analysis) | Data Analyst* | AI 資料分析與自然語言查詢 |
| 儀表板 (Dashboards) | Data Dashboard | 資料視覺化與互動儀表板 |

- `constants/apps.tsx`：`name` 改新名稱、`description` 改為（或納入）SEO 副標。
- 頁面標題同步：`Portfolio/index.tsx`、`Finance/index.tsx`、`JobSearch/index.tsx`、
  `Dashboard/DashboardList.tsx`（標題文字；內文用語如「我的儀表板」可維持，不需硬改）。
- `index.html`：`<title>` 改為 `English Coach - AI 英文學習與單字訓練`（站台首頁以已被收錄的
  英文模組為主），並加 meta description。**不得動 PR #386 的 verification meta。**
- `WordSeoTextFormatter.cs`：`TitleSuffix` 由 `EngAI Learn Dictionary` 改為 `English Coach`
  （例：`{word} - English Coach 英文字典`），Helmet 與 `BotPreviewMiddleware` 共用。
- 更新受影響的既有測試。

## Acceptance Criteria
- title/meta/OG 與 UI 上不再出現 `EngAI Learn`、`My Portfolio`、舊 Finance 標題。
- 名稱與上表一致；既有測試更新並通過；無其他行為變更。
- 路由 URL 不變（不改 path，避免 SEO/書籤失效）。

## Technical Notes
- grep `EngAI Learn` 與上表舊名稱（client + server）；註解內的「市場儀表板」等不需改。
- *「Data Analyst」字面是「分析師（人）」，作為工具名稱易混淆；已建議 Boss 改 `Data Analysis`，
  Boss 回覆前先以 `Data Analyst` 之外的項目開工，該項最後處理。

## Dependencies
PR #386 合併（同檔 `index.html`，避免衝突）。

## Result

## Review
