# JackyAIApp — Product Decisions

## DECISION-001: 不採用信託／履約保證，也不成立公司來解決預付法規問題

### Date
2026-09-12

### Decision
不會為了讓現行「儲值型 Credits」模式合法化而去辦理信託、履約保證保險，也不會為此去登記公司。任何收費模式都必須在「個人（免公司登記）＋不需履約保證」的前提下可行。

### Reason
Boss 明確表示不想跑銀行辦信託／履約保證，也沒有意願成立公司。

### Impact
排除「維持現行儲值錢包、另外補信託」這條路線（原 Open Question 1 的選項 a）。任何新方案都必須在此前提下重新設計，直接影響 Credits & Payments 模組的收費機制走向（見 `.agent/boss/PM.md` 2026-09-12 提案）。

### Source
Boss

---

## DECISION-002: 不採用訂閱制

### Date
2026-09-12

### Decision
不採用月費／年費訂閱制作為收費模式。

### Reason
Boss 判斷此產品的使用族群不太可能願意為訂閱付費。

### Impact
排除原 Open Question 1 的選項 c（訂閱制）。收費模式需往「單次購買」或其他非訂閱、非儲值的方向設計。

### Source
Boss

---

## DECISION-003: 暫緩營利目標，優先做「留得住用戶」與「降低營運成本」，並以不下廣告的方式追求成長

### Date
2026-09-12

### Decision
在金流／收費模式問題解決之前，**暫停以營利為導向的產品投入**（包含 DECISION-001/002 之後原本要走的「單筆商品化銷售」提案）。近期產品目標改為：
1. 做出使用者願意長期使用的功能（提升留存與產品本身的吸引力）。
2. 降低雲端／營運成本。
3. 讓產品/網站變熱門（成長、擴大使用者基數），但**不透過付費廣告**達成。

Boss 的邏輯：使用者都不願意用，根本不會想付錢；付費轉換的前提是先有一群真的在用、願意留下來的使用者。

### Reason
Boss 判斷在使用者基礎/黏著度還不夠的階段推商業化，優先順序錯了；且金流/法規問題本來就還沒解決（DECISION-001/002 已排除多條路線），與其卡在收費模式設計，不如先把資源放回產品力與成長本身。

### Impact
- `Credits & Payments` 模組的所有後續投入（含 2026-09-12 提出的「單筆商品化銷售」提案）**全部暫停**，改列 LOW / ON HOLD，不再是近期 Requirements 焦點。
- 新增「成長／留存（不下廣告）」為近期最高優先的產品方向，需要重新盤點 Requirements。
- Tech Lead 尚不需要評估付款流程重新設計；如果之後要投入資源，會是在「降低雲端成本」的技術方向上。

### Source
Boss

---

## DECISION-004: 成長策略改以「真的能提升英文能力的功能」為主軸，捨棄分享／邀請類推廣機制

### Date
2026-09-14

### Decision
放棄原 Growth & Retention 需求中「分享每日挑戰/連續天數/測驗結果」與「好友邀請/排行榜」這類推廣型機制。改以強化產品本身的學習成效（聽/說/讀/寫四項核心能力）作為成長與留存的主要手段——當功能好用、真的讓人變強，口碑推薦是自然結果，不是設計出來的分享按鈕。

### Reason
Boss 指出：自己學英文也不會想分享學習紀錄，正在學習的人多半沒自信或沒有分享慾，這類機制不太可能有人用。與其先設計推廣機制，應該先從功能面思考「什麼功能真的能增強英文聽說讀寫能力」，產品夠好自然會被推薦。

### Impact
- `REQUIREMENTS.md` 的 `Growth & Retention` 需求：移除「Shareable results」與「Referral/invite mechanics」兩項（移到新增的 Rejected 區塊記錄原因，非直接刪除歷史），SEO／公開單字頁維持（已上線、無關分享機制，予以保留）。
- 新增需求 `Skill-Building Feature Expansion（聽說讀寫能力強化）`，列出四個具體功能提案（依 PM 建議優先序：口說發音／流暢度回饋 → 閱讀理解短文 → 開放式寫作批改 → 聽寫），標記 HIGH priority，作為近期 Growth & Retention 實際要做的工作內容。
- 尚未指派 Tech Lead 動工——PM 已在 Requirements 的 Open Questions 中記錄「若無 Boss 其他指示，下次會直接請 Tech Lead 評估口說回饋功能（優先序第一項）」，比照先前 SEO 項目「不用每次都等裁示」的模式。

