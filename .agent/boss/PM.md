# Boss ↔ PM 私有溝通檔

> 2026-09-30 依 DECISION-008 瘦身：已結案的舊對話已移除（完整內容見 git history，
> 決策見 `product/DECISIONS.md`）。**新的 Boss 提問改開 GitHub Issue**（見 #387），
> 本檔僅留最近一次往來。

## 未解決事項摘要
- Boss：合併 PR #386（Search Console 驗證）→ 按驗證 → 提交 sitemap。
- Boss：確認 `Data Analyst` vs `Data Analysis` 命名、首頁 title 預設（Tech Lead 待答）。
- Tech Lead：評估 Issues 流程對 TECH_LEAD.md／TASK／READY task 認領的影響（Issue #387）。

---
# PM → Boss (2026-09-30)

## Status
回覆您「溝通方式 / PM.md 越來越大」的問題。需要您選方向（見下），其餘無變動。

## 我的看法
問題有兩個：(1) 您要滑到最底才看得到新訊息；(2) 每次 Agent 啟動都要讀整份檔案，
PM.md 目前約 52KB，是實際的 token 浪費。**Decisions／Requirements／Tasks 這些
「結論型」文件沒問題，真正膨脹的只有「對話型」的 PM.md 與 TECH_LEAD.md。**

## 選項
- **A. 維持 .agent，但瘦身**：已解決的舊對話移除（git history 完整保留，
  決策本來就在 DECISIONS.md），最新訊息放最上面。最省事、零新工具，但
  仍是單一長檔，體感改善有限。
- **B. GitHub Issues（建議）**：repo 已在 GitHub，免費，Agent 已有 GitHub 工具可讀寫。
  每個話題一個 Issue（標籤如 `needs-boss` / `pm` / `tech-lead`），
  討論在 Issue 留言串內，結案就 close；您手機收得到通知、可直接在 App 回覆，
  只需看 `needs-boss` 標籤即可，不必翻檔案。Agent 每次只讀開啟中的 Issue，
  token 用量大幅下降。可再搭配免費的 GitHub Projects 看板追蹤 TASK 狀態。
  `.agent/product/*`、`DECISIONS.md`、`tasks/` 維持不動。
- **C. 自建於網站內**：可做但需要開發成本，且與目前「留存、降低成本」目標無關，
  也要自己維護登入與通知，**目前不建議**。
- **Jira 免費版**：我尚未查證其目前的免費額度與限制，且不確定 Agent 是否有
  可用的整合，不當作事實下結論；相較 B 沒有明顯優勢。

## 建議
選 **B**，並先做 **A 的一小步**：核准後，PM 將 PM.md 已結案的舊對話移除（保留
未解決事項摘要在最上面），之後新的 Boss 問題改開 Issue。

## Tech Lead Actions
- 我無權修改 TECH_LEAD.md，請您（或 Tech Lead 讀到此段時）評估 B 對 Tech Lead
  流程的影響：TASK 檔是否改為 Issue／Project 看板、排程 Agent 搶同一 READY task 的
  問題（PROJECT_STATUS 已列為待答）是否能藉 Issue assignee 一併解決。

## Boss Attention Required
1. 選 A / B / C（或其他）？核准前我不會刪除或搬移任何 PM.md 內容。
2. 目前仍待您處理：合併 PR #386（Search Console 驗證）、確認 Data Analyst 命名。


---
# Boss → PM
太好了!!我覺得這個提議不錯，我們改用擬建議的B方案
你再幫我通知Tead Lead，感謝

---
# PM → Boss (2026-09-30 二次回覆)
已依您核准的 B 方案：(1) 開 Issue #387 通知 Tech Lead；(2) 記錄 DECISION-008；
(3) 本檔瘦身。TECH_LEAD.md 我無權修改，留給 Tech Lead 評估。您目前無需再做事，
除上方「未解決事項」兩項。
