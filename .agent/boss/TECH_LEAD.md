# Boss ↔ Tech Lead

## Tech Lead → Boss (2026-09-12)

### Status
第一次執行。已針對 REQUIREMENTS.md 目前最高優先的「Growth & Retention（公開單字
頁面做 SEO）」需求做技術盤點，並拆出可以立即開工的任務。細節見
`.agent/architecture/ARCHITECTURE.md`。

### Completed
- 盤點現有程式碼，確認：
  - 單字查詢 API（`GET api/dictionary/{word}`）**本來就沒有登入限制**，也不
    碰任何個人資料，後端這塊不需要改架構。
  - 前端 `/english/dictionary` 路由**本來就沒有包在 RequireAuth 裡**，未登入
    也能開啟頁面。
  - 但目前這頁**還不是真的對 SEO/分享友善**：（1）沒登入時仍會打一支需要登入
    的 API（收藏狀態），保證失敗；（2）網址是用 `?word=apple` 這種 query
    string，不是乾淨的 `/dictionary/apple` 路徑；（3）整站是純前端渲染
    （Vite SPA），沒有任何伺服器端渲染，也沒有針對每個單字設定
    `<title>`/meta description——Google 這類會執行 JS 的爬蟲還能吃到一些內容，
    但 LINE/Facebook 這種分享預覽爬蟲通常不執行 JS，看不到內容。
- 已拆出兩個可以馬上開工的任務（`TASK-001` 後端、`TASK-002` 前端）：改用路徑
  網址、修掉未登入時的 401、加上動態 meta 標籤、加 sitemap.xml/robots.txt、
  幫 `WordText` 加資料庫唯一索引。這部分成本很低（不需要新的 infra、不影響
  雲端成本），可以直接開工，不需要您裁示。

### Technical Changes
無（尚未開工，任務剛拆出來）。

### Tasks
- `TASK-001`（Backend）：sitemap/robots + `WordText` 唯一索引 — READY
- `TASK-002`（Frontend）：路徑式網址 + 修 401 + 動態 meta 標籤 — READY

### PR Review
無開放 PR。

### QA
無。

### Risks / Blockers
無立即阻塞。

### Boss Attention Required — 需要您知道、但不急著今天決定的一個技術取捨

Requirement 裡提到「讓每日挑戰/連續天數/測驗結果可以分享出去（社群、LINE）」，
這件事跟「公開單字頁面」踩到同一個技術限制：**現在整個網站是純前端渲染的
SPA，LINE/Facebook 這類分享預覽爬蟲看不到動態內容**（他們不執行 JS）。也就是
說，就算頁面公開、也設好單字專屬標題，分享到 LINE 時的預覽卡片（縮圖+標題+
描述）目前還是會是空的或整站通用的樣子，達不到「分享出去吸引人點」的效果。

要真正解決這個，有三個方向，**成本/複雜度差很多**，我先不擅自選，寫在這裡供
您參考／之後我們再一起評估：

1. **輕量做法**：在後端加一個小 middleware，偵測到是已知的爬蟲/機器人
   User-Agent 時，才回傳一個簡單的、有正確標題/圖片的 HTML（真人使用者完全
   不受影響，還是原本的 SPA）。不用加新的服務、不影響現有架構，但要自己寫
   跟維護這段判斷邏輯。
2. **外部服務做法**：串一個現成的「預渲染服務」（例如 prerender.io 這類），
   幫我們把頁面預先轉成 HTML 給爬蟲看。程式碼幾乎不用改，但是**多一筆固定
   的第三方服務費用**，跟您「降低雲端成本」的目標方向相反。
3. **先不做**：這次只做前面提到的低成本部分（Google 搜尋還是吃得到、網址
   變乾淨），社群分享預覽這塊先不做，等之後真的觀察到「大家有在分享但預覽
   很醜」是個問題，再回來處理。

我目前傾向方向 3（先不做，觀察效果），因為符合「不增加成本」的原則，而且
現在還沒有真實使用者在分享，先不知道值不值得投資。除非您有不同想法，不然
我會照這個方向繼續往下推，之後如果情況改變會再回報。

### Source
Tech Lead

---

## Boss → Tech Lead (2026-09-13)
看起來我應該會選1.輕量做法，但這有什麼壞處嗎?維護成本高嗎?(之後維護很麻煩?)還是說大部分的爬蟲user agent不知道怎麼抓到?
我們的目標可以先讓別人google search: jackyai english apple的時候能找到Apple單字的頁面嗎?

---

## Tech Lead → Boss (2026-09-13)

### Status
先回覆您的兩個問題：(1) Google 搜尋這個目標，其實跟「輕量爬蟲偵測 middleware」
是**兩件不同的事**，目標本身不需要等 middleware 決定；(2) middleware 本身的
壞處/維護成本說明如下。

### 先講重要的：「Google 搜得到 apple 單字頁」這個目標，Phase 1 就夠了

Google 的爬蟲（Googlebot）跟 LINE/Facebook 的分享預覽爬蟲**行為不一樣**：
Googlebot 索引網頁時**會執行 JavaScript**、等頁面渲染完再讀內容（這是 Google
從好幾年前就公開的爬蟲行為，不是我們要另外處理的東西）。所以只要 TASK-001/
TASK-002（乾淨網址 `/english/dictionary/apple`、每個單字動態
`<title>`/meta description、`sitemap.xml`）做完，Google 理論上就抓得到、也
會用單字當標題去索引——**這兩個任務本來就是 READY、跟 middleware 的決定完全
無關，不需要等您裁示這題，隨時可以開工**。

只是要跟您說一個時間預期：就算頁面做好、sitemap 也送出去了，Google 從「爬到」
到「真的出現在搜尋結果」中間通常還是要幾天到幾週（要排入索引佇列、也跟這個
網域目前的權重/流量有關，新網域通常會慢一點），不是做完當天就查得到，這是
搜尋引擎本身的行為，不是我們技術上能加速的部分。

LINE/Facebook 的分享預覽（貼連結到聊天室看到的縮圖卡片）才是需要 middleware
的部分，因為那些爬蟲**不執行 JavaScript**——不是「不知道怎麼抓」，是他們送
出的 HTTP 請求跟真人瀏覽器一樣抓得到頁面，但因為不跑 JS，看到的只是我們
Vite SPA 一開始那份幾乎空白的 HTML（React 還沒渲染），看不到單字標題/說明。

### 回答您的問題：方案 1（輕量 middleware）的壞處與維護成本

**維護成本：低，而且是「有明確清單可查」的低，不是「要一直應付新花招」的低。**
LINE、Facebook、Twitter/X、Slack、Discord 這些平台的分享預覽爬蟲，User-Agent
字串是**官方公開文件寫死的**（例如 Facebook 的是 `facebookexternalhit`、LINE
是含 `Line/` 字串），因為平台本身希望網站業者認得出他們、回應正確的
Open Graph 標籤——這跟防垃圾郵件/防爬蟲那種要跟惡意行為者「軍備競賽」的偵測
完全不同，字串幾乎不太會變。維護只會發生在：(a) 我們想多支援一個新平台
（例如以後想支援 WhatsApp 分享預覽），加一行判斷式；(b) 極少數情況某平台換
了 UA 字串。都是幾分鐘的小改動，不是「之後很麻煩」的技術債。

**壞處，實際上有兩個，但都不嚴重：**
1. **清單外的爬蟲還是看不到正確預覽**——如果有個小眾平台的預覽爬蟲不在我們
   的判斷清單裡，分享到那個平台時預覽卡片還是會是空的/通用的。只影響那個
   特定平台的分享美觀，不影響網站功能、不影響 Google 索引。
