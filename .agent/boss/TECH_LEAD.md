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
15. 2026-10-07：TASK-032、TASK-033（#422）皆已 MERGED。無待決事項。

## 2026-10-09 — Tech Lead：回覆 #407 新需求「不加品牌詞也能搜到所有單字」（排程執行）

- 無 open PR。Boss 於 #407 要求 `apple` / `apple english` / `apple 英文` 等一般查詢也要在第一頁。
- 技術評估：**排名無法由程式保證**。「apple」本身被 Apple 公司主導；「apple 英文」類長尾查詢可能，但取決於網域權重、收錄量與外部連結，新站需數週至數月。程式端能做的只有輔助。
- 已建 TASK-034（Backend, READY, MEDIUM）：JSON-LD DefinedTerm + sitemap lastmod。
- **待 Boss 決定**：單字頁 title 目前為 `English Coach - {單字}`（你已核准）。若要吃到「英文」類查詢，建議改為 `{單字} 英文意思、發音、例句 | English Coach`（單字置前、含中文關鍵字）。這會改你先前定案的格式，故需你同意；同意後我會另開小任務。
- Boss 可做（非程式）：Search Console 對新網域 apps.jacky-studio.com 提交 sitemap、抽測要求索引；對外分享連結（社群／PTT／Dcard／學習論壇）有助收錄。
16. 2026-10-09：TASK-034（#427）已 MERGED。你在 #407 同意單字頁 title 改為 `{單字} 英文意思、發音、例句 | English Coach` → 已建 TASK-035（READY）。排名仍無法保證；Search Console 提交 sitemap 你暫緩，無待決事項。
17. 2026-10-09：PR #430（TASK-035 單字頁 title）Tech Lead 審查通過 → READY FOR BOSS REVIEW，待 Boss 合併。部署後以 `apps.jacky-studio.com/english/dictionary/apple` 檢視 `<title>`。
18. 2026-10-10：PR #430（TASK-035）已 MERGED，部署後請以 `apps.jacky-studio.com/english/dictionary/apple` 檢視 `<title>`。#407 已回報；是否關閉由你決定。無待決事項。
19. 2026-10-10：Issue #431（回落煞車）技術設計完成，已建 TASK-036（後端訊號，READY）、TASK-037（前端卡片，READY）、TASK-038（第二階段盤中預估，BLOCKED）。設計見 `ARCHITECTURE.md`「回落煞車」。**Q1 未決前功能只有 admin 看得到**（App Setting 開關，之後改設定即可開放，不需改碼）。
    - **待 Boss**：(a) 提供回測用報酬指數 CSV（`date,close`，2003-01～2026-10-08）作為測試 fixture——不阻擋開工；(b) Q1／Q2／Q3 請回覆 #431（PM 已列出）。
    - **風險（技術面）**：Portfolio 全域沒有股票分割處理（0050 2025-06-18、00631L 2026-03-31），本功能以「快照早於分割日就不顯示股數」防護；證交所格式無法在排程沙箱驗證，改由 GitHub Actions LiveContract 驗證。合規屬 Boss／PM 決定，Tech Lead 不下結論。
