# PROGRESS（新的在最上面）

## 2026-10-25 11:41 – 第 17 輪（選取文字問 Studio AI、審查）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠。
- **選取文字問 Studio AI**（D-086）：任何有 Studio 的頁面選一段字 → 上方工具列「解釋／翻成中文（或英文）／問 AI」；選一個英文單字 →「查字典（不經 AI）／加入單字庫／問 AI」。Studio 自己回答裡的選取（短文裡不認識的字）接在原對話，頁面上的選取開新對話。前端：`SelectionBar`、`readSelection`、`selectionActions`；Studio 的 `onSelection`。
- **頁面文字當外部文字**：chat 請求多 `quotesPageText`（`ChatRequest.QuotesPageText`），伺服器在這一輪開始前把對話標成讀過外部文字（寫入一律先問）。測試：`AnthropicChatStreamTests`（引用頁面文字的訊息，自動的寫入照樣被問、對話被標記）、面板（立即送、排隊送、輸入框送都帶旗標）、Studio、SelectionBar、selectionActions。
- **審查**（subagent，D-087）：一個高風險已修（「問 AI」放進輸入框的頁面文字送出時沒帶旗標 → `inputQuotesPageText`），兩個中風險、兩個低風險已修。
- 文件：兩份 CLAUDE.md、D-086～D-087、SHOWCASE（亮點一列、demo 第 3 步、已知限制、日誌）；截圖 `select-sentence-*`、`select-translate-*`、`select-word-studio-*`（`.loop/shots/selection.cjs`）。
- **巡檢 /portfolio 的 Studio 歡迎畫面**（桌機、手機，`studio-portfolio-welcome-*`）：四個建議排列正常；歡迎訊息多一句「在頁面上選取一段字，也能直接叫我解釋或翻譯」，讓人發現選取工具列。
- 驗證：cscheck 新診斷 0；前端 lint、test（136 檔 1454 個）、build 全過；CI 40160603830（選取文字＋quotesPageText，第一次就綠）、**40161718965（審查修正）綠**；**40161885791（本輪最後一次含程式碼的 push：歡迎訊息）綠**。
- 問題：無阻礙。

## 2026-10-25 09:42 – 第 16 輪（用說的／貼截圖記交易、審查、巡檢）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠。
- **`update_holding`**（D-081）：「我今天加碼 0050 兩張，均價 185，幫我記下來」→ 確認卡「『0050』加 2,000（成交價 185）（存成今天的快照）」→ 最新快照改那一筆、存成今天（台北）的快照；成本照平均成本法；只改既有持倉（新增回連結卡）、對到兩筆不猜、買進沒價格不存。記完的接著問「我的投資組合現在怎麼樣？」；/portfolio 的 Studio 建議「幫我記一筆今天的交易」（AI 會問細節或請他貼截圖）。測試：`UpdateHoldingToolTests`（19 個情境）、`StudioChatProfileTests`、`studioChat.test.ts`。
- **貼成交回報截圖記帳**（D-083）：只改 Studio 的 prompt（附件本來就共用）；截圖情境 `studio-trade-image-*`（`.loop/shots/fixtures/trade-confirmation.png` 是自己畫的虛構回報；`chat.cjs` 補了附件上傳與內容的假 API）。
- **寫入後頁面立刻重讀**（D-085）：面板的 `onToolDone` → Studio 的 `refreshAfterTool`（`update_holding` → 投資組合、`add_word_to_repository` → 單字庫與複習）；抽屜後面的頁面不用手動重新整理。測試：`refreshAfterTool.test.ts`、Studio 與面板的測試。
- **審查**（subagent，D-082）：無高風險；兩個中風險已修（價格與成本幣別不同只收 `cost`、同日第二次修改照實說取代了當天的快照）。
- **巡檢**：/english/exam、/english/repository 手機版 Studio 的建議正常；/finance 回答下方的複製／朗讀是滑鼠移上去才出現（桌機設計如此，`MessageActions`），截圖沒有 hover 所以看不到，不是 bug。每日挑戰不放進 Studio（D-084）。
- 文件：server CLAUDE.md（模組表、Studio 工具）、D-081～D-084、SHOWCASE（亮點一列、demo 第 4 步、日誌）、PLAN；截圖 `studio-trade-*`（含允許後的 `studio-trade-done-*`）、`studio-trade-image-*`。
- 驗證：cscheck 新診斷 0；前端 lint、test、build 全過；CI 40152058952（update_holding，第一次就綠）、40152437241、40152714154（審查修正）、40153238288（/portfolio 建議）綠，**40154034322（本輪最後一次含程式碼的 push：寫入後頁面重讀）綠**。前端全部測試 134 檔 1423 個通過。
- 問題：無阻礙。

## 2026-10-25 07:42 – 第 15 輪（用自己的單字寫短文、朗讀、審查）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠。
- **`saved_words`**（D-078）：Studio AI 讀使用者最近存的單字（`IRepositoryService.GetRecentWordsAsync`：最近 1–30 個、一個詞性與中文意思、總字數）；/english/repository 的第一個建議「用我單字庫裡最近存的字寫一段短文」，接著問「換個主題再寫一段」「考我幾題」；今日簡報有到期的字時最後問要不要考幾題。測試：`SavedWordsToolTests`、`RepositoryServiceTests`（SQLite：新的在前、只算自己的、上下限）、`StudioChatProfileTests`。
- **朗讀**（D-079）：三個助理的回答下方多「朗讀」（`useSpeechOutput`＋`utils/speechText`：Markdown 變成字、英中分段各用自己的聲音）。
- **審查**（subagent，D-080）：無高風險；兩個中風險（朗讀的擁有權、長段落）與三個低風險已修。
- 文件：兩份 CLAUDE.md、D-078～D-080、SHOWCASE（亮點兩列、demo 第 3 步之後、日誌）、PLAN；截圖 `studio-writing-*`、`studio-read-aloud-*`（`.loop/shots/chat.cjs` 加了這個情境）。
- 驗證：cscheck 只有已知雜訊（`AsNoTracking`）；前端 lint、test（130 檔 1397 個，加上這輪的新測試）、build 全過；CI 40144454772（saved_words，第一次就綠）、40145153097（朗讀）綠、**40145843957（本輪最後一次含程式碼的 push：審查修正）綠**。
- 問題：Write 工具把 regex 裡的 `\u3000` 這類跳脫寫成真的字元，被 eslint 的 no-irregular-whitespace 擋下——regex 裡的 Unicode 範圍用 python 寫入或事後 grep 確認。

