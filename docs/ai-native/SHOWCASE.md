# Studio AI — 成果發表（11/5）

> **初稿（2026-10-24 第 6 輪，第 8 輪補上本站 MCP server）**。功能還在擴充，階段 2（11/4–11/5）定稿並補齊截圖與統計。故事線的來源是 [design/02](design/02-vision-and-showcase.md)。

**一句話：一個在每一頁都叫得出來的 AI 助理，會用本站自己的功能，也能接上你在外面的任何 remote MCP server（含 OAuth 登入），而且會改變東西的動作一定先問你。反過來，網站本身也是一個 MCP server：你的 Claude Desktop、Cursor 也能用本站的功能。**

## 功能與亮點

| 你看到的 | 背後 | 為什麼跟上 2026 的趨勢 |
|---|---|---|
| **Studio AI**：任何頁面右下角按鈕或 ⌘K／Ctrl+K 叫出，知道你在哪一頁 | 共用 chat 引擎的 `studio` profile；本站功能都是工具（`ISiteTool`） | 「網站本身就是工具」：同一個 AI 到處都在 |
| **⌘K 也是指令列**：在 Studio 打「複習」「portfolio」、自己 dashboard 的名稱或一個英文單字，輸入框上方立刻出現「前往『複習』」「查字典：review」，按下直接到（不問 AI、不花用量）；打完整的問題就交給 AI | 前端的 `quickActionsFor`（頁面清單由測試對齊伺服器的 `SitePages`），只認頁面名稱與單一英文單字 | Raycast／Linear 式的指令列與 AI 助理合而為一：簡單的事零延遲，複雜的事交給 agent |
| **打「/」就有現成指令**：Studio 輸入框打 `/` → 今日簡報、考我單字、用我的單字寫短文、投資組合健檢…一排按鈕，打 `/簡報`、`/quiz` 就篩出來，按下直接送出；/finance（`/外資`、`/景氣`）與 /data-analysis（`/欄位`、`/dashboard`）自己的助理也有各自的一組 | 共用的 `matchSlashCommands`＋`QuickChips`，每個助理的清單在它的 `AiChatConfig.slashCommands`；Studio 的名稱跟本站 MCP server 的 prompts 一樣，名稱跟本站 MCP server 的 prompts 一樣，所以 Claude Code 裡的 `/mcp__jacky-studio__quiz_me` 與本站的 `/quiz` 是同一件事 | 斜線指令是 2026 AI 產品的共通語言（Claude、ChatGPT、Cursor） |
| **用說的**：三個助理的輸入框都有麥克風，說「給我今天的簡報」字就出現在框裡 | 瀏覽器內建的 Web Speech API（zh-TW），不經本站伺服器、不花 AI 額度；Firefox 沒有就不顯示 | 語音是 2026 AI 助理的基本互動 |
| **聽它說、跟它對話**：用麥克風問的問題，**回答一結束就自動念給你聽**（打字問的不念）；首頁今日簡報旁的 **「用聽的」**，早上不用看螢幕就聽完行程、複習與資產；每個回答下方也有「朗讀」，英文段落用英文聲音、中文用中文聲音 | 瀏覽器的 speechSynthesis，`speechText` 把 Markdown 變成字並依語言切段；`SendOptions.readAloud`：只在回答正常結束才念、每則只念一次、念的時候「停止朗讀」一直顯示（D-120）；不經伺服器、不花用量 | 語音進、語音出：ChatGPT／Gemini 的語音對話，在網頁上用瀏覽器內建能力做到 |
| **看得見 AI 在想什麼**：回答開始前，助理上方出現「💭 思考中… 3 秒」與它正在想的最後一句；回答出來後收成「已思考 8 秒」，按下展開整段推理（先查什麼、為什麼一次叫三個工具、結論怎麼來）；三個助理都有 | Haiku 5.5 本來就會思考，只是預設不回傳；請求改成 `thinking.display: summarized`（計費相同），`ModelTurn` 把思考摘要串流成 SSE `thinking`，前端 `ThinkingTraceView` 摺疊顯示。純文字、不存、不算回答（D-136） | 2026 的 AI 產品都把推理攤開給人看（Claude 的 thinking、ChatGPT 的 reasoning summary）：等待變成看得懂的過程，也看得出 AI 為什麼這樣回答 |
| **AI 今日簡報**：首頁一鍵，Studio AI 同時讀行程、學習進度、投資組合，整理成三段重點與「先做哪件事」 | `askStudio`（任何頁面帶一句話打開 Studio）；模型在一次回覆裡呼叫三個工具，卡片與文字一起出現 | Agent 的價值是「跨 app 組合」：一個問題用到三個資料源 |
| **「帶我去複習」→ 頁面真的切過去**，對話留在旁邊說明到了之後怎麼做；每個回答下方有 2–3 個 **「接著問」** 一鍵續問 | `open_page` 的 `go` → `tool_result.navigate`（只限本站工具、站內路徑、沒讀過外部內容）；接著問依回答用到的工具決定（不花 token） | AI 不只回答，還會操作網站本身 |
| **「考我今天該複習的單字」→ 回答裡直接出現可以作答的測驗卡**：一次一題、按下立刻顯示對錯與解說，答完自動記進 SM-2 複習排程（+XP） | `review_quiz`（每日挑戰的題型產生器，不花 AI）→ `quiz` 卡片 → 前端呼叫既有的 `review/submit`；模型被告知考哪些字但不准說出答案 | Generative UI 不只顯示結果，還能「互動」：對話裡長出一個小 app |
| **「找出我最近英文最弱的地方」→ AI 教練**：讀你最近 30 天的測驗紀錄，表格列出最常答錯、而且最近還是錯的字，再點出最弱的一種測驗（例如聽寫只對 45%），最後一句「現在考你 3 題？」接到只考那幾個字的測驗卡 | `weak_spots`（`IStatsService.GetWeakSpotsAsync`：自己的 `ExamAttempts`，各類型答對數、最常錯的 10 個字；唯讀、不花用量）→ 表格卡片；`review_quiz` 的 `words` 只考那幾個字；學習統計與測驗頁的建議、「/」的「找出我的弱點」、本站 MCP prompt `my_weak_spots` | 資料早就在，缺的是「讀懂並告訴你下一步」——AI 原生化不是加新資料，而是讓既有資料會說話 |
| 單字庫頁「用我單字庫裡最近存的字寫一段短文」→ AI 讀你最近存的 5 個字，寫一段把它們**粗體標出**的英文短文＋中文翻譯，按「朗讀」聽它念 | `saved_words`（最近存的字與一個中文意思、總字數；不花 AI 的讀取）＋模型的寫作 | 個人化的 AI 學習：教材是你自己的字 |
| **選一段字就能問**：在任何頁面選取一句例句、一段分析 → 選取處上方出現「解釋／翻成中文／問 AI」；選一個英文單字是「查字典／加入單字庫」——在 Studio 自己寫的短文裡選一個不認識的字，一鍵加入單字庫。/finance 與 /data-analysis 也一樣，問的是那一頁自己的助理（選「多頭排列」→ 解釋）；作答中的考題不給（不會一選就洩漏答案） | `aiChat/selection`（`SelectionBar`、`selectionStepOf`，Studio 與各 app 的助理共用；選取停 250ms 才出現；`noSelectionAsk` 標出作答中的題目）；頁面上的文字可能是別人寫的，所以請求帶 `quotesPageText`，伺服器把對話標成讀過外部文字，寫入一律先問 | AI 不在旁邊的框裡等你，而是出現在你正在讀的地方（Notion、Arc、Apple Intelligence 式的 inline AI） |
| **Studio AI 記得你**：說「記住：我在準備多益，目標 800 分，短文用商務情境」→ 確認卡寫出整句筆記 → 之後每個新對話的短文、簡報、出題都照這個方向（資料分析的助理也讀得到，只讀）；「你記得我什麼？」、/connectors 上逐則刪除 | `AssistantMemories`（每則 200 字、每人 30 則）；`remember`（寫入，先問）／`forget`（照抄筆記文字、Destructive）；筆記放在 system prompt 的 `<user_memories>`，標明「不是指令」、`<>` 換成全形，防持久化的 prompt injection | ChatGPT、Claude、Gemini 2025–2026 的「記憶」：個人化的 AI，而且使用者看得到、刪得掉 |
| 「查 serendipity，加入我的單字庫」→ 單字卡 → **確認卡** → 加入 | `look_up_word`（讀）、`add_word_to_repository`（寫，先確認） | Agent 會動手，所以 human-in-the-loop |
| 「我的投資組合現在怎麼樣？」→ **持倉表格卡片** + 重點分析 | `portfolio_summary`，照 /portfolio 的估值 | 結果是 UI，不是一坨文字（generative UI） |
| 「我今天加碼 0050 兩張，均價 185，幫我記下來」（或**貼一張券商成交回報的截圖**）→ **確認卡**「『0050』加 2,000（成交價 185）（存成今天的快照）」→ 允許 → 投資組合更新 | `update_holding`（寫入，先確認）：最新快照改那一筆、存成今天的快照，成本照平均成本法（買進加上付出的錢、賣出保留平均成本）；買進沒說價格就先問、對到兩筆就不猜 | 跨 app 的「用說的記帳」：AI 不只讀你的資料，也幫你維護它 |
| 「我今天有什麼行程？」→ **Google 日曆／Outlook 行事曆表格卡片** + 空檔建議 | 原生 connector `google_calendar_events`、`outlook_calendar_events`：用既有 Google／Microsoft 帳號連線（唯讀），兩家共用同一套資料型別與卡片，行程標題視為外部文字；今日簡報兩個都讀、只講有連線的 | 不只 MCP：自家程式呼叫 API 也是同一套工具抽象 |
| 「我手上有哪些 Jira 票？」（或打 **`/jira`**）→ **Jira 表格卡片** + 先處理哪張的建議，每張票的 key 直接連到 Jira | 原生 connector `jira_issues`：用既有 Atlassian 帳號連線（唯讀、不用新 scope），先找到你的 Jira 網站再用 JQL 搜尋；問特定專案或狀態時 AI 自己寫 JQL，寫錯會照 Jira 的說明自己修正 | 既有的帳號連線不用重連，就變成 AI 的工具；Claude Desktop 透過本站 MCP server 也查得到 |
| 「在 WEB-120 留言：今天先量 LCP…」→ **確認卡寫出票號與整則留言** → 允許 → 連到那則留言的卡片 | `jira_add_comment`：第一個會寫入外部服務的原生 connector（既有連線的 `write:jira-work`）；留言超過 2000 字拒絕、不截斷，確認的就是送出的；讀過 Jira 的對話一定先問 | AI 不只讀你的工作系統，也能替你動手——但每一次都經你確認 |
| /data-analysis 最上方的 AI 對話：問問題 → **互動圖表**（卡片上直接換成長條、圓餅…，不再問 AI）→ 「存到業績追蹤 dashboard」→ 確認 → 完成 | `ask_data`（沿用 問 AI 的檢查過的查詢管線）、`describe_data`、`save_to_dashboard` | AI 只用經過驗證的查詢，不自己寫 SQL |
| **一句話做出整個 dashboard**：「幫我做一個業績總覽 dashboard（3 到 4 張圖），存完打開」→ AI 自己挑趨勢、組成、排名三四張互補的圖 → 一張確認卡列出全部，按 **「全部允許（3）」** → 頁面切到剛做好的 dashboard | 只靠 prompt：同一個回覆一起呼叫多個 `ask_data`，再一起呼叫多個 `save_to_dashboard`（依序執行，第一張建立、之後的找到它）；確認卡的「全部允許」只在全部是本站工具、沒有無法復原的動作、沒讀過外部內容時出現 | Agent 做的是「一件事」而不是「一個步驟」：規劃、執行多步、一次讓你確認 |
| **AI 之間交棒**：在任何頁面跟 Studio AI 說「幫我做一個業績總覽 dashboard」→ 它說「交給資料分析的 AI 助理」→ 頁面自己切到 /data-analysis，那裡的助理**自動展開並接手**，畫圖、出確認卡 | `open_page` 的 `go`＋`ask` → `tool_result.handoff`（只跟著站內導覽送、讀過外部內容的對話不送）→ react-router 的 location state（外部連結設不了）→ 目的頁的 launcher 以新對話送一次後清掉 | 多 agent 協作（handoff）：全站助理是入口與調度者，專門的助理帶專門的工具做事；使用者不必知道該去哪問、也不用重打一次 |
| dashboard 上一鍵 **「AI 解讀」**：Studio AI 用最新資料重算每張圖，說出最重要的變化與異常（手機上 dashboard 一列一張圖） | `read_dashboard`（只讀自己的 dashboard，別人分享的不讀）；dashboard 頁的路徑讓「這個 dashboard」就是正在看的那個；同一個工具也開放給本站 MCP server，Claude.ai 裡也能問「我的業績追蹤 dashboard 怎麼樣？」 | 資料不只畫成圖，AI 還會讀圖、說重點 |
| **Connectors**：目錄一鍵加入 tw-stock、DeepWiki；貼任何 MCP URL 自動偵測登入方式 | MCP 官方 SDK（Streamable HTTP）、SafeHttp 防 SSRF | MCP 是 AI 接工具的標準 |
| **不知道網址也能加：直接搜 MCP 官方目錄**——目錄最後一張「找更多 MCP server」→ 打 `github`、`search` → 列出可以直接連線的 remote MCP server（網址、要 token 還是 API key、原始碼連結）→ 按「使用」就走一樣的偵測、登入、工具流程，API key 的 header 已經填好 | `McpRegistry`：伺服器把 registry.modelcontextprotocol.io（v0.1）整份讀一次、全站共用快取 1 小時，只留 Streamable HTTP、固定 https 網址的；內容當不可信資料（純文字、只留 https 連結），只是指標（D-122、D-123） | 官方 MCP Registry 是各家客戶端共用的「server 黃頁」：別人做好的 server 搜得到、按一下就接上 |
| **跟 Studio AI 說「幫我找能查天氣的 MCP server」**——它在官方目錄找到幾個，卡片列出主機、要不要 API key；按「加入」就到 /connectors，新增對話框已經搜好那一個，自己確認工具與權限後連線 | `find_mcp_server`（Studio 專用、唯讀、`OutsideText`）＋ `mcp-servers` 卡片；`/connectors?registry=` 打開對話框並填好搜尋，Studio 抽屜自己收起（D-124、D-125） | AI 知道自己缺什麼工具、幫你找，但**裝不裝由你**：human-in-the-loop 的「擴充自己」 |
| **接上就試：「在 Studio AI 試試看」**——新增 MCP server 的最後一步、或任何 connector 的詳情裡一顆按鈕，Studio AI 開新對話，挑一個只會讀取的工具**實際用一次**，再說還能請它做哪些事 | `tryConnectorPrompt`＋`askStudio(…, { fresh: true })`：新對話重新讀工具清單，剛接上的 connector 就在裡面；名稱可能是發布者寫的，只當一行引號裡的字（D-126） | 從「找到工具」到「AI 用上它」一氣呵成：擴充 AI 的能力不再是設定頁，而是一段對話 |
| 需要登入的 MCP server：**自動跑完 MCP 授權規格** | 401 → PRM（RFC 9728）→ AS metadata（RFC 8414／OIDC）→ DCR／CIMD／手填 client → PKCE S256 + `resource` + `iss` → token 加密 → 到期自動 refresh → 403 `insufficient_scope` 時 step-up | 2025-11 起的 MCP 授權規格（PRM、CIMD、RFC 8707 resource、RFC 9207 iss） |
| 每個工具「自動／需確認／停用」；確認卡可勾「以後直接執行」；讀過外部資料（含行事曆邀請）後的寫入一律先問；工具說明被改了就改回需確認 | `ToolPermissions`、taint（D-031、`OutsideText`）、工具結果包成不可信資料、`CachedTools.Changed`（rug pull） | prompt injection 與 tool poisoning 防護 |
| **網站本身就是 MCP server**：在 /connectors 產生 token，一鍵複製 Claude Code／Claude Desktop／Cursor／VS Code 的設定，別家的 AI 就能查字典、看你的學習進度與投資組合 | `POST /api/mcp`（Streamable HTTP、JSON 回應、無 session）、只存雜湊的 PAT、預設唯讀、跟 /connectors 的權限連動；**Claude.ai／ChatGPT 只要貼網址**：本站同時是 OAuth 授權伺服器（PRM、AS metadata、DCR、PKCE、同意頁） | MCP 是雙向的：網站不只是 MCP client，也是 MCP server |
| **在 Claude.ai／ChatGPT 裡也看得到本站的卡片**：問「我的投資組合」，回答下方直接出現本站的持倉表格（深淺色跟著 host） | MCP Apps（`ui://` 資源、`structuredContent`、postMessage 的 `ui/initialize`／`tool-result`／`size-changed`），一個內嵌的 HTML 畫所有卡片 | MCP Apps（2026 的 generative UI 標準）：工具不只回文字，也回 UI |
| **別家 AI 裡的斜線指令**：Claude Code 打 `/mcp__jacky-studio__daily_briefing` 就是今日簡報；還有「考我單字」（一次一題）、「解讀 dashboard」、「投資組合健檢」「找出我的弱點」等 7 個現成指令（VS Code、Claude Desktop 的選單也有） | MCP prompts（`prompts/list`／`prompts/get`）：每個 `SitePrompt` 宣告在它用到的工具旁邊，token 用不到的工具就不列出它；參數整理成一行、最多 100 字 | MCP 的三種能力（tools、resources、prompts）本站對外全部提供 |
| **失敗了按一下「重試」**：AI 服務忙碌、網路斷掉、回覆沒收完時，失敗的回答下方出現「重試」，問題原地重送（附檔、引用的頁面文字都照原樣），不用重打；對話太長、模型拒答這種再問也一樣的不給，已經動過手（跑過本站或 connector 工具）的也不給，免得同一筆寫入做兩次；長對話串流時只重畫正在寫的那則（每段 329 ms → 3.4 ms） | `useChatConversation.retry`：伺服器存過的問題當成編輯它自己、沒存過的重新送出；`isRetryableChatError`；`memo(MessageItem)`（D-138～D-140） | 2026 的 chat 產品的基本功：失敗可以一鍵重來、長對話不卡 |
| **AI 替對話取名字**：問完第一個問題，左邊紀錄列表那一列從「台積電最近一個月的走勢如何？技術面和籌…」變成「台積電近一個月走勢」，之後一眼就找得到 | 第一個回答之後前端呼叫 `POST conversations/{id}/title`，`ConversationTitler` 用 Haiku 5.5（effort low）讀問題與回答開頭取 ≤24 字標題；每個對話一次、不另外計費（D-141、D-142） | Claude、ChatGPT 的對話列表都是 AI 取的名字；三個助理共用，不用設定。對話多了，紀錄欄上方可以**搜尋標題與內容**（D-143、D-191）；想用自己的話也可以按鉛筆**改名**（D-144） |
| **把對話帶走**：標題列「匯出對話」→ 複製成 Markdown 或下載 .md：問題、回答、用了哪些工具、表格與連結卡片都在，貼進筆記或信件就能用 | `conversationMarkdown`（純前端、不送伺服器、不計費）：表格卡片轉 Markdown 表格（數字照畫面的千分位、`\|` 跳脫）、本站連結補成完整網址、Data Analysis 的圖表寫成資料表（`AiChatConfig.cardTables`）、測驗這種 app 自己畫的卡片寫一行說明（D-147、D-149） | ChatGPT、Claude 都能分享或匯出對話；這裡是三個助理共用的一顆按鈕 |
| **表格直接進試算表**：每張表格卡片（持倉、外資買賣超、Jira、Data Analysis 的圖表資料）下方「複製表格」「下載 CSV」，貼進 Excel／Google 試算表就是一格一格；按欄名就排序（市值由大到小、損益 % 照數值），複製的就是排好的順序 | `cards/tableExport`（純前端、不計費）：複製是 TSV、CSV 帶 BOM（Excel 不亂碼）、數字不帶千分位；工具給的字以 `=`／`+`／`-`／`@` 開頭會加 `'`，貼上或打開都不會被當公式執行（CSV injection，跟伺服器的 Details 匯出同一條規則），貼上時開頭是 `"` 的格子也不會被拆開、`0050` 不會變 50；工具找到更多筆時會說只含畫面上的（D-150、D-151）；排序 `cards/tableSort`（D-152）；「複製成文件表格」同時放 HTML 與 TSV，HTML 只有表格標籤、每個值都跳脫（D-168） | ChatGPT 的表格也能下載；這裡多了公式注入防護，因為表格內容來自外部工具 |
| **表格一鍵變圖**：同一張表格卡片下方「畫成圖」→ 表格原地變成圖：持倉的市值是長條（照你排的順序，就是排名圖），切「圓餅圖」看配置比例，切「損益 %」看誰賺誰虧（負的往左）；每月營收這種日期欄自動畫成折線 | `cards/tableChart`（純前端、不問 AI、不計費）：自己判斷哪幾欄是數字、用哪欄當名字（代號接名稱「2330 台積電」）、哪些圖適合（折線只給日期、圓餅不給負數與百分比）；ECharts 在第一次按下時才載入、載入失敗退回表格；工具給的字只畫在 canvas，tooltip 一律跳脫（D-153） | 生成式 UI 的下一步：AI 給資料，畫面讓你自己換角度看——不必為了換一張圖再問一次 AI |
| /finance 的 Stock Intelligence：**行為不變**，工具 chip 與進度改用中文（「個股歷史股價」「技術指標」），回答下方有「接著問」 | 同一套共用 chat（`FinanceChatRequestTests` 逐欄位鎖住請求）；中文名稱是前端的對照表（`FINANCE_TOOL_LABELS`） | — |