20. 2026-10-11：**PR #433（TASK-036 後端）、PR #432（TASK-037 前端）Tech Lead 審查通過 → READY FOR BOSS REVIEW**（兩者 CI 皆綠；不代表核准合併，由 Boss 決定）。審查細節在各 task 的 Review 區與 PR 留言。
    - **建議合併順序**：#433 → #432（單獨先合 #432 也無害：後端沒上線時 `drawdownBrakeEnabled` 不會出現，卡片不顯示）。兩者目前都只有 admin 看得到。
    - 我另以獨立的 Python 版 Boss 參考規則重放後端的 golden 序列（1500 日），**0 筆不一致**。後端在 Actions 的 LiveContract 抓到真實格式差異並已修（MFI94U 日期表頭含全形空白）；`holidaySchedule` 格式也已確認。
    - **待 Boss（#433 部署後，約 2 分鐘回補完成）**：以 admin 開 Portfolio 卡片或呼叫 `GET /api/portfolio/drawdown-brake`，確認 `state=OFFENSE`、`drawdownPct` 約 −1%。原本的「真實資料驗收」測試因缺回測 CSV 而 **Skipped**，這是替代的 smoke check；CSV 補上後測試自動啟用（`JackyAIApp.Server.Tests/Fixtures/TaiexTotalReturn.csv`，`date,close`）。
    - **待 Boss 確認＝PM 的 Q5（不阻擋合併，但 Audience=All 前必須確認）**：卡片寫「歷史回測（1997–2026，隔日成交版本）」。#431 §2 只在「回測年化／最大回撤」欄標 1997–2026，「隔日成交版本」欄未標期間，Engineer 是依欄位順序推定。若實際期間不同，只改 `drawdownBrakeCopy.ts` 的 `BACKTEST_PERIOD`。
    - **FYI（PM 已於 A6 認可）**：再平衡文案與 §10 範例不同——範例的「漲多、升到」只適用行情推動；切換日後沒照做、曝險仍偏離時也會走這條，那時說「漲多」是錯的，改為「目前有效曝險 X 倍，高於／低於目標 Y 倍，建議…」。減碼／加碼／不動三種與範例逐字相同。要逐字照範例請告知。
    - **風險／觀察（非阻擋）**：證交所當日報酬指數的公布時間未知；若晚於 17:00，「資料未更新」會每天短暫誤報，部署後觀察，改兩個常數即可。目前沒有任何持股的使用者看不到卡片（Q1／Q3 決定後再評估入口）。
    - **仍未決**：Q1（對誰開放，合規）、Q2（提醒送達方式）、Q3（策略現金假設）尚無 Boss 回覆（#431）。TASK-038（第二階段盤中預估）維持 BLOCKED，待 #433 合併後我再轉 READY。
    - **PR #432 目前與 master 有衝突（僅文件，非程式）**：`.agent/status/PROJECT_STATUS.md`——PM 在 PR 開出後於 master 檔尾追加一段，PR #432 也在檔尾追加一段。解法＝兩段都保留（把 master 合進 #432 分支、衝突處兩邊都留）。我不替 Engineer 推他們的分支（權限邊界），請任一 Engineer session 處理，或 Boss 在 GitHub 以「Resolve conflicts」按鈕保留兩段即可。#433 與 master 無衝突。
21. 2026-10-11（排程執行）：**PR #433 已合併**；#432 仍待你合併（與 master 只有 `PROJECT_STATUS.md` 的文件衝突，GitHub「Resolve conflicts」兩段都留即可；細節在 TASK-042）。
    - **你的回測資料我驗證過了**：6 個資料檔的 SHA256 都與 README 一致（其中 5 個要先還原成 CRLF——repo 的 `.gitattributes` 把行尾轉成 LF，所以表面雜湊不同，資料本身相同）；我另以獨立的純 Python 依規則重放 `taiex_tri.csv`，**2026-10-08＝OFFENSE、距高點 −1.02%**，與你的預期一致。TASK-039 會把它變成測試 fixture，結束「Skipped」。
    - **關於你擔心的計算量**（詳見 `ARCHITECTURE.md`「回落煞車 — 重新整理、cache 盤點、可觀測性」）：個人化建議在瀏覽器算、伺服器收不到持股；伺服器對證交所的請求全來自背景服務與全站單一冷卻，**與使用者數無關**；每次檢視只讀一張約 750 列的表（TASK-041 加 60 秒快取後 ≤ 1 次／分鐘）。**計算量不是這個功能的風險點**；真正擋住「開放全部使用者」的是合規（PM 的 R1，你與 PM 的範疇）、TASK-042 的文案更正、和約 14 天觀察「資料未更新」是否誤報。你問的「13:15 會拿到被 cache 的舊值嗎」：正式訊號 13:15 本來就只有前一交易日的值（當日值 14:00 後才有），不是 cache；盤中價格最多舊 10 秒。
    - **新任務**：TASK-039（驗收資料，HIGH）、041（重新整理＋資料時間，MEDIUM）、040（可觀測性，待 041）、042（#432 文案更正＋策略部位占比＋解衝突，HIGH）、043（前端重新整理按鈕，待 041）；TASK-038（盤中預估）改為待 041。
    - **大盤走勢比較圖**：A（過去 5 年同期間）已設計並拆成 TASK-044（後端）／TASK-045（前端），不加功能開關（你已決定不限 admin、`/finance` 本來就公開、你合併 PR 就是最後一關）。**B（相似區段）我先不排工，請你看過實測再決定**：用你的 TWII.csv 實測，同一組門檻下**今天（2026-10-08）找不到相似區段**；沒有門檻時永遠找得到「最像的一段」，所以畫面會有約 7 成時間顯示「找不到」；而相似區段「之後」的走勢與現在實際後續走勢的相關係數約 +0.04（樣本重疊、樣本內，僅供量級）。9 個歷史範例表在 `ARCHITECTURE.md`「Stock Intelligence 大盤走勢比較圖」。圖仍可以做，但它是「歷史對照」而不是「訊號」，PM 的警語與「不放預測文字」是合適的。
    - **你不需要回覆任何事才能繼續**；若要做 B 或調整門檻，在 #431 回覆即可。
    - **風險／限制**：沙箱連不到證交所與 Yahoo（403）、也沒有 .NET，格式與測試結果以 GitHub Actions 為準；可觀測性的 KQL 是初稿、未實測，首批資料進來後需校正欄位名。
