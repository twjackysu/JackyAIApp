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