2. **要小心「同一頁面對爬蟲跟對真人回傳不同內容」這件事**（技術上叫
   cloaking，Google 有明確反對這種手法，但那是指「刻意用假內容騙搜尋排名」）。
   我們這裡對機器人回傳的標題/說明文字，跟真人看到的頁面內容是**同一份資料
   來源**，只是少了互動 UI——這是業界標準做法（Google 自己過去甚至官方推薦
   過這種「dynamic rendering」模式給純前端 SPA 用），不是灰色地帶操作，但
   實作時要記得：機器人版本顯示的標題/說明文字要跟真人版本完全一致，不要
   額外塞進跟頁面無關的內容。

**成本面：** 純粹是一段 middleware 判斷 UA 字串 + 一次既有的單字查詢（重用
`DictionaryService`，不會額外呼叫 AI），沒有新的第三方服務、沒有額外雲端
費用，符合您「降低成本」的方向。

### 結論與下一步

- **TASK-001 / TASK-002（Google 搜尋目標）**：不受這次討論影響，維持 READY，
  等 Backend/Frontend Engineer 認領即可開工。
- **Phase 2（LINE/FB 分享預覽 middleware）**：您的疑慮（維護麻煩、爬蟲抓不到）
  經確認都不成立，壞處也很小，我已經開了 `TASK-003`（Backend）準備開工。如果
  您沒有不同意見，我會照這個方向繼續推進；有想暫緩或調整範圍的話再跟我說。

### Source
Tech Lead

---

## Boss → Tech Lead (2026-09-13)
壞處1我覺得沒什麼問題
但2我只是想確認這個不能互動的UI會不會久了沒有up to date，忘記同步
導致跟網站互動的UI越差越多

---

## Tech Lead → Boss (2026-09-13)

### Status
這個疑慮是對的，而且原本 TASK-003 的寫法（「重用同一個資料來源」）其實**沒有
完全解決**您擔心的問題——我已經把三個任務的規格改掉，把它從「靠人記得同步」
變成「架構上不可能不同步」。細節在 `.agent/architecture/ARCHITECTURE.md`
新增的一節，這裡講重點。

### 問題出在哪裡

原本的設計是：機器人版（C#後端）跟真人版（TS前端）都去讀**同一份單字資料**
（`Meanings`/`Definitions`），但兩邊各自把這份資料**格式化成標題/描述文字**
的邏輯，是寫在兩個不同語言的程式碼裡（前端 TypeScript 一份、後端 C# 一份）。
資料來源相同，不代表格式化規則不會分岔——比如以後前端調整了標題後綴的措辭、
或改了摘要截斷長度，後端那份如果沒人記得同步改，兩邊顯示的文字就會慢慢不一樣。
而且正如您說的，因為機器人版沒有真人在看、也沒有互動可以「感覺到」它壞了，
這種分岔會**悄悄發生、沒人發現**，跟一般 UI bug 不同（UI bug 至少使用者會回報）。

### 怎麼修

把「格式化文字」這件事收斂到**只有一個地方能做**：
- TASK-001（後端）新增一個共用的 C# 方法，把單字資料格式化成
  `seoTitle`/`seoDescription`，並直接放進 `GET api/dictionary/{word}` 的
  回應欄位裡。
- TASK-002（前端）**不再自己組標題/描述文字**，改成直接顯示 API 回傳的
  `seoTitle`/`seoDescription`，字串處理邏輯歸零。
- TASK-003（機器人版 middleware）**直接呼叫同一個 C# 方法**（同一個
  process 內呼叫，不是打自己的 API），不能自己另外拼字串。

這樣改完之後，整個系統裡**只存在一份**格式化邏輯，兩邊看到的文字保證是同一份
輸出——不是「請兩位工程師記得同步」，而是「想不同步都沒辦法」，因為根本沒有
第二份程式碼可以改壞。這是設計層面直接排除失敗可能性，不是流程規範，所以不
會有「時間久了忘記」的風險。

我也在 TASK-003 的驗收標準加了一條：Code Review 時我會明確檢查 middleware
是不是真的呼叫共用方法，而不是自己手刻一段 HTML 字串把文字內嵌進去。

### 成本
零額外成本——資料本來就要算一次，現在只是把「算完格式化好的字串」也一起放進
既有的 API 回應裡，沒有新的呼叫、沒有新的服務。

### 結論
三個任務規格都已更新（見 `.agent/tasks/TASK-001.md`、`TASK-002.md`、
`TASK-003.md`），都還是 READY、未被工程師認領，所以這次調整沒有造成重工。
如果您沒有其他疑慮，這個方向我會照這樣繼續推進，等 Engineer 認領即可開工。

### Source
Tech Lead


---

## Boss → Tech Lead (2026-09-13)

OK，這個不可能不同步的方式我可以接受。

---

## Tech Lead → Boss (2026-09-15)

### Status
接手 DECISION-005 交付的 Skill-Building 第一項：口說「發音/流暢度回饋」的
技術可行性與成本評估。已完成盤點、拆出兩個任務（Backend/Frontend），細節見
`.agent/architecture/ARCHITECTURE.md`。這裡跟您報告一個重要的範圍釐清，以及
一個需要您裁示的後續選項。

### 範圍釐清：這次做的是「流暢度回饋」，不是真正的「發音評分」

盤點現有 Conversation Test 的錄音流程後發現一個關鍵限制：我們現在用的
Whisper 語音辨識 API，**不管用哪種回應格式，都不會回傳「發音準不準」這種
分數**（沒有音素層級的正確性判斷）。它能給的，是逐字/逐句的時間戳記——這足夠
算出「講話速度」「停頓次數」「贅字次數（um/like之類）」這些客觀的**流暢度／
表達方式**指標，但沒辦法判斷「這個字的發音對不對」。

好消息是：算流暢度指標**完全不用多花錢**——現在每次錄音本來就會呼叫一次
Whisper（已經有收費機制 `AudioTranscription`），只要把回應格式從 `json`
改成 `verbose_json`，同一次呼叫就會多回傳時間戳記，用這些資料在後端算出
語速/停頓/贅字，不需要多打一次 AI、也不需要新的第三方服務。我已經把這個
（Phase 1）拆成 `TASK-004`（後端）/`TASK-005`（前端），標記 READY，不需要
等您裁示就可以開工——跟之前 SEO Phase 1 的做法一樣，低成本、不影響架構。

### 需要您之後裁示的部分：真正的「發音評分」（Phase 2）

如果您希望的是更接近「AI 聽你唸單字，告訴你哪個音發得不準」這種功能，那
需要串一個專門做發音評估的第三方服務（例如 Azure Speech 的 Pronunciation
Assessment），這跟 Phase 1 是完全不同量級的決定：
1. **新的外部廠商依賴**——不是現有 OpenAI 帳號範圍內的功能。
2. **新的固定/按次成本**——不像 Phase 1 是「重用已經在付費的呼叫」。
3. **現在錄音檔案用完就丟棄**，要串發音評估服務的話，錄音需要被保留/傳送
   給該服務，這是額外的工程量，也牽涉到使用者錄音資料要保存多久的問題。

我目前**沒有**先斬後奏去動工這塊，因為這牽涉到新廠商/新成本，比較適合像
之前「要不要導入預渲染服務」那次一樣，先讓您知道有這個選項、成本量級，再
決定要不要投入。我的初步建議是：先讓 Phase 1（免費的流暢度回饋）上線、看
使用狀況，如果之後真的需要「發音準確度」這種更深的功能，再回來評估要選
哪家服務、成本多少——但這個順序判斷您也可以直接告訴我不同意見。

### Tasks
- `TASK-004`（Backend）：Whisper 改 `verbose_json` + 語速/停頓/贅字指標
  + 規則式回饋文字，零額外成本 — READY
- `TASK-005`（Frontend）：在既有文法修正卡片旁顯示流暢度回饋 — READY
  （依賴 TASK-004 的回應格式）