### Source
Boss

---

## DECISION-005: 核准 Skill-Building 優先序與第一項交付 Tech Lead；長期目標擴大為「產品全面持續改進」而非僅限英語學習

### Date
2026-09-14

### Decision
1. 核准 PM 提出的 Skill-Building Feature Expansion 優先序：口說發音／流暢度回饋
   → 閱讀理解短文 → 開放式寫作批改 → 聽寫。
2. 核准 PM 將第一項（口說發音／流暢度回饋）交付 Tech Lead 進行技術可行性／成本
   評估，不需再等進一步裁示。
3. 設定新的長期產品目標：**持續改進網站功能，讓 User 更願意一直使用該 Web
   App**，範圍**不限於英語學習**功能——Finance（台股分析）、Portfolio、PDF
   Unlocker 等其他模組，未來也應比照 Skill-Building 的做法（盤點現況缺口 →
   提出具體功能）納入持續改進的範疇。

### Reason
Boss 對優先序與交付 Tech Lead 的判斷沒有異議（「1. 可以 2. 同意」），並主動
補充：不希望長期改進只侷限在英語學習這個支柱，產品其他模組（財經/投資組合/
PDF 工具）同樣需要持續被檢視與改進，才能真正提升整體留存與黏著度。

### Impact
- `REQUIREMENTS.md`：Skill-Building 的 Open Questions 標記為已解決；第一項
  （口說回饋）正式進入「等待 Tech Lead 評估」狀態，記錄於
  `PROJECT_STATUS.md` 供 Tech Lead 下次執行時認領。
- `REQUIREMENTS.md`：Product Positioning／Growth & Retention 新增「長期目標
  擴大為產品全面持續改進」的說明，並在 Growth & Retention 的 Future
  Consideration 新增一項——未來 PM 應對 Finance/Portfolio/PDF Unlocker 做
  同樣的「盤點缺口→提案」流程，尚未執行，留待後續 run。
- 不影響目前已核准的 Skill-Building 工作範圍或優先序本身，是新增一個更上層、
  持續適用的產品方向原則。

### Source
Boss

---

## DECISION-006: 拔除網站所有收費／金流機制，全面轉為免費產品

### Date
2026-09-21

### Decision
移除網站上所有付款/收費相關機制——Stripe、PayPal、ECPay 三個金流 provider、
Credit Pack 購買流程、`/pricing`／`/credits` 儲值頁面——不是像 DECISION-003
那樣「維持現狀、暫緩投入」，而是**主動拆除**。原本靠 Credits 扣點把關的 AI
功能（測驗生成/批改、對話、TTS/STT、個股 AI 分析、聊天機器人）需要一套不涉及
金錢的替代機制來控制雲端成本，PM 提案「每項功能每人每日免費額度」（沿用
`market-digest` 已有的「每人每天一次」共用模式，不是無限量開放），已寫入
`REQUIREMENTS.md` 的 Credits & Payments 需求供 Tech Lead 評估；此提案尚待
Boss 進一步確認細節，若沒有回覆，PM 會比照既有模式下次直接交付 Tech Lead
做技術可行性評估。同時，這個決定讓 Finance Gap Audit 提出的「免費量化功能
是否刻意導流到付費 AI 分析」這個定價問題直接失去意義（因為不會再有付費分頁）
——一併結案，不需要 Boss 另外裁示。

### Reason
Boss 明確表示已經想把網站所有收費方式都拔掉：Stripe/PayPal 台灣不開放使用；
ECPay/Payuni 兩邊都卡在審核不通過，原因就是預付型 Credits 的信託法規問題
（DECISION-001/002 已確認無法繞過）。Boss 不想為此去辦信託或成立公司
（DECISION-001），與其讓一套永遠無法真正上線收款的系統繼續佔用維護成本、
還持續造成「免費導流付費」這類設計上的曖昧地帶，不如整個拿掉，把資源完全
收斂回 DECISION-003 已核准的方向——留存功能與降低雲端成本。