22. 2026-10-11（排程執行，第 2 次）：**無 open PR**（#432／#433／#435／#436 皆已合併；TASK-042 → MERGED，我事後對照 `reproduce_spec.txt` 驗證卡片數字一致）。TASK-041／044／045 Engineer 進行中、尚無 PR。
    - **你在 #431 的新需求（整個流動資產納入、分桶、NTSD 排除、其他持股一律不動）已完成技術設計**：`ARCHITECTURE.md`「回落煞車 — 以整個流動資產判斷曝險、分桶顯示」；任務 **TASK-046（Frontend，READY，HIGH）**。**純前端，後端與 API 不動**；曝險一律用既有的 `exposureFactor`，所以沒有 NTSD 時卡片曝險與右上角水位是同一個數字（有單元測試對帳）。TASK-043（重新整理按鈕）改排在 046 之後，避免同一張卡片衝突。
    - **核心算法**（0050／00631L／現金調整到讓「整體」曝險盡量接近目標，其他持股只計入曝險、不給建議）與 3 個情境（進攻可達、切換防守且其他持股單獨超過目標、進攻但其他持股太多）已貼在 #431 給你看。**情境金額是示意，不是你的實際持股**（我看不到你的持股資料）。
    - **需要你知道（不阻擋開工，可推翻，各只改一處）**：(1) 最低現金 k 以「策略範圍資產」為基準（不是只看核心）；其他持股占比很高時，核心可投入的錢會被 k 壓得很少（情境 3）。(2) 現金類包含債券／貨幣型 ETF，執行時可能需要賣它們或換匯，卡片只標金額、不給建議。(3) 範圍內任何一檔持股價格抓失敗，就暫停整張卡片的建議並列出是哪幾檔（避免用錯的曝險給錯的股數）。
    - **你仍不需要回覆任何事才能繼續**。待你的事項不變：#431 的 Q1（誰能看）依你的決定先維持 admin；合併 TASK-046／041／044／045 的 PR 時再看。