## Demo 腳本（約 10 分鐘）

前置：production（或本機）已有 `Settings:AnthropicApiKey`；用一般帳號登入；Portfolio 有一份快照；/data-analysis 的預設資料源可用（見 FOR_JACKY 2）。

1. **開場：AI 今日簡報（1 分鐘）**：首頁最上方「AI 今日簡報」→ 右側滑出 Studio AI，一次呼叫 Google 日曆（與 Outlook 行事曆）、學習進度、投資組合（回答上方三個工具 chip）→ 行程表格、「12 個單字待複習」卡片、三段重點與「建議先做的事」。之後任何頁面按 ⌘K 都叫得出它，建議問題依目前頁面變化；按輸入框的麥克風可以用說的。
   ![首頁的 AI 今日簡報](screenshots/home-briefing-card-desktop.png)
   ![Studio 的今日簡報](screenshots/studio-briefing-desktop.png)
   **看它想**：送出後、回答出來前，回答上方是「💭 思考中… 1 秒」與它正在想的最後一句（「三個工具彼此無關，可以一次一起呼叫」）；回答出來後收成「已思考 N 秒」，按下展開整段思考摘要（D-136）。/finance、/data-analysis 的助理也一樣。
   ![思考中](screenshots/thinking-live-phone.png)
   ![展開思考摘要](screenshots/thinking-open-desktop.png)
   ![語音輸入](screenshots/studio-voice-desktop.png)
   **用聽的**：今日簡報卡片右邊的耳機「用聽的」→ 同一份簡報，回答一結束就自動念出來（下方按鈕變成「停止朗讀」）；用麥克風說完送出的問題也一樣自動念（D-120）。
   ![今日簡報的用聽的](screenshots/home-briefing-listen-desktop.png)
   ![用說的問、用聽的答](screenshots/studio-voice-reply-phone.png)
   ![Studio 打開](screenshots/studio-open-desktop.png)
   **指令列**：在 Studio 輸入框打「review」→ 上方出現「前往『複習』」與「查字典：review」，按一下就到，完全不經 AI；打一整句問題就照常送給 AI。
   ![⌘K 指令列](screenshots/studio-quick-desktop.png)
   打 dashboard 名稱的一部分（「業績」）→「打開『每月業績追蹤』」，按下直接到那個 dashboard。
   ![指令列打開 dashboard](screenshots/studio-quick-dashboard-desktop.png)
   打一個 **「/」** → 輸入框上方出現「現成指令」：今日簡報、考我單字、用我的單字寫短文、投資組合健檢…，按一下就送出（同一組指令在 Claude Code 裡是 `/mcp__jacky-studio__…`，見第 8 步）。
   ![Studio 的斜線指令](screenshots/studio-slash-desktop.png)
   在自己的 dashboard 上打「/」，第一個就是 **「解讀這個 dashboard」**；在字典的單字頁是 **「解釋 serendipity」**——指令跟著頁面走。
   ![dashboard 頁的斜線指令](screenshots/studio-slash-dashboard-desktop.png)