### Impact
- `REQUIREMENTS.md` 的 `Credits & Payments` 需求：從「LOW / ON HOLD（維持
  運作、不投入）」改為「規劃移除」，新增 PM 提出的「免費每日用量額度」替代
  機制提案，作為 Tech Lead 下一步技術評估的預設方向。
- 既有的 Credit 餘額/交易紀錄：因為三個 provider 從未通過審核、沒有任何真實
  金流交易發生過（見原 Requirements 文件 Constraints 段落），拆除時不涉及
  真實退款問題，純粹是移除未上線的沙盒功能。
- `Finance` Gap Audit 的「免費量化 vs. 付費 AI 分析定價重疊」開放問題：結案，
  不再需要 Boss 裁示。
- `PDF Unlocker` 的「批次處理」文件落差：Boss 確認不需要新增批次處理功能，
  維持現有單檔案行為即可，該落差已結案，只待有人順手把 `CLAUDE.md`／舊文件
  的「支援批次處理」描述改掉（非急件、非 PM 可直接修改的檔案範圍）。
- Finance/Portfolio Gap Audit 的整體優先序（cross-link → 預測準確度追蹤 →
  多股比較 → CSV 匯入 → 再平衡建議 → FIRE 追蹤 → 觀察名單/價格警示）Boss
  已核准（「1. 是，可以」），可以繼續依序交付 Tech Lead，不受這次金流決定
  影響。

### Source
Boss

---

## DECISION-007: 免費用量上限機制的具體設計——對使用者完全隱藏「付費/點數」概念，超量才提示聯絡作者

### Date
2026-09-28

### Decision
確定 DECISION-006（拔除收費機制）之後「如何控管 AI 成本」的具體做法：
1. **一般使用者完全不會看到任何跟收費/點數/付費相關的文字或畫面**——不是
   「免費」「額度」這種還留有金錢聯想的字眼，而是使用者的體驗應該就是
   單純的功能本身，沒有任何暗示「這原本要花錢」的痕跡。
2. 後台仍然對原本會扣點的每個功能（測驗生成/批改、對話、TTS/STT、個股 AI
   分析、聊天機器人……）**保留每人每日（或每期間）用量上限**，作為唯一的
   成本控管機制——不是完全無限制開放。
3. 當使用者觸及該功能的用量上限時，該功能會直接不能用，並顯示一則提示：
   「您已超出目前的使用量，如果您是重度使用者，請聯絡作者
   jacky19918@gmail.com 幫您調高上限」（文案細節由 Tech Lead/前端工程師
   決定，但語意需符合：不提付費、不提點數，只講「用量」與「聯絡作者」）。

### Reason
Boss 明確表示：不希望一般使用者看到任何收費或點數相關的東西（因為產品目前
是完全免費、也沒有合法的收費管道），但背後仍然需要有一個機制悄悄限制過度
使用，避免 DECISION-006 拔除收費後 AI 成本失控；對於真的用量很大的重度
使用者，用「直接聯絡作者」取代「自助加值」作為例外處理方式。這與 PM 在
DECISION-006 提案的「每人每日免費用量上限」方向一致，這次只是把「使用者
看到什麼」與「超量時的處理方式」訂到底。

### Impact
- 解除 DECISION-006 遺留的最後一個開放問題（`REQUIREMENTS.md` → Credits &
  Payments → Removal 的 Open Questions），Credits & Payments 移除工作現在
  可以正式交付 Tech Lead 進行技術評估與任務拆解，不用再等 Boss 裁示細節。
- `REQUIREMENTS.md` 的 Acceptance Criteria 需新增：UI 上不得出現「credit」
  「點數」「付費」「購買」等字樣；超量時的提示訊息需包含聯絡信箱
  `jacky19918@gmail.com`；`InsufficientCreditsModal`（或其替代元件）的文案
  需整個換掉，不能只是把「購買」按鈕拿掉但保留「你的點數不足」這種措辭。
- 各功能實際的每日/每期間用量數字（例如：測驗生成一天幾次、個股 AI 分析
  一天一次、TTS/STT 每月上限多少）仍是 Tech Lead 的技術評估範疇，非 PM 這次
  決定的內容。

### Source
Boss


## DECISION-008: Boss ↔ Agent 對話改用 GitHub Issues；結論型文件維持 `.agent/`