23. 2026-10-11（排程執行，第 3 次）：**#437～#442 皆已由你合併並部署成功**（Build and deploy run 526～531），六個任務 → MERGED；TASK-038（盤中預估）轉 READY。
    - **你在 #431 的兩點修正**（00685L 併入 00631L、水位含 NTSD）：PM 已記錄 DECISION-021／需求 12 v4.3，**我的技術設計與 PM 的假設一致**。任務 **TASK-047**（Frontend，HIGH）；TASK-048（Backend，LOW，只加測試鎖定 00685L 倍數＝2）。改動小、純前端，不碰後端與 API。「改版前後對照」（持有 NTSD＋00685L）貼在 #431：改版前卡片曝險 1.077 對不上水位 1.133；改版後兩者相同。
    - ~~想請你回答一題：00685L 有做過股票分割嗎？~~ → **已回覆（Boss，2026-10-11）：沒有分割（上市未久）**，故不在 `SPLIT_EVENTS` 加條目；日後若分割，在表中加一行。
    - **我對「NTSD 算是不同的」的解讀**：NTSD 不併入正 2 類，只計入曝險、仍不給建議（與 PM 的 A27 相同）。若你其實要 NTSD 有自己的加減碼建議，請在 #431 說一聲——那會反轉 DECISION-020，是新需求。
    - **風險**：NTSD 是美股（Yahoo 報價），價格抓失敗時，整張卡片會暫停建議（因為 E 會算錯）；TASK-047 的 Optional 項讓「重新整理」重抓失敗的持股。
    - **請你目視確認**：`/finance` 的「過去 5 年同期間」圖是否有真實加權指數資料（#439／#440 已部署；我的沙箱連不到正式環境）；KQL 與 App Insights 的 `drawdownbrake` 指標（TASK-040）部署後貼查詢驗證。

24. 2026-10-11（排程執行，第 4 次）：**#444／#445／#446 皆已由你合併**（TASK-047／038／048 → MERGED），我事後審閱：後端盤中預估我逐行讀了，**沒有需要追加修正的地方**；`ARCHITECTURE.md` 已更新。MIS 經 TWStockLib 的實際路徑已用 Actions 實測（contract-tests run 102 通過）。
    - **你問「00685L 沒跟 00631L 合併、NTSD 沒含進去，是誰還沒做？」→ 沒有人漏做，是部署把正式環境回滾了**：TASK-047 在 #444（13:15 UTC）就合併部署了；13:50 UTC 你合併 #445 的同時 #443 也被標為已合併，兩個部署同時跑，#443 部署的是它自己的舊分支頭（**不含 TASK-047**），而且比 #445 的部署**晚完成 1 分鐘**，把正式環境蓋回舊版；直到 #446 合併、run 535 於 14:46 UTC 完成才回到最新。你截圖的畫面（「不含 NTSD」、只有 00631L 一列）正是那個舊版。**請強制重新整理（Ctrl+Shift+R）再看**；SPA 分頁不重新載入不會換新的前端。若重整後仍是舊畫面，請告訴我，那就是另一個問題。
    - **新任務 TASK-049（HIGH，READY）**：部署工作流程加 `concurrency`、並改成部署執行當下的 master 最新（不再用 `merge_commit_sha`），避免再發生「舊的蓋掉新的」。它會改變 CLAUDE.md 寫的既有部署規則，**合併前請你看一眼**；不同意可退回成只加 `concurrency` 的最小版本（任務書有寫差異）。在它合併前：**某個 PR 的提交已被另一個 PR 包含時（像 #443 ⊂ #445），請直接 close 不要 merge。**
    - **我沒辦法直接看正式環境**（沙箱連不到 `apps.jacky-studio.com`），結論來自 GitHub Actions 紀錄與 git 歷史，不是目視。
    - **請你在 2026-10-12（週一）盤中目視確認盤中預估**（TASK-038 的未實測部分）：09:00 起以 admin 開 Portfolio 回落煞車卡片，應出現「預估」區塊、資料時間隨 MIS 前進；14:00 後正式資料入庫時預估消失；若預估狀態與正式不同會出現「訊號已更正」。重新整理按鈕現在也會一併取最新預估（最短 10 秒）。
    - **仍未決**：Q1（誰能看）、Q2（提醒送達方式）、Q3（策略現金假設）。你不需要回覆任何事才能繼續。
