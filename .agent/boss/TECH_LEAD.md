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
6. Credits & Payments 移除：Tech Lead 已規劃完成（TASK-021~024，設計見 ARCHITECTURE.md）；待 Boss 確認事項見 Issue #394（每日預算 60、TASK-024 DROP 同意）。
7. **工作流程規則**（Boss 2026-09-30 說明目錄是 `.agent/`，非 `.agents/`）：已寫入 `ARCHITECTURE.md` 的「Workflow conventions」：(a) Engineer 開工前先 push `Status: IN_PROGRESS` 到 master；(b) 新 Boss 提問改開 GitHub Issue。若 Agent 指示檔另有存放處需同步，請 Boss 告知。
8. TASK-019 已合併（#388）；TASK-020 已合併（#389，MERGED）。
9. 2026-10-03：Issue #411（MUI 升 9.4.0）→ 已建 TASK-027（READY，Frontend）。Issue #407（SEO）→ 已於 Issue 回覆調查結果。
10. 2026-10-04：TASK-027 已 MERGED。Issue #409 → TASK-028（Portal 今日行動）已 MERGED（#414）；Portal 顯示投資組合變化列為 Future Work（需輕量後端端點，待優先順序決定）。
11. **待 Boss 決定（用量統計，#409）**：Tech Lead 建議 Azure App Service 已有 Application Insights 時優先用它（零前端程式、免費額度內、不引入第三方 cookie）；否則 Matomo 需自架（運維成本）、GA4 免費但有隱私/cookie 揭露。自建後台不建議。請 Boss 指定方案，再開 task。
12. 2026-10-04：用量統計已定案 Application Insights（DECISION-010）→ 建立 TASK-029（READY）。後端已有 `UseAzureMonitor`，但 SPA 路由切換不產生 server request，故採「瀏覽器端 SDK 動態載入 + `GET /api/config/telemetry` 提供連線字串」，不需改 CI。**待 Boss 決定**：browser SDK 會使用 cookie（`ai_user`/`ai_session`）存匿名 ID，是否需要隱私／Cookie 告知文字？（Tech Lead 建議：頁尾加一行簡短「本站使用匿名統計」說明即可；若要完全不用 cookie 則「不重複使用者數」會不準。）
13. 2026-10-04（回覆 Boss 關於 cookie 同意框與識別使用者）：
    - **同意對話框：不做阻擋式 banner**。台灣個資法對「匿名、不含個資」的統計不要求事前 opt-in（不同於 GDPR 對歐盟訪客）；你看到的 banner 多半是為歐盟/廣告追蹤。我們只收匿名 page view、無廣告、無第三方分享，改用頁尾一行告知即可（已寫入 TASK-029），UX 較好、實作成本近零。若日後有歐盟用戶或加廣告，再重新評估。
    - **是否識別 user**：目前**維持匿名**，不呼叫 `setAuthenticatedUserContext`。理由：(a) Application Insights 的 cookie ID 已能算「不重複訪客／回訪率」；(b) 本站登入用戶的長期留存其實可直接從既有 DB（streak、XP、最後活動日、複習紀錄）以後端查詢得到，不需把 Google 身分送到第三方遙測，隱私風險最低。若將來需要「登入用戶漏斗」，優先用後端 DB 查詢或送不可逆雜湊 ID，而非 email/Google ID。已同步 PM 知悉；若你要改為識別用戶請告訴我，屬隱私決策會再回報。

# Tech Lead ↔ Boss
是的那就用cookie吧，然後我看別的網站都會顯示是否同意的提示框，我們也會這樣做嗎?
匿名統計目前應該可以，但長期要觀測的話好像需要知道user是誰?(雖然會有隱私問題，我也不確定哪個好，你跟PM決定吧)
14. 2026-10-06：TASK-030（#418）、TASK-031（#420）已 MERGED；TASK-032 READY；新增 TASK-033（#407 單字頁內部連結，READY）。無需 Boss 決策。
15. 2026-10-07：TASK-032 已 MERGED；PR #422（TASK-033）技術審查通過，待 Boss 合併。
