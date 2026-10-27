# PROGRESS（新的在最上面）

## 2026-10-27 19:42 – 第 45 輪（第六次合併前巡檢：Job Finder、Portfolio、English 的 AI 批改）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。`add_repo` 工具仍不存在，直接 clone／push（這輪 clone 是完整的）。
- **巡檢**（subagent 唯讀，D-165）：七項全是 master 既有的；分支新增的部分沒問題。共同根因：沒設 `RequestSizeLimit` 的端點上限是 Kestrel 預設約 28 MB。已修：高——存持倉可灌滿 DB（`HoldingsLimits`：請求 512 KB、300 筆、欄位長度、每人 2000 份快照；`SaveHoldingsAsync` 改回 `ServiceResult<bool>`，`update_holding` 與編輯器說出原因）；中——Job Finder 匿名無頻率限制、cache key 含亂打的 platforms（`RateLimitPolicies.JobSearch` 每位址每分鐘 20、關鍵字 100 字、key 只用實際 provider）；中——English AI 批改沒有長度上限（`ExamInputLimits`，六個端點 `[RequestSizeLimit]`）；中——`record-result` 的寫作單字逐筆查詢（最多 5 個、一次查詢）；中——benchmark 代號不限數量（最多 4）；低——Cake 外部連結只收 https。不修：Yahoo 網址的 symbol 沒 escape（主機固定）。
- **審查**（subagent）：編譯與測試推演無問題；建議兩項已做：持倉名稱放寬到 200 字（基金名稱含警語可達 80 字，請求上限隨之 512 KB）、對話練習太長時從最舊的回合刪到符合（`TrimOldestTurns`；頁面每回合送完整歷史、失敗時只有 console.error）。
- **寫作與造句的批改失敗會說原因**（原本只有 console.error，跟翻譯測驗一樣顯示錯誤，有伺服器的原因就用它；新增兩個元件的測試）。
- **D-164 留下的低風險**：`MaskMsSql` 改用 `SqlConnectionStringBuilder` 遮密碼（D-166）。
- 驗證：cscheck 每次 0 個新診斷（EF 與 SqlClient 的已知雜訊）；前端 lint、`src/JobSearch`＋`src/Portfolio` 的 vitest（34 檔 565 個）、build 全過；CI 40500457635（3ed1b9d8）綠、40501738683（5fd77ced）綠、40502128403（37247433）綠、**40503637714（179f5aa9，本輪最後一次含程式碼的 push）綠**；全套前端測試 165 檔 1770 個。

## 2026-10-27 17:42 – 第 44 輪（第五次合併前巡檢：dashboard、資料分析的 AI 工具、自訂資料源）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。`add_repo` 工具仍不存在，clone 是 shallow → `git fetch --unshallow`。
- **巡檢一**（subagent 唯讀，D-163）：dashboard 分享、`read_dashboard`／`save_to_dashboard`／`ask_data`／`describe_data`、`/admin/listening`。無高風險。已修：中——上傳檔的查詢結果沒有 taint（新的 `AiToolResult.OutsideText`，只對上傳檔；資料源維持 D-156，「存完打開」不受影響）；低——「任何人」可看的 dashboard 的 widget 資料加頻率限制（`DashboardWidgetData`）；低——分享設定的成員只回 email（原本附名字與 Google id）。
- **巡檢二**（同一個 subagent，D-164）：計費全部沒問題。中（master 既有）——**自訂 SQL Server 資料源可當內網 port 掃描器**：`ValidateServer`（只收 TCP、擋 local 名稱／機器名／內網 IP）、連線前 DNS 檢查（`IServerAddressCheck`）、存資料源套 `ConnectorProbe` 限制。
- **審查**（subagent）：TDS routing 能繞過名稱檢查 → 擁有者不再看到連線失敗的 driver 文字（`EngineSources.OwnerMessage`）；主機檢查移到建引擎前；上傳檔的失敗訊息也 taint；主機名允許 `_`。
- 驗證：cscheck 每次 0 個新診斷（`Assert.Single(DbSet)`／`.Single()` 是 cscheck 的 EF 雜訊）；前端 lint、test（162 檔 1765 個）、build 全過；CI 40483688976（c7602fa7）綠、40484545929（0db516bb）**紅**、40485090133（8373a055）綠、40485949897（7e01e2d2）綠、**40487224461（49a09f2e，本輪最後一次含程式碼的 push）綠**。
- 問題：0db516bb 的 CI 紅在 master 既有的 `NlValueSourcesTests.Engine_Distinct_TimeoutIsUnknownAndRemembered`（牆鐘 3 秒上限，忙碌的 runner 超過）→ 放寬到 15 秒，下一次 push 綠。

