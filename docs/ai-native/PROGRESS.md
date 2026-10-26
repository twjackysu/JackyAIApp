# PROGRESS（新的在最上面）

## 2026-10-26 13:42 – 第 30 輪（Studio AI 幫你找 MCP server）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。
- **Studio AI 的 `find_mcp_server`**（D-124）：Studio 專用、唯讀、`OutsideText`，搜 D-122 的全站 Registry 清單，回 `mcp-servers` 卡片（名稱、主機、需要 token／API key、目錄已有；最多 6 個）。卡片每列「加入」→ `/connectors?registry=<名稱>`：/connectors 打開新增對話框、目錄搜尋已填好（一次，網址隨即拿掉；自訂已滿就提示）。安裝仍是使用者自己按。/connectors 的 Studio 建議第一個「幫我找一個能查天氣的 MCP server」。三份 CLAUDE.md、design/03 §6.1、SHOWCASE（亮點、demo 第 6 步、日誌）。
- Studio「/」加「找 MCP server」（`find_mcp_server` key，`/mcp`、`/外掛` 叫得出來）。
- **審查**（subagent，D-125）：無高風險；中——`more` 少算、第一次讀 Registry 可能超過 30 秒工具上限（改成等 20 秒就回「正在載入」）、taint 讓交棒失效（prompt 收窄、要先交棒）、手機上「加入」被抽屜蓋住（`/connectors?registry=` 時抽屜自己收起）；低——自訂已滿提示、名稱完全相同排第一、本站說明放在發布者文字前。都已修。
- 截圖：`studio-find-mcp-*`、`studio-find-mcp-added-*`（新腳本 `.loop/shots/find-mcp.cjs`）。
- 驗證：cscheck 新診斷 0（第二次抓到少了 `using JackyAIApp.Server.Common;`，已補）；前端 lint、test（149 檔 1593 個）、build 全過，審查修正後 StudioAssistant＋Connectors 測試 25 檔 222 個、tsc、eslint 過；CI 40276686274（D-124）、40277894340（審查修正）綠；CI_RESULT_PLACEHOLDER
- 問題：無阻礙。

## 2026-10-26 11:42 – 第 29 輪（/connectors 搜尋 MCP 官方目錄）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。
- **/connectors 能搜尋 MCP 官方目錄**（D-122）：`IMcpRegistry`（`Services/Connectors/McpRegistry.cs`）＋ `GET api/connectors/registry?search=`；只列 active、Streamable HTTP、固定 https 網址的 remote server，內容當不可信資料（純文字、截斷、只留 https 連結），標出必填 header 與本站目錄已有的。前端：新增對話框的「不知道網址？從 MCP 官方目錄找」（`RegistrySearch`、`registryView`）、目錄最後一張「找更多 MCP server」；選一個就走原本的 probe → 認證 → 工具，名稱用 Registry 標題、API key 的 header 先填好。每日 live contract test `McpRegistryContractTests`。三份 CLAUDE.md、design/03 §6.1、FOR_JACKY 第 12 條、SHOWCASE（亮點、demo 第 6 步、日誌）。
- **審查**（subagent，D-123）：中——每個搜尋字串各打一次 Registry、各占快取（可被放大）、Registry 的 search 只比對名稱且一頁多半是 stdio 而誤報「找不到」、上一個 server 的 header 帶到下一個 → 改成整份依 cursor 讀完（最多 40 頁）、全站一份清單快取 1 小時、本機比對名稱／標題／說明；每次選擇從空白表單開始。低——載入中沿用舊結果（`currentData`）、`role="status"`。
- 截圖：`connectors-registry-*`（新腳本 `.loop/shots/registry.cjs`）。
- 驗證：cscheck 新診斷只有已知的 `[GeneratedRegex]` 雜訊；前端 lint、test（146 檔 1579 個）、build 全過，審查修正後 Connectors 測試 13 檔 102 個、tsc、eslint 過；CI 40266175355 紅（測試用 `Uri.ToString()` 比對跳脫）→ 40266576062 綠；40267339578 紅（測試的 `Page` 多載把單一參數當成 cursor）→ **40267670752（本輪最後一次含程式碼的 push b85b8033）綠**。
- 問題：兩次 CI 紅都在測試本身，見 NEXT 的 CI 狀態。