2. **AI 操作網站（30 秒）**：在首頁 ⌘K →「帶我去複習」→ 背後的頁面直接切到「複習」，Studio 說明怎麼翻牌（手機上回答完抽屜自動收起，再按按鈕對話還在）。
   ![帶我去複習](screenshots/studio-go-desktop.png)
3. **本站即工具（1 分鐘）**：輸入「serendipity 是什麼？幫我加入單字庫」→ 回答上方顯示用了「查字典」→ 單字連結卡 → 出現**確認卡**「加入單字庫：serendipity」→ 按「允許」→「已加入」與「前往單字庫」。確認卡下方寫著「3 分鐘內沒有回覆，這次就不執行」（最後一分鐘倒數秒數）；切到別的分頁時，分頁標題變成「（等你確認）Jacky's Studio」，這時重新整理頁面，瀏覽器會先問要不要離開（D-105～D-107）。
   ![確認卡](screenshots/studio-approval-desktop.png)
   接著到 /english → ⌘K → 第一個建議「考我今天該複習的單字」→ 回答裡出現**測驗卡**，直接按選項：對錯與解說立刻出現，三題答完顯示「答對 2 / 3 題」並記進複習排程（+XP），下方「再考我 3 題」。
   ![Studio 的複習小測驗](screenshots/studio-quiz-desktop.png)
   ![作答後立刻顯示答案與解說](screenshots/studio-quiz-answered-desktop.png)
   ![答完：答對幾題、記進複習排程與 XP](screenshots/studio-quiz-result-phone.png)
   到 /english/stats → ⌘K → 第一個建議「找出我最近英文最弱的地方」→ 「最近的弱點」表格（最常答錯的字、最近一次對錯）＋兩句重點（最弱的測驗類型、還是錯的字）→ 下方「考我最常錯的這幾個字」→ 測驗卡考的就是表格裡那幾個字（`review_quiz` 的 `words`；D-116、D-117）。
   ![找出我的弱點](screenshots/studio-weak-spots-desktop.png)
   統計頁最上方就有「**AI 弱點分析**」卡片（跟首頁的今日簡報同一個元件 `AskStudioCard`），不用先知道 ⌘K，按一下就是同一段分析（D-118）。
   ![學習統計頁的 AI 弱點分析卡片](screenshots/stats-weak-card-phone.png)
   到 /english/repository → ⌘K → 第一個建議「用我單字庫裡最近存的字寫一段短文」→ 「我的單字庫」卡片（共 86 個字）＋一段用到 5 個字的英文短文（粗體）與中文翻譯 → 按回答下方的 **朗讀**（喇叭圖示），英文用英文聲音、翻譯用中文聲音念；下方「換個主題再寫一段」「考我幾題」。
   ![用自己的單字寫短文](screenshots/studio-writing-desktop.png)
   **選取就能問**：在短文裡把不認識的「ephemeral」選起來 → 上方出現「查字典／加入單字庫／問 AI」→ 按「加入單字庫」→ 同一個對話裡出現確認卡。再到字典頁，選一句例句 → 「解釋／翻成中文／問 AI」→ 按「翻成中文」→ Studio 開新對話，翻譯＋值得學的字詞表。到 /finance 問助理台積電的走勢，在回答裡選「多頭排列」→ 工具列換成 /finance 助理的「解釋／翻成英文／問 AI」→ 按「解釋」，接在同一段對話（截圖 `select-finance-*`）。
   ![在 Studio 的短文裡選一個字](screenshots/select-word-studio-desktop.png)
   ![字典頁選一句例句](screenshots/select-sentence-desktop.png)
   ![翻成中文與值得學的字詞](screenshots/select-translate-desktop.png)
   ![朗讀](screenshots/studio-read-aloud-phone.png)
   **它記得你**：在單字庫頁 ⌘K 說「記住：我在準備多益，目標 800 分，短文用商務情境」→ **確認卡**「記住：『在準備多益，目標 800 分；短文用商務情境』」→ 允許 →「Studio AI 記得的事」卡片。開一個新對話再按「用我單字庫裡最近存的字寫一段短文」→ 短文是商務情境、提到多益（模型讀到的是 system prompt 裡的筆記）。問「你記得我什麼？」會照清單回答；記住「我負責北區業績」後，到 /data-analysis 問「最近業績怎麼樣」，資料分析的助理會先看北區並說明（它只讀筆記、不能改）；沒叫它記也行：「我下個月要考多益，用我最近存的字寫一段短文」→ 短文寫完，最後一張確認卡「記住：『下個月要考多益』」（一個對話最多提議一次，按拒絕就不再提）；到 /connectors 的「Studio AI 記得的事」看得到每一則，可以刪。
   ![記住之前先問](screenshots/studio-memory-desktop.png)
   ![記住了](screenshots/studio-memory-done-desktop.png)
   ![Studio AI 主動提議記住](screenshots/studio-memory-offer-desktop.png)
   ![/connectors 的 Studio AI 記得的事](screenshots/connectors-memory-desktop.png)