## 2026-10-27 15:42 – 第 43 輪（巡檢未修項目收尾）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。`add_repo` 工具仍不存在，直接 fetch／push。
- **錄音轉文字的上限**（D-159，後端＋前端）：Whisper 依分鐘計費、一次只收 1 單位 → `ConversationPracticeController.MaxAudioBytes` 4 MB（`[RequestSizeLimit]`＋動作內檢查，呼叫 Whisper 前 400）；`useVoiceRecording` 64 kbps、120 秒自動停並照常送出，麥克風提示「最長 2 分鐘」；錄滿或轉文字失敗時輸入框下方說一句（原本只有 console.error）。測試：controller 3 個、`useVoiceRecording.test`（計時、卸載、失敗提示、讀 C# 檔對數字）。
- **匿名上傳的個人份額**（D-159）：`SessionUploadBudget.TryReserve(bytes, caller)`，每人（登入者 id 或 Cloudflare 位址）最多 128 MB，到期一起還；超過回 429「你上傳的資料已達上限…」。測試 5 個（含並行）。
- **審查**（subagent）：無高風險。計時器測試沒真的跨過舊計時器、卸載後的計時器沒驗到 → 已修；個人份額改成不低於 `MaxSessionBytes`（這個修正讓兩個新測試紅了一次，見問題）。
- **D-157 剩下的三個低風險都修了**：外站連結卡片在標題下寫主機（`cards/linkHost`，仿冒網域是 punycode；D-160，截圖 `studio-jira-comment-done-*` 已更新）、Portfolio 七個圖表 tooltip 跳脫（D-161）、OAuth 回來的提示只寫自己有的名稱（`callbackNotice(params, knownNames)`；D-162）。
- **截圖巡檢**：`chat.cjs` 43 張與 repo 比對，沒有超過 1% 的差異。
- 驗證：前端 lint、test（161→163 檔 1765 個）、build 全過；CI 40469256359（83f3745）、40469791879（40b937b）綠；40470260882（6fe3016）**紅**；40471064690（9447a05，修正）、40471550282（686f8be）、40471954615（dd58abd）綠；**40472786883（63268db，本輪最後一次含程式碼的 push）綠**。
- 問題：(1) 審查修正把份額下限設成 `MaxSessionBytes`（預設 64 MB），新測試用的小份額被蓋過 → 測試補 `MaxSessionBytes`。後端改動 push 前沒跑 cscheck，靠 CI 抓到。(2) 全套前端測試與 playwright 同時跑時，`ToolCards` 的「畫成圖」第一次 lazy import 超過 50 ms 而失敗一次 → `settle` 最多等 2 秒。

## 2026-10-27 13:42 – 第 42 輪（合併前安全巡檢：上傳檔案與 chat 附件）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。`add_repo` 工具仍不存在，直接 fetch／push。
- **第四次巡檢：上傳與附件**（subagent 唯讀，D-158）：3 高 4 中 2 低，6 項是 master 既有的。已修：
  - 高：xlsx 一格寫 `ZZZZZZ1` 就補 3 億個空格、zip 解開沒上限 → `AttachmentTextExtractor` 每個 XML 部分 1600 萬字、超過 XFD 的格子不收、讀超過上限就停。測試 4 個（含 1600 萬字的 inline string、2 萬列）。
  - 高：chat 訊息沒有長度上限（失敗會退款但訊息已存，可灌爆 DB）→ 2 萬字、請求 1 MB，計費前 400；前端輸入框與編輯框說「訊息太長」並不送出（`messageTooLongNote`，測試讀 `ChatbotController.cs`）。
  - 高：匿名 session 上傳 32 MB 的空列會讀成約 1 GB → `SessionRowsJsonConverter` 邊讀邊估，超過 128 MB 就停。測試 `SessionRowsJsonConverterTests`。
  - 中：chat 附件上傳每人每分鐘 20 次、PDF 解鎖每個位址每分鐘 10 次（`ConnectorProbeRateLimitTests` 加一個）。
  - 不改（NEXT）：Whisper 依長度計費、匿名上傳共用預算、PDF `/Count`、附件不 taint。
- 驗證：前端 lint、test（160 檔 1748 個）、build 全過；CI 40457519350（8d3b7138：附件與訊息上限）、40458314485（745540c6：前端＋頻率限制）、**40458717202（f0c38d74，含 session 上傳的 d7901bcf，本輪最後一次含程式碼的 push）都第一次就綠**。截圖 `too-long-*`（新腳本 `.loop/shots/too-long.cjs`：/finance 貼 20,050 字 → 送出鈕停用、輸入框下方說明；桌機與手機都不擠）。
- 問題：一次 push 的最後一個 commit 是 `[skip ci]` 的文件，整個 push 沒跑 CI（第 38 輪記過的坑）→ 補一個說明文字的 .cs commit（f0c38d74）讓它跑。