## 2026-10-26 09:42 – 第 28 輪（語音對話：用說的問、用聽的答）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。
- **語音對話**（D-120，只動前端）：輸入框裡有麥克風聽到的字，送出的那則回答**正常結束就自動朗讀**（`SendOptions.readAloud` → `Message.readAloud`；`useSpeechOutput` 的 `autoReadKey`，每則一次）；打字問的不念。三個助理（Studio、/finance、/data-analysis）共用。首頁今日簡報卡片多一個 **「用聽的」**（`AskStudioCard` 的 `listen`、`askStudio(…, { readAloud })`；手機只顯示耳機圖示）。念的時候回答下方的按鈕保持顯示（可按停止）。
- **審查**（subagent，D-121）：高——麥克風會聽到助理自己的聲音（按麥克風先 `stopReading`、正在聽就不自動念：`isListening`）、iOS Safari 需要手勢內開始的語音（`unlockSpeech`）；中——面板收起／抽屜關掉時不念並停止、不蓋掉手動朗讀。都已修；低風險（用說的「/」不念、`:has()`）接受。
- 截圖：`home-briefing-listen-*`、`studio-briefing-listen-*`、`studio-voice-reply-*`（新腳本 `.loop/shots/listen.cjs`；`speechSynthesis` 是唯讀屬性，替身要用 `Object.defineProperty`）。SHOWCASE 亮點列、demo 第 1 步、日誌一列。
- 驗證：前端 lint、build、test（145 檔 1568 個）全過；CI 40256706443（D-120）綠；**本輪最後一次含程式碼的 push 7809a5e2（審查修正）的 CI 40258346196 綠**。
- 問題：第一次審查修正時 `isListening` 的計數在測試替身的 `abort()`（不送 end 事件）後沒有歸零，自動朗讀就不念 → `cancel` 與 unmount 也釋放計數。

## 2026-10-26 07:42 – 第 27 輪（找出我的弱點、review_quiz 指定字）

- 開工：master 沒有新 commit（5dc1160 已合過）；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。clone 是 shallow，先 `git fetch --unshallow`。
- **Studio AI 能讀「最近的弱點」**（D-116）：`IStatsService.GetWeakSpotsAsync`（自己最近 7–90 天的 `ExamAttempts`，各測驗類型答對數、最常錯的 10 個字，最近一次還是錯的排前面）→ 工具 `weak_spots`（唯讀、不計費、表格卡片；沒做過測驗回連結卡片到 /english/exam）。入口：/english/stats、/english/exam 的 Studio 建議「找出我最近英文最弱的地方」、Studio「/」的「找出我的弱點」、本站 MCP prompt `my_weak_spots`、接著問。沒有新資料表或 migration。
- **`review_quiz` 可以指定字**（D-117）：拍截圖時發現 AI 說「先考這幾個還是錯的字」但按下去考的是別的到期字 → `IReviewQuizService.CreateForAsync(words)`：依指定順序、只考自己單字庫的字；都不在單字庫回連結卡片。接著問改「考我最常錯的這幾個字」。
- **學習統計頁的「AI 弱點分析」卡片**（D-118）：今日簡報的卡片抽成共用的 `AskStudioCard`，統計頁最上方一張，按下開新對話問弱點。截圖 `stats-weak-card-*`。
- **審查**（subagent，D-119）：無高風險；中風險「混了沒到期的字就整份不記」→ 測驗卡逐題 `due`，只記到期的；低風險（大小寫、空白 words、沒出題的說法、未知類型名稱、模型要寫出前三個字）都已修。
- 截圖：`studio-weak-suggest-*`、`studio-weak-spots-*`（新腳本 `.loop/shots/weak-spots.cjs`）；SHOWCASE 加亮點一列、demo 第 3 步一段、日誌一列。
- 驗證：cscheck 新診斷只有已知的 EF 擴充方法雜訊；前端 lint、test（144 檔 1553 個）、build 全過；前端最後一次 test 145 檔 1556 個全過；CI 40246857688（weak_spots，第一次就綠）、40247554029（指定字）、40248200966（統計頁卡片）綠；**40248730817（本輪最後一次含程式碼的 push e5a1da45：審查修正）綠**。
- 問題：無阻礙。