4. **我的資產（1 分鐘）**：到 /portfolio → ⌘K →「幫我看看我的持股，哪幾檔虧最多？」→ 持倉表格卡片 + 重點。
   ![Studio 讀投資組合](screenshots/studio-portfolio-desktop.png)
   回答下方的「接著問」：按「哪一檔佔比最高？風險集中嗎？」直接續問。
   接著說「我今天加碼 0050 兩張，均價 185，幫我記下來」→ 確認卡用一句話寫出要做的事 → 允許 →「已更新 元大台灣50（0050）：5,000 → 7,000，成本 900,000 → 1,270,000 TWD，存成今天的快照」，抽屜後面的投資組合頁**同時重新整理**（不用按重新整理），下方「我的投資組合現在怎麼樣？」一鍵看更新後的表格。
   ![用說的記一筆加碼](screenshots/studio-trade-desktop.png)
   ![允許之後：已更新與接著問](screenshots/studio-trade-done-desktop.png)
   **或直接貼券商的成交回報截圖**（按輸入框的 +）：「幫我把這兩筆記進投資組合」→ Studio AI 先列出讀到的每一筆（代號、買賣、數量、均價）→ 兩筆更新一起出現在確認卡，逐筆允許或拒絕。
   ![貼成交回報截圖記帳](screenshots/studio-trade-image-desktop.png)
   接著問「我今天有什麼行程？」（需先在 /connectors 的帳號連線連上 Google 或 Microsoft）→ 行事曆表格卡片與空檔（工作行程在 Outlook 的人一樣看得到，D-134）。
   ![Studio 讀 Google 日曆](screenshots/studio-calendar-desktop.png)
   打 **`/jira`** →「我的工作待辦」→ Studio 用 Atlassian 帳號連線讀 Jira，表格列出手上還沒完成的票，再建議先處理哪張（每個 key 連到 Jira）；接著問「只看優先度高的」，AI 自己寫 JQL 再查一次（D-130）。
   ![Studio 讀 Jira](screenshots/studio-jira-desktop.png)
   接著按「在最新那張留言：我今天會處理」（或自己說要在哪張票留什麼）→ 確認卡寫出票號與整則留言 → 按「允許」→ 連到 Jira 上那則留言（D-132）。
   ![在 Jira 留言的確認卡](screenshots/studio-jira-comment-approval-desktop.png)
   **持股的市場面交給 Stock Intelligence**：回到 /portfolio → ⌘K → 建議「我的持股最近外資在買還是賣？」→ Studio AI 讀完持股（表格卡片）說「我把這三檔交給 Stock Intelligence 的 AI 助理了」→ 頁面切到 /finance，那裡的助理收到「我持有 2330 台積電、0050、2454 聯發科，這幾檔最近外資在買還是賣？」並用 tw-stock 工具回答——兩個 AI 各用自己的工具接力，使用者一個字都沒重打。
   ![Studio 讀持股後交棒](screenshots/studio-holdings-handoff-desktop.png)
   ![Stock Intelligence 接手回答](screenshots/studio-holdings-finance-desktop.png)