### Date
2026-09-30

### Decision
新的 Boss 提問與 Agent 討論改在 GitHub Issues 進行（標籤建議 `needs-boss` / `pm` / `tech-lead`，結案即 close）。`REQUIREMENTS.md`、`DECISIONS.md`、`tasks/`、`PROJECT_STATUS.md`、`ARCHITECTURE.md` 維持不變。`boss/PM.md` 已瘦身，舊對話保留於 git history。

### Reason
對話型檔案過大（PM.md ~55KB、TECH_LEAD.md ~59KB），Boss 須捲到底、Agent 每次讀整檔浪費 token。

### Impact
- Tech Lead 需評估 TECH_LEAD.md 瘦身、TASK 是否改看板、READY task 重複認領問題（Issue #387）。
- 修改 `.agents/` 指示檔需 Boss 明確授權，PM 未動。

### Source
Boss（2026-09-30 核准 PM 建議的 B 方案）


## DECISION-009: 首頁六個應用皆視為重要；導入用量統計（方案待定）

### Date
2026-10-04

### Decision
1. English Coach／Wealth Portfolio／Stock Intelligence 為最優先，但首頁其餘六個應用同樣重要，不得因「次要」而隱藏或停止改善；PM 需逐一檢視其 UX／留存改進空間。
2. 同意導入用量統計以支撐產品決策；方案（Azure Application Insights／GA4／Matomo）由 Boss 選定。若功能受限於 DataAnalysisLibrary，可改該 repo，但不得破壞其設計架構，且動工前須先與 Boss 確認範圍。

### Reason
Boss 於 Issue #409 回覆；沒有數據前無法判斷模組去留。

### Impact
Portal 卡片僅做排序／分群，不下架任何應用；統計方案確定後由 Tech Lead 開 task。

### Source
Boss（Issue #409，2026-10-03）


## DECISION-010: 用量統計採用 Azure Application Insights

### Date
2026-10-04

### Decision
用量統計採用 Azure Application Insights（Boss 確認 Azure 端已啟用）。

### Reason
不需第三方服務、免費額度內、無需自建後台（Tech Lead 與 PM 皆建議）。

### Impact
新增 Requirement「匿名用量統計」；由 Tech Lead 建 task。隱私告知需求待確認。

### Source
Boss（Issue #409，2026-10-04）

## DECISION-011: 正式環境網域為 apps.jacky-studio.com

### Date
2026-10-08

### Decision
正式網址為 `https://apps.jacky-studio.com`（Cloudflare 代理 → Linux App Service `jackyai-linux`）。
舊的 `jackyai.azurewebsites.net` 不再使用。

### Reason
Boss 已取得自有網域並完成遷移（見 repo `CLAUDE.md` 部署區段）。

### Impact
- 先前文件中提及舊網域的 SEO／Search Console 項目為歷史紀錄，以新網域為準。
- 自有網域帶來的新可能性（如 OAuth 品牌驗證、Cloudflare 快取／防護）尚未排入需求，待 Boss 決定。

### Source
Boss

## DECISION-012: 單字頁 SEO title 改為「單字在前、含中文關鍵字」；外部分享暫不做

### Date
2026-10-09

### Decision
- 單字頁 title 由 `English Coach - {單字}` 改為 `{單字} 英文意思、發音、例句 | English Coach`（取代先前核准的格式）。
- #407 建議的「把網站分享到學習社群／論壇」暫不執行（Boss 知悉其對流量的幫助，之後再議）。

### Reason
單字在前並含「英文」類關鍵字，才有機會命中「apple 英文」等長尾查詢。排名無法保證（新網域通常需數週到數月），此為提高機會，不是承諾結果。

### Impact
- Tech Lead 已建 TASK-035 實作；首頁與其他頁 title 不變。
- 驗收看「title 格式」，不看「是否上第一頁」。

### Source
Boss（Issue #407，2026-10-09）

## DECISION-013: 「回落煞車」策略的固定規則與禁止事項

### Date
2026-10-10