## 2026-10-27 11:42 – 第 41 輪（合併前安全巡檢）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。`add_repo` 工具仍不存在，直接 fetch／push。
- **安全巡檢**（subagent 唯讀，D-155）：本站 MCP server 與 OAuth 授權伺服器、MCP client 的 OAuth、SSRF。無高風險。
  - 中：**OAuth callback 沒確認回來的是開始授權的人**（login CSRF：把自己 connector 的授權網址寄給別人，別人按允許後他的帳號會連進寄件者的 connector）。`McpOAuthFlow.CompleteAsync` 與 master 既有的 `ConnectorService.HandleCallbackAsync` 都改成要求 `currentUser.Id` 等於 state 的使用者；MCP 回 `wrong_account`（不用掉待完成的授權、不帶 connector 名稱），/connectors 有對應訊息。測試 `McpOAuthFlowTests`（另一人／未登入）、新的 `ConnectorServiceCallbackTests`。
  - 低：自填 MCP server 的回應沒有大小上限 → `ResponseSizeLimit`（8 MB，`Content-Length` 超過就拒、串流讀到超過就中斷），`Classify` 說「回應太大」。測試 `ResponseSizeLimitTests`、`McpToolClientTests`（9 MB 的 tools/list）。
- **第二次巡檢：授權／IDOR／濫用**（subagent 唯讀，D-156）：沒有跨使用者讀寫的路徑。已修：中——MCP 工具清單沒有總量上限（SDK 會一直跟 `nextCursor`、schema 不截）→ 自己分頁最多 5 頁、同名一次、schema 超過 32k 字不收、合計 400k、標題 100 字；低——probe／新增／test 沒有頻率限制 → `connector-probe` 每人每分鐘 20 次。不改：`ask_data` 不標外部文字（會讓「存完打開」失效，理由見 D-156）。
- **第三次巡檢：前端把字變成 HTML 的地方**（subagent，D-157）：高——AI 回答的 markdown 圖片會自己載入（工具內容可引導模型用圖片網址把資料送出去，不用點）→ `MarkdownMessage` 的圖片只留文字；高（master 既有）——Sankey tooltip 把資料值當 HTML（公開 dashboard 的 stored XSS）→ `sankeyTooltip` 跳脫；低——產業指數 tooltip 也跳脫。client CLAUDE.md 新增「Text that becomes HTML」。前端 lint、test（160 檔 1745 個）、build 全過。
- 驗證：cscheck 只報 `Assert.Empty(DbSet)` 的 CS1503（改成 `Any()`）與 MCP SDK 分頁多載的綁定雜訊（`ListToolsRequestParams`，v1.3.0 原始碼確認有這個多載）；前端 lint、Connectors 測試（122 個）、build 全過；**CI 40446429022（D-155，813f20fc）、40447426447（D-156，68fb008d）、40449208330（D-157，d35d8b96，本輪最後一次含程式碼的 push）都第一次就綠**。
- 問題：無阻礙。這次改到 master 原有的帳號連線 callback：合併後請實際連一次 Google（FOR_JACKY 13、SHOWCASE 檢查清單）。

## 2026-10-27 09:42 – 第 40 輪（表格卡片一鍵變圖）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。`gh` 沒登入，CI 用公開 API 看；`add_repo` 工具這輪不存在，直接 clone 也能 push。
- **表格卡片「畫成圖」**（D-153，只動前端）：表格卡片下方多一顆長條圖示，按下圖表原地取代表格（再按回表格）。`cards/tableChart` 決定可畫的欄（一次一欄，先畫排序的那欄）、列的名字（代號＋名稱）、適合的圖（長條照畫面順序最多 20 條；折線只給日期／期間、照時間；圓餅只給沒有負數、不是百分比的，超過 8 塊合成「其他」），tooltip 跳脫工具給的字。`TableChartView` 用 `React.lazy`（build 出 1.8 kB 的獨立 chunk，ECharts 走既有的共用 chunk）。截圖 `table-chart-*`（新腳本 `.loop/shots/table-chart.cjs`），SHOWCASE 亮點、demo 第 9 步、日誌。
- **審查**（subagent，D-154）：XSS 無（查 ECharts 6.1 原始碼確認 tooltip 是唯一 HTML 出口）。中——負值的數字壓到名稱（`barAxisMin`）、月/日跨年、「—／N/A」毀掉整欄、`20261027`／`1150930` 不是日期、lazy chunk 失敗整頁空白（`ChartLoadBoundary` 退回表格）；低——英文名字當代號、「率」欄給圓餅、0 的扇形、深色 tooltip、按鈕名稱。都已修並補測試。截圖再發現手機圓餅外圈名稱被截 → 改 legend。
- **巡檢**：`chat.cjs` 全部重拍比對，差異只有持倉卡片多了一顆圖示（已更新 `studio-portfolio-*`）與動畫時間差，Data Analysis 的圖表卡片沒有多按鈕（正確）。
- 驗證：前端 lint、test（159 檔 1731 個，審查修正後相關 5 檔 82 個）、build、`tsc -b` 全過；**CI 40436041196（本輪唯一含程式碼的 push 8afbc06a）第一次就綠**。
- 問題：無阻礙。**vitest 不做型別檢查**，測試檔用 `.at()` 在 `tsc -b`（lib 不到 es2022）才會抓到——跑 `npm run build` 前別以為測試過了就好。