25. 2026-10-12（排程執行，第 5 次）：**無 open PR、無新的 Boss／PM 留言**；唯一動作是 **TASK-049（#447）事後審閱並結案**——你選的 B2（push 觸發＋`paths-ignore`＋`concurrency`＋部署 master 當下最新）我逐行讀過，**認可**，比我原本寫的 `pull_request: closed` 版本更乾淨（push 事件沒有 `merge_commit_sha` 指到舊分支頭的問題）。run 537（push 觸發）已成功。
    - **你需要知道的行為改變**（已在 `CLAUDE.md`／`ARCHITECTURE.md`）：含程式碼的直接 push 到 `master` 現在會部署（含 EF migration）；只動 `.agent/**`／`*.md` 的 push 不會。Agent 的程式碼一律走 PR，這點不變。
    - **未實測、請順手留意**：第一次連續合併兩個 PR 時，Actions 裡較早排隊的 run 應顯示 *cancelled*，最後一個 run 部署 master 最新——那是預期行為，不是失敗。若最後一個 run 是紅的，重跑它（或從 `master` 手動 dispatch，**不要從舊分支**）。
    - **不需要你回覆任何事**。今天（週一）仍請你盤中目視確認盤中預估（第 24 項）。

26. 2026-10-12（排程執行，第 6 次）：**無 open PR、無需你決定的事項；沒有程式碼變更。**
    - **你直接合併的 #449（每個功能自己的分享預覽）、#448（`UserWords` 唯一索引＋刪重複列）我事後審閱了，無須追加修正**；細節與架構事實已寫入 `ARCHITECTURE.md` 最後一節。兩點供你知道：(a) #449 把站台入口標題由 `English Coach - AI 英文學習與單字訓練` 改成 `Jacky Studio — AI 學習、投資與實用工具`、各 app 為 `{App} | Jacky Studio`（單字頁標題不變）；`SitePages.cs` 與 `apps.tsx` 是兩份各自維護的資料，**沒有自動比對的測試**，新增／改名 app 時請兩邊都改。(b) #448 的遷移在**啟動時**對正式資料庫刪除重複列（不可逆）；Actions 的 deploy 成功不等於遷移成功，我的沙箱看不到正式環境，**PM 問的「正式環境是否已成功套用、刪了幾筆」我也答不出來**。你方便時在正式資料庫查 `SELECT MigrationId FROM __EFMigrationsHistory WHERE MigrationId LIKE '%UniqueUserWord'`（應有一列）與 `SELECT UserId, WordId, COUNT(*) FROM UserWords GROUP BY UserId, WordId HAVING COUNT(*) > 1`（應為空）即可確認；筆數遷移沒有記錄，已無法回溯。若 Repository 頁刪除／加入單字有異常請告訴我。PM 已記為 DECISION-022（品牌標題）／DECISION-023（唯一性），與我的審閱一致。
    - **#431 與 #407 已由你關閉**（2026-10-12 03:40／03:44 UTC，`completed`）；#431 的 Q1／Q2／Q3 沒有回覆紀錄，我依既有設計**維持功能只有 admin 看得到**，不再追問。需要開放給所有人或調整時，重開 #431 或開新 issue 即可。
    - **今天排程的 contract test（run 103）失敗**：3/57 個，**全是 TWSE `t187ap05_L`（月營收）查不到 2882／1301／2308 的列**，其餘 54 個（含 MFI94U、MIS、Yahoo、FundRich）全過。研判為**證交所每月 10 日後的資料更新窗口**：9/12（run 68）同一個測試也失敗（當時 1/3 查不到 2891），9/13 自行恢復。**對正式環境影響小**：`FetchRevenueData` 找不到列時回 `null`，AI 個股分析只是少「月營收」那一項，不會報錯。我的沙箱連不到證交所，無法直接驗證。**不開任務、不通知**；下次排程（run 104）若 `t187ap05_L` 仍失敗，我再升級處理（可能需要讓測試容忍月初更新窗口，或確認 TWSE 是否改了資料集）。
    - **部署串行化首次實測成功**：你 06:32:48／06:33:29 UTC 連續合併 #449／#448（run 538／539），539 的 job 在 538 結束後 **1 秒**才開始（06:35:17 vs 06:35:16），且最後部署的是較新的 `master` tip（`1f5c747`）。「被取代的排隊 run 顯示 cancelled」仍未見到（需要 ≥ 3 個 push 同時排隊）。
    - **仍等你**：今天（週一）盤中目視確認回落煞車卡片的「預估」區塊（第 24 項）。**不需要你回覆任何事**。