### Decision
- 適用範圍僅限台股大盤（發行量加權股價報酬指數）、0050、00631L；不得套用個股或其他槓桿 ETF。
- 訊號參數固定為 `DD_TRIGGER = 0.08`、`REENTER_SMA = 60`、`LOOKBACK_HIGH = 252`，Agent 不得自行調整。
- 不提供：防守 E ≥ 0.5 的選項、不煞車的固定槓桿選項；不做：突破新高減碼、深跌提前加碼、階梯式加碼。
- 相關畫面固定顯示「依歷史回測規則產生的訊號，過去績效不代表未來，不構成投資建議。」

### Reason
Boss 的回測結論（見 Issue #431）。PM 未獨立驗證回測數字，僅記錄 Boss 的決定。

### Impact
- 見 `REQUIREMENTS.md`「0050 + 00631L『回落煞車』加減碼提醒」。
- **尚未決定、不屬本決策**：對一般使用者開放的範圍（Q1）、提醒送達方式（Q2）。

### Source
Boss（Issue #431，2026-10-10）

## DECISION-014: 「回落煞車」分期、開放策略與提醒方式（Boss 回覆 Q1～Q4）

### Date
2026-10-11

### Decision
- **分期同意**：第一階段＝收盤後正式訊號＋目標配置＋加減碼提醒與股數；第二階段＝13:15 盤中預估＋「訊號已更正」。第一階段績效數字顯示「隔日成交版本」，並一併標示期間與最大回撤。
- **Q1 對誰開放**：目前僅 admin 可見，之後再慢慢開放；是否開放全部使用者，要先靠可觀測性紀錄（log／指標）判斷計算量能否負擔。
- **Q2 提醒方式**：站內顯示就好，不要 Email（也不做推播）；第二階段的「站外提醒」移出範圍。
- **Q3 假設確認**：策略總資產＝0050＋00631L（取自持股快照）＋使用者在此功能指定的策略現金；全部建議股數為 0 時顯示「不動」；持股快照超過 7 天顯示過期提示。
- **新增需求**：頁面提供「重新整理資料」以繞過 cache、顯示資料時間（因 13:15 可能拿到被 cache 的舊值）。
- **股票分割風險**：Boss 假設使用者會自己更新到正確股數，不為此追加工作。
- **回測數字**：以 Boss 2026-10-11 留言中的更正表為準（取代 #431 內文 §2 的績效表）；來源為 Boss 本機 `tw_backtest`（`reproduce_spec.py`），PM 未獨立驗證。

### Reason
Boss 於 Issue #431 的回覆。Boss 擔心計算量，故採 admin 先行＋可觀測性決定是否擴大；提醒只在站內以避免新增 Email 管道的成本與範圍。

### Impact
- `REQUIREMENTS.md` 對應段落已更新（v2）：新增需求 8（資料新鮮度與手動重新整理）、需求 9（可觀測性）；Out of Scope 加入站外提醒。
- **合規（R1）不因此解決**：Boss 選 admin-only 的理由是計算量，不是合規；`Audience=All` 之前，需求 9 的觀察結果與 R1 的合規決定仍都是前置條件，PM 屆時會再向 Boss 提醒。
- PR #432 的畫面文案仍是更正前的數字，需 Tech Lead 安排更新（不阻擋合併，須在 `Audience=All` 之前完成）。
- 「納入其他持股一併判斷」（Q6）與「歷史相似期間比較圖」（Q7）只是 Boss 的提議，尚未定案，**不屬本決策**；DECISION-013 的適用範圍與固定規則不變。

### Source
Boss（Issue #431，2026-10-11）


## DECISION-015: 「回落煞車」Q5／Q6 結案；「歷史相似區段比較圖」的方向（Boss 回覆 Q5～Q7）

### Date
2026-10-11

### Decision
- **Q5**：「隔日成交」績效欄的回測期間同為 1997–2026（Boss：「如果從 1997 開始不會造成困擾的話可以從 1997」；`results/reproduce_spec.txt` 標頭證實與主欄為同一序列）。
- **Q6**：其他持股**先做 A＋B 就好**——策略只看 0050＋00631L＋使用者指定的策略現金（A），並在卡片顯示「此策略部位占總資產 X%」純資訊（B）；**不**把其他持股納入加減碼判斷（C 不做）。DECISION-013 的適用範圍不變。
- **回落煞車維持獨立卡片**，不重用「再平衡建議」區塊（Boss 同意）。
- **Q7 方向**（細節仍待 Q8，見下）：新增「歷史相似區段比較圖」——系統自動找出歷史上最相似的區段；使用者可手動調整「用來比對的過去區段」，「現在」區段不可移動、至多調整長度；找不到時顯示「找不到過去的相似區段」；圖上畫**收盤價**；**不限 admin**（Boss 意向）。