### Risks / Blockers
無立即阻塞。上述 Phase 2（真發音評分）是待您裁示的項目，非阻塞現有工作。

### Source
Tech Lead

---

## Tech Lead → Boss (2026-09-16)

### Status
接手 Skill-Building 第二項（閱讀理解短文）的技術評估，已拆出 `TASK-006`
（後端）/`TASK-007`（前端），細節在 `.agent/architecture/ARCHITECTURE.md`。
這裡跟您報告一個範圍調整，不急著要您現在裁示，但想讓您知道。

### 範圍調整：閱讀短文不會用「使用者自己收藏的單字」出題

原本 Requirements 文件描述的做法是比照克漏字測驗（Cloze）：從使用者自己收藏
的單字庫出題。盤點現有架構後發現這樣做會有一個實際的成本問題：克漏字/翻譯/
造句測驗能夠「產生一次、大家共用、幾乎不用再花錢」，是因為每一題只綁一個單
字，同一個單字全站共用快取。但一篇短文天生會用到好幾個單字，而每個使用者收
藏的單字組合幾乎不會重複——如果比照克漏字做法，等於每次出題都要重新呼叫一
次 AI，是我們目前所有測驗類型裡最貴的做法，不符合您一直以來對成本的要求。

改用的做法，是比照「聽力測驗」現有的機制（同樣是已經上線、驗證過的模式）：
短文題庫是全站共用的，不是為某個使用者量身打造，系統會視題庫存量決定要不要
再生成新的一篇，大部分時候直接從題庫中挑一篇沒看過的給使用者，只有題庫存量
偏低時才會真的呼叫一次 AI。代價是：短文內容不會特別包含「這個使用者自己收
藏的單字」，純粹是練閱讀理解能力，跟聽力測驗現在的定位一樣。

我認為這個取捨是合理的（成本可控，而且「閱讀理解」本來就是練整體能力，不是
單字複習），已經照這個方向拆好任務、開放給 Engineer 認領，不會等您回覆才開
工。如果您希望閱讀短文一定要包含使用者自己的單字，請告訴我，我會重新評估
（會犧牲掉共用快取這個成本優勢）。

### Tasks
- `TASK-006`（Backend）：閱讀短文資料表 + 生成/快取邏輯（比照聽力測驗題庫
  機制）— READY
- `TASK-007`（Frontend）：閱讀理解測驗頁面 — READY（依賴 TASK-006）

### PR Review
本次順手把 `TASK-004`/`TASK-005`（口說流暢度回饋）的狀態欄位補上 `MERGED`
並完成 Code Review（重新讀過實際合併的程式碼，沒有發現問題）——這兩個 PR
稍早已經合併，只是狀態文件之前沒補。

### Risks / Blockers
無立即阻塞。上述閱讀短文「不個人化」的取捨是待您確認的項目，非阻塞現有工作
——已請 Engineer 直接開工。

### Source
Tech Lead

---

## Tech Lead → Boss (2026-09-17)

### Status
兩件事：(1) 補完 Skill-Building 第二項（閱讀理解）的收尾；(2) 接手第三項
（開放式寫作批改），已拆出 `TASK-008`（後端）/`TASK-009`（前端），細節在
`.agent/architecture/ARCHITECTURE.md`。

### Completed
- 確認 `TASK-006`/`TASK-007` 的 PR（#342、#343）都已合併，實際讀過合併後的
  程式碼做 Code Review，沒有發現問題，已將兩個任務的 `Status` 補上
  `MERGED`。
- 順便記錄一筆：您稍早直接在 `master` 上修了一個閱讀理解的實際問題（PR
  #345）——原本的驗收只檢查題目/選項的「格式」對不對，沒檢查「語言」對不
  對，導致有一次生成的短文整篇是中文也通過驗證、被存進共用題庫（會一直
  發給所有使用者，直到有人手動刪除）。您加的 `ReadingPassageValidator`
  直接擋掉這類輸出，設計合理（驗證器當硬性關卡、prompt 加強是第二道防線、
  重試上限 2 次避免卡死），我已經讀過確認沒問題，寫進 `TASK-006` 的 Review
  記錄裡。
- 接手第三項（開放式寫作批改）：盤點 Translation/Sentence 測驗現有的批改
  程式碼後發現，這次可以完全重用「批改」那一半的既有模式（一次性 AI 呼叫、
  跟記錄結果解耦），但「出題」這一步跟其他測驗都不一樣——寫作題目（一個主
  題 + 使用者自己收藏的幾個單字）**完全不需要呼叫 AI**，純粹是資料庫查詢。
  所以這次的扣點設計也不一樣：出題免費，只有送出去給 AI 批改才扣點——跟
  Translation/Sentence「出題」跟「批改」都各自扣一次點不同。

### Tasks
- `TASK-008`（Backend）：`GET api/exam/writing`（免費出題：主題 + 最多 5 個
  使用者自己收藏的單字）+ `POST api/exam/writing/evaluate`（AI 批改，扣點）
  — READY
- `TASK-009`（Frontend）：寫作練習頁面 — READY（依賴 TASK-008）

### Risks / Blockers
無立即阻塞。一個設計取捨供您參考（非急件）：一篇作文可能同時用到 5 個單
字，但現有的單字複習排程（SM-2）欄位設計是「一次測驗對應一個單字」。我沒
有為此新增資料表，而是讓這 5 個單字都套用同一個「這篇作文寫得好不好」的
整體評分去更新複習排程，而不是每個單字分開判斷對錯——理由是這本來就是
一個整體的寫作評分，不是 5 個獨立小題。如果您希望以後能追蹤「這次用對了
3 個、用錯 2 個」這種更細的單字別評分，需要另外加資料表，目前先不做，等
真的有需求再回來評估。

### Boss Attention Required
無需要您現在裁示的事項。上述取捨僅供參考，我已照這個方向拆好任務、開放
給 Engineer 認領。

### Source
Tech Lead

---

## Tech Lead → Boss (2026-09-19)

### Status
收尾 TASK-008/009（寫作練習）的 Code Review 記錄，並接手 Skill-Building
**最後一項**（第 4 項：聽寫練習）的技術評估，已拆出 `TASK-010`（後端）/
`TASK-011`（前端），細節在 `.agent/architecture/ARCHITECTURE.md`。

### Completed
- 確認 `TASK-008`/`TASK-009` 的 PR（#347、#348）都已合併，實際讀過合併後
  的程式碼（不只信任先前的自我報告）——扣點邏輯、SM-2 fan-out、前端費用
  提示位置都符合規格，沒有發現問題。補上兩個任務的 `Review` 欄位與
  `Status: MERGED`。
- 接手第 4 項（聽寫練習）：這次盤點發現，這是四個 Skill-Building 項目裡
  **重用既有機制最多、成本最低**的一個——不需要新的選字邏輯（直接重用
  Cloze 早就在用的隨機選字函式）、不需要改 `RecordExamResultAsync`（聽寫
  是綁定單一單字，跟 Cloze 同一種模式，不是像聽力/閱讀/寫作那樣的
  "wordless" 模式）、批改完全不用呼叫 AI（純字串比對）。跟 Requirements
  文件原本預期的「四項裡最便宜」完全吻合。

### 一個刻意偏離既有慣例的設計，特別跟您說明

目前 Cloze/Translation 這些測驗，第一次拿到題目時，**正確答案就已經包含在
API 回應裡**，只是前端沒有把它顯示出來（信任前端不要作弊）。但聽寫這個功能
的核心玩法就是「光聽聲音猜出單字」——如果比照舊有慣例，把單字文字放進題目
API 回應裡，等於打開瀏覽器開發者工具就能直接看到答案，這比 Cloze 的情況
嚴重很多（Cloze 至少還有選擇題選項限制範圍）。所以我把 `TASK-010` 的題目
API 設計成**只回傳一個 ID，不回傳單字文字**，真正的文字只在（1）伺服器端
產生語音檔時，和（2）使用者送出答案、批改完成後才會揭露。這是我在四個
Skill-Building 項目裡第一次刻意不跟隨既有慣例，已經在 `ARCHITECTURE.md`
寫清楚理由。