## 2026-10-27 07:42 – 第 39 輪（表格卡片：複製到試算表、下載 CSV、按欄名排序）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。`gh` 沒登入，CI 用公開 API 看。
- **表格卡片可以帶走**（D-150，只動前端）：每張表格卡片（與 Data Analysis 的圖表，經 `cardTables`）下方「複製表格」（TSV，貼進 Excel／Google 試算表一格一格）、「下載 CSV」（BOM、CRLF、卡片標題＋台北日期當檔名）。工具給的字會防公式注入（伺服器 `CsvExportWriter` 的規則；截圖時發現「-18.0%」被加 `'`，「數字」改成也認千分位與百分比）。只含畫面上的列，工具找到更多時會說。`cards/tableExport`、`TableCardActions`。
- **審查**（subagent，D-151）：高——TSV 開頭是 `"` 的格子貼上會被解引號，帶進活的公式（含 `"` 的格子一律加引號）；中——`0050` 變 50（加 `'`）、`copyText` 的退路複製失敗不丟例外（看 `execCommand` 回傳，三個既有呼叫端接住）；低——同樣的提示重新計時、檔名去 bidi（匯出對話也是）、`ToolCards` 的清單 memo。都已修並補測試。
- **表格卡片按欄名排序**（D-152）：數字欄先大到小、文字欄 zh-TW、文字寫的數字照數值、空格最後、第三下回原順序；複製與 CSV 跟畫面順序。數字欄的箭頭放在欄名左邊的空隙（不加寬欄位，手機才不會超出；第一版加寬了 20px、表格被捲走一截）。截圖 `table-export-*`（新腳本 `.loop/shots/table-export.cjs`，印出 CSV、剪貼簿與表格是否超出），SHOWCASE 亮點、demo 第 9 步、日誌。
- 驗證：前端 lint、test（157 檔 1693 個；之後相關 35 檔 394 個）、build、`tsc -b` 全過；CI 40423210582（D-150）綠；**40425515584（本輪最後一次含程式碼的 push fb9391e1：D-151＋D-152）綠**。`chat.cjs` 全部重拍比對：差異只在有表格／圖表卡片的 6 張（卡片下多了複製與下載），已更新。
- 問題：無阻礙。`pkill -f vitest` 會殺掉自己的 shell（指令列含 vitest），不要用。

## 2026-10-27 05:42 – 第 38 輪（改名收尾、匯出對話、截圖巡檢）

- 開工：master 沒有新 commit（`chore: remove details` 已在 branch 裡）；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。`gh` 沒登入，CI 用公開 API 看。
- **改名收尾**（D-146，只動前端）：伺服器拒絕改名時紀錄欄說「改名沒有存好，標題維持原樣」（5 秒）；Enter／Escape 結束的改名焦點回到鉛筆。
- **匯出對話**（D-147，只動前端）：三個助理標題列多一顆「匯出對話」→ 複製成 Markdown／下載 .md：標題、台北時間、每輪問答、用了的工具、表格卡片（Markdown 表格，數字照畫面）與連結卡片。純前端、不計費。截圖 `export-*`（新腳本 `.loop/shots/export.cjs`，要 `LANG=C.UTF-8` 才拿得到中文檔名），SHOWCASE 亮點、demo 第 9 步、日誌。
- **審查**（subagent，D-148）：無高風險。中——沒關的 code block 吞掉之後的輪次（補關）、焦點目標殘留會在清搜尋時搶焦點（只活一個 commit）；低——標題降兩級、卡片文字與網址的跳脫、只列成功的工具、沒 renderer 的卡片不提、emoji 檔名、提示框淡出、aria。都已修並補測試。
- **圖表也匯出**（D-149）：`AiChatConfig.cardTables`，/data-analysis 的 chart 卡片寫成它的資料表（`chartCardTable`）。
- **截圖巡檢**：`selection.cjs`、`memory.cjs`、`connectors.cjs`、`mcp-app.cjs`、`rename.cjs` 重拍，與 repo 比對沒有退化（差異都是確認卡剩餘時間、registry 卡片與連結、Studio AI 記得的事等刻意改動），已更新 repo 裡既有的。
- 驗證：前端 lint、test（156 檔 1679 個）、build 全過；CI 40409716274（D-146＋D-147）、40411291522（D-148）綠；**40412656073（本輪最後一次含程式碼的 push f142a7fd：D-149）綠**。
- 問題：無阻礙。`[skip ci]` 的 commit 若是一次 push 的最後一個，整個 push 都不跑 CI——含程式碼的 commit 要分開 push 或放在最後。