## 2026-10-25 05:41 – 第 14 輪（指令列打開 dashboard、頁面建議、複習小測驗、審查）

- 開工：master 的新 commit（chore: remove details）已在 branch 裡；上一輪最後的 CI 綠。
- **巡檢**（字典頁、/portfolio、/dashboards 列表上的 Studio；新增 MCP server 三步驟的手機版）：字典頁的建議還是泛用的「幫我查 serendipity」→ 改成畫面上那個字（`dictionaryWordOf`）；手機上新增 MCP server 完成步驟的工具說明被權限按鈕擠成「Search t…」→ 窄螢幕時按鈕放到下面。歡迎訊息加一句指令列提示。
- **指令列打開 dashboard**（D-075）：打名稱的一部分 →「打開『每月業績追蹤』」；列表每次打開抽屜重讀；/dashboards 列表頁的第一個建議「幫我解讀『X』這個 dashboard」。
- **複習小測驗**（D-076）：`review_quiz`（`ReviewQuizService`：到期的字，沒有就用最快要複習的；每日挑戰的產生器 `GenerateFor`，不花 AI）→ `quiz` 卡片（`QuizCardView`：一次一題、立刻對錯與解說、答完送 `review/submit` 記進 SM-2＋XP）；/english 的第一個建議「考我今天該複習的單字」，接著問「再考我 3 題」。
- **審查**（subagent，D-077）：無高風險；兩個中風險已修（重開對話的測驗卡可重複記分 → 卡片 renderer 的 `saved`，只當練習；沒到期的字提早作答會把排程推遠 → 只當練習），一個低風險已修（干擾選項不再排序整張 Words 表）。
- 文件：兩份 CLAUDE.md、D-075～D-077、SHOWCASE（亮點一列、demo 第 1、3 步、日誌）；截圖 `studio-quiz-*`、`studio-quiz-result-*`、`studio-dictionary-desktop`、`studio-quick-dashboard-*`、`connectors-add-*`。
- 驗證：前端 lint、test（約 1394 個）、build 全過；CI 40136255328（指令列＋建議）、40136894481（review_quiz 後端，第一次就綠）、40137508555（quiz 卡片）、40138680323（審查修正）綠；**40139002502（本輪最後一次含程式碼的 push：/dashboards 建議）綠**。前端全部測試 1395 個通過。
- 問題：無阻礙。

## 2026-10-25 03:42 – 第 13 輪（巡檢 /finance 與 /connectors、/finance 工具中文名稱、⌘K 指令列、審查）

- 開工：master 只有「chore: remove details」，已在 branch 裡；上一輪最後的 CI 綠。這輪起 clone 是 shallow，`git fetch --unshallow` 後才看得到完整歷史。
- **巡檢**：`connectors.cjs` 加了目錄（tw-stock、DeepWiki、Notion、Linear）與四個已安裝的 connector（本站、DeepWiki、Notion 需要重新授權、自訂的 bearer server），拍目錄、三個詳情抽屜；`chat.cjs` 加 /finance（一行框 → 展開 → 送出 → 工具 chip、接著問）。首頁手機版下半部看過，沒問題。
- 發現並修好：自訂 MCP server 的抽屜把網址列兩次（`drawerDescription`）；/finance 的工具 chip 寫「stock history」（**D-071**：`AiChatConfig.toolLabels`、`FINANCE_TOOL_LABELS`，`withToolLabel` 在 `useChatStreaming` 補 `displayName`，標題列也變中文）。
- **⌘K 指令列**（D-072）：Studio 輸入框打頁面名稱或一個英文單字 → 上方「前往『複習』」「查字典：review」，按下直接去，不經 AI；`QUICK_PAGES` 由測試讀伺服器的 `SitePages.cs` 對齊。面板新 prop `inputAccessory`。
- 評估後不做：/finance 的助理讀投資組合（D-073，無法驗證 hosted MCP＋本站工具的組合，怕 /finance 壞掉）。NEXT 的「dashboard 頁的接著問帶去 /data-analysis」也不做（價值小）。
- **審查**（subagent，D-074）：無高風險；一個中風險（指令列換頁後同一回答的 navigate 仍會帶走）與四個低風險已修。
- 文件：client CLAUDE.md、SitePages.cs 註解、D-071～D-074、SHOWCASE（亮點、demo 第 1 步指令列、第 9 步 /finance 截圖、後續建議、日誌）；截圖 `studio-quick-*`、`finance-answer-*`、`connectors-desktop`（換新）、`connectors-drawer-*`。
- 驗證：前端 lint、test（128 檔 1376 個）、build 全過；CI 40127740177（工具中文名）綠、40128623308 **紅一次**（SitePages.cs 註解裡寫了 `new("key", "/path", …)`，被前端一致性測試當成頁面）→ 40129014469 綠、**40129836280（本輪最後一次含程式碼的 push：審查修正）綠**。
- 問題：無阻礙。

## 2026-10-25 01:42 – 第 12 輪（「存完打開」、dashboard 的 AI 解讀、確認卡精簡、手機 dashboard、審查）