## 2026-10-26 05:42 – 第 26 輪（app 助理的「/」、Enter 選指令、CI 的 SDK 改回）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。
- **CI**：`ai-native-ci.yml` 的 .NET SDK 改回 `'10.0.x'`（10.0.403 已可下載）→ CI 40236816069 綠。
- **/finance 與 /data-analysis 自己的助理也有「/」現成指令**（D-114）：`SlashCommand`／`matchSlashCommands`（`aiChat/utils/slashCommands`）與 chip（`aiChat/components/QuickChips`，原 Studio 的 `QuickActionChips`）搬進共用的 aiChat；`AiChatConfig.slashCommands`。/finance 六個（都是既有範例問題，請求不變）、/data-analysis 四個（上傳檔案時沒有 dashboard）。截圖 `assistant-slash-*`（`slash.cjs` 第三段）。
- **「/」草稿按 Enter（或送出鈕）送出第一個列出的指令**（D-115）：面板的 `onSendDraft`；沒有列出指令、或挑了附件時照打的字送；第一個 chip 標 ↵ 與 `aria-keyshortcuts`。重拍 `studio-slash-*`。
- **審查**（subagent）：無高、中風險；低風險的附件被留下、↵ 被念進按鈕名稱、placeholder 少了 Shift+Enter 都已修，補了附件的測試。
- 驗證：前端 lint、test（144 檔 1550＋個）、build 全過；CI 40237590760（D-114）、40238218185（D-115）綠；**40238937360（本輪最後一次含程式碼的 push 7ff7dcd：審查修正）綠**。
- 問題：無阻礙。

## 2026-10-26 03:42 – 第 25 輪（「/」指令跟著頁面走、CI 的 .NET SDK）

- 開工：master 沒有新 commit（已合過）；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。
- **Studio 的「/」加上頁面自己的指令，排在最前面**（D-113、`pageSlashCommands`）：字典單字頁「解釋 {word}」（key `explain_word`）、/portfolio「記一筆交易」（`RECORD_TRADE`）、/dashboards「做一個新的 dashboard」（`MAKE_DASHBOARD`）、自己的 dashboard「解讀這個 dashboard」（`read_my_dashboard`，只給 `isOwner`、清單讀到才給，開新對話）。只動前端。截圖 `studio-slash-dashboard-desktop`、`studio-slash-word-desktop`（`slash.cjs` 第二段）。
- **CI 紅（不是程式的問題）**：.NET 10.0.403 今天發布，`10.0.x` 挑到它但 CDN 還下載不到（annotation：Could not find .NET Core SDK with version = 10.0.403；版本從 GitHub 上的 dotnet/core `releases.json` 查到）。`ai-native-ci.yml` 暫時釘 `10.0.402` → 綠。FOR_JACKY 第 11 條。
- 驗證：前端 lint、test（143 檔 1536 個）、build 全過；CI 40227880687、40228819697 紅（SDK 下載）→ 40229106292（釘版本）綠；**40229794608（本輪最後一次含程式碼的 push 0e16958：/portfolio 指令）綠**。
- 問題：見上（SDK）。

## 2026-10-26 01:42 – 第 24 輪（本站 MCP server 的現成指令、Studio 的「/」、審查）