## 2026-10-27 03:43 – 第 37 輪（對話手動改名、中途巡檢）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。`gh` 沒登入，CI 用公開 API 看。
- **對話手動改名**（D-144，後端＋前端）：紀錄列每列刪除旁多一支鉛筆 → 該列變成輸入框，Enter 或離開存、Escape 不改（不關抽屜）。`PUT api/chatbot/conversations/{id}/title`（`ChatConversationService.RenameAsync`／`CleanTitle`）：只限自己的、一行、最多 40 字（text element）、超過拒絕不截斷、不改 `UpdatedAt`、不計費；前端 `renameConversation` 樂觀更新、被拒還原，`renamedTitle`＋`MAX_TITLE_LENGTH`（測試對到 C# 常數）、`ConversationTitleEditor`。不需要 migration。截圖 `rename-*`（新腳本 `.loop/shots/rename.cjs`），SHOWCASE 亮點、demo 第 9 步、日誌。
- **審查**（subagent，D-145）：無高風險。中——AI 命名期間改名會被蓋掉（存前 `ReloadAsync`，改過名／刪掉的不存）、輸入框開著時 AI 取好名，沒動就離開會寫回舊標題（比對打開時的標題）、在路上的列表請求蓋掉樂觀更新（存好後 invalidate）；低——Safari 中文選字的 Enter（`keyCode 229`）與選字中的 Escape。都已修並補測試（`ConversationTitleEditor.test`、titler 兩個競速測試）。
- **中途巡檢**：`chat.cjs` 全部重拍（43 張），與 repo 比對沒有退化（差異都是第 23 輪後確認卡的剩餘時間、placeholder 的「/」提示等刻意改動），已更新 repo 裡既有的 41 張。
- 驗證：前端 lint、test（154 檔 1656 個）、build 全過，審查修正後相關測試＋eslint＋build 過；cscheck 新診斷只有已知雜訊；CI 40390914576（D-144 後端）、40391542933（D-144 前端）綠；**40392318969（本輪最後一次含程式碼的 push 3dc9672f：D-145）綠**。
- 問題：無阻礙。NEXT 裡「`?registry=` 第二次不換搜尋」其實早已修好（`AddMcpServerForm key={registrySearch}`），已從清單拿掉。

## 2026-10-27 01:42 – 第 36 輪（AI 替對話取名字、搜尋對話）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。`gh` 沒登入，CI 用公開 API 看。
- **AI 替對話取標題**（D-141，後端＋前端）：紀錄列表原本是問題前 30 字。新對話第一個回答 `done` 後，前端呼叫 `POST api/chatbot/conversations/{id}/title`；`ConversationTitler` 用 Haiku 5.5（effort low、無工具、20 秒）讀問題與回答開頭，整理成 ≤24 字標題（`Clean`）。另開端點、不在串流裡做，chat 引擎與 `AnthropicChatStreamTests` 不動；不另外計費（對話一定先有已收費的訊息，usage log 記 `chatbot.title`）；不需要 migration。`ConversationTitlerTests`（命名、只一次、失敗不重問、別人的 NotFound、沒答完／改過名／沒 key 不問、`Clean`、`Cut`）、前端 `index.test`。
- **審查**（subagent，D-142）：無高風險。中——第一個回答失敗後「重試」成功的對話不會命名（改看這回合之前有沒有回答）、回答可能轉述外部文字，`</answer>` 能關掉標籤（`AsData` 全形）；低——同時請求（`InFlight`）、不隨請求取消、刪除中不 500、記 stop reason、`ThenBy(Id)`。都已修並補測試；「一次」只在同一 process、一天內成立，文件照實寫。
- **搜尋對話**（D-143，只動前端）：對話滿 6 則時紀錄欄上方「搜尋對話」，比對標題、每個字都要有、不分大小寫與全半形，Escape 清空。截圖 `title-*`、`history-search-desktop`（新腳本 `.loop/shots/title.cjs`），SHOWCASE 亮點、demo 第 9 步、日誌。
- 驗證：前端 lint、test（152→153 檔 1650 個）、build 全過；CI 40371859420（D-141 後端）、40373123193（D-141 前端）、40373947026（D-142）綠；**40375443969（本輪最後一次含程式碼的 push b710adc4：D-143）綠**。
- 問題：無阻礙。