5. **資料 → 圖表 → dashboard（2 分鐘）**：/data-analysis → 最上方對話框「畫出各地區每月營收，存到『業績追蹤』dashboard，存完打開」→ 圖表卡片 → 確認卡用一句話說明「把『各地區每月營收』存到 dashboard『業績追蹤』，存好後打開它」（chartId 這類原始參數收在「顯示送出的參數」）→ 允許 → 回答結束後頁面自己切到那個 dashboard，看到同一張圖（之後會用最新資料重畫）。沒說「打開」就只留連結卡。
   ![Data Analysis 對話](screenshots/data-analysis-chat-desktop.png)
   ![存到 dashboard 的確認卡](screenshots/data-analysis-save-desktop.png)
   圖表卡片上的圖示可以直接換成直條圖、圓餅圖…（不再問 AI）；下方的「接著問」一鍵續問。
   ![換圖與接著問](screenshots/data-analysis-followups-desktop.png)
   **一句話做出整個 dashboard**：開新對話，按歡迎畫面的第四個建議「幫我做一個業績總覽 dashboard（3 到 4 張圖），存完打開」→ AI 先看欄位，再一口氣畫出三四張互補的圖（趨勢、各地區占比、前 5 名產品）→ 一張確認卡列出全部存檔，按「全部允許（3）」→ 回答結束後頁面切到剛做好的「業績總覽」。
   ![一句話做出整個 dashboard 的確認卡](screenshots/data-whole-dashboard-desktop.png)
   **或從任何頁面交棒**：在首頁 ⌘K 跟 Studio AI 說「幫我做一個業績總覽 dashboard，存完打開」（/dashboards 列表頁的 Studio 建議裡就有這一句）→ 它回「交給資料分析的 AI 助理了」→ 回答結束後抽屜收起、頁面切到 /data-analysis，那裡的助理自己展開、送出同一個請求（訊息上方標著「由 Studio AI 轉來」），接著就是上面的三張圖與「全部允許」。
   ![Studio AI 交棒](screenshots/studio-handoff-desktop.png)
   ![由 Studio AI 轉來](screenshots/studio-handoff-from-desktop.png)
   ![資料分析的助理接手](screenshots/studio-handoff-data-desktop.png)
   到了 dashboard 頁，按標題旁的 **「AI 解讀」** → Studio AI 開新對話，讀這個 dashboard 的每張圖（用最新資料重算）→「北區是成長引擎、南區在下滑」這類重點，下方的接著問「哪張圖的變化最大？」。
   ![dashboard 的 AI 解讀按鈕](screenshots/dashboard-ai-button-desktop.png)
   ![Studio AI 解讀 dashboard](screenshots/studio-dashboard-desktop.png)
6. **接上外面的世界（2 分鐘）**：/connectors → 目錄裡的 Notion 或 Linear 按「前往授權」→ 用自己的帳號登入 → 回到 /connectors 顯示已連線與工具 → Studio 問「我在 Linear 上還有哪些未完成的 issue？」。或一鍵加入 DeepWiki → 回到 Studio 問「twjackysu/JackyAIApp 這個 repo 的架構是什麼？」→ 工具進度顯示「DeepWiki・…」→ 回答。再示範「新增 MCP server」：貼一個需要 OAuth 的 MCP URL → 「找到了！這個 server 用 OAuth 登入（xxx）」→ 前往授權 → 回來後工具列表出現。
   ![Connectors：目錄裡的 Notion、Linear 一鍵授權](screenshots/connectors-oauth-desktop.png)
   ![自己的 MCP server：偵測到 OAuth](screenshots/add-mcp-oauth-desktop.png)

   **不知道網址？** 目錄最後一張「找更多 MCP server」→「搜尋官方目錄」→ 打 `exa` → Exa Search 標著「需要 API key（x-api-key）」→ 按「使用」→ 直接到登入這一步，名稱與 header 都填好，貼上 key 就連線。已在本站目錄的（DeepWiki）標「上方目錄已有」，不會加兩次。
   ![從 MCP 官方目錄搜尋](screenshots/connectors-registry-desktop.png)
   ![選好之後：名稱與 header 已填好](screenshots/connectors-registry-picked-desktop.png)

   **或直接問 Studio AI**：在 /connectors 按 ⌘K → 第一個建議「幫我找一個能查天氣的 MCP server」→ 卡片列出 Weather、Open-Meteo（主機、需要 API key）與一兩句推薦 → 按 Weather 的「加入」→ 抽屜收起、新增對話框已搜好 `io.example/weather` → 按「使用」繼續。
   ![Studio AI 找 MCP server](screenshots/studio-find-mcp-desktop.png)
   ![按「加入」：對話框已搜好](screenshots/studio-find-mcp-added-desktop.png)
   **接上就試**：連線成功的最後一步（或之後在 DeepWiki 的詳情裡）按 **「在 Studio AI 試試看」** → 對話框收起、Studio AI 開新對話：「示範一下你能用『DeepWiki』做什麼…」→ 工具 chip「DeepWiki・讀 wiki 目錄」→ 一份 repo 文件目錄，加上三個可以接著問的例子（D-126）。
   ![connector 詳情的在 Studio AI 試試看](screenshots/connector-try-drawer-desktop.png)
   ![Studio AI 示範剛接上的 connector](screenshots/studio-try-connector-desktop.png)
7. **安全（1 分鐘）**：在 connector 詳情把某個工具設成「停用」→ AI 說它沒有這個能力；「需確認」的工具一律出確認卡；按「停止」整輪什麼都不執行。
8. **反過來：網站本身就是 MCP server（1 分鐘）**：/connectors 往下捲到「讓其他 AI 使用本站」→「產生 token」（名稱 Claude Code、唯讀）→ 複製「Claude Code」那一行指令貼到終端機 → 在 Claude Code 問「用 jacky-studio 查一下 serendipity，再看我今天要複習幾個字」→ 它呼叫本站的 `look_up_word`、`learning_progress`。再打 `/mcp__jacky-studio__daily_briefing`（產生 token 的對話框會寫出這行）→ Claude Code 一次呼叫行程、學習進度、投資組合三個工具，整理成三段簡報；`/mcp__jacky-studio__quiz_me` 則一次問一題。回到 /connectors 看到 token「最後使用」更新；按「撤銷」後 Claude Code 立刻連不上。
   **或在 Claude.ai**：設定 → Connectors → 新增自訂 connector，只貼 `https://apps.jacky-studio.com/api/mcp` → 跳到本站的同意頁「Claude 想用你的身分使用 Jacky Studio 的工具，完成後會回到 claude.ai」→ 允許 → 回到 Claude.ai，工具出現。
   接著在 Claude.ai 問「我的投資組合現在怎麼樣？」→ 回答下方出現 **Jacky Studio 的持倉表格卡片**（MCP App），問「serendipity 是什麼」→ 單字卡，按「看完整解釋」開到本站字典頁；token 允許寫入時卡片上還有「加入單字庫」，在 Claude 裡按一下就加進本站的單字庫（MCP App 經 host 呼叫 `tools/call`）。
   ![其他 AI 的 OAuth 同意頁](screenshots/site-mcp-oauth-consent-desktop.png)
   ![MCP App：在別家 AI 裡畫出本站卡片（以 playwright 扮 host 的截圖）](screenshots/mcp-app-card-light.png)
   ![MCP App 的「加入單字庫」按鈕](screenshots/mcp-app-action-light.png)
   ![讓其他 AI 使用本站](screenshots/site-mcp-section-desktop.png)
   ![產生 token 與設定](screenshots/site-mcp-token-desktop.png)
9. **Stock Intelligence（30 秒）**：/finance 問「分析台積電最近的走勢」，跟以前一樣；回答上方的工具 chip 現在是中文（「個股歷史股價」「技術指標」），下方有「接著問」。在輸入框打 **「/」** → 「今日重大消息、外資在買什麼、台指期籌碼…」一排現成指令（/data-analysis 的助理也有自己的一組：有哪些欄位、最值得注意的三件事、做一個業績總覽 dashboard）。
   ![Stock Intelligence](screenshots/finance-answer-desktop.png)
   ![/finance 的斜線指令](screenshots/assistant-slash-finance-desktop.png)
   **失敗了就重試**：AI 服務忙碌時，回答寫「錯誤: AI 服務目前忙碌中」，下方一顆「重試」，按下問題原地重送、不用重打。
   ![失敗的回答下方的重試](screenshots/retry-failed-desktop.png)
   ![重試後的回答](screenshots/retry-done-phone.png)
   **AI 替對話取名字**：問完第一個問題，看左邊的紀錄列表：一兩秒後，那一列從問題開頭換成 AI 取的短標題（「台積電近一個月走勢」）。
   ![取名前：問題開頭](screenshots/title-before-desktop.png)
   ![取名後：AI 取的短標題](screenshots/title-after-desktop.png)
   對話多了（6 則以上），紀錄欄上方有 **「搜尋對話」**：打「台積電」只留下標題有台積電的對話。
   ![搜尋對話標題](screenshots/history-search-desktop.png)
   記不得標題，只記得「AI 說過毛利率那次」？打 **「毛利率」**：標題先即時比對，停下來一下後連**對話內容**也搜，標題沒有的對話帶著那段話出現（找到的字標成綠色）；按下去直接**捲到那則訊息並亮一下**。
   ![搜尋對話內容](screenshots/history-content-search-desktop.png)
   ![打開後捲到找到的那則](screenshots/history-content-search-reveal-desktop.png)
   想用自己的話？滑到那一列按 **鉛筆**，標題變成輸入框，打「月報用：台積電走勢」按 Enter 就改好了（Escape 不改）；改過名的 AI 不會再改回去。
   ![改名中](screenshots/rename-editing-desktop.png)
   ![改好的標題](screenshots/rename-done-desktop.png)
   **把對話帶走**：標題列的 **分享圖示（匯出對話）** →「複製成 Markdown」或「下載 .md 檔」。問題、回答、AI 用了哪些工具，連同持倉表格（Markdown 表格、數字跟畫面一樣有千分位）和連結卡片都在裡面，直接貼進 Notion、信件或 Obsidian；/data-analysis 助理畫的圖表會寫成它的資料表。
   ![匯出對話的選單](screenshots/export-menu-desktop.png)
   ![複製好了](screenshots/export-copied-phone.png)
   只要表格？按欄名 **「損益 %」** 就照數值排序（再按一次反過來、第三次回到原本的順序）；每張表格卡片下方有 **「複製表格」**（貼進 Excel／Google 試算表就是一格一格，順序跟畫面一樣）和 **「下載 CSV」**（Excel 直接打開不亂碼）；要寫報告就按 **「複製成文件表格」**，貼進 Word 或 Gmail 是一張有表頭、數字靠右的表格；圖表卡片也有，拿到的是它的資料。
   ![表格卡片下的複製與下載](screenshots/table-export-desktop.png)
   想看圖？按表格下方的 **「畫成圖」**（長條圖示）→ 表格原地變成市值長條；上方切 **「圓餅圖」** 看配置、切 **「損益 %」** 看賺賠（負的往左畫）；再按一次回到表格。完全不問 AI（D-153）。
   ![畫成圖：市值長條](screenshots/table-chart-bar-desktop.png)
   ![圓餅圖（手機）](screenshots/table-chart-pie-phone.png)
   ![損益 %：負的往左](screenshots/table-chart-pct-desktop.png)