- 開工：master 沒有新 commit（已合過）；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。clone 是 shallow，先 `git fetch --unshallow`。
- **本站 MCP server 提供 prompts**（D-110、design/03 §9.3）：`prompts/list`／`prompts/get`，`SitePrompt` 由各模組宣告在工具旁邊（今日簡報、解釋單字、考我單字、用我的單字寫短文、投資組合健檢、解讀 dashboard）；token 用得到全部工具才列出；參數只讀字串、整理成一行、最多 100 字。Claude Code 裡是 `/mcp__jacky-studio__daily_briefing`。測試：`SiteMcpServerTests`（含真的 SDK client 的 `ListPromptsAsync`／`GetPromptAsync`）、新的 `SitePromptDefinitionsTests`。
- **產生 token 的對話框**寫出 Claude Code、VS Code、Claude Desktop 怎麼叫出現成指令（`ClientConfig.prompts`；指令用等寬字、手機可斷行）。重拍 `site-mcp-token-*`（新腳本 `.loop/shots/site-token.cjs`）。
- **Studio 打「/」出現現成指令**（D-111）：`slashCommands`，六個，按下就送出（今日簡報開新對話），名稱與 MCP prompts 一致（測試讀伺服器檔案）。截圖 `studio-slash-*`（`.loop/shots/slash.cjs`）。
- **審查**（subagent，D-112）：無高風險；定義測試只認 `ISiteTool`、加註冊檢查；`Clean` 多處理 U+2028／U+2029／格式字元；`/` 比對收緊。
- 驗證：cscheck 新診斷 0；前端 lint、test、build 全過；CI 40218324796（prompts，第一次就綠）、**40219935497（本輪最後一次含程式碼的 push：Studio「/」＋審查修正）綠**。
- 問題：無阻礙。

## 2026-10-25 23:42 – 第 23 輪（確認卡：重新整理、倒數、分頁標題、時間到的說法、審查）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。
- **評估「確認卡重新整理就消失」**（D-105）：turn 的取消連著 `RequestAborted`，重新整理後伺服器已沒有 turn 可接；要接回得做 resumable stream（turn 與請求拆開），會動到用量結算與 /finance，凍結前不做。改成有確認卡在等時，重新整理或關閉頁面先問（`useWarnBeforeLeaving`、`hasOpenApproval`）。
- **確認卡顯示剩多少時間**（D-106）：「3 分鐘內沒有回覆，這次就不執行」，最後一分鐘倒數秒數；`APPROVAL_TIMEOUT_MS` 由測試讀 `ClientToolRunner.cs` 對齊。重拍 `studio-approval-*`。
- **分頁標題「（等你確認）」**（D-107）：切去別的分頁也看得到助理在等你（`useWaitingTitle`，跨對話計數）。
- **時間到不再寫「已拒絕」**（D-108）：`resolveApproval` 標 `expired`，寫「沒有在時限內回覆，沒有執行」。
- **審查**（subagent，D-109）：無高風險；兩個中風險（按下與時間到的競爭讓卡片復活、輸給時間到的允許寫成已拒絕）、三個低風險（倒數比伺服器晚 → 扣 5 秒；react-helmet 換頁蓋掉標記 → MutationObserver；一輪結束時開著的卡片寫「沒有執行」）都已修。
- 驗證：前端 lint、test（142 檔 1520 個）、build 全過；CI 40209125116（離開前先問）、40209760748（倒數）、40210314691（分頁標題）、40210932204（時間到）綠；**40211672020（本輪最後一次含程式碼的 push：審查修正）綠**。
- 問題：無阻礙。這輪全是前端，後端沒動。