- 開工：master 只有 README 精簡（已在 branch 裡）；上一輪最後的 CI 綠。
- **畫面巡檢**：三個截圖腳本全部重拍、逐張看。發現：存 dashboard 的確認卡列出 `chartId: k3f9` 這種對使用者無意義的參數；手機上 dashboard 兩張圖並排、圖例被截成「1/3」。
- **確認卡精簡**（D-067）：本站工具有一句話說明時，原始參數收進「顯示送出的參數」；外部 connector 照舊全列。
- **「存完打開」**（D-066）：`save_to_dashboard` 的 `open` → `Navigate`；`AiAssistantLauncher` 的 `onNavigate` 等回答正常結束才換頁（停止或失敗不換，`AssistantActivity.answered`）；/data-analysis 傳 `useNavigate()`，Finance 不變。
- **dashboard 的「AI 解讀」**（D-068）：Studio／本站 MCP server 的 `read_dashboard`（只讀自己的、每張圖用最新資料重算、6 張 × 40 列、20 秒預算、`OutsideText`）；在 `/dashboards/{id}` 時 Studio 的 prompt 寫出那個 id；擁有者在 dashboard 標題旁有「AI 解讀」（開新對話），Studio 在 dashboard 頁的建議與接著問。
- **手機 dashboard 一列一張圖**（D-069，`Dashboard/viewLayout`，只改顯示）。
- **審查**（subagent，D-070）：無高風險；中風險（read_dashboard 整個逾時）與三個低風險已修，兩個低風險接受。
- 截圖：`.loop/shots/chat.cjs` 加 dashboard 場景；新增 `dashboard-ai-button-*`、`studio-dashboard-*`，更新 `data-analysis-save-*`、`studio-approval-*`。SHOWCASE 亮點一列、demo 第 5 步、日誌。
- 驗證：前端 lint、test（126 檔 1354 個）、build 全過；CI 40118152832（存完打開）、40118543937（read_dashboard，第一次就綠）、40119335774（AI 解讀＋手機版面）、40119697002（審查修正）、**40120425041（本輪最後一次含程式碼的 push：停止時不換頁）綠**。
- 問題：`messages.at()` 在 vitest 能跑、`tsc` 的 lib 不認得（build 才抓到）——前端改動一定要跑 `npm run build`。

## 2026-10-24 23:42 – 第 11 輪（今日簡報開新對話、接著問、圖表卡片換圖、「帶我去」、審查）

- 開工：master 的新 commit 已在 branch 裡；上一輪最後的 CI 綠。
- **今日簡報開新對話**（D-061）：`askStudio(text, { fresh })` → 面板 `ask.fresh` → `useChatConversation.send(…, { fresh: true })`（`conversationId: ''`、清掉畫面上的舊對話；回答中時照樣排隊，輪到才開新對話）。測試：開新對話且之後接著新的、排隊中的 fresh。
- **接著問**（D-062）：`utils/followUps`（依回答用到的工具，最多 3 個、不重複、不含剛問的）、`FollowUps`、`Message.answered`；Studio、Data Analysis、Finance 各自的規則（Studio 不會在複習頁提議「帶我去複習」，`studioFollowUpsFor`）。按下直接送出。
- **圖表卡片換圖**（D-063）：`ChartCardView` 列出資料適用的圖（`matcher.evaluate`），換圖不問 AI；「加到 dashboard」存畫面上的那種。
- **「帶我去」**（D-064）：`open_page` 的 `go` → `AiToolResult.Navigate` → `tool_result.navigate`（本站工具、站內路徑、沒讀過外部內容才送）→ 前端 `sitePathOf`、`onNavigate`；Studio 換頁、手機上回答完才收起、/finance 這類頁面回答完才去。
- **審查**（subagent，D-065）：無高風險；兩個中風險（taint 後仍會自動換頁、去 /finance 時回答串流進被關的抽屜）與一個低風險已修；dashboards 的存取沒有問題。
- 截圖：`.loop/shots/chat.cjs` 加「帶我去」（桌機、手機收起後再打開）與 Data Analysis 換圖＋接著問；存進 `screenshots/`（`studio-go-*`、`data-analysis-followups-*`、`studio-portfolio-*` 更新）。
- 文件：兩份 CLAUDE.md、SHOWCASE（亮點、demo 第 2 步「帶我去」、換圖與接著問、日誌）、DECISIONS D-061～D-065。
- 驗證：前端 lint、test（125 檔 1345 個）、build 全過；CI 40109000286（簡報＋接著問）、40109618018（換圖）、40109752797（open_page go，後端第一次就綠）、40110765141（前端 navigate）、40111291190（審查修正，綠）、**40111733787（本輪最後一次含程式碼的 push）綠**。
- 問題：無阻礙。