### Reason
Boss 於 Issue #431 的第二輪回覆。Q6：網站本來就顯示目前曝險，Boss 可自行扣除 0050／00631L 得知其他曝險，且目標只是讓 0050＋00631L 的績效有效成長。
Q7：Boss 想自己檢視「相似的過去走勢」並參考其之後的走勢（「過去不一定等於未來，僅供參考」）。

### Impact
- `REQUIREMENTS.md`（v3）：新增需求 11（策略部位占總資產比例）；需求 10 改寫為 Boss 方向＋PM 假設 A10～A13；Q5／Q6 結案；新增 Q8、風險 R7。
- **尚未決定、不屬本決策**：圖上的「收盤價」是哪個標的、圖放哪裡／對象、合規（R1／R7）下的開放時機（Q8）。**Q8 回覆前不排工**。
- 「不限 admin」是針對相似區段圖；DECISION-014 的「回落煞車卡片僅 admin 可見」不變。兩者的對象設定需分開（PM 理解，待 Boss 於 Q8 確認）。
- **合規不因此解決**：相似區段圖會顯示「相似區段之後的走勢」，是最容易被讀成預測的部分；PM 未查證相關法規，不下結論（R1／R7）。
- PM 未重跑 `reproduce_spec.py`，回測數字仍是 Boss 提供、PM 未獨立驗證（僅核對輸出檔與文件一致）。

### Source
Boss（Issue #431，2026-10-11）


## DECISION-016: 大盤走勢比較圖放在 /finance 的 Stock Intelligence，不放 Portfolio；新增「過去 5 年同期間走勢圖」

### Date
2026-10-11

### Decision
- 「歷史相似區段比較圖」（DECISION-015 的 Q7 方向）改放 **`/finance` 的 Stock Intelligence**，不放 Portfolio、也不放回落煞車卡片——Boss：「那個和個人資產好像比較沒關係」。
- 新增「**過去 5 年相同日期區段**走勢圖」：同一段月/日區間，每年一條線、不同顏色並有圖例，「僅供使用者參考」；Boss 範例為加權指數 2022～2026 的 1/1～9/30。
- 兩張圖合併為獨立需求「Stock Intelligence 大盤走勢比較圖」（`REQUIREMENTS.md` 文末）；原回落煞車需求 10 移出。

### Reason
Boss 於 Issue #431 的補充留言（2026-10-11 02:38 UTC）。市場層級的圖與使用者持股無關，放在 Stock Intelligence 更合理。

### Impact
- 回落煞車需求（含 TASK-036／037／038）不受影響；兩者互不依賴、互不阻擋。
- DECISION-015 的「不限 admin」由 `/finance` 現有的存取方式決定：PM 讀程式確認該路由沒有 `RequireAuth`，**未登入訪客預設也看得到**。這是否可接受、合規（R1／R7）如何取捨，仍待 Boss 明確回覆（Q8-3）；PM 未查證法規，不下結論。
- **尚未決定、不屬本決策**：標的（PM 建議加權指數，Q8-1）、對象（Q8-3）、同期間圖預設期間等細節（Q9，有 PM 預設假設）。
- Tech Lead 可先設計同期間圖與提議相似度演算法（預設標的為加權指數）；兩者的上線仍由 Boss 審查並合併。
- 與 DECISION-015 的關係：位置改由本決策為準，且 DECISION-015 提到的「Q8 回覆前不排工」放寬為上一點（歷史紀錄不改寫）。
- 後續（同日，Boss 指示小事不必問）：標的＝加權指數（不含息，**Boss 已同意**）、預設期間、「過去 5 年」含今年、警語字句皆由 PM 決定並記錄於 `REQUIREMENTS.md`（A14～A17），Boss 可隨時推翻；對象照 Boss 既有決定（不限 admin），合規仍未查證、列為 R7，不再追問。

### Source
Boss（Issue #431，2026-10-11）