## 2026-10-25 21:42 – 第 22 輪（記憶的收尾：資料分析只讀、主動提議記住、審查）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。
- **今日簡報用上筆記**：最後一句建議會用跟今天有關的筆記（目標、考試日期），`StudioChatProfileTests` 鎖住。
- **資料分析的助理只讀筆記**（D-102、design/08 §8）：共用的 `MemoryNotesPrompt`（`Block` 由 Studio 抽出、逐字不變；`ReadOnlyFor` 給沒有 remember／forget 的助理）。沒有筆記時 prompt 一字不差；讀不到筆記時照常回答。/finance 不讀（prompt 逐字鎖住）；「你記得我什麼？」不加工具。
- **Studio AI 主動提議記住**（D-103）：順口提到的目標或偏好 → 回答後呼叫 `remember`（`AlwaysAsks`＝一定是確認卡），一個對話一次、只根據使用者自己打的話。截圖 `studio-memory-offer-*`（`memory.cjs`，`ONLY=offer` 只拍它）。
- **/connectors 記憶區塊**：手機標題一行（`typography: { xs: 'subtitle1', sm: 'h6' }`）、說明改成一個字串（原本換行在中文之間多出空格）、提到資料分析只讀。
- **審查**（subagent，D-104）：無高風險；字級、筆記用法與「不要因筆記呼叫工具」的矛盾、提議來源、讀不到筆記、逐字測試都已修。
- 驗證：cscheck 新診斷 0；前端 lint、test（140 檔 1501 個）、build 全過；CI 40199245143 紅（`SessionUploadLimitsTests` 的 DI 驗證少了 `IAssistantMemoryService`）→ 40199683114 綠；40199896238（主動提議）綠；**40200592893（本輪最後一次含程式碼的 push：審查修正）綠**。
- 問題：新的建構子依賴要記得補 `SessionUploadLimitsTests.Registration_*` 的 DI 替身（它用 ValidateOnBuild 驗整個 `AddDataAnalysisServices`）。

## 2026-10-25 19:42 – 第 21 輪（交棒標示、截圖巡檢、Studio AI 的記憶、審查）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。clone 是 shallow，先 `git fetch --unshallow`。
- **交棒來的訊息標「由 Studio AI 轉來」**（D-099）：只在前端（`SendOptions.handedOverBy` → `Message.handedOverBy` → `MessageArea` 的 caption），請求 body 不變。截圖 `studio-handoff-from-*`（`selection.cjs` 情境 F）。
- **截圖巡檢**：重拍 `chat.cjs` 全部情境，與 repo 裡的比對；差異都是前幾輪刻意的改動（接著問、歡迎訊息），更新截圖。沒有退化。
- **Studio AI 的記憶**（design/08、D-100）：「記住：我在準備多益…」→ 確認卡寫出整句筆記 → 之後每個對話的 system prompt 都有 `<user_memories>`（標明不是指令、`<>` 全形）。`AssistantMemories` 資料表＋手寫 migration `20261025200000_AddAssistantMemories`（只新增）、`AssistantMemoryService`（每則 200 字、每人 30 則、同樣的話不重複）、`remember`／`forget`（照抄文字、Destructive）、`GET`／`DELETE api/assistant/memories`、/connectors 的 `MemorySection`、Studio 歡迎訊息與「你記得我什麼？」。**交給 /data-analysis 帶資料源**評估後不做（D-100）。
- **審查**（subagent，D-101）：三個中風險已修——`remember` 可設自動而附件不 taint、本站 MCP server 對外提供 `remember`、`<>` 全形造成忘不掉 → `AiToolDescriptor.AlwaysAsks`（伺服器與前端都照它：不給自動、不對外、確認卡沒有以後直接執行與全部允許）、`Normalize` 存全形；低風險 `forget` 大小寫完全相符、`CreatedAt` 標 UTC 已修；上限的併發競爭接受。
- 文件：兩份 CLAUDE.md、design/08、D-099～D-101、SHOWCASE（亮點一列、demo 第 3 步）、PLAN；截圖 `studio-memory-*`、`connectors-memory-*`（`.loop/shots/memory.cjs`）。
- 驗證：前端 lint、test（140 檔 1500＋個）、build 全過；CI 40190641700（交棒標示）、40191141568（資料表＋migration，ModelMatchesMigrations 第一次就綠）、40191402480（工具＋prompt）、40191760063（API）、40192581910（/connectors）綠；審查修正 40192873631 被下一次 push 取消（正常），**40193004221（本輪最後一次含程式碼的 push：AlwaysAsks 送到前端，含審查修正）綠**。前端最後一次 lint、test（140 檔 1501 個）、build 全過。
- 問題：`memory.cjs` 用 chat.cjs 的 `open()`（含 chat 的 fetch 替身）開 /connectors 會是空白頁，原因沒追；改成跟 `connectors.cjs` 一樣只用一個 route 就正常。