## 2026-10-24 21:42 – 第 10 輪（跨訊息 taint、AI 今日簡報、語音輸入、MCP App 按鈕、畫面巡檢）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠。
- **跨訊息 taint**（D-055）：`ChatConversations.ReadOutsideText`（直接加進還沒上線的 `AddChatProfileAndCards`，三個 Designer 與 snapshot 一起補），讀過外部內容的對話之後每輪的寫入都先問；兩個端對端測試。
- **畫面巡檢**（`.loop/shots/chat.cjs`：Studio 確認卡、持倉表格、Data Analysis 存 dashboard、今日簡報、語音，桌機＋手機；chat 串流用頁面內 fetch 替身，可停在確認卡）。發現並修好：確認卡排在回答文字上方、等確認時還在顯示「思考中」、標題列顯示原始工具名「正在查詢 look_up_word」且等確認時不變（`hooks/chatStatus`，D-057）、表格卡片在手機放不下 4 欄（數字欄不換行、窄邊距）。
- **首頁「AI 今日簡報」**（D-056）：`askStudio(text)` 讓任何頁面帶一句話打開 Studio；Studio 的 prompt 定義簡報（日曆＋學習進度＋投資組合，三段＋先做哪件事）；Studio 建議第一個就是它。
- **語音輸入**（D-058）：三個助理的輸入框都有麥克風（瀏覽器 Web Speech API、zh-TW、即時顯示，不自動送出；Firefox 不顯示）。
- **MCP App 卡片按鈕**（D-059）：`IOffersAppActions`／`SiteAppAction`，查字典的卡片在 Claude／ChatGPT 裡多「加入單字庫」，經 host 的 `tools/call`，只給能呼叫該工具的 token。
- **審查**（subagent）：無高／中風險，三個低風險已修（D-060）。
- 文件：兩份 CLAUDE.md、design/03 §9.2、design/06 §2、SHOWCASE（亮點兩列、demo 第 1 步改成今日簡報、新截圖、日誌）。
- 驗證：CI 40099476741（taint，第一次就綠）、40100615860（前端）、40101302429（今日簡報）、40102307019 **紅一次**（測試重用同一個 JsonObject 當兩次 arguments：「node already has a parent」）→ 40102535138 綠、**40103374636（本輪最後一次含程式碼的 push：審查修正）綠**。前端 lint、test（124 檔 1327 個）、build 全過。
- 問題：Chromium 自己有 `SpeechRecognition`（不只 webkit 前綴），截圖腳本要兩個都換掉。

## 2026-10-24 19:42 – 第 9 輪（本站 MCP server 成為 MCP App、收尾、安全審查）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠。
- **本站 MCP server 收尾**：連結卡的相對網址在 tools/call 結果文字後補成「標題：完整網址」；`register`／`token` 依 `CF-Connecting-IP` 每分鐘 30 次（D-049，被擋回 OAuth 格式）；OAuth 發的 token 標 `ViaOAuth`（直接改還沒上線的 `AddSiteAccessTokens` migration，D-051），/connectors 顯示「經 OAuth 登入」；serverInfo 帶 `websiteUrl`、`icons`。
- **MCP App**（D-050，design/03 §9.2）：`SiteMcpApp`（`ui://jacky-studio/card.html`，內嵌、只用 textContent、連結經 `ui/open-link`、跟 host 深淺色），initialize 宣告 `resources` 與 `io.modelcontextprotocol/ui`，每個工具 `_meta.ui.resourceUri`，結果多 `structuredContent.card`。Claude／ChatGPT 裡會畫出本站的持倉表格與單字卡。
- **Studio 引導**：問「怎麼讓 Claude 用這個網站」→ Connectors 連結與說明；/connectors 的 Studio 建議多一則；「讓其他 AI 使用本站」說明改寫（Claude.ai／ChatGPT 貼網址、其他用 token）。
- **安全審查**（subagent，唯讀，範圍：SiteMcp、OAuth 授權伺服器、MCP OAuth client、SafeHttp）：無高／中風險；修三個低風險（DCR redirect 總長、SafeHttp 不走 proxy＋擋 168.63.129.16、MCP App 只收 parent 訊息），兩個接受（D-052）。
- **第二次審查**（agent loop、connector、本站工具）：**高風險**——外部 connector 工具在 /connectors 設的「需確認」在 chat 沒生效（權限用伺服器名存、chat 用 `m1__…` 查），改 `AiToolDescriptor.PermissionName` 並加端對端測試；中風險——清理後同名的工具會讓對話每輪中斷，改成只留第一個；低風險——確認逾時的競態、卡片連結的控制字元（前後端都擋）。D-053。
- **第三次審查**（前端）：**高風險**——AI 回答的 HTML 預覽原本 innerHTML 在本站 origin，改成無權限 sandbox iframe；中風險——Studio 換頁不再丟對話、斷掉的串流當失敗、離開頁面停止串流；低——回合結束關閉確認卡、回呼訊息重複名稱。D-054。
- `.loop/shots/`：`connectors.cjs`（dist＋假 API）、`mcp-app.cjs`（自己扮 MCP Apps host；`<img onerror>` 當資料只顯示成文字、按鈕走 open-link），截圖存進 `screenshots/`（`mcp-app-card-*`、`site-mcp-section-*`）。
- 文件：design/03 §9.1–9.2、server CLAUDE.md、SHOWCASE（亮點、demo 第 7 步、截圖、日誌）、CONTRIBUTING、FOR_JACKY 9、PLAN、D-049～D-052。
- 驗證：每個後端 commit 先 cscheck（0 個新診斷）再看 CI：40091300375（收尾）、40091669851（ViaOAuth migration）、40091861427（Studio）、40092081827（MCP App）、40092790787（前端說明）、40093015146（icons）、40093462486（安全修正）、40094057242／40094269512（第二次審查修正與測試）、**40096172144（本輪最後一次含程式碼的 push：前端審查修正）綠**。前端 lint、test（121 檔 1300 個）、build 全過。
- 問題：cscheck 的 EF 參考這輪幾乎綁不到（base／head 都 999 種診斷），EF 相關檔案的錯誤會被蓋住，以 CI 為準（NEXT §1）。