10. **收尾**：下面的架構圖，以及「新增一個 connector 要寫多少 code」（[CONTRIBUTING-CONNECTORS.md](CONTRIBUTING-CONNECTORS.md)）。

## 架構

```mermaid
flowchart LR
  subgraph Browser["瀏覽器（共用 components/aiChat）"]
    Studio["Studio AI（⌘K）"]
    DA["/data-analysis 助理"]
    Fin["/finance 助理"]
    Conn["/connectors"]
  end
  subgraph Server["ASP.NET Core"]
    Chat["Chat 引擎（Services.Chatbot）<br/>agent loop・SSE・確認・usage 計費"]
    Profiles["IChatProfile<br/>studio / data-analysis / finance"]
    Site["本站工具 ISiteTool<br/>字典・單字庫・Portfolio・Google 日曆・Outlook・Jira・open_page"]
    DATools["ask_data・describe_data・save_to_dashboard"]
    CTS["ConnectorToolSource<br/>使用者的 MCP connector"]
    OAuth["MCP OAuth<br/>PRM・AS metadata・CIMD/DCR・PKCE・refresh"]
    Safe["SafeHttp（只連公開位址）"]
    SiteMcp["本站 MCP server<br/>POST /api/mcp・PAT"]
  end
  Others["Claude Desktop・Claude Code・Cursor・VS Code"]
  Anthropic["Anthropic Messages API<br/>（tw-stock 走 MCP connector）"]
  MCP["使用者的 remote MCP servers"]
  AS["Authorization servers"]

  Studio & DA & Fin -->|SSE| Chat
  Conn --> CTS
  Chat --> Profiles
  Profiles --> Site & DATools & CTS
  Chat <--> Anthropic
  CTS --> Safe --> MCP
  OAuth --> Safe --> AS
  Others -->|Bearer jsk_…| SiteMcp --> Site
```

- **設計模式**：一個 chat 引擎 + 每個 app 一個 profile（system prompt、工具、page context）；工具一律是 `IAiTool`（描述 + `RunAsync`），本站功能、Data Analysis、外部 MCP 都一樣；工具結果是不可信資料，卡片是宣告式 JSON 由前端 renderer 畫。
- 新增 connector：見 [CONTRIBUTING-CONNECTORS.md](CONTRIBUTING-CONNECTORS.md)（remote MCP 只要一行目錄設定；本站功能實作一個 `ISiteTool`）。

## 合回 master 的檢查清單

- [ ] **不需要新的 secret**：Studio AI、Data Analysis 助理共用既有的 `Settings:AnthropicApiKey`；MCP token 用既有的 `TokenEncryption:Key` 加密。
- [ ] **MCP OAuth 的網址**（不用註冊 OAuth app，是給外部授權伺服器看的）：redirect URI `https://apps.jacky-studio.com/api/connectors/callback/mcp`、client metadata `https://apps.jacky-studio.com/api/connectors/oauth/client-metadata`，都由既有的 `Connectors__CallbackBaseUrl` 推出來，不用新設定。
- [ ] **EF migration**（production 啟動時執行，只新增、不動既有資料）：`20261024100000_AddChatProfileAndCards`（`ChatConversations.Profile` 預設 finance、`ChatMessages.CardsJson`）、`20261024120000_AddConnectorInstallations`（新表）、`20261024180000_AddSiteAccessTokens`（新表）、`20261025200000_AddAssistantMemories`（新表，Studio AI 的記憶）。
- [ ] 刪除 `.github/workflows/ai-native-ci.yml`、`.loop/`。
- [ ] 合併後會自動部署到 production（Build and deploy）。
- [ ] **本站 OAuth 不用新設定**：issuer 是既有 `Connectors__CallbackBaseUrl` 的 origin；client 註冊用 ASP.NET Core data protection（App Service 會保存金鑰；若重啟後 Claude.ai 說 client 無效，重新加一次 connector 即可）。
- [ ] **Cloudflare**：`/api/mcp`、`/api/oauth/*`、`/.well-known/*` 會被非瀏覽器的 client（Claude Code、mcp-remote）呼叫；若開了 Bot Fight Mode／Super Bot Fight Mode，請替 `apps.jacky-studio.com/api/mcp` 加例外（FOR_JACKY 9）。
- [ ] **既有帳號連線的行為變更**（D-155）：Google／Microsoft／Atlassian 的 OAuth callback 現在要求回來的瀏覽器登入的是開始授權的那個帳號（防 login CSRF）。正常在 /connectors 按「連線」不受影響；合併後請實際連一次 Google 確認。
- [ ] **既有功能多了上限**（D-158）：AI 助理一則訊息最多 2 萬字（輸入框會說明、長文改附加文字檔）；附件上傳每人每分鐘 20 次；`/pdf` 解鎖每個位址每分鐘 10 次；Word／Excel 附件解開後單一 XML 超過 1600 萬字就讀不出；/data-analysis 上傳的資料列讀進記憶體超過約 128 MB 就拒絕。正常使用碰不到，若有人回報「訊息太長」或 429 再調。
- [ ] **再兩個上限**（D-159）：考試的錄音轉文字最多 4 MB（錄音 2 分鐘自動停並照常轉文字，麥克風提示「最長 2 分鐘」，轉文字失敗時輸入框下方會說）；/data-analysis 的匿名上傳每個人（登入者或位址）最多佔 128 MB（app setting `DataAnalysis__SessionUpload__MaxBytesPerCaller`）。
- [ ] **Data Analysis 的上限**（D-163、D-164，master 既有功能）：自訂 SQL Server 資料源只能連網路上的主機（內網 IP、`localhost`、沒有點的機器名、named pipe、解析到內網的名稱都拒絕）；新增／修改資料源每人每分鐘 20 次；「任何人」可看的 dashboard 的 widget 資料，未登入每個位址每分鐘 120 次、登入者 600 次。合併後若你有存資料源，確認它還能讀。
- [ ] 用量：AI 對話照既有每日預算計費（每則訊息 `UsageCosts.ForChatMessage`、`ask_data` 照 問 AI）。

## 本機試跑

1. 後端：`JackyAIApp.Server` 的 `appsettings.Development.json` 填 `Settings:AnthropicApiKey`、`TokenEncryption:Key`（64 bytes base64），`dotnet run`。
2. 前端：`jackyaiapp.client` 的 `npm run dev`，開 https://localhost:5173，登入。
3. 本機的 client metadata URL 是 localhost，外部授權伺服器讀不到，所以 OAuth 會改走 DCR 或手填 client（`McpOAuthUrls`）。

## 已知限制與後續

