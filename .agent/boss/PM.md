# Boss ↔ PM 私有溝通檔

> 2026-09-30 依 DECISION-008 瘦身：已結案的舊對話已移除（完整內容見 git history，
> 決策見 `product/DECISIONS.md`）。**新的 Boss 提問改開 GitHub Issue**（見 #387），
> 本檔僅留最近一次往來。

## 未解決事項摘要（2026-10-11 更新，吸收 Boss 12:09 UTC 留言後）
- **目前沒有需要 Boss 回覆的事。** Boss 在 #431 的最新留言（00685L 併入正2、水位要含 NTSD）已記為 **DECISION-021**，需求 12 更新為 v4.3：
  (1) **NTSD 改為計入曝險**（推翻 DECISION-018 的排除）——卡片曝險與右上角水位恆為同一個數字，「不含 NTSD」對帳行移除；NTSD 仍不給建議、一律「不動」。
  (2) **00685L 與 00631L 合為一個「正2 槓桿 ETF」桶**，只給整組一個加碼／減碼／不動的結論與金額。
  PM 的小假設（可推翻）：A27（水位含 NTSD ＝ NTSD 計入卡片曝險、不給建議）、A28（兩檔都持有時股數依市值比例分攤）、A29（00685L 倍數為 2）。我看不到您附的截圖（403），依留言文字判斷。
- 先前的 Q10 已結案：您選「B＋C」＝個股、基金都計入整體曝險，但建議只針對 0050、正2 槓桿 ETF 和現金，其他持股一律「不動」（DECISION-020）。
- 回落煞車的需求 12（v4.3）已定案：卡片曝險對得起右上角水位、分桶顯示、退役「策略現金」輸入（TASK-046／PR #437 已合併）；v4.3 的調整交 Tech Lead 拆任務。
- **R1 合規：Boss 決定暫不處理（DECISION-019）**——PM 記為「Boss 已知悉、接受風險、暫不查證」，不再提醒、不再列為開放前置條件；固定警語（不構成投資建議）保留。開放全部使用者前只剩需求 9 的可觀測性觀察。
- PR #432／#433／#435／#436 都已由 Boss 合併（合併即部署）；PM 複核卡片文案的回測數字已是您更正後的版本。功能仍僅 admin 可見。
- **工作方式（Boss 2026-10-11 指示）**：小事不要問 Boss。PM 之後小的模糊一律自己做合理假設並記錄、Boss 可隨時推翻；只有「法律／合規、金錢、會改變範圍或不可逆的對外曝險」才提問，且一次只問一個、附建議。
- 「歷史相似區段圖」與「過去 5 年同期間圖」（`/finance` Stock Intelligence，DECISION-016）：Boss 已同意標的（加權指數）、對象全開放、Q9 PM 預設；相似區段圖（B）Tech Lead 實測後請 Boss 決定是否排工（尚未回覆，不阻擋 A 圖）。
- Boss：Search Console 提交 sitemap 已暫緩（#407）；#407 是否關閉由 Boss 決定。
- 已結案（自舊摘要移除）：PR #386／#432／#433／#435／#436 已合併；`Data Analysis` 命名與首頁 title 已定案；Issues 流程（#387）已採用；#431 的 Q1～Q10、回測 CSV／資料快照；Q6（被 DECISION-017 取代）；R1 提醒（DECISION-019）。

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