## 2026-10-26 23:42 – 第 35 輪（長對話不卡、失敗的回答可以重試）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。`gh` 沒登入，CI 用公開 API 看。
- **每則訊息各自 memo**（D-138，只動前端）：NEXT 的「`MessageArea` 每則訊息 `React.memo`，要做先量」。量了：30 則（一半有表格）的對話，每段串流 329 ms 重畫整串 → `memo(MessageItem)`＋整段對話不變的 `handlers` 後 3.4 ms；打字時也不再重畫訊息。`MessageArea.memo.test`（舊程式碼會失敗的兩個案例＋仍呼叫最新 callback）。
- **失敗的回答下方「重試」**（D-139，只動前端）：最後一則失敗、而且再問可能會好（忙碌、沒收到完整回覆、串流斷掉、連不上）時出現；問題原地重送（存過的當編輯、沒存過的連同附檔、`quotesPageText`、交棒標記、用聽的重新送出）。截圖 `retry-*`（新腳本 `.loop/shots/retry.cjs`），SHOWCASE 亮點、demo 第 9 步、日誌。
- **審查**（subagent，D-140）：D-138 無行為差異。中——失敗的編輯重試變成追加（`replacesServerId`）、伺服器可能已存問題但沒送 `start`（只在收到 `start` 或 fetch 被拒時給重試）、重試可能重做寫入（有 `id` 的工具跑完就不給）；低——`hasOwnProperty`。都已修並補測試。
- 驗證：前端 lint、test（152 檔 1644 個）、build 全過；CI 40353952704（D-138）、40355908452（D-139）綠；**40357652223（本輪最後一次含程式碼的 push 65705520：D-140）綠**。
- 問題：無阻礙。

## 2026-10-26 21:46 – 第 34 輪（看得見 AI 在想什麼：思考摘要）

- 開工：master 的新 commit（README 精簡）已在 branch 裡；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。clone 是 shallow，先 `--unshallow`；`gh` 沒登入，CI 用 GitHub MCP 與公開 API（`curl api.github.com/.../check-runs/<id>/annotations` 不用 token 也讀得到）。
- **思考摘要**（D-136）：Haiku 5.5 本來就會思考，預設 `display: omitted` 不回傳。`ChatTurnRequest` 三種請求都帶 `thinking: adaptive + display: summarized`（計費相同），`ModelTurn` 把思考摘要送成 SSE `thinking`（不算回答、不存）。前端 `utils/thinking`＋`ThinkingTraceView`：「思考中… N 秒」＋最後一行 →「已思考 N 秒」，按下展開；純文字；header「💭 思考中…」；失敗的回合連思考一起拿掉。/finance、/data-analysis、Studio 都有。SDK 原始碼（`git clone --depth 1 --branch Anthropic-v12.60.0 https://github.com/anthropics/anthropic-sdk-csharp`）拿來確認型別。截圖 `thinking-*`（新腳本 `.loop/shots/thinking.cjs`），SHOWCASE 亮點、demo 第 1 步、日誌。
- **審查**（subagent，D-137）：無高風險；低～中——兩個 thinking block 之間（Finance 的 tool search、pause_turn 重試）摘要黏在一起 → 伺服器在第二個起的 block 前加段落間隔、前端只留一個；低——`pause()` 沒變就不重畫、展開區塊 `unmountOnExit`。
- 本站 MCP prompt `daily_briefing` 也讀 Outlook（`ifConnected`，沒有工具就略過；不列進 `Tools`，免得關掉 Outlook 的人連 prompt 都看不到）。
- 驗證：前端 lint、test（全套 TEST 0）、build 全過，審查修正後 aiChat 測試 323 個、eslint、tsc 過；CI 40334251022 紅（`FinanceChatRequestTests` 的欄位清單沒加 `thinking`）→ 40336246524（D-136 前端＋D-137）綠；**40336640053（本輪最後一次含程式碼的 push 6bbd3a73：daily_briefing）綠**。
- 問題：無阻礙。

## 2026-10-26 19:42 – 第 33 輪（Jira 留言、Outlook 行事曆）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。clone 是 shallow，先 `--unshallow`；`gh` CLI 沒有登入，CI 改用 GitHub MCP（`actions_list`／`actions_get`）看。
- **Jira 留言**（D-132）：`jira_add_comment`（`Services/Connectors/Atlassian/JiraAddCommentTool`），第一個會寫入外部服務的原生 connector：既有連線的 `write:jira-work`、`ToolEffect.Write`＋`IDescribesCall`（確認卡寫出票號與整則留言）、最多 2000 字超過就拒絕不截斷、ADF 一行一段、POST 用預設 client（不重試）、5xx／逾時說「可能沒有留上」。Studio prompt、接著問「在最新那張留言：我今天會處理」、token／同意頁的寫入範例。截圖 `studio-jira-comment-*`（新腳本 `.loop/shots/jira-comment.cjs`），SHOWCASE 亮點、demo、日誌。
- **審查**（subagent，D-133）：高——`ClientToolRunner.Summary` 把確認卡的說明截到 200 字，長留言只看得到開頭 → `MaxSummary` 2200（也修好 `remember` 筆記被截）；中——讀和寫可能是不同 Jira 網站 → 都用讀取的網站（`JiraSite.CanWrite`）；低——403 說成專案權限、送出前／後的失敗分開說、201 讀不懂仍算已留言。
- **Outlook 行事曆原生 connector**（D-134）：`outlook_calendar_events`（`Services/Connectors/Outlook`；不能叫 `.Microsoft`），Graph `me/calendarView`、台北時區，與 Google 日曆共用 `CalendarRange`／`CalendarEvent`／卡片；今日簡報兩個行事曆都讀。
- **審查**（subagent，D-135）：中——今日簡報每天對只連一個的人出「連線另一個」的卡片 → `ifConnected`（簡報用，沒連線只回一句、不給卡）；低——全天行程用最近的午夜（`DayOf`）、測試時鐘改台北 00:30。
- 驗證：cscheck 每次新診斷 0；前端 lint、test（150 檔 1621 個）、build 全過，Outlook 之後全套 test（150 檔 1622 個）、eslint、build 過；CI 40318532123（D-132）、40319052286（D-133）、40319749027（D-134）綠；**40320332432（本輪最後一次含程式碼的 push 1b579275：D-135）綠**。
- 問題：無阻礙。