- 確認流程在同一個請求裡等待：重新整理或關閉頁面會失去待確認的問題、什麼都不執行（D-026）。有確認卡在等時，瀏覽器會先問「確定要離開？」；要重新接回得把一輪對話從請求拆開（resumable stream），沒有在這次做（D-105）。
- 語音輸入與朗讀用瀏覽器自己的語音：聲音好壞依裝置與瀏覽器而定（Chrome／Edge／Safari 有，Firefox 沒有語音輸入），開發時只能用替身驗證（D-058、D-079）。
- 選取文字的工具列只用桌機滑鼠與 playwright 驗證過；手機上是放在選取的下方（瀏覽器自己的複製選單在上方），實機的樣子合併後請用手機看一次（D-086）。
- MCP client 只支援 Streamable HTTP（不支援舊 SSE transport，D-029）；每次請求一個 session。
- 本站 MCP server 的 OAuth 沒有 refresh token（90 天後重新授權），授權碼存在記憶體（重啟時正在登入的人要重來）；只用假的 client 測過，Claude.ai／ChatGPT 的實際流程要合併後實測（FOR_JACKY 9）。Claude Desktop 的設定檔方式要經 `mcp-remote`（需要 Node.js）。
- 讀過外部內容（外部 connector、Google 日曆）的對話，之後每一輪的寫入都會先問（D-055），連「以後直接執行」的工具也一樣——今日簡報讀了日曆，所以同一個對話接著「加入單字庫」會出確認卡；開新對話就不會。
- /finance 的助理本身仍不會讀投資組合（D-073：Finance 改走 agent loop 並同時用 hosted MCP 與本站工具的組合，沒辦法在開發環境對真的 API 驗證）。「我的持股最近外資在買還是賣？」改由交棒做到（D-097）：Studio AI 讀持股、把代號交給 /finance 的助理；在 /finance 裡直接問自己的持股，它仍不知道你持有什麼。
- 本站的 MCP App 只用 playwright 自己扮的 host 驗證過（照規格的 `ui/initialize`、`tool-result`、`size-changed`、`open-link`）；Claude.ai／ChatGPT 實際怎麼畫要合併後實測（FOR_JACKY 9）。站內 Studio AI 的卡片仍是宣告式 JSON（D-019），沒有改用 iframe。
- 無障礙只以程式審查與 jsdom 測試驗證（D-181～D-185），沒有用實際的螢幕閱讀器（NVDA／VoiceOver）走過；對話紀錄的列仍是按鈕裡包按鈕（改名、刪除），選取工具列沒有鍵盤入口（NEXT 0.5）。

## 開發日誌與統計

（階段 2 填入最終數字）