## 2026-10-24 17:42 – 第 8 輪（本站對外的 MCP server：PAT、OAuth 授權伺服器、同意頁）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠。
- **本站 MCP server**（D-044～D-046）：`POST /api/mcp`（自己寫的最小 Streamable HTTP server，無 session、JSON 回應，initialize／ping／tools/list／tools/call）、`SiteToken` 驗證方案只掛在這個端點、PAT（`jsk_`、只存 SHA-256、30／90／365 天、每人 10 個、可撤銷）、migration `AddSiteAccessTokens`（新表）、提供的工具照 /connectors 權限與 token（預設唯讀、寫入要勾、無法復原的不給）、每 token 每分鐘 120 次、檢查 Origin。測試含**官方 SDK 的 client 端對端打這個 server**。
- **/connectors「讓其他 AI 使用本站」**：伺服器網址、token 清單（唯讀／可寫入、最後使用、到期）、兩段式撤銷、產生後只顯示一次 token 並附 Claude Code／Claude Desktop（mcp-remote）／Cursor／VS Code 設定一鍵複製。
- **本站當 OAuth 授權伺服器**（D-047、D-048）：PRM、AS metadata、DCR（不用資料表，data protection）、同意頁 `/oauth/authorize`（未登入先登入並帶著 query 回來；顯示 client 與實際導回網域；使用者決定可否寫入）、一次性授權碼＋PKCE＋resource＋iss、access token 就是 PAT（90 天）。Claude.ai／ChatGPT 只要貼網址。
- 文件：design/03 §9／§9.1、design/06 §9、兩份 CLAUDE.md、CONTRIBUTING 附註、SHOWCASE（demo 第 7 步、架構圖、合併清單）、FOR_JACKY 9。
- 驗證：每個後端 commit 先 cscheck（0 個新診斷，只剩 EF 雜訊）再看 CI：40083725395（MCP server＋PAT＋migration，第一次就綠）、40084568532（前端）、OAuth 零件、40085264395（OAuth 端點）、**40085800201（本輪最後一次含程式碼的 push：同意頁）綠**。前端 lint、test（120 檔 1294 個）、build 全過；playwright 截圖 /connectors 的新區塊、token 對話框、同意頁（桌機、手機）。
- 問題：無阻礙。Claude.ai／ChatGPT 的實際 OAuth 流程與 Cloudflare 是否擋非瀏覽器流量，要合併後實測（FOR_JACKY 9）。

## 2026-10-24 15:42 – 第 7 輪（M4：TestKit、Google 日曆、step-up、以後直接執行、rug pull）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠。
- **TestKit**：`TestHelpers/FakeMcpServer`（Streamable HTTP MCP server，給真的 SDK）、`FakeAuthorizationServer`（MCP 401＋PRM＋AS metadata＋DCR＋token 端點，驗 PKCE／redirect URI／resource、refresh token 輪替；`Approve` 扮使用者）、`FakeWeb`、`TestStateService`。`McpOAuthSignInTests` 走完 probe → 新增 → 授權 → 回呼 → 列工具 → chat 呼叫工具 → 到期 refresh → 撤銷後重新授權（第一次 CI 紅：測試共用一個 DbContext，回呼拿到已追蹤的舊列沒存到 Ok；改成每個請求一個 context，跟 app 一樣）。`SiteToolDefinitionsTests` 檢查本站每個工具的 Definition。
- **Google 日曆原生 connector**：`GoogleCalendarTool`（`google_calendar_events`，今天／明天／7 天，台北時間），沿用既有 Google 帳號連線；沒連線回連結卡。`AiToolDescriptor.OutsideText`：行事曆邀請是別人寫的字，讀過後這輪的寫入一律先問（D-039）。Studio 提示與建議「我今天有什麼行程？」。table 卡片欄位可 `nowrap`。
- **MCP step-up**：403 `insufficient_scope` → scope 併進登入、NeedsAuth、保留 token，重新授權時一起要（D-040）。
- **確認卡「以後直接執行」**：本站工具的寫入、沒讀過外部內容時可勾，允許後設成自動（D-041）。
- **rug pull 防護**：重新列工具時，設成自動的工具若說明／schema／效果變了改回需確認（D-042）。
- **OAuth 回呼中文訊息**（`kind=mcp`，D-043）。
- 文件：CONTRIBUTING（原生 connector、TestKit）、server／client CLAUDE.md、SHOWCASE（Google 日曆 demo 步驟與截圖、確認卡截圖更新）。
- 驗證：每個後端 commit 先 cscheck（0 個新診斷）再看 CI：40075630687（TestKit，紅一次如上）、40076039924（修正＋Google 日曆）、40076727511（step-up）、40077184496（回呼訊息）、40077412480（工具一致性）、40078062263（以後直接執行＋rug pull）、**40078794952（本輪最後一次含程式碼的 push：nowrap）綠**。前端 lint、test（115 檔 1271 個）、build 全過；playwright 截圖 Studio 讀 Google 日曆、確認卡。
- 問題：無阻礙。Google 日曆在 production 的實際權限需要 Jacky 驗（FOR_JACKY 8）。