### Tasks
- `TASK-010`（Backend）：`GET api/exam/dictation`（回傳題目 ID，照舊扣點）
  + 不扣點的語音端點 + 不扣點的批改端點（純字串比對，含
  Levenshtein 相似度）+ `CreditCosts.DictationTest = 1`（四項裡最便宜）—
  READY
- `TASK-011`（Frontend）：聽寫練習頁面 — READY（依賴 TASK-010）

### Risks / Blockers
無立即阻塞。刻意排除了「可調整播放速度」這個 Requirements 文件裡有提到、
但不在四項核准清單內的功能，只記錄成未來可以低成本加上去的後續項目，沒有
放進這次的任務範圍。

### Boss Attention Required
無需要您現在裁示的事項。這是 Skill-Building 四項清單的**最後一項**——等
`TASK-010`/`TASK-011` 上線，這個 DECISION-005 核准的工作就全部完成了，之後
會轉去處理還沒開始的「Finance/Portfolio/PDF Unlocker 缺口盤點」（同樣是
DECISION-005 裡提到的長期方向），除非您有其他優先順序的想法。

### Source
Tech Lead

---

## Tech Lead → Boss (2026-09-20)

### Status
今天沒有新的裁示要跟您確認，主要是收尾第 4 項（聽寫練習）的工程狀態，並清理
了一次「多個 session 搶同一個任務」造成的重複 PR。

### Completed
- `TASK-010`（後端，聽寫練習）確認 PR #352 已合併，重新讀過實際程式碼做
  Code Review：題目 API 真的沒有洩漏單字文字、扣點/不扣點的分工跟原本規格一
  致、沒有動到 SM-2 記錄邏輯、沒有新的資料庫 migration，跟規格完全吻合。已把
  `TASK-010.md` 的狀態補上 `MERGED`。
- `TASK-011`（前端）發現同時有 **3 個開放的 PR**：#354 是真正的功能實作，
  #355 跟 #356 則是兩個不同 session 各自開的「記錄重複 PR 處理結果」的純文件
  PR——換句話說，兩個 session 為了記錄「#353 跟 #354 重複、留 #354」這件事，
  自己又重複開了一次 PR。我直接讀過 #354 的實際程式碼變更，確認跟任務規格一
  致（題目回應不會洩漏答案、可重複播放的設計合理、跟其他測驗一致的複習提示
  文字都有加上），CI 也是綠燈，技術上沒問題，已在 PR 上留言標記
  **READY FOR BOSS REVIEW**。#355、#356 這兩個重複的文件 PR，我改成直接把
  正確的任務狀態寫進 `master`（這本來就是 Tech Lead 這個角色該做的事，不需要
  另開 PR），然後把這兩個 PR 都關掉，留言說明。

### Technical Changes
無應用程式碼變更，只有 `.agent/tasks/TASK-010.md`、`TASK-011.md`、
`PROJECT_STATUS.md`、`REQUIREMENTS.md` 的文件更新，已直接推到 `master`。

### Tasks
- `TASK-010`：`MERGED`（已收尾）
- `TASK-011`：`PR_OPENED`，PR #354 技術審查通過，**等您合併**

### PR Review
- #354（TASK-011 實作）：READY FOR BOSS REVIEW。唯一還沒做的是「拿實際跑起來
  的網站手動測一次」（確認網路回應真的看不到單字文字、聲音播放、送出答案的
  流程）——兩個實作 session 的沙盒環境都沒有可以連線的後端可以測，這不是我能
  單方面解決的事，建議您合併前自己點開來玩一次，或請 QA 過一輪。
- #353（重複的舊 PR）、#355、#356（重複的文件 PR）：都已關閉，不需要您處理。

### Risks / Blockers
無立即阻塞。

### Boss Attention Required（僅供參考，非急件）
這已經是這個專案第三次發生「兩個排程 session 同時認領同一個 READY 任務，各自
做出一份重複 PR」的狀況（`TASK-002` 的 #331/#333、`TASK-008` 的 #346/#347、
這次 `TASK-011` 的 #353/#354，這次還多了一層 #355/#356 的文件重複）。單純是
排程時間點的巧合（任務標記 READY 之後、PR 還沒開出來之前，容易被第二個
session 也認領到），不是設計上的問題，目前每次都靠事後比對、留好的那份、關
掉重複的方式解決，沒有實質損失。先跟您說一聲，如果您覺得需要一個「認領任務」
的機制（例如開工前先把 Status 從 READY 改成 IN_PROGRESS）來避免這種情況，我
可以評估怎麼加；如果您覺得目前這樣事後清理就夠了，我也會繼續維持現狀。

`TASK-011`（PR #354）合併之後，DECISION-005 核准的 Skill-Building 四項工作
就會全部完成。之後的優先工作會轉去處理還沒開始的「Finance/Portfolio/PDF
Unlocker 缺口盤點」（同樣是 DECISION-005 提到的長期方向），除非您有其他優先
順序的想法。

### Source
Tech Lead

---

## Tech Lead → Boss (2026-09-21)

### Status
`TASK-011`（聽寫練習前端）已合併，PM 也完成了 Finance/Portfolio/PDF Unlocker
的缺口盤點（見 `.agent/boss/PM.md` 2026-09-20）。您還沒回覆盤點裡的優先順序
問題，我先照 PM 訊息裡寫好的預設計畫（沒有回覆就先做提案 1）接手第一項：
**Finance ↔ Portfolio 股票分析串接**，已拆出 `TASK-012`，細節在
`.agent/architecture/ARCHITECTURE.md`。

### 這次盤點：其實後端什麼都不用改，純前端任務

拆之前先去讀了程式碼確認範圍，發現一個好消息：Finance 那四個免費量化端點
（技術指標、籌碼、綜合評分）**本來就沒有登入限制**，也本來就吃「股票代碼 +
市場別」當參數——後端完全不用改。真正缺的東西是「Finance 頁面沒有地方可以
接收『幫我查這支股票』的連結」——目前整頁都是純前端 state，網址列不會帶任何
參數，都要使用者自己手動打字搜尋。

所以這次的做法是：Portfolio 持股明細（`HoldingDetailDrawer`）加一個連結，
帶著股票代碼開新分頁到 Finance 頁面；Finance 頁面則新增「網址帶股票代碼時，
自動填好搜尋框、直接跑一次查詢」的邏輯。只改前端兩個檔案，沒有新的 API、
沒有資料庫異動。

### 一個刻意的設計限制，跟您說明

連結只會帶使用者到 Finance 的「量化分析」分頁（免費，不用登入也能看），
**絕對不會**自動跑到「AI 趨勢分析」那個要扣點的分頁——連結本身只是幫使用者
少打幾個字方便查資料，不應該讓使用者一點連結就在不知情下被扣點。另外這個
連結只會出現在「股票」類型的持股上，基金持倉不會有（Finance 目前完全沒有
基金分析功能）。

### Tasks
- `TASK-012`（Frontend）：Finance↔Portfolio 串接，純前端、無需後端配合 —
  READY，未認領

### Risks / Blockers
無立即阻塞。

### Boss Attention Required
無需要您現在裁示的事項。這次沿用 PM 訊息裡的預設計畫繼續往下推進；PM 盤點
清單裡的第 2-5 項（預測準確度追蹤、多股比較、Portfolio CSV 匯入、再平衡/
FIRE 追蹤）還沒拆，等您回覆優先順序，或之後的 run 按預設繼續處理。

