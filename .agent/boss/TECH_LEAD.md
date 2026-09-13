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