## 2026-10-24 13:41 – 第 6 輪（MCP OAuth、Portfolio／學習進度工具、用說的存 dashboard、SHOWCASE 初稿）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠。
- **MCP OAuth（M3）**：`McpOAuthDiscovery`（401 的 `WWW-Authenticate` → PRM → AS metadata〔RFC 8414 → OIDC〕，驗 resource／issuer、必須 S256、只收 https、64KB、全經 SafeHttp）、`McpOAuthFlow`（成為 client：DCR → CIMD → 手填〔D-034〕；authorize 帶 PKCE、resource、scope、state，verifier 只在伺服器；回呼驗 state、一次性、iss 後換 token）、`McpOAuthTokens`（到期前 60 秒 refresh、每個安裝一把鎖、失敗且過期 → NeedsAuth 保留 client）、端點 `installations/{id}/authorize`、`callback/mcp`（字面路由）、`oauth/client-metadata`；probe 遇 401 回 oauth。工具呼叫被拒認證 → connector 標 NeedsAuth。PRM 的 resource 接受同源上層路徑（D-035）。目錄加 Notion、Linear（OAuth，未實測，D-037）。前端原本就準備好，playwright 截圖確認目錄與「偵測到 OAuth」對話框。
- **本站工具**：Portfolio 的 `portfolio_summary`（Portfolio 模組可以用 Connectors；4 欄持倉表格卡片放得進 Studio 側邊）、English 的 `learning_progress`（到期複習、每日挑戰、連續天數、XP），Studio 提示與 /portfolio 建議更新。
- **Data Analysis 用說的存 dashboard**：`ask_data` 回傳 `chartId`（`DataAnalysisCharts`，記憶體、依使用者與對話），`save_to_dashboard`（寫入 → 確認卡）存使用者看到的那張圖到同名 dashboard 或新建（D-036）。
- **確認卡更好讀**：本站工具可以說「這次會做什麼」（`IDescribesCall` → `approval_required` 的 `summary`，connector 的工具不採用），存 dashboard 的確認卡不再只看到 chartId。
- **SHOWCASE.md 初稿**：亮點表、8 分鐘 demo 腳本（附截圖）、mermaid 架構圖、合併檢查清單、本機試跑、已知限制、日誌表。
- 文件：server CLAUDE.md（模組表、Studio、Connectors 的 MCP OAuth 三個單元、Data analysis）、CONTRIBUTING-CONNECTORS（目錄可用 oauth）。
- 驗證：每個後端 commit 先 cscheck（0 個新診斷），再看 CI：40068025275（OAuth）、40068413684（Portfolio）、40068952998（save_to_dashboard）、40069345138（Notion／Linear）、40069571595（NeedsAuth）、40070057395（Studio 提示）、**40070962719（本輪最後一次含程式碼的 push：確認卡 summary）綠**。前端 lint、test（114 檔 1262 個）、build 全過。
- 問題：無阻礙。Notion／Linear 的實際 OAuth 流程無法在 session 內測（連不到外網）。

## 2026-10-24 11:42 – 第 5 輪（確認流程、connector 後端、自己的 MCP server、Studio AI）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠。
- **同請求確認（human-in-the-loop）**：`ChatApprovalRegistry`、`approval_required`／`approval_resolved`、`: ping`、`POST conversations/{id}/approvals/{pendingId}`；拒絕／逾時回報模型「沒有執行」，按停止整輪結束（D-026）。後端 `ToolCardGuard` 守卡片上限。CI 40059904526 綠。
- **Connector 後端**：migration `AddConnectorInstallations`（新表，手寫 Designer＋snapshot，CI 的 `ModelMatchesMigrationsTests` 過）、`IAiTool` 搬到 Connectors（D-027）、`ToolPermissions`、`SiteToolSet`、`ConnectorCatalogService`＋`AiConnectorsController`（catalog、tool-permissions、enabled、delete）；chat 每輪照使用者設定（停用不送、需確認才問）。CI 40060529705 綠。
- **自己的 MCP server**：`McpToolClient`（官方 SDK、Streamable HTTP、一律 SafeHttp，D-029）、`ConnectorInstallService`（probe、新增：免認證／API key／Bearer，憑證加密；test）、`ConnectorToolSource`（工具名 `m1__…`）接進 Data Analysis 助理。用 HttpMessageHandler 扮 MCP server 跑真 SDK 的測試在 CI 通過（40061092086）。之後加 taint（讀過外部結果後寫入一律先問，D-031）、目錄的內建 MCP（tw-stock、DeepWiki，D-033）。
- **Studio AI**：後端 `studio` profile（頁面清單、目前頁面、`open_page`）、English 的 `look_up_word`／`add_word_to_repository`（寫入 → 確認卡，`ISiteTool`）；前端 `components/StudioAssistant`（右下角按鈕＋⌘K／Ctrl+K、右側抽屜／手機全螢幕、`AiAssistantPanel variant="sheet"`）。Connectors 回到首頁 portal。
- playwright（`dist/`＋假 API＋模擬 SSE）截圖：Studio 回答與連結卡（桌機、手機）、確認卡、/connectors 有自訂 MCP 時，存到 `screenshots/`。
- `CONTRIBUTING-CONNECTORS.md` 完成（三種貢獻方式、程式樣板、測試樣板、安全清單）；三份 CLAUDE.md 更新。
- 驗證：每個後端 commit 先跑 cscheck（只剩已知雜訊；MCP SDK 原始碼已加入 cscheck），再看 CI；前端 lint／test（1260+）／build 全過。CI：40059904526、40060529705、40061092086、40061855784、40062208900、40062408658 綠。
- 問題：`7d5a813` 的 CI 前端測試紅一次——/data-analysis 頁面測試在登入狀態下 launcher 預先載入面板 chunk，測試結束後 import 才完成（EnvironmentTeardownError）；照 /finance 測試 mock 面板修好（`5702005`）。

## 2026-10-24 09:41 – 第 4 輪（離線型別檢查、agent loop、對話 profile、Data Analysis 助理）

