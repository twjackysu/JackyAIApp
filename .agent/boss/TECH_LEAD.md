# Boss ↔ Tech Lead

> 依 DECISION-008：**新的 Boss 提問與討論改在 GitHub Issues 進行**（標籤 `needs-boss` / `tech-lead`，結案即 close）。
> 本檔僅保留「未結案事項」摘要；舊對話全文保留於 git history（此檔瘦身前最後版本：`git log -- .agent/boss/TECH_LEAD.md`）。
> 已定案的技術決策見 `.agent/architecture/ARCHITECTURE.md` 與 `.agent/product/DECISIONS.md`。

## 未結案事項（2026-09-30）

1. ~~Data Analyst 命名~~ → 已定案 `Data Analysis`（Boss 2026-09-30）。
2. ~~首頁 title~~ → 已定案：首頁 `English Coach - AI 英文學習與單字訓練`；單字頁 `English Coach - {單字}`；Exam 等頁不帶單字（TASK-019 已更新）。
3. **只改顯示名稱，不改路由 URL。**
4. Search Console：#386 已合併；待 Boss 於 Search Console 驗證並提交 sitemap。
5. Phase B（TASK-020，server-side word metadata injection）Boss 已核准，排在 TASK-019 之後。
6. Credits & Payments 移除範圍規劃（DECISION-006/007）：Tech Lead 下一項工作。
7. **`.agents/` 修改**：Boss 已授權（#387），但 repo 內找不到 `.agents/` 目錄（指示檔不在此 repo）。請 Boss 告知位置，或確認由 Boss 自行加入兩條規則：(a) Engineer 開工前先 push `Status: IN_PROGRESS` 到 master；(b) 新 Boss 提問改開 GitHub Issue。