| 日期 | 輪 | 重點 |
|---|---|---|
| 10/24 | 1 | 研究、設計定稿、M1 起步（golden test、SSE 修正、SafeHttp） |
| 10/24 | 2 | 共用 chat profile、前端 config 化、ToolActivity／ApprovalCard |
| 10/24 | 3 | /connectors v2 前端、工具結果卡片 |
| 10/24 | 4 | agent loop、Data Analysis 助理、離線型別檢查 |
| 10/24 | 5 | 同請求確認、connector 後端、自己的 MCP server、Studio AI |
| 10/24 | 6 | MCP OAuth、Portfolio 工具、用說的存 dashboard、本頁初稿 |
| 10/24 | 7 | TestKit、Google 日曆、step-up、以後直接執行、rug pull 防護 |
| 10/24 | 8 | 本站對外的 MCP server（PAT、/connectors 的設定一鍵複製、OAuth 授權伺服器與同意頁） |
| 10/24 | 9 | 本站 MCP server 成為 MCP App（別家 AI 裡畫出本站卡片）、OAuth 端點 rate limit、「經 OAuth 登入」標示、Studio 引導 |
| 10/24 | 10 | taint 跨訊息保存、首頁「AI 今日簡報」（askStudio）、確認卡放在文字之後、標題列顯示工具名稱與「等你確認」、手機表格卡片、語音輸入、MCP App 卡片上的「加入單字庫」按鈕 |
| 10/25 | 11 | 今日簡報開新對話、回答下方的「接著問」、圖表卡片直接換圖、「帶我去」（AI 直接切換頁面）、審查修正 |
| 10/25 | 12 | Data Analysis「存完打開」（存好 dashboard 後頁面直接切過去）、dashboard 的「AI 解讀」（`read_dashboard`）、確認卡只說一句話（原始參數收起來）、手機 dashboard 一列一張圖、截圖巡檢 |
| 10/25 | 13 | 巡檢 /finance 與 /connectors 目錄、抽屜：/finance 的工具 chip 改中文、自訂 MCP server 的抽屜不再重複網址；⌘K 指令列（打頁面名稱或英文單字直接去，不經 AI） |
| 10/25 | 14 | ⌘K 指令列打開自己的 dashboard、字典頁的 Studio 建議跟著畫面上的字、手機上新增 MCP server 的工具權限不再擠在一行；**複習小測驗**（`review_quiz`＋可作答的 quiz 卡片，答完記進複習排程） |
| 10/25 | 15 | Studio AI 讀你的單字庫（`saved_words`），用你自己的字寫短文；回答的「朗讀」（英中分聲音）；今日簡報有到期的字時問要不要考幾題；審查修正 |
| 10/25 | 16 | **用說的（或貼截圖）記一筆交易**（`update_holding`：確認後更新投資組合、成本照平均成本法、幣別不同只收總額）；巡檢 /english/exam、/english/repository 手機版 Studio |
| 10/25 | 17 | **選取文字問 Studio AI**（解釋、翻譯、問 AI；一個英文單字查字典或加入單字庫，Studio 短文裡的字也能選）；頁面文字當外部文字（`quotesPageText`） |
| 10/25 | 18 | 選取工具列擴到 /finance 與 /data-analysis 的助理（共用 `aiChat/selection`）；作答中的考題、每日挑戰、閃卡、測驗卡不給選取工具列 |
| 10/25 | 19 | **一句話做出整個 dashboard**（多張圖一次查、一張確認卡存完）；確認卡的「全部允許／全部拒絕」（只限本站工具、未讀外部內容）；修好 Jacky 改名後的紅 CI；第 18 輪審查剩下的兩個低風險 |
| 10/25 | 20 | **AI 之間交棒**（Studio AI 的 `open_page` 帶 `ask`，把做 dashboard、查市場的事交給 /data-analysis、/finance 自己的助理接手）；「我的持股最近外資在買還是賣？」（Studio 讀持股、交給 Stock Intelligence）；/dashboards 的 Studio 建議做新 dashboard；巡檢 /data-analysis 下方「問 AI」結果的選取；審查修正 |
| 10/25 | 21 | 交棒訊息標「由 Studio AI 轉來」；**Studio AI 的記憶**（`remember`／`forget`、`<user_memories>`、/connectors 的「Studio AI 記得的事」、`AlwaysAsks`） |
| 10/25 | 22 | 記憶收尾：今日簡報用筆記、資料分析的助理只讀筆記、**Studio 主動提議記住**（確認卡把關）；/connectors 記憶區塊手機版 |
| 10/25 | 23 | 確認卡打磨：剩餘時間倒數、分頁標題「（等你確認）」、重新整理前先問、時間到寫「沒有在時限內回覆」；評估重新接上確認卡（resumable stream）後不做 |
| 10/26 | 24 | **本站 MCP server 的現成指令**（MCP prompts：Claude Code 打 `/mcp__jacky-studio__daily_briefing` 就是今日簡報，共 6 個，token 用得到的工具才列出）；Studio 輸入框打 **「/」** 出現同一組現成指令；產生 token 的對話框寫出各 AI 怎麼叫出；審查修正 |
| 10/26 | 25 | 「/」指令跟著頁面走：單字頁「解釋 {word}」、/portfolio「記一筆交易」、/dashboards「做一個新的 dashboard」、自己的 dashboard「解讀這個 dashboard」排在最前面 |
| 10/26 | 26 | /finance 與 /data-analysis 自己的助理也有「/」現成指令（`AiChatConfig.slashCommands`；比對與 chip 搬進共用的 `aiChat`）；CI 的 .NET SDK 改回 `10.0.x` |
| 10/26 | 27 | **找出我的弱點**（`weak_spots`：測驗答對率與最常錯的字，AI 點出最該加強的地方，再用只考那幾個字的測驗卡〔`review_quiz` 的 `words`〕；學習統計頁的「AI 弱點分析」卡片；「/」與本站 MCP prompt `my_weak_spots`） |
| 10/26 | 28 | **語音對話**：用麥克風問的問題，回答結束自動朗讀；首頁今日簡報的「用聽的」（D-120） |
| 10/26 | 29 | **/connectors 搜尋 MCP 官方目錄**：不知道網址也能加 remote MCP server（D-122）；審查後改成整份讀一次、本機比對名稱與說明（D-123） |
| 10/26 | 30 | **Studio AI 幫你找 MCP server**（`find_mcp_server`：卡片按「加入」到 /connectors，對話框已搜好；安裝仍由你按；D-124、D-125） |
| 10/26 | 31 | **接上就試**（「在 Studio AI 試試看」：新增 MCP server 的最後一步與 connector 詳情，Studio 開新對話用一個讀取工具示範；D-126） |
| 10/26 | 32 | **Jira 原生 connector**（`jira_issues`：既有 Atlassian 連線變成 AI 工具，JQL 搜尋、表格卡片、Studio「/jira」；D-130）；改用打字後不再自動朗讀、Studio 第二次「加入」換新搜尋（D-129） |
| 10/26 | 33 | **在 Jira 留言**（`jira_add_comment`：第一個寫入外部服務的原生 connector，確認卡寫出整則留言；D-132）；審查修正：確認卡的說明不再截到 200 字、讀與寫用同一個 Jira 網站（D-133）；**Outlook 行事曆原生 connector**（`outlook_calendar_events`，今日簡報兩個行事曆都讀、不再叫人去連另一個；D-134、D-135） |
| 10/26 | 34 | **看得見 AI 在想什麼**：三個助理都串流思考摘要（「思考中… 3 秒」→「已思考 8 秒」，按下展開；D-136） |
| 10/27 | 35 | 長對話不卡：每則訊息各自 memo，串流時只重畫正在寫的那則（每段 329 ms → 3.4 ms；D-138）；**失敗的回答下方「重試」**（問題原地重送，附檔與引用的頁面文字照原樣；D-139）；審查修正：失敗的編輯重試仍是編輯、伺服器可能已存或工具跑過的不給（D-140） |
| 10/27 | 36 | **AI 替對話取名字**：新對話第一個回答之後，紀錄列表換成 AI 取的短標題（三個助理都有；D-141）；審查修正：失敗後重試成功的也命名、回答裡的標籤改全形、同時請求只問一次（D-142）；對話滿 6 則時紀錄欄可以搜尋標題（D-143） |
| 10/27 | 37 | 對話可以**手動改名**（紀錄列的鉛筆 → 輸入框，Enter 存、Escape 不改；伺服器只收一行、最多 40 字、不改排序；D-144） |
| 10/27 | 38 | 改名被拒會說、鍵盤改名後焦點回到鉛筆（D-146）；**匯出對話**（複製成 Markdown 或下載 .md，表格、連結卡片與 Data Analysis 的圖表資料一起；三個助理都有；D-147、D-149）；全站截圖巡檢（無退化） |
| 10/27 | 39 | **表格直接進試算表**：每張表格卡片與 Data Analysis 的圖表下方「複製表格」「下載 CSV」（三個助理都有，防公式注入；D-150、審查 D-151）；表格卡片按欄名排序（D-152） |
| 10/27 | 40 | **表格一鍵變圖**：表格卡片「畫成圖」（長條／折線／圓餅、一次一欄、照排序、日期自動折線；ECharts 延遲載入、失敗退回表格；D-153） |
| 10/27 | 41 | **合併前安全巡檢**：OAuth callback 只接受開始授權的那位使用者（防 login CSRF，MCP 與既有 Google／Microsoft／Atlassian 連線一併修）、自填 MCP server 每個回應最多 8 MB（D-155）；工具清單上限、probe 頻率限制（D-156）；AI 回答的圖片不載入（防資料外洩）、Sankey tooltip 的 stored XSS（D-157） |
| 10/27 | 42 | **合併前安全巡檢：上傳與附件**：Word／Excel 附件的解壓上限與欄位上限（一格就能吃光記憶體）、chat 訊息 2 萬字（失敗退款但已存，原本可灌爆 DB）、匿名 session 上傳邊讀邊量記憶體、附件上傳與 PDF 解鎖的頻率限制（D-158） |
| 10/27 | 43 | 巡檢未修的兩項：錄音轉文字 4 MB、錄音 2 分鐘自動停（Whisper 依分鐘計費、一次只收 1 單位）、匿名上傳每人最多佔一半預算（D-159）；錄音失敗不再無聲無息；**外站連結卡片寫出主機**（仿冒網域顯示成 punycode，D-160） |
| 10/27 | 44 | **第五次巡檢：dashboard、資料分析的 AI 工具、自訂資料源**：上傳檔的查詢結果算外部文字（檔案裡的字不能讓自動執行的寫入直接跑；資料源不變，「存完打開」照常，D-163）；公開 dashboard 的 widget 資料頻率限制；自訂 SQL Server 資料源只能連網路上的主機、連線前解析名稱（原本可當內網 port 掃描器，D-164）；新 AI 端點的計費全部確認無漏洞 |
| 10/27 | 45 | **第六次巡檢：Job Finder、Portfolio、English 的 AI 批改**（七項都是 master 既有）：存持倉的大小與快照份數上限（原本一個帳號幾次請求就能灌滿 DB）、找工作每個位址的頻率限制與 cache key 收斂、AI 批改送進 prompt 的長度上限（固定扣額度、按 token 計費；對話太長先忘掉最舊的回合）、寫作結果與 benchmark 的數量上限（D-165）；資料源連線字串的密碼整段遮住（D-166） |
| 10/27 | 46 | **表格貼進文件**：表格卡片多一顆「複製成文件表格」，貼到 Word／Gmail／Google 文件仍是有表頭的表格（HTML，全部跳脫；D-168）；巡檢留下的小洞收尾：對話練習、每日挑戰、複習、聽寫送出失敗會說原因與怎麼重送（D-170、D-171），持倉編輯器與 CSV 匯入逐列標出超過上限的列（D-167），Yahoo 代號一律 escape（D-169） |
| 10/28 | 47 | **第七次巡檢：登入、帳號連線、匿名端點**：匿名的文字轉語音加頻率限制、快取以位元組設上限（原本一個迴圈就能吃滿記憶體、用完語音額度，D-173）；帳號連線的 token 一次只換一個（D-174）；麥克風、對話開始、語音播放、登出失敗都會說原因（D-172） |
| 10/28 | 48 | **第八、九次巡檢：共用 chat 引擎的穩健度、新 AI 介面的無障礙**：「停下來改問這個」時新問題等被停的回答存好才開始（原本歷史順序會亂、之後每輪兩則連續 assistant，D-177）；工具輪數用完時那句「我來查一下」不再當成回答（D-178）；停在確認卡上的寫入 chip 不再打勾（D-179）；螢幕閱讀器聽得到回答開始／結束與「需要你的確認」（D-181）；收合助理、決定確認卡、作答測驗時焦點不掉（D-182～D-184） |
| 10/28 | 49 | **無障礙收尾與第十次巡檢：本站 OAuth**：對話紀錄的改名／刪除不再包在開啟按鈕裡、鍵盤刪除後焦點到補上來的那一列（D-186、D-187）；選取工具列用 Alt+A 進入，抽屜裡也行（D-188、D-189）；過期 token 不再擋住 OAuth 登入、同意頁不能被框、仿冒網域顯示成 punycode、同意的 POST 限頻（D-190） |
| 10/28 | 50 | **對話紀錄搜尋內容**：停止打字後伺服器搜訊息內容，標題沒有的對話帶片段出現、搜尋的字標色（D-191）；審查後標題已找到的不佔名額、只搜最近 300 個對話、emoji 不比對、片段當按鈕的描述（D-192）；打開後捲到找到的那則訊息並亮一下（D-193） |
| 10/28 | 51 | **審查收尾與第十一次巡檢：dashboard 分享、/admin/listening**：同一個搜尋結果再按會重新亮滿、換對話一定回到最後（D-194）；匿名看 Anyone dashboard 不再拿到 widget 的資料源、表名與篩選值，widget 有大小與數量上限、題庫分頁夾值（D-195）；沒登入的訪客以 IPv6 /64 計算頻率限制（D-196） |
| 10/28 | 52 | 之後才畫好的內容（延遲載入的卡片、回答最後的「接著問」）不再把對話推走：跟著最後的留在最後、搜尋打開的那則留在原位，讀者自己按出來的不被捲走（D-197、D-199）；**Studio 找 MCP server 時知道你已經加過哪些**，不再叫你重加（D-198） |

截至第 6 輪：feature/ai-native 比 master 多 60+ 個 commit（不含 merge）。