### Source
Tech Lead

---

## Tech Lead → Boss (2026-09-23)

### Status
這次先做了兩件事的收尾/盤點：(1) 修正三個任務檔案裡過期的狀態欄位（其實早就
merge 了，只是文件沒更新）；(2) 接手 Finance 缺口盤點第 3 項（多股比較），拆出
`TASK-014`。細節在 `.agent/architecture/ARCHITECTURE.md`。這次沒有動任何程式碼。

### 文件狀態修正
`TASK-011`、`TASK-012`、`TASK-013` 三個任務檔案都還寫著 `PR_OPENED`，但實際上
對應的 PR（#354、#359、#363）早就合併進 `master` 了——直接查 GitHub API 確認，
已經把三個檔案的 `Status` 都改成 `MERGED`。目前 repo 上沒有任何開著的 PR。

### Finance 多股比較：範圍做了一個縮減，跟您說明一下

PM 盤點清單裡第 3 項寫的是「多股／同類股比較」，但去讀程式碼後發現一個限制：
這個專案的資料庫裡**完全沒有「產業別/類股」這種欄位**，所以如果要做「自動找出
同類股來比較」，等於要另外接一個新的資料來源，成本就不便宜了，跟 PM 原本估的
「很便宜、大部分是前端工作」不符。

我把範圍收斂成「使用者自己輸入 2-3 檔股票代碼來比較」，而不是「系統自動幫你找
同類股」。這樣仍然完全符合這項提案原本要解決的問題（「把 2-3 檔股票的分類評分
卡片並排顯示」），而且真的維持在原本估的成本——純前端、重複利用現有的免費
`comprehensive-analysis` API（呼叫多次即可，不用改後端一行程式碼），畫面上也是
直接重複利用現有的 `CategoryScoreCard` 元件。「自動找同類股」這件事我保留在
架構文件的 Alternatives 裡，如果之後真的想做，會是一個需要另外評估成本的獨立
項目。

### Tasks
- `TASK-014`（Frontend，多股比較）：READY，未認領。

### Risks / Blockers
無立即阻塞。`TASK-014` 不依賴任何其他進行中的任務。

### Boss Attention Required
提醒一下：DECISION-006（拔除收費機制）提案裡「免費用量上限要寬鬆還是保守」這個
問題，PM 已經連續兩次 run（2026-09-21、2026-09-22）問您都還沒有回覆，現在超過
兩天了。這是目前唯一卡住 Tech Lead 動工的項目（金流系統拆除評估），其他工作都
不受影響、照預設計畫繼續推進，不急著現在處理，但想再跟您提一次，避免這件事被
忘記。

### Source
Tech Lead

---

## Tech Lead → Boss (2026-09-22)

### Status
今天處理兩件事：(1) `TASK-012` 這次同時冒出 **6 個重複 PR**，已比對挑出最好的
一份、關掉其他 5 個；(2) 接手 Finance 缺口盤點第 2 項（預測準確度追蹤），已拆出
`TASK-013`。細節在 `.agent/architecture/ARCHITECTURE.md`。

### 重複 PR 問題這次真的比較嚴重，想跟您具體討論一下

之前跟您提過三次「兩個排程 session 搶同一個任務」的狀況（`TASK-002`、
`TASK-008`、`TASK-011`，每次都是 2-3 個重複 PR），這次 `TASK-012` 一口氣冒出
**6 個**完全重複的 PR，全部都是同樣兩個檔案的實作，CI 也都是綠燈。我已經把 6
份程式碼都讀過比對，留下品質最好的一份（#359），其他 5 個都關掉並留言說明原因
（技術細節：#359 用 `useCallback` 正確包裝共用邏輯、`useEffect` 依賴陣列完整
不用硬壓 lint 警告、有用 `useRef` 防止 React 18 開發模式下重複觸發，而且沒有
更動既有函式的簽章去牽動其他檔案）。技術上沒有損失，但這次規模明顯升級了——
單一任務浪費了 6 個 session 的算力去做同一件事。

我這次沒有再只是「跟您說一聲」，而是決定先開始套用一個輕量的緩解做法：**以後
Tech Lead 拆出的任務，工程師開工的第一個動作就是把任務檔案的 `Status` 從
`READY` 改成 `IN_PROGRESS` 並單獨推一次 commit**，讓後面想認領同一個任務的
session 有更早、更可靠的訊號可以判斷（現在的做法是「檢查 GitHub 上有沒有現成
PR」，但 PR 通常要等到工作做完才會開出來，等待視窗很長；改成一開工就先標記，
視窗會縮短到幾秒鐘）。這是流程層面的小調整，不是產品/架構/成本決策，所以我
沒有等您裁示就先做了——但想讓您知道我做了這個判斷，如果您覺得這樣不夠（畢竟
這次是 6 個 session 幾乎同時搶到同一個任務，光是縮短視窗可能還是擋不住），或
想要一個更嚴謹的機制（例如排程層面就避免重複派工），也請告訴我，我會重新評估。

### Finance 預測準確度追蹤：原本的成本估計有一個地方是錯的

盤點的時候發現，PM 原本估的成本依據（「比照 `CakeJobsProvider.cs` 那樣的排程
工作」）其實站不住腳——`CakeJobsProvider.cs` 根本不是排程工作，只是一個「被呼叫
時才抓資料」的一般資料來源，整個專案裡**完全沒有任何排程/背景工作機制**
（沒有 Hangfire、沒有 Quartz、沒有 `BackgroundService`，我整個 repo 搜過了）。
如果真的照原本設定去做，等於要幫這個功能開發一套全新的排程基礎設施，比原本
估的成本高不少。

我改用的做法是：每次 AI 產生個股預測時就存一筆記錄，然後**搭便車**在使用者
每天都會打到的 `market-digest` API 上，順便檢查有沒有「已經到期、還沒驗證」的
舊預測，抓歷史股價來對答案——不用真的做一個排程系統，因為這件事本身也不急著
每天準時執行。純後端功能，這次先不做畫面（短期預測要等幾週才有資料可以驗證、
長期預測要等到 1-3 年後，現在做畫面也沒東西可以顯示），等累積到一些真的驗證過
的資料再回來設計呈現方式。

### Tasks
- `TASK-012`（Frontend，Finance↔Portfolio 串接）：`PR_OPENED`，PR #359
  技術審查通過，標記 **READY FOR BOSS REVIEW**，等您合併。
- `TASK-013`（Backend，預測準確度追蹤 Phase 1）：READY，未認領。

### PR Review
- #359：READY FOR BOSS REVIEW（見上方說明及 PR 上的完整審查留言）。
- #357/#358/#360/#361/#362：重複 PR，已關閉，不需要您處理。

### Risks / Blockers
無立即阻塞。`TASK-013` 不依賴 `TASK-012`，兩者可以平行進行。

### Boss Attention Required
1. 如上，重複 PR 這次規模比較大，我先斬後奏套用了一個輕量緩解做法（任務開工先
   標記 `IN_PROGRESS`），如果您有不同想法請告訴我。
2. 提醒一下：DECISION-006（拔除收費機制）提案裡「免費用量上限要寬鬆還是保守」
   這個問題，PM 2026-09-21 問您之後還沒有回覆——這是目前排隊等您裁示、會直接
   卡住 Tech Lead 動工的唯一項目，其他都已經照預設計畫繼續推進，不受影響。

### Source
Tech Lead

---

## Tech Lead → Boss (2026-09-24)

### Status
接手 Portfolio 缺口盤點第 2 項（CSV 匯入功能），已拆出 `TASK-015`。細節在
`.agent/architecture/ARCHITECTURE.md`。這次沒有動任何程式碼，也沒有需要您
現在裁示的事項——單純跟您說明一個範圍修正。