- 開工：master 沒有新 commit；CI 仍因帳單停擺（rerun 40045469489 同樣「recent account payments have failed」）。
- **`.loop/cscheck`（D-021）**：NuGet 與 CI 都不能用，改用 apt 的 dotnet-sdk-10.0 內附的 Roslyn 直接編譯 server／tests，Anthropic SDK、xunit assert、Moq、EF Core 用 GitHub 上的原始碼當參考編譯，與 master 比較只列新診斷（約 1 分鐘）。驗證過會抓到故意放的型別錯誤；上一輪未經 CI 的 `6a75d3a` 檢查結果 0 個新診斷。
- **agent loop（M2 核心，`d1c413e`）**：`IAiTool`／`AiToolDescriptor`／`ToolEffect`、`ChatProfileSetup.Tools`、`ChatTurnRequest` 加 client tools 與「工具結果是不可信資料」提示、`ModelTurn` 用 `BetaMessageContentAggregator` 多輪（最多 8 次請求）、`ClientToolRunner`（逐一執行、30 秒逾時、SSE `tool_result`、`<tool_output trust="untrusted">` 包裝且不能被提前關閉）。寫入型工具在確認流程完成前不執行（D-023）。Finance 沒有工具，路徑不變。
- **對話屬於 profile（`7e8948d`）**：migration `AddChatProfileAndCards`（只新增 `ChatConversations.Profile`、`ChatMessages.CardsJson`，手寫 Designer＝新 snapshot），`ChatRequest.Profile`（未知 → 收費前 400）、列表 `?profile=`、不同 profile 的對話不續聊不 rewind；前端列表帶 profile。D-022：connector 表另一個 migration。
- **page context ＋ Data Analysis 助理**：`ChatProfileContext`（UserId、台北日期、Page）、`ChatRequest.Context`（最多 8 個短項目）、`DataAnalysisChatProfile`（key `data-analysis`）與 `AskDataTool`（包既有 問 AI 管線，照 NlSummarize 收費，回 chart 卡片，plan 帶頁面的資料源）。前端 `AiChatConfig.pageContext`、`/data-analysis` 最上方掛上 `AiAssistantLauncher`，歡迎建議改成 ask_data 答得出來的問題（D-024）。
- **`describe_data` 工具**（欄位、型別、日期範圍，不收費）、**卡片存進 `CardsJson`**（重新打開對話時畫出來，前端 `Message.cards` 經 `parseToolCard` 過濾）、AI chat 底部免責說明改由 app 設定（Data Analysis 不再顯示「不構成投資建議」）。
- playwright（`dist/` + 假 API + 模擬 SSE）截圖確認 /data-analysis 的對話、圖表卡片、手機版面，存到 `docs/ai-native/screenshots/`。
- server CLAUDE.md 的「AI assistant (chat)」與「Data analysis」補上 agent loop、profile、page context、ask_data、describe_data、卡片儲存。
- 驗證：每個後端 commit cscheck 0 個新診斷（只剩已知雜訊）；前端 lint、test（1249）、build 全過。CI 無法驗證（帳單），所有後端測試都還沒實際跑過（NEXT 第 1 節列了清單與最可能出錯的地方）。
- **CI 在 10:14 左右恢復**（不知道誰處理的，FOR_JACKY 0 已劃掉）：`7e8948d`（含 agent loop、migration、profile）CI 綠；`f643aae`／`0a26878` 的 run 40055177229 只有一個測試紅——`SessionUploadLimitsTests` 單獨註冊 DataAnalysis 服務做 DI 驗證，少了 `IUsageLimitService`（`c595fe4` 補 mock）。cscheck 抓不到這類執行期 DI 問題。
- 收尾合併 master（`5dc1160` README 精簡），merge `9c450c4`：**CI run 40055896595 綠**（server build＋全部測試、client）——本輪與上一輪所有後端變更都已由 CI 驗證。
- 問題：停擺期間無法驗證是主要風險，已解除。

## 2026-10-24 07:41 – 第 3 輪（/connectors v2 前端）

- 開工：master 沒有新 commit；branch CI 仍因帳單停擺（rerun 40033649446 一樣「recent account payments have failed」，D-016），所以本輪只做本機可完整驗證的前端。
- **/connectors v2**（設計 05 §3）：先補 `Connectors/index.test.tsx` 鎖住現有頁面行為（帳號卡片、連線導向、中斷、OAuth 回呼訊息），再演進：
  - `AiConnectorsSection`：「AI 可以使用的」（`InstalledSection`，健康狀態 chip、工具權限計數）、「目錄」（`CatalogSection`，分類 chip、一鍵連線／填 key／前往授權）、「新增 MCP server」。目錄載入失敗只顯示一行說明，下方帳號連線照常（後端還沒做時、或合併後端點壞掉時都不會拖垮頁面）。
  - `ConnectorDetailDrawer`：網址、登入方式、scope、最後測試、測試連線／重新授權／停用／移除（二次確認），工具表用 `ToolPermissionToggle`；本站工具（alwaysOn）只能調權限。
  - `AddMcpServerDialog`：網址 → 伺服器偵測（probe）→ 認證（none／bearer／API key／OAuth，含手填 client）→ 列出工具與權限可直接調。目錄中要 key 的項目直接從認證步驟開始。
  - API 契約：`apis/connectorsApis` 新增 8 個 endpoint 與型別，定稿寫進設計 03 §7（新增 `installations/probe`、`tool-permissions`；`installationId: null` 代表沒有列的內建 connector）。
  - `ToolEffect`／`ToolPermission` 改由 API 型別提供（`toolPermission.ts` 轉出、`useChatStreaming` 的 `ToolEvent.effect` 用同一型別）。
- 頁面標題由「Connected Services」改為「Connectors — 讓 AI 使用你的工具與服務」（設計 05 §3，app 名稱同步），既有卡片區加上小標題。Connectors 暫不放回 portal（D-020）。
- 用 playwright 截圖（`dist/` + 假 API，桌機 1280、手機 390）檢查：手機上詳情的工具表被擠成直排，改成每個工具一列。
- **工具結果卡片（generative UI，D-019）**：`components/aiChat/cards`（`parseToolCard` 過濾不可信內容、`TableCardView`、`LinkCardView`、`ToolCards`），`AiChatConfig.cardRenderers` 讓 app 註冊自己的 kind；`ToolStep.card` 由 `tool_result.card` 而來。截圖確認 Finance 對話裡的表格與連結卡片。
- **Data Analysis 助理的設定**（`DataAnalysis/aiChatConfig.ts`，未掛上頁面）與 `chart` 卡片（`assistant/ChartCardView`，用頁面的 `renderViz`，有 plan 就能「加到 dashboard」）。
- **chat 每一輪送 `profile`**（`ChatStreamingOptions.profile`）：後端還沒有這個欄位時 System.Text.Json 預設略過，不影響 Finance；`index.test.tsx` 斷言 chat body 的測試加上 `profile: 'finance'`（API 變更，非退化）。
- Connectors 頁的重新整理（與 OAuth 成功回來）同時重讀 AI connector 目錄（`refreshSignal`）。
- 決策 D-018～D-020。
- 驗證：每次 push 前本機 lint、test（最後 111 檔 1244 個）、build 全過。CI 無法驗證（帳單），本輪沒有後端變更。
- 問題：中途 auto mode classifier 連續 6 次沒有回應（Bash 暫時不能用），改用 Write/Edit 先寫文件，之後恢復。有一個 commit（`feat(client): AI chat 的每一輪送出 profile`）push 時帶了一個 lint 警告（useCallback 依賴），下一個 commit 立刻修好。