27. 2026-10-13（排程執行，第 7 次）：**無 open PR、無需你決定的事項。** 你在 #450～#457 直接合併的變更（Data Analysis「問 AI」重構、示範資料產生器、全站 chat 模型改 `gpt-6-luna`、明細匯出 CSV）我事後審閱了，**無須追加修正**；#450～#456 的 CI 全綠、`Build and deploy` run 540～546 全部成功（`990ca6c`），#457（`2a44448`）的 run 547 也成功，正式環境＝`2a44448`。架構事實與細節在 `ARCHITECTURE.md` 最後一節。兩件請你知道（都不阻擋，也不需要回覆）：
    - **成本**：#455 把所有 chat 呼叫統一到 `gpt-6-luna`（$0.10／$0.50 每 1M token，單價與舊模型接近），但它是推理模型、**推理 token 計入輸出**，我在 repo 與沙箱裡看不出實際會燒多少，所以**絕對成本可能上升**；每日預算 60 不用調（各功能一起換、相對比例不變）。建議部署滿一週後查 `AiUsageLogs`（每次呼叫一列，有 `Model`、`CompletionTokens`、`EstimatedCostUsd`），比較換模型前後各 `Feature` 的平均每次成本——查詢範例在 `ARCHITECTURE.md`（沒在正式資料庫跑過）。
    - **#455 沒對真的 OpenAI 實測（你在 PR 自己寫了）——我也驗證不了**：沙箱呼叫不了 OpenAI、連不到正式環境。間接證據是 #456 描述的 15 次真實請求顯示「問 AI」走的共用路徑對 luna 可用；`StockCodeResolver`（你點名風險最高的）不在證據內。讀碼發現它失敗時**完全不記 API 的錯誤訊息**，而且 `AiUsageLogs` 只在回應帶 `Usage` 時才寫列，所以被 OpenAI 拒絕的請求不會出現在 `Success = 0` 裡。最快的確認是**手動查一次「台積電」、開一次對話測驗**；之後可用 Log Analytics 查 `OpenAI completion failed`／`Could not resolve user input`（`ARCHITECTURE.md` 有查詢，未實測）。我建了 **TASK-051**（LOW）補那行錯誤日誌。
    - **#457 匯出 CSV**：公式注入防護、檔名、預設來源資料表釘死、10 萬列上限都沒問題。兩點取捨留給你（不排任務）：匿名匯出沒有 rate limiter（會整表讀共用的 5 DTU 資料庫）；匯出整批放記憶體（B1 約 1.75 GB；10 萬列寬表可能數百 MB，是我的估算、未量測）。要硬化的最小做法是匿名來源上限降到約 2 萬列，或匯出要求登入。
    - **隱私（需要你或 PM 決定要不要處理）**：「問 AI」現在**每次提問**都把來源裡所有「相異值 ≤300 個」的字串欄位（≤30 欄、合計 ≤2000 個值）送給 OpenAI；以前只送 Phase 1 挑出的相關欄位。預設示範資料沒問題，但使用者連自己的資料庫時，人名／部門／客戶代碼這類低基數欄位的值也會被送出，而前端沒有任何告知文字。我的建議是在輸入框旁加一行說明（成本近零）；是否要做、要不要讓使用者排除欄位，是隱私決策，我不自行排任務。
    - **FYI**：`scripts/demo-data/demo-data-out/sales_orders.sql`（約 7 MB）被追蹤，但同目錄 `.gitignore` 列了 `demo-data-out/`（是強制加入的）；產生器對固定 `--seed`／`--end` 是 deterministic，若不是刻意留著可 `git rm --cached`（Optional）。
    - **contract test 連兩天紅（run 103／104）**：唯一失敗項是 `t187ap05_L`（月營收）某幾家公司查不到列；同次 2603 通過所有欄位斷言 ⇒ **欄位沒變，是資料覆蓋**。對正式環境影響小（找不到列時個股分析只少月營收一項）。依我 10/12 的承諾已建 **TASK-050**（Backend, LOW, READY）：把測試改成驗證形狀，不因單一公司缺列而紅。原因（TWSE 每月 10 日後分批更新）是**假設**，我連不到 TWSE 無法驗證。不通知你。
    - **回覆 PM 的 Tech Lead Action**：TASK-030／031／033（#418／#420／#422）的變更檔案只有前端，**沒有新增 `[UsageLimit]` 或 AI 端點**（TASK-032 依其 Result 也是純前端）。唯一的但書：Conversation 摘要的建議單字點擊會呼叫既有的匿名字典查詢，新字會觸發一次（每字全站一次）字典生成——設計上就是免費。
    - **仍等你**：手機 ≤400px 看一眼 Exam 選單、貼一次 App Insights 各頁面造訪數字（PM 的非阻擋請求，見 `boss/PM.md`）。