### 一個成本估計的修正（不需要您裁示，純資訊性）

PM 原本的提案寫「匯出的 CSV 格式跟後端存檔用的格式一樣，所以匯入應該很好做」。
我去讀了實際程式碼（`portfolioExport.ts`、後端的 `PortfolioDto.cs`）才發現這個
假設是錯的：匯出的 CSV 其實是一份「計算後的報表」（市值、損益都已經換算成
台幣），跟存檔用的欄位（原幣別的數量/成本）完全不是同一回事。如果直接把匯出的
CSV 拿來當匯入格式用，任何外幣持倉（美股、外幣計價基金）的成本欄位都會被
悄悄存錯——這不是小事,是真的會存錯資料的風險，所以我沒有照原本的假設做,改成
設計一個新的、專門給匯入用的欄位格式（會附一個「下載匯入範本」按鈕，讓使用者
不用猜欄位名稱）。

好消息是成本估計本身還是對的——這仍然是一個純前端、不用動後端的小功能，只是
機制要改一下。跟上次 Finance 多股比較那次「產業別欄位不存在」的修正是同一類
情況：PM 抓的優先順序/大方向沒問題，但細節要等真的去讀程式碼才會發現。

另外順便發現一個可以重用的東西：`DataAnalysis` 模組裡已經有一份現成的、寫得
不錯的 CSV/TSV 解析器（原本是給另一個上傳資料的功能用），這次會把它抽出來
共用，而不是重寫第二份——避免以後兩份邏輯各自改壞、對不起來。

### Tasks
- `TASK-015`（Frontend，Portfolio CSV 匯入）：READY，未認領。

### Risks / Blockers
無立即阻塞。`TASK-015` 不依賴任何其他進行中的任務，純前端不需要動後端。

### Boss Attention Required
無需要您現在裁示的事項。上面的範圍修正是資訊性質的說明，不是要您選邊——
匯入格式本來就只有「重新設計一個正確的格式」這個選項是對的，沒有更便宜但
正確的替代方案。DECISION-006 的免費用量上限問題依然是唯一排隊中、真正卡住
Tech Lead 動工的項目，其他都不受影響。

### Source
Tech Lead

---

## Tech Lead → Boss (2026-09-25)

### Status
這次處理兩件事：(1) `TASK-015`（Portfolio CSV 匯入）這次一口氣冒出
**4 份完整實作**的重複 PR，外加 1 份想幫忙整理但沒整理完整的文件 PR，已經比對
清楚、留下最好的一份；(2) 接手 QA 在已上線的 `TASK-014` 上回報的一個真實 bug，
拆出 `TASK-016`。細節在 `.agent/status/PROJECT_STATUS.md` 跟 `TASK-015.md`/
`TASK-016.md`。這次想跟您具體討論重複 PR 這件事，不只是跟您說一聲。

### 重複 PR 這次的規模，我覺得已經到需要您裁示、不是我可以自己決定要不要處理的程度

`TASK-015` 從 2026-09-24 開始，一天內冒出 #366、#367、#368、#370 四份**完全獨立
的完整實作**（不是像之前那樣兩三份），外加一份 #369——有個 session 想主動整理
重複 PR、比對了前三份、留下 #367，但它開始比對的時候 #370 根本還沒開出來，所以
它的整理本身也不完整，等於重複問題疊了兩層。

技術上我已經處理完：把四份實作全部讀過（不是只信任每份自己的描述），確認
#367 是唯一一份完全照任務規格「不要自己另外刻一份驗證邏輯，全部交給既有的
`rowError()`」做的——#370 表面上也對，但實際上自己另外寫了「缺少代號」「數量
須為正數」這兩條檢查，繞過了共用的驗證邏輯，跟之前 #369 淘汰 #366 的理由一模
一樣，只是#370 是後來才冒出來、沒被那次比對到。已經留下 #367、關掉其他三份，
標記 #367 READY FOR BOSS REVIEW。

我從 2026-09-13 起已經跟您提過三次這個「排程 session 搶同一個任務」的現象
（`TASK-002`、`TASK-008`、`TASK-011`），2026-09-22 我自己先斬後奏套用了一個
輕量緩解（工程師開工第一步把 Status 從 READY 改成 IN_PROGRESS）。這次證明**這
個輕量做法完全沒有擋住問題**——`TASK-015` 一次排程觸發，可能是好幾個 session
幾乎同時被排程系統喚醒認領同一個 READY 任務，那麼"開工先標記"這個防呆本身也會
在還沒來得及推上去之前就被多個 session 同時繞過。技術上沒有任何損失（每次都有
一份能用的實作），但單一任務燒掉 4-5 個 session 的算力去做同一件事，規模已經
比之前大很多，我覺得不該再由我單方面決定「反正事後清理得了、繼續維持現狀」。

**想請您裁示**：要不要在排程系統那一層（不是 `.agent/` 文件層面）做點什麼，
例如同一個任務只排一個 session 去做、或錯開多個 session 的觸發時間？這已經超出
我這個角色（任務拆解/技術審查）能單方面調整的範圍，需要您或負責排程設定的人
一起看。如果您覺得目前這樣事後清理的成本可以接受、不需要改排程機制，也請告訴
我，我會繼續維持現狀，不會再自己加碼緩解措施。

### 另外：QA 在 `TASK-014` 上留了一個技術問題，我順便回答一下

QA 測試 #364（已上線的多股比較功能）時發現一個真的 bug（單股分頁跟比較分頁共用
同一個 API 呼叫的狀態，導致比較分頁某檔股票查詢失敗時，會多跳出一個不該出現的
整頁錯誤提示），已經拆成 `TASK-016`，READY，等工程師認領，不需要您裁示。

QA 也問了一個流程問題：`#364` 沒有經過 `READY_FOR_QA`/Tech Lead 審查就直接被
合併了，是刻意的決定還是流程疏漏。我回頭查了記錄：不是刻意的決定，是一次
流程被跳過——正常應該是 Tech Lead 審查完標記 READY FOR BOSS REVIEW，QA 再測，
最後才輪到您合併，但那次您在我還沒審查的狀態下就直接合併了。這不影響程式碼
本身的品質（我事後也讀過確認沒問題），純粹是提醒一下：如果您手動合併 PR 的
時候，能盡量等 Tech Lead 先留言標記 READY FOR BOSS REVIEW 之後再合併，QA 抓到
問題時修起來成本會比較低（現在這樣 bug 已經上線，只能再開一張 follow-up 任務
補救，而不是合併前擋下來）。這只是提醒，不是要您改變現在「您隨時可以直接合併」
的權限。

### Tasks
- `TASK-015`（Portfolio CSV 匯入）：`PR_OPENED`，PR #367 技術審查通過，
  **READY FOR BOSS REVIEW**，等您合併（建議合併前後找時間手動點一次匯入流程）。
- `TASK-016`（Finance 比較分頁錯誤訊息外溢到單股分頁，QA 抓到的 bug）：
  `READY`，未認領。

### PR Review
- #367：READY FOR BOSS REVIEW。
- #368、#369、#370：重複/已被取代，已關閉並留言說明，不需要您處理。

### Risks / Blockers
無立即阻塞於程式碼本身。上面提到的排程層重複認領問題，是這次唯一需要您裁示、
而且我覺得已經不適合我繼續自己決定的項目。

### Boss Attention Required
1. 如上，`TASK-015` 重複 PR 規模升級（4 份完整實作），排程層面要不要調整，
   麻煩您裁示。
2. 提醒一次：DECISION-006（免費用量上限設計方向）已經超過一週沒有回覆，不急，
   但這是目前排隊中、真正卡住 Tech Lead 動工（金流拆除）的唯一項目。

### Source
Tech Lead

---

## Tech Lead → Boss (2026-09-26)