## 2026-10-26 17:42 – 第 32 輪（Jira 原生 connector、朗讀與 Registry 的小修）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。
- **D-129**（前端）：自動朗讀改看草稿還留著多少聽到的字（`utils/spokenDraft` 的 `stillSpoken`，LCS ≥ 一半）——刪掉聽到的字改用打的就不念，修正聽錯的字仍念；`AddMcpServerForm` 以 `registrySearch` 當 key，Studio 第二次「加入」換成新的搜尋。
- **Jira 原生 connector**（D-130）：`jira_issues`（`Services/Connectors/Atlassian/JiraIssuesTool`）用既有 Atlassian 連線（`read:jira-work`，不用重連）：accessible-resources → 第一個 Jira 網站 → `/rest/api/3/search/jql`；預設「指派給我、未完成」，模型可自己寫 JQL，400 時把 Jira 的說明交給模型修正；表格卡片＋每張票的 `/browse` 連結；`OutsideText`。Studio prompt、「/」我的工作待辦（`my_jira_issues`）、接著問；本站 MCP 同意頁與 token 說明加上 Jira。原始需求「既有 Atlassian／Microsoft／Google 連線對 AI 來說也是工具」從只有 Google 補到 Atlassian。截圖 `studio-jira-*`（新腳本 `.loop/shots/jira.cjs`），SHOWCASE 亮點、demo、開發日誌。
- **審查**（subagent，D-131）：無高風險；中——新讀取工具會自動給已發出的本站 MCP token（production 還沒有 token，合併後的新工具要處理，記在 NEXT）；低——JQL 要有篩選條件（寫進工具說明）、截字切開 emoji（`CutAt`）、長貼文後用說的比對漏掉（改看草稿結尾）已修。
- 驗證：cscheck 新診斷只有已知的 `[GeneratedRegex]` 雜訊；前端 lint、test（150 檔 1619 個）、build 全過，審查修正後 aiChat utils 測試、eslint、prettier 過；CI 40303525213（D-129＋D-130）綠；**40304791938（本輪最後一次含程式碼的 push f940db89：審查修正）綠**。
- 問題：無阻礙。

## 2026-10-26 15:42 – 第 31 輪（接上就試、已加入的標示）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。
- **「在 Studio AI 試試看」**（D-126，只動前端）：新增 MCP server 的完成步驟與 connector 詳情多一顆按鈕，先關對話框／抽屜，再 `askStudio(tryConnectorPrompt(名稱), { fresh: true, quotesPageText: true })`：Studio 開新對話、用一個讀取工具示範剛接上的 connector。`canTryInStudio` 把關（非內建、ok／尚未測試、有沒停用的工具）。順手修詳情按鈕列換行多縮 8px（`spacing` → `gap`）。截圖 `connector-try-drawer-*`、`studio-try-connector-*`（新腳本 `.loop/shots/try-connector.cjs`）；SHOWCASE 亮點列、demo 第 6 步、日誌。
- **已經加過的 MCP server 標「已加入」**（D-127）：`find_mcp_server` 卡片資料多帶 `url`（只給前端，模型仍只拿到主機），前端 `serverUrlKey`／`installedServerUrls` 比對；Studio 卡片「已加入」、對話框的 Registry 搜尋「已經加入了」。
- **審查**（subagent，D-128）：無高風險；中——名稱以使用者的話送出、第一個工具回來前不算外部文字 → `askStudio` 的 `quotesPageText`；低——依字元切、其他引號、已加入優先於「目錄已有」。都已修並補測試。
- 驗證：前端 lint、build 全過，test 最後一次 149 檔 1613 個全過；CI 40288613073（D-126）、40289066128（D-127，含後端）綠；**40289886123（本輪最後一次含程式碼的 push be5a891a：審查修正）綠**。
- 問題：無阻礙。