## 2026-10-24 05:42 – 第 2 輪（M1-5、M1-7、M2 前端零件）

- 合併 master（#569 Data Analysis 頁拆檔，純前端）：本機 lint／test（1130）／build 全過。
- **M1-5 共用 chat 引擎 profile 抽象（後端）**：新 module Assistant（`Services.Chatbot` + `Services.Assistant`）、`IChatProfile`／`ChatProfileSetup`／`IChatProfiles`、`ChatTurnRequest.Build`、`FinanceChatProfile` 與 `MarketBackground` 搬到 `Services.Finance.Assistant`、controller 傳 `"finance"`。Finance 的 request 測試只改指向（`TestHelpers/FinanceChatRequest`），斷言不變；新增 `ChatProfileTests` 5 個。server CLAUDE.md 模組表與「AI assistant (chat)」更新。**尚未經 CI 驗證**（見問題）。
- **M1-7 前端 config 化**：`AiChatConfig` + context，Finance 文案搬到 `Finance/aiChatConfig.ts`；refused 文案 app 可覆寫。既有測試斷言不改，只在 render 外包 config。
- **M2 前端零件**：`ToolActivity`（回答上方顯示用了哪些工具，Finance 現有的 tw-stock 工具立刻受惠）、`ApprovalCard` + `decide()`（human-in-the-loop 確認卡，接設計 04 §1.3 的 SSE 事件與端點）、`ToolPermissionToggle`（自動／需確認／停用）。
- 驗證：前端每次 push 前本機 lint、test（最後 102 檔 1161 個）、build 全過。
- **問題（阻礙）**：GitHub Actions 從本輪起不執行任何 job——annotation 是「The job was not started because recent account payments have failed or your spending limit needs to be increased」（run 40029336575，rerun 一次同樣結果；之後 40030813265 等也是）。後端無法 build／測試。已寫進 FOR_JACKY 第 0 條，決策 D-016。

## 2026-10-24 03:42 – 第 1 輪（續：提早開工 M1）

- 設計定稿（D-012），開始 M1：
  - `FinanceChatRequestTests`：逐欄位鎖住 Finance request（system prompt 原文、model、max_tokens、effort、MCP server、工具、beta、body 欄位集合）。CI run 40016078956 綠。
  - NuGet spike：`ModelContextProtocol.Core` 1.3.0 在 CI restore/build 成功（同一 run）。
  - `splitSseLines`：修好前端 SSE 跨 chunk 事件遺失的 bug（8 個測試）。
  - `ClientToolRoundTripTests`：用真的 SDK 證明 client 工具往返（`BetaMessageContentAggregator` + JSON 轉 param，thinking signature 保留）。查 SDK 原始碼發現它有現成的累積器與 `Anthropic.Mcp` 轉換範例，設計 04 的最大風險大幅降低。
  - 新 module Connectors + `TwStockMcp`（tw-stock 設定從 AnthropicChatService 搬出）。
  - 前端 `Finance/components/aiAssistant` → `components/aiChat`（純搬家）。
  - 合併 master（#567、#568）。CI run 40018092469 綠。
  - M2 先做的獨立小塊：`SafeHttp` + `OutboundAddressPolicy`（SSRF，含 DNS rebinding 防護與 40+ 個測試案例）、`McpServerUrl`（https、canonical URI）、`ToolNames`（前綴、64 字上限）。最後一次含程式碼的 push：CI run 40019395272 綠。
  - branch CI 的前端測試改用 github-actions reporter（失敗測試變 annotation）。
- Jacky 中途留言兩則：KB connector 建議（記為 D-013，M4 延伸）、要求定期同步 master（D-014，用 merge）。
- 問題：第一次 push spike 測試時少了 `AnthropicModel` using 別名，CI 編譯失敗，下一個 commit 修好。run 40017652281 前端 Test 步驟失敗一次但看不到是哪個測試（當時還沒有 reporter），下一個 run 同樣的程式碼就綠了，推測是 flaky；之後若再發生，annotation 會指出測試名稱。

## 2026-10-24 03:42 – 第 1 輪（階段 0）

- 建立 `docs/ai-native/`（PLAN、NEXT、PROGRESS、DECISIONS、FOR_JACKY、SHOWCASE、design/）與 `.loop/` 鎖。
- 讀懂現有架構（Finance chat、Connected Services、Data Analysis、usage limit、架構測試），寫進 `design/01-research.md`。
- 趨勢研究：MCP 2026-07-28 stateless 規格、MCP 授權（PRM、CIMD、DCR deprecated、PKCE、RFC 8707、RFC 9207、step-up）、MCP Apps（SEP-1865 Final）、Anthropic MCP connector（工具在 Anthropic 端執行、無確認機制）。
- 設計草稿 02–07；請一個 subagent 做對抗式評審，修正：模組環、Finance 測試無法「一行不改」、SSE 必須送 done、跨 chunk bug 變成必修、對話 profile 外洩、filtered unique index、agent loop 的 block 累積風險、改成同請求內等待確認、`ask_data` 包既有 NL 管線、單一 migration。決策記在 D-001～D-011。
- 驗證：本輪只有文件，未改程式碼；branch CI 最新一次（run 40009250817）為綠。
- 問題：無。