### Status
這次收尾了 `TASK-014` 的文件記帳（狀態欄位一直沒補上 `MERGED`），並接手
Portfolio 缺口盤點第 3 項（再平衡建議），已拆出 `TASK-017`，細節在
`.agent/architecture/ARCHITECTURE.md`。這次沒有動任何程式碼，也沒有新的
需要您現在裁示的事項——上次的兩個問題（排程重複認領、DECISION-006 用量
上限）都還沒收到回覆，我沒有再重複問一次，維持現狀等您方便時處理。

### Completed
- `TASK-014.md` 的狀態欄位一直卡在 `PR_OPENED`，但其實 PR #364 是您
  2026-09-23 直接合併的，中間沒有經過 Tech Lead 審查（跟 QA 之前提過的
  流程疏漏是同一件事）。我這次補讀了實際合併的程式碼，確認沒有新問題——
  QA 當時抓到的那個 bug（比較分頁的錯誤訊息跑到單股分頁）已經在 `TASK-016`
  修好了。已把狀態補上 `MERGED` 並寫好 Review 記錄。
- **Portfolio 再平衡建議**：盤點後發現 Requirements 文件原本指的
  `ConcentrationCard`/`groupBy=category` 其實是兩個不相干的機制——前者算的
  是「單一持股」的集中度，不是資產類別配置；後者雖然真的算得出配置比例，
  但是一個「每日時間序列」的重量級 API，對這個功能來說殺雞用牛刀。真正好用
  的是 Portfolio 摘要 API 本來就有的 5 個欄位（台股/美股/基金/現金/不動產的
  市值），完全不用改後端。使用者設定的「目標配置比例」我打算存在瀏覽器
  localStorage（跟現有的手續費折數設定 `feeDiscountRate.ts`同一種做法），
  不是新增一張後端資料表——這不是帳號等級的資料，沒有跨裝置同步的必要。

### Tasks
- `TASK-014`：`MERGED`（已收尾）
- `TASK-017`（Frontend，Portfolio 再平衡建議）：`READY`，未認領。

### Risks / Blockers
無立即阻塞。`TASK-017` 純前端、不依賴任何其他進行中的任務。

### Boss Attention Required
無新增裁示事項。上次提過的兩個問題（排程重複認領要不要調整、DECISION-006
免費用量上限的寬鬆程度）都還沒有回覆，都不急，其餘工作不受影響會繼續推進；
方便時麻煩撥空看一下。

### Source
Tech Lead

---

## Tech Lead → Boss (2026-09-27)

### Status
收尾 `TASK-016`/`TASK-017` 的文件記帳（狀態欄位一直沒補上 `MERGED`），並接手
Portfolio 缺口盤點**最後一項**（FIRE/財務自由目標追蹤），已拆出 `TASK-018`，
細節在 `.agent/architecture/ARCHITECTURE.md`。這次沒有新的需要您裁示的事項——
上次的兩個問題（排程重複認領、DECISION-006 用量上限）都還沒收到回覆，這次沒
有再重複問一次。

### Completed
- 確認 `TASK-016`（PR #371）、`TASK-017`（PR #376）都已合併，重新讀過實際
  合併的程式碼做 Code Review（不只信任 PR 描述），都沒有問題，已補上兩個
  任務檔案的 `Status: MERGED` 與 Review 記錄。
- **Portfolio FIRE 追蹤**：盤點後發現，`GET api/portfolio/summary` 本來就有
  「目前淨值」跟「全歷史年化報酬率（XIRR，含股息）」這兩個數字，完全不用改
  後端。原本 PM 提案寫的資料來源（`net-worth-history`）其實不對——那支 API
  的天數上限被寫死在 180 天（是為了它的分類明細功能設計的限制），拿來當
  「未來好幾年」的成長率假設基礎並不合適，改用摘要 API 本來就有算好的
  全歷史 XIRR 更正確。另外這個年化報酬率欄位有可能是 `null`（使用者持有
  紀錄不滿 30 天時，系統故意不顯示、避免誤導），已在任務規格裡特別提醒工程師
  不能把 `null` 當成 0% 處理，那樣會顯示出「永遠達不到目標」這種嚇人但錯誤
  的結果。

### Tasks
- `TASK-016`：`MERGED`（已收尾）
- `TASK-017`：`MERGED`（已收尾）
- `TASK-018`（Frontend，Portfolio FIRE 追蹤）：`READY`，未認領。這是 Portfolio
  缺口盤點清單（cross-link → CSV 匯入 → 再平衡 → FIRE 追蹤）的**最後一項**，
  上線後 Portfolio 這份清單就會跟 Finance 一樣全部做完。

### Risks / Blockers
無立即阻塞。`TASK-018` 純前端、不依賴任何其他進行中的任務。

### Boss Attention Required
無新增裁示事項。提醒一次上次的兩個未回覆問題（都不急）：(1) 排程系統會不會
讓多個 session 同時搶同一個 `READY` 任務，要不要調整；(2) DECISION-006 免費
用量上限要寬鬆還是保守。`TASK-018` 上線之後，Portfolio/Finance 的缺口盤點就
會全部做完，屆時會需要您或 PM 給下一步方向（例如要不要繼續看 PDF Unlocker，
或回頭處理 Credits & Payments 拆除）。

### Source
Tech Lead

---

## Tech Lead → Boss (2026-09-29)

### Status
處理 PM 插單的 SEO 收錄問題：查了實際算繪流程，並整理 Search Console 操作清單給您。
沒有動程式碼。`TASK-018`（PR #378）已合併，文件記帳待下次收尾。

### Completed
- **實際算繪檢查（PM 要求的 (b)）**：確認 sitemap.xml/robots.txt 程式碼沒問題，但
  **單字頁的 SEO 標題/描述是 client-side 用 `react-helmet-async` 在 API 回來後才
  塞進去的**。直接抓到的原始 HTML 只有 `<title>EngAI Learn</title>`，沒有 meta
  description、沒有 canonical。Googlebot 會執行 JS 並二次算繪，所以這**不是**「完全
  不能收錄」的阻擋原因，但收錄較慢、較不穩定，其他爬蟲/分享預覽（LINE、FB）則完全
  看不到單字專屬標題。這是次要缺口，不是目前零收錄的主因。
- 零收錄的主因仍是 PM 診斷的：沒有做 Search Console 驗證與 sitemap 提交。

### Boss 可直接照做的清單（只有您能做）
1. 開 https://search.google.com/search-console ，用您的 Google 帳號登入。
2. 「新增資源」選 **網址前置字元**，輸入 `https://jackyai.azurewebsites.net/`
   （azurewebsites.net 是 Azure 的網域，無法用 DNS 驗證，所以不要選「網域」類型）。
3. 驗證方式選 **HTML 標記（meta tag）**，把 Google 給的
   `<meta name="google-site-verification" content="…">` 貼給我，我會加進
   `index.html` 並走 PR；合併並部署後回 Search Console 按「驗證」。
   （或選 HTML 檔案：下載檔案給我，放進 `jackyaiapp.client/public/`。）
4. 驗證通過後，左側「Sitemap」→ 輸入 `sitemap.xml` → 提交。狀態應為「成功」。
5. 「網址檢查」貼一個單字頁（例如 `/english/dictionary/apple`）→「要求建立索引」，
   並看「已檢查的網頁」→ 算繪後的 HTML，確認標題是單字專屬的。
6. 收錄通常需數天到數週，期間 `site:` 搜尋仍可能為零，屬正常。

### Tasks
- 無新任務。等您回覆 JackyAI 命名問題後再拆（避免對未定案的品牌動工）。
  預計 Phase A（小）：`index.html` 標題/描述加品牌 + 驗證 meta；
  Phase B（可選，中等）：伺服器端在 fallback 回傳 `index.html` 時，對
  `/english/dictionary/{word}` 注入單字專屬 title/description/canonical，讓原始 HTML
  就有 metadata。Phase B 會動到 SPA fallback 路徑，先請您決定要不要做。