## 2026-10-25 17:41 – 第 20 輪（AI 之間交棒、持股外資動向、/dashboards 建議、選取巡檢、審查）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。
- **AI 之間交棒**（D-095）：Studio AI 的 `open_page` 多 `ask`（只在 `go` 且頁面是 finance／data-analysis），`AiToolResult.Handoff` 由 `ClientToolRunner` 跟著 navigate 送（`tool_result.handoff`，最多 500 字；讀過外部內容就連 navigate 一起不送，並告訴模型畫面沒切換：`NotMovedNote`）。前端：Studio 等回答以回答結束才帶著問題切頁（router location state），目的頁的 `AiAssistantLauncher` 展開、以新對話送一次、清掉 state（`aiChat/utils/handoff`）。Studio prompt：資料、圖表、dashboard、個股市場的事直接交棒。
- **持股的市場面**（D-097）：/portfolio 的 Studio 建議與 `portfolio_summary` 後的接著問「我的持股最近外資在買還是賣？」→ Studio 讀持股、把代號交給 /finance 的助理（不帶金額）。補上 D-073 留下的缺口，不改 Finance。
- **/dashboards 的 Studio 建議**「幫我做一個業績總覽 dashboard」（D-098，交棒給資料分析）。
- **巡檢 /data-analysis 下方「問 AI」的選取**：解讀文字選起來 → 頁面助理的「解釋／翻成英文／問 AI」，正常；輸入框裡的問題不出工具列。不用改。截圖 `select-nl-interpretation-desktop`（`selection.cjs` 情境 H）。
- **審查**（subagent，D-096）：無高風險；一個低風險已修（先交棒、後面又立刻切頁時，回答結束不再被拉回去）；附件不 taint、使用者自己接手時不交棒、交棒用預設資料源——接受並記錄。
- 截圖：`studio-handoff-*`、`studio-handoff-data-*`（情境 F）、`studio-holdings-handoff-*`、`studio-holdings-finance-*`（情境 G）。
- 驗證：cscheck 新診斷 0；前端 lint、test（139 檔 1491 個）、build 全過；CI 40183035832（交棒，後端第一次就綠）、40183265044（審查修正）、40183500106（持股交棒）綠；**40183931712（本輪最後一次含程式碼的 push：/dashboards 建議）綠**。
- 問題：無阻礙。

## 2026-10-25 15:41 – 第 19 輪（修紅 CI、一句話做出整個 dashboard、確認卡全部允許、審查）

- 開工：master 沒有新 commit；**最新一次 CI 是紅的**：Jacky 15:30 的 commit 25c21dd 把 `QuickActions.tsx` 改名成 `QuickActionChips.tsx`（避免 Windows／macOS 上與 `quickActions.ts` 只差大小寫），但 `StudioAssistant/index.tsx` 的 import 沒跟著改 → 修 import、元件名稱與 client CLAUDE.md（CI 40174073059 綠）。
- **第 18 輪審查剩下的低風險**（D-092）：助理的標題列與對話紀錄標 `noSelectionAsk`；Studio 與 `AiAssistantLauncher` 的 ask 改成依序佇列（`aiChat/hooks/useAskQueue`，面板 `onAskSent(id)`）。
- **巡檢 /data-analysis 的選取**：助理回答裡選「南區則緩步下滑」→ 工具列「解釋／翻成英文／問 AI」，按下送出的訊息正確（接在原對話、沒有 quotesPageText，以 console 確認）。截圖 `select-data-*`（`selection.cjs` 情境 D）。
- **一句話做出整個 dashboard**（D-090）：只改 /data-analysis 助理的 prompt 與歡迎畫面第四個建議；多個 `save_to_dashboard` 依序執行，第一張建立、之後的找到它（新測試）。
- **確認卡的「全部允許（N）／全部拒絕」**（D-091）：只在全部是本站工具、沒有 destructive、沒讀過外部內容時給全部允許；「以後直接執行」每個工具只問一次；`ApprovalCard` 以 `pendingId` 為 key。截圖 `data-whole-dashboard-*`（情境 E）。
- **上傳檔案時不提 dashboard**（D-094）：`dataAnalysisAiChatFor(uploadedFile)`，建議、接著問、歡迎訊息拿掉 dashboard。
- **審查**（subagent，D-093）：無高風險；中風險「外部 MCP 的 effect 是對方自己說的，全部允許可能放行夾帶的刪除」已修（只限本站工具）；上傳檔案時查完第一張就停（prompt）。其餘低風險記在 D-093。
- 驗證：前端 lint、test（137 檔 1477＋個）、build 全過；CI 40174073059、40174872913、40175193675（後端 prompt＋測試，第一次就綠）、40175961415 綠；40176286605（審查修正）、**40177212324（本輪最後一次含程式碼的 push）綠**。
- 問題：無阻礙。