28. 2026-10-14（排程執行，第 8 次）：**無 open PR、無需你決定的事項；沒有程式碼變更。** 你合併的 #458（TASK-050，月營收 contract test 改驗形狀）、#459（TASK-051，`StockCodeResolver` 失敗時記錄錯誤）、#460（持倉清單占比改以流動總資產為分母，不動產不計占比）我事後審閱了，**無須追加修正**；deploy run 548～550 全部成功，正式環境＝`e0ef06e`。細節在各 TASK 的 Review 與 `ARCHITECTURE.md` 最後一節。
    - **#460 兩點供你知道**：現在持倉清單占比、水位、再平衡建議三處分母一致（都是流動總資產）；占比欄標題加了說明泡泡。我沒有目視畫面（沙箱連不到正式環境）。
    - **今天早上 contract test（run 107）是 TASK-050 的第一個觀察點**，我下次排程會看；你不需要處理。
    - **仍等你（皆非阻擋）**：手機 ≤400px 看一眼 Exam 選單、貼一次 App Insights 各頁面造訪數字；10/20 之後貼 `AiUsageLogs` 成本查詢（`ARCHITECTURE.md`）。**不需要你回覆任何事**。

29. 2026-10-14（排程執行，第 9 次）：**無 open PR、無需你決定的事項；沒有程式碼變更。** 你直接合併的 #461（IG 限動圖加入基金與現金占比）我事後審閱了，**無須追加修正**；`Build and deploy` run 551 成功，正式環境＝`a2f4f44`。今天早上的 contract test（run 107）**全數通過**，TASK-050 改動後的第一個觀察點是綠的。
    - **#461 一點供你知道**：限動圖各區塊的占比分母（台股＋美股＋基金＋現金）與 #460 的持倉清單占比、水位、再平衡建議是同一組四類，口徑一致。我沒有渲染過產出的圖，三區塊（COMPACT）版面的實際外觀與長基金名稱被截斷的樣子，請你目視（你自己寫的 PR，大概已看過）。
    - **PM 的 DECISION-028（「問 AI」資料外送告知）**：已建 **TASK-052**（Frontend，LOW，READY）。這是 PM 的預設、你可隨時撤回；我與 PM 都沒有查證它是否足以符合個資法／OpenAI 條款，也不下結論（比照 DECISION-019）。
    - **匿名匯出硬化：我決定現在不排任務**（PM 把決定交給我）。理由：示範表預設就是 2 萬列，「上限降到約 2 萬」在現況下沒有任何效果；沒有濫用或記憶體不足的證據；治本要量測、串流要改 `DataAnalysisLibrary`（你的範圍）。重新評估的觸發條件寫在 `ARCHITECTURE.md` 最後一節。**唯一需要你的資訊（可選、不阻擋）**：正式環境 `dbo.Orders` 現在大約幾列？若遠大於 2 萬，這項會升級。
    - **仍等你（皆非阻擋）**：手機 ≤400px 看一眼 Exam 選單、貼一次 App Insights 各頁面造訪數字；10/20 之後貼 `AiUsageLogs` 成本查詢。**不需要你回覆任何事**。