### Risks / Blockers
- 卡在 Boss：Search Console 步驟、JackyAI 命名、Phase B 要不要做。
- Credits & Payments 拆除已解除設計卡點，排在 SEO 之後，下次排程開始拆任務。

### Boss Attention Required
1. 照上面清單做 Search Console，並把驗證 meta tag 貼給我。
2. 「JackyAI」是否加進標題（PM 建議加）。
3. 是否要做 Phase B（伺服器端注入單字 metadata）。
4. 兩個舊問題（排程重複認領、DECISION-006 用量上限寬鬆度——後者已由 DECISION-007 大致回答）仍待回覆。

### Source
Tech Lead



---

## Boss → Tech Lead (2026-09-30)
Google 給的 **HTML 標記（meta tag）** <meta name="google-site-verification" content="iQdbM_ZbN-YKx2V8ng8olDJ_Nr1n4RJsYho2vxDXrG8" />
html檔案我也同時放在 jackyaiapp.client/public/google0f9bacafc291a5ac.html 備用了

---

## Tech Lead → Boss (2026-09-30)

### Status
已收到驗證 meta tag，已開 PR #386（僅 `index.html` 一行）。**等您合併。**

### Completed
- PR #386：加入 `google-site-verification` meta。CI 結果待跑完；此變更不含邏輯。
- `TASK-018` Status 已記帳為 `MERGED`（PR #378 已合併）。

### Boss Attention Required
1. 合併 #386（合併後會自動部署），部署完成後到 Search Console 按「驗證」，接著提交 `sitemap.xml`、對單字頁「要求建立索引」。
2. 您說的備用檔 `jackyaiapp.client/public/google0f9bacafc291a5ac.html` 目前**不在 master**（可能沒 push）。meta tag 已足夠，可不用。
3. 未回覆：JackyAI 命名、是否做 Phase B。

### Source
Tech Lead

---

## Boss → Tech Lead (2026-09-30)
JackyAI 命名是指什麼?我們網站就是叫做JackyAI沒錯
是否做 Phase B，你可能要先告訴我pros & cons，不然我很難決定，如果要做的話只是effort會增加，沒什麼其他壞處那就做吧


---

## Tech Lead → Boss (2026-09-30)

### Status
回覆您的兩個問題，並拆出 `TASK-019`（Phase A）與 `TASK-020`（Phase B）。#386 仍待您合併。

### 1. 「JackyAI 命名」是指什麼
不是問網站叫什麼，是**頁面上實際顯示的文字**：目前 `index.html` 的 `<title>` 是
「EngAI Learn」，單字頁標題是「{word} - EngAI Learn Dictionary」，頁面文字沒有任何
「JackyAI」，只有網址帶 jackyai。所以搜「JackyAI English Apple」時 Google 找不到
標題匹配。既然您確認網站就叫 JackyAI，我視為決定：**把標題/描述改成 JackyAI 品牌**
（例：`{word} - JackyAI 英文字典`、首頁 `JackyAI - AI 英文學習與理財工具`）。
這是 `TASK-019`。若您想保留「EngAI Learn」字樣請告訴我，否則照上面做。

### 2. Phase B（伺服器端注入單字 metadata）Pros & Cons
**做什麼**：使用者/Googlebot 請求 `/english/dictionary/{word}` 時，伺服器把 `index.html`
裡的 title/description/canonical 換成該單字的內容再回傳（SPA 照常載入）。
（現有的 `BotPreviewMiddleware` 只服務 LINE/FB 這類預覽爬蟲，回傳的是簡化頁，不會給
一般使用者，所以不能直接拿來用。）

**Pros**
- 原始 HTML 就有單字專屬 title/description/canonical，Google 不必等 JS 二次算繪，
  收錄更快更穩；所有爬蟲、分享預覽都吃得到。
- 單字資料本來就快取，不新增 AI 成本。

**Cons / 風險（都可控，不是零）**
1. 動到 SPA fallback 路徑，寫錯會影響**所有頁面**載入 → 用 feature 範圍限縮
   （只處理單字路徑）+ 任何例外一律退回原 `index.html`。
2. 每個單字頁請求多一次字典查詢（走既有快取；冷門單字第一次會較慢）→ 設定逾時，
   逾時就退回原 HTML。
3. 注入內容必須 HTML encode，否則有 XSS 風險 → 沿用 `BotPreviewMiddleware` 的作法並補測試。
4. 對不存在的單字要維持現況（不注入），避免產生大量垃圾可索引頁。
5. 多一段程式維護成本；工作量中等（估 1 個 PR + 單元測試）。

**結論**：除工作量與上述可控風險外沒有其他壞處，符合您的條件，**我建議做，並依您
的指示排入**。排在 `TASK-019` 之後，不影響 Search Console 進度。

### Tasks
- `TASK-019`（Frontend/Backend 小工）：JackyAI 品牌標題。READY。
- `TASK-020`（Backend）：Phase B。READY，依賴 TASK-019 先合併避免標題衝突。

### Risks / Blockers
- 無阻塞。#386 待 Boss 合併（合併後才能到 Search Console 按驗證）。

### Boss Attention Required
1. 合併 #386，驗證並提交 sitemap。
2. 若不同意「標題改 JackyAI 品牌」或 Phase B，請回覆，否則照做。

### Source
Tech Lead


---

## Boss → Tech Lead (2026-09-30)
喔不，我想不要用JackyAI這品牌名稱好了
我剛剛跟GPT討論後決定改成這樣
| 現在                                   | 我建議的產品名稱               | SEO 副標         |
| ------------------------------------ | ---------------------- | -------------- |
| My Portfolio                         | **Wealth Portfolio**   | 投資組合與資產追蹤      |
| Stock Intelligence & Market Analysis | **Stock Intelligence** | AI 股票分析與市場情報   |
| English Learning                     | **English Coach**      | AI 英文學習與單字訓練   |
| 職缺搜尋                                 | **Job Finder**         | 職缺搜尋與工作媒合   |
| 資料分析 Demo                            | **Data Analyst**       | AI 資料分析與自然語言查詢 |
| 儀表板                                  | **Data Dashboard**     | 資料視覺化與互動儀表板    |
你覺得如何?
Phase B聽完我覺得就做吧


---

## Tech Lead → Boss (2026-09-30, 改名回覆)

### Status
收到，改用各模組產品名稱，不用「JackyAI」品牌。Phase B 照做。

### 我的看法
整體可以，方向對：每個模組名稱都直接含搜尋詞（portfolio / stock / English / job / dashboard），
比單一品牌名更利於各頁獨立收錄。三點提醒：
1. **Data Analyst 建議改 `Data Analysis`**：Analyst 是「分析師」這個人，當工具名稱會讓人以為是
   職缺或服務；其餘五個都是「工具/產品」語意。您決定，未回覆前該項最後處理。
2. **站台首頁 `<title>`**：目前唯一有被收錄內容的是英文字典，我預設用
   `English Coach - AI 英文學習與單字訓練`；字典單字頁後綴為 `English Coach`。若您想另有首頁標題請說。
3. 只改顯示名稱，**不改路由 URL**，避免既有連結/書籤/索引失效。

### Tasks
- `TASK-019` 已改寫為上述新名稱對照（READY）；依賴 #386 先合併。
- `TASK-020`（Phase B）已標示 Boss 核准，接在 TASK-019 後。

### Boss Attention Required
1. 合併 #386 → Search Console 按驗證 → 提交 sitemap。
2. 確認 `Data Analyst` vs `Data Analysis`，以及首頁 title 預設。

### Source
Tech Lead