## 2026-10-25 13:42 – 第 18 輪（選取工具列巡檢、擴到 /finance 與 /data-analysis 的助理、審查）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠。clone 是 shallow，先 `git fetch --unshallow`。
- **巡檢選取工具列**（D-088）：/english/exam 的題目選起來就能「翻成中文」「查字典」＝把答案遞到手上 → 作答中的題目加 `noSelectionAsk`（`ExamRoundShell` 的題目區、每日挑戰 `QuestionCard` 到顯示結果為止、複習 `Flashcard` 到翻面為止、Studio 的 `QuizCardView` 到該題答完為止）；dashboard 編輯版面時的格線也不給。/portfolio 持倉表的輸入框與 Select 選單本來就被排除。
- **選取也能問 /finance 與 /data-analysis 的助理**（D-089）：選取三件套搬到共用的 `components/aiChat/selection/`，按下去做什麼抽成 `selectionStepOf`（Studio 與 `AiAssistantLauncher` 共用），助理區塊標記改成 `data-ai-assistant`；launcher 的工具列沒有「加入單字庫」，一個英文字給「解釋／查字典／問 AI」。截圖 `select-finance-*`（`.loop/shots/selection.cjs` 的情境 C，`ONLY=C` 只拍它）。
- **審查**（subagent）：無高風險（taint 沒有繞過、沒有兩條工具列、Studio 行為不變）。兩個中風險已修：拖過被標記的題目（起點終點都在外面）仍會帶出題目文字 → `readSelection` 也檢查 range 是否碰到標記元素；launcher 回答寫到一半按「查字典」會離開頁面中斷回答 → 等回答結束才前往。低風險（history 欄的標題算在助理區塊、快速連按兩次只送最後一個、Studio 抽屜與 launcher 共用標記）記在 NEXT，現況不會發生或影響很小。
- 驗證：前端 lint、test（136 檔 1467 個）、build 全過；CI 40167126057（noSelectionAsk）綠、40167921815（launcher 選取）綠；40176286605（審查修正）、**40177212324（本輪最後一次含程式碼的 push）綠**。
- 問題：後半段 auto mode 的安全分類器連續 10 次沒有回應，那一段工作被中斷（不是程式的問題）；恢復後補跑測試再 commit。

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

## 2026-10-24 每日摘要 – 第 1～9 輪（第 27 輪合併；細節見 git log 與 SHOWCASE 的開發日誌）

- 第 1 輪：階段 0 研究與設計文件 design/01–07，設計定稿（04:10），提早開工 M1。
- 第 2～3 輪：M1-5、M1-7、M2 前端零件；/connectors v2 前端。
- 第 4 輪：離線型別檢查（`.loop/cscheck`，D-021）、agent loop、對話 profile、Data Analysis 助理。
- 第 5 輪：寫入確認流程、connector 後端、使用者自己的 MCP server、Studio AI。
- 第 6 輪：MCP OAuth、Portfolio／學習進度工具、用說的存 dashboard、SHOWCASE 初稿。
- 第 7 輪：M4（TestKit、Google 日曆原生 connector、403 step-up、以後直接執行、rug pull 防護）。
- 第 8 輪：本站對外的 MCP server（PAT、OAuth 授權伺服器、同意頁）。
- 第 9 輪：本站 MCP server 成為 MCP App、收尾、安全審查。
- 每輪的後端改動都由 branch CI 驗證並在收尾時為綠。