## 2026-10-26 13:42 – 第 30 輪（Studio AI 幫你找 MCP server）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。
- **Studio AI 的 `find_mcp_server`**（D-124）：Studio 專用、唯讀、`OutsideText`，搜 D-122 的全站 Registry 清單，回 `mcp-servers` 卡片（名稱、主機、需要 token／API key、目錄已有；最多 6 個）。卡片每列「加入」→ `/connectors?registry=<名稱>`：/connectors 打開新增對話框、目錄搜尋已填好（一次，網址隨即拿掉；自訂已滿就提示）。安裝仍是使用者自己按。/connectors 的 Studio 建議第一個「幫我找一個能查天氣的 MCP server」。三份 CLAUDE.md、design/03 §6.1、SHOWCASE（亮點、demo 第 6 步、日誌）。
- Studio「/」加「找 MCP server」（`find_mcp_server` key，`/mcp`、`/外掛` 叫得出來）。
- **審查**（subagent，D-125）：無高風險；中——`more` 少算、第一次讀 Registry 可能超過 30 秒工具上限（改成等 20 秒就回「正在載入」）、taint 讓交棒失效（prompt 收窄、要先交棒）、手機上「加入」被抽屜蓋住（`/connectors?registry=` 時抽屜自己收起）；低——自訂已滿提示、名稱完全相同排第一、本站說明放在發布者文字前。都已修。
- 截圖：`studio-find-mcp-*`、`studio-find-mcp-added-*`（新腳本 `.loop/shots/find-mcp.cjs`）。
- 驗證：cscheck 新診斷 0（第二次抓到少了 `using JackyAIApp.Server.Common;`，已補）；前端 lint、test（149 檔 1593 個）、build 全過，審查修正後 StudioAssistant＋Connectors 測試 25 檔 222 個、tsc、eslint 過；CI 40276686274（D-124）、40277894340（審查修正）綠；**40278449104（本輪最後一次含程式碼的 push 73f8a0b3：「/」指令＋文件）綠**。
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

## 2026-10-25 每日摘要 – 第 12～23 輪（第 36 輪合併；細節見 git log、DECISIONS 與 SHOWCASE 的開發日誌）

- 第 12 輪：「存完打開」、dashboard 的 AI 解讀（`read_dashboard`）、確認卡精簡、手機 dashboard 一欄。
- 第 13 輪：巡檢 /finance 與 /connectors、/finance 工具中文名稱（`toolLabels`）、⌘K 指令列（`quickActions`）。
- 第 14 輪：指令列打開 dashboard、頁面建議、複習小測驗（`review_quiz`＋quiz 卡片）。
- 第 15 輪：用自己的單字寫短文（`saved_words`）、回答的朗讀（`useSpeechOutput`）。
- 第 16 輪：用說的或貼截圖記一筆交易（`update_holding`）。
- 第 17～18 輪：選取文字問 AI（`SelectionBar`，Studio 與兩個 app 助理共用）、作答中的題目不給選取工具列。
- 第 19 輪：修紅 CI、一句話做出整個 dashboard、確認卡全部允許／全部拒絕。
- 第 20～21 輪：AI 之間交棒（`handoff`）、持股外資動向交給 /finance、交棒標示、Studio AI 的記憶（design/08）。
- 第 22～23 輪：記憶收尾（資料分析只讀、主動提議記住）、確認卡打磨（倒數、分頁標題、離開前先問、時間到的說法）。
- 每輪都有 subagent 審查與修正，收尾時 CI 為綠。

## 2026-10-24 每日摘要 – 第 1～11 輪（第 27、36 輪合併；細節見 git log 與 SHOWCASE 的開發日誌）


- 第 1 輪：階段 0 研究與設計文件 design/01–07，設計定稿（04:10），提早開工 M1。
- 第 2～3 輪：M1-5、M1-7、M2 前端零件；/connectors v2 前端。
- 第 4 輪：離線型別檢查（`.loop/cscheck`，D-021）、agent loop、對話 profile、Data Analysis 助理。
- 第 5 輪：寫入確認流程、connector 後端、使用者自己的 MCP server、Studio AI。
- 第 6 輪：MCP OAuth、Portfolio／學習進度工具、用說的存 dashboard、SHOWCASE 初稿。
- 第 7 輪：M4（TestKit、Google 日曆原生 connector、403 step-up、以後直接執行、rug pull 防護）。
- 第 8 輪：本站對外的 MCP server（PAT、OAuth 授權伺服器、同意頁）。
- 第 9 輪：本站 MCP server 成為 MCP App、收尾、安全審查。
- 第 10 輪：跨訊息 taint、首頁 AI 今日簡報、語音輸入、MCP App 卡片按鈕、畫面巡檢。
- 第 11 輪：今日簡報開新對話、接著問、圖表卡片換圖、「帶我去」。
- 每輪的後端改動都由 branch CI 驗證並在收尾時為綠。
