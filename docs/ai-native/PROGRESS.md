# PROGRESS（新的在最上面）

## 2026-10-30 11:42 – 第 59 輪（demo 腳本逐步校對：簡報之後先開新對話；手機標題列；全站截圖巡檢）

- 開工：鎖不存在；master 沒有新 commit；上一輪最後的 CI（40989760157）綠；沒有 Jacky 的新 commit。`add_repo` 仍不存在，既有 clone 切到 `feature/ai-native`（`--unshallow`）。鎖是手寫的（`xxd` 不存在，改用 `od`；格式正確，`lock.py check` 讀得懂）。
- **D-215**（NEXT (a)）：`selection.cjs` 的假串流加 `hold`，F／G 交棒在 `done` 前停住、等回答文字出現才拍，再放行等 URL 切換；`studio-handoff-phone`、`studio-holdings-handoff-phone`（原本是 D-200 前的舊首頁）與 desktop 重拍。
- **D-214**（NEXT (b)）：對照程式逐步走精華版與完整腳本，發現今日簡報一定會呼叫行事曆工具（`OutsideText`，沒連線也算），那個對話之後 `open_page` 的 `go` 不切頁、不交棒——原腳本第 2 步「帶我去複習」與精華版的交棒都接在同一個對話，現場頁面會不動。不改行為（design/06），改腳本：每步先按「新對話」，第 7 步補「也不再自動切頁或交棒」當安全示範；建議按下只放進輸入框，寫明再按 Enter；精華版的測驗改 `/考我`＋Enter。
- **D-216**（NEXT (c) 巡檢發現）：/finance 與 /data-analysis 助理在手機上的標題列只剩「台股資…」（D-147 加了匯出鈕之後）。拿掉空墊片、文字區 `flexGrow`、三顆按鈕成組且手機上間距縮小；桌面不變。
- **截圖巡檢**（NEXT (c)）：30 支腳本全跑（`mcp-host-official` 需要執行下載的套件，跳過），以 0.05% 比對、逐張看過，沒有退化；更新 41 張（手機標題列新版面，及 D-127／D-147／D-168 後未換的舊圖），加上 D-215 的 3 張。
- 驗證：前端 lint、全套測試（183 檔 1903 個）、build 全過；手機標題列以截圖確認「台股資料已連線」「全站助理」完整。CI 41002751763（f421e03a）第一次就綠。只動前端，沒跑 cscheck。

## 2026-10-30 09:42 – 第 58 輪（SHOWCASE 5 分鐘精華版、FOR_JACKY 回國順序、截圖巡檢更新 45 張、觸控螢幕的首頁說明句）

- 開工：鎖不存在；master 沒有新 commit；上一輪最後的 CI（40977142905）綠；沒有 Jacky 的新 commit。`add_repo` 仍不存在，重新 clone `feature/ai-native`（完整歷史）。
- **SHOWCASE**（NEXT 3 的準備）：開頭的「初稿」狀態列改成現況；Demo 前加「5 分鐘精華版」（五步各一個趨勢，指回完整腳本的步驟）；第 7 步「安全」補說明與兩張既有截圖（connector 詳情的權限、讀過 Jira 後的確認卡）；功能表裡重複的半句刪掉。
- **FOR_JACKY**：最上面加「回國後建議的順序」（合併前要拍板的、合併、部署後依風險實測的順序）。
- **截圖巡檢**（NEXT 2，D-213）：發現第 56 輪的 1% 門檻漏掉 D-200 改過首頁說明句後的舊圖；以 0.05% 重比 chat＋10 支＋14 支腳本，逐張看過後更新 45 張（全是現在的畫面）。新增 `.loop/shots/compare.py`。
- **D-212**：首頁說明句在觸控螢幕（`(hover: none) and (pointer: coarse)`）改說「按右下角的按鈕」，不提沒有的 ⌘K；判斷不出來時維持原句。
- 驗證：前端 lint、全套測試（183 檔 1903 個）、build 全過；新測試在 `Portal.test.tsx`（stub `matchMedia`）。CI 40989760157（0f18e50a）第一次就綠。只動前端，沒跑 cscheck。

## 2026-10-30 07:42 – 第 57 輪（MCP App：換亮暗不重畫、第二則 ui/message 回 isError；SHOWCASE 校對）

- 開工：鎖不存在（`lock.py check` FREE）；master 沒有新 commit；上一輪最後的 CI（40961309416）綠；沒有 Jacky 的新 commit。`add_repo` 仍不存在，既有 clone 切到 `feature/ai-native`（`--unshallow`）。
- **D-210**（NEXT 000(a)）：換亮暗原本以 `key={mode}` 整個重畫 app。先在 headless Chromium 實驗：`color-scheme: light dark` 不會跟著 iframe 元素走（底色變白），**動態改 `<meta name="color-scheme">` 會立即生效、底色維持透明**。所以文件以畫出時的 scheme 開始，`appSrcDoc` 在 app 之前放 `SCHEME_SCRIPT`（只認 parent 送來的 `host-context-changed`），app `initialized` 後送 `hostContextChanged(theme)`。本站目前全站深色，所以這是規格面與穩健度的修正。
- **D-211**（NEXT 000(b) 的一部分）：第二則 `ui/message`（第一則還在等讀者）原本回 `{}` 卻沒顯示 → 改回 `{ isError: true }`（`McpUiMessageResult.isError`，查過官方 API 文件）；顯示出來的仍立即回 `{}`（等讀者決定會撞上 SDK 的 60 秒逾時）。
- **SHOWCASE 校對**：所有截圖與連結都存在；migration 清單與 `git diff origin/master -- Migrations` 一致（4 個）；已知限制裡「對話紀錄按鈕包按鈕、選取工具列沒有鍵盤入口」已在 D-186／D-188 修好 → 改寫；demo 第 6 步補上 D-209（畫面外暫停、改為允許）與截圖。
- 驗證：前端 lint、全套測試（183 檔 1902 個）、build 全過；新測試 `McpAppTheme.test.tsx`（同一個 iframe、srcdoc 不變、ready 前不送）；`SCHEME_SCRIPT` 原字串在 Chromium 跑過（CSP 下換兩次亮暗都透明，錯的 method 不動）；`mcp-app-allow.cjs` 重拍與 repo 的圖差異 0。CI 40976174812（b7e2d8d5，D-210）第一次就綠；40977142905（f6c28271，D-211，本輪最後一次含程式碼的 push）第一次就綠。只動前端，沒跑 cscheck。

## 2026-10-30 05:42 – 第 56 輪（MCP App 卡片：畫面外暫停、拒絕後改為允許；截圖巡檢）

- 開工：鎖不存在（`lock.py check` FREE）；master 沒有新 commit；上一輪最後的 CI（40947180302）綠；沒有 Jacky 的新 commit。`add_repo` 仍不存在，既有 clone 切到 `feature/ai-native`。
- **D-209**（NEXT 0000 剩下的兩項）：`useOnScreen`（IntersectionObserver＋`visibilitychange`，ref 不重畫）——卡片捲出畫面或分頁藏起來時，已允許的只給 app 工具的呼叫在前端回 `PAUSED_MESSAGE`、不送伺服器，看得到後照常；被「不用」／「停止允許」拒絕的工具列在 iframe 上方，附「改為允許」（`allowAgain`；按下保護 `PRESS_GUARD_MS`，拒絕清單一變就重新起算，連按兩下「不用」不會落到它）。
- **截圖巡檢**（NEXT 2）：`chat.cjs` 43 張、`connectors`／`selection`／`search`／`memory`／`slash`／`mcp-app` 重拍，與 repo 比對：>1% 的只有 (1) `home-briefing-card-phone`（首頁副標題第 4x 輪改成 ⌘K 的說明，已更新 repo 的圖）、(2) `studio-slash-phone`（現成指令從 6 個變 9 個，預期）、(3) `studio-handoff-*`、`studio-holdings-handoff-*`（送出後 700 ms 拍的，手機上抽屜正在收起，是拍攝時機；之後的 `*-data-*` 無差異）、`select-data-*` 1.5%（字型抗鋸齒）。**沒有退化**。
- 驗證：前端 lint、全套測試（182 檔 1897 個）、build 全過；`mcp-app-allow.cjs` 多拍「停止允許 → 改為允許」（`studio-mcp-app-allow-stopped-*` 進 repo），印出改為允許後 1 行「已允許」。CI 40961309416（af66cbd3）第一次就綠。只動前端，沒跑 cscheck。

## 2026-10-30 03:42 – 第 55 輪（MCP App 的寫入要讀者在卡片上允許：讓官方 system-monitor 範例能用）

- 開工：鎖不存在；master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。`add_repo` 工具仍不存在，用既有的 clone 切到 `feature/ai-native`（`--unshallow`）。鎖這輪是手寫的（格式正確，`lock.py check` 讀得懂）；下一輪照 NEXT 用 `lock.py write`。
- **NEXT 000(c) 官方其他範例**：裝了 `server-budget-allocator`、`server-system-monitor@2.0.4`，但**執行下載的套件被權限分類器擋下（Code from External）**，改成只讀原始碼。發現 system-monitor 的 app 每 2 秒呼叫只給 app、**沒標 `readOnlyHint`** 的 `poll-system-stats`，本站一定拒絕（D-201 只准唯讀）。budget-allocator 只有一個工具，沒問題。
- **D-207**：`McpAppService.AppCallOf`（唯讀＋自動 → 跑；`AppOnly`、非破壞、沒停用 → 要 `readerAllowed`，否則 `AppCallNeedsReader` 403；其他永遠不跑）、前端 `appToolGate`（問、最多 3 個等待、允許後不再問、不用之後不再問伺服器、停止允許；不存）；app 的呼叫改用自己的 `RateLimitPolicies.McpAppCall`（120／分）。
- **審查**（subagent 唯讀，D-208）：修了 app 用 `size-changed` 把「允許」移到游標下（問題改放 iframe 上方＋600 ms 防誤按）、連按兩下允許排在後面的另一個工具（一次一個工具、`decide` 帶名字）、重畫後舊 id 回給新 app（丟掉等待中的呼叫）。
- 驗證：前端 lint、全套測試（182 檔 1893 個）、build 全過；cscheck 0 個新診斷；新截圖腳本 `.loop/shots/mcp-app-allow.cjs`（假 app：問 → 允許 → `readerAllowed=true` → 之後直接跑 → 停止允許後 app 收到拒絕；桌機＋手機，截圖 4 張進 repo）。CI 40944862751（d8bc6314，D-207）第一次就綠；40947180302（4408c6a8，D-208，本輪最後一次含程式碼的 push）第一次就綠。
- 踩到：背景跑 lint→test→build 時，等待條件看到**上一次留下的** `BUILD_EXIT`，第一次截圖用了舊的 dist（NEXT 第 35 輪就寫過）——等待時要確認 log 比 test.log 新。

## 2026-10-30 02:05 – 第 54 輪（MCP Apps 收尾：宣告能力、`ui/message`、用官方範例 app 驗證）

- 開工：鎖不存在（上一輪正常釋放）；master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。clone 是 shallow，`--unshallow` 後切到 `feature/ai-native`。
- **NEXT 000(b) 找能 demo 的 app server**（D-205）：公開免認證的 `https://mcpplaygroundonline.com/mcp-app-server`（這個環境連不到，未驗證）；**MCP 官方 ext-apps 的範例 server 都在 npm**，在 sandbox 裡跑了 `@modelcontextprotocol/server-basic-vanillajs@2.0.4`，看到它的 `tools/list` 帶 `_meta.ui`、`resources/read` 回 249 KB 的 `text/html;profile=mcp-app`。
- **NEXT 000(e) 宣告 `io.modelcontextprotocol/ui`**（D-203）：規格要 server 先看 client 能力再給 app，本站原本沒宣告。`McpAppsCapability`（handler）只改 `initialize` 的 JSON；`FakeMcpServer.AppsOnlyForUiClients` 扮照規格的 server。
- **NEXT 000(d) `ui/message`**（D-204）：官方範例 app 的「Send Message」原本 -32601 → 卡片下方先問，按「放進輸入框」才進輸入框、當外部文字、不會自己送出（`AppDraftContext`、面板的 `onAppText`）。
- **審查**（subagent 唯讀，D-206）：無高風險；修了 app 在讀者按下前換字（一次一則）、`/` 開頭的字變成現成指令（用「」包起來）、註解與序列化。
- 驗證：前端 lint、全套測試（1878 個）、build 全過，審查修正後 mcpApp 的 28 個測試與 tsc、eslint 再跑一次；**`.loop/shots/mcp-host-official.cjs` 用官方 app 的真 HTML**：官方 SDK 的 `App` 接受本站 `ui/initialize` 的回答、畫出時間、Get Server Time 經 app-call、Send Message → 放進輸入框、Open Link 要讀者按（桌機＋手機，截圖 3 張進 repo）；舊的 `mcp-host.cjs` 結果不變。cscheck 0 個新診斷。CI 40926326281（3b5c0546，D-203）、**40927424179（6c0e659f，D-204＋D-206，本輪最後一次含程式碼的 push）**都第一次就綠。

## 2026-10-29 23:50 – 第 53 輪（本站也當 MCP Apps 的 host；首頁說明句改講 Studio AI；鎖的修正）

- 開工：上一輪（10/28 11:42 開始的那輪）留下格式錯的鎖（`expires_at` 空字串、欄位錯位）且沒有任何 commit，之後約 34 小時沒有一輪做事；判定過期後接手。新增 `.loop/lock.py`（Python 標準庫，看不懂的鎖以 commit 時間判斷）。master 沒有新 commit；上一輪最後的 CI 綠；`add_repo` 工具仍不存在，用既有的 clone 切到 `feature/ai-native`。
- **首頁 hero 那句**改講 Studio AI 與 ⌘K（登入者；訪客維持原句，D-200）。
- **MCP Apps host**（D-201，design/09 新增）：使用者 MCP server 的工具帶 `_meta.ui.resourceUri` 時，回答裡畫出該 server 的 app——後端保留 `ui://`／visibility、結果交給 app（`AppResultJson`）、`mcp-app` 卡片（不含 HTML）、`GET installations/{id}/app`（`resources/read`）、`POST …/app-call`（只限唯讀且自動的工具）；前端 `cards/mcpApp`（單層 `sandbox="allow-scripts"` srcdoc iframe、開頭寫死 CSP 與 color-scheme、JSON-RPC 橋、open-link 要讀者按）。
- **審查**（subagent 唯讀，D-202）：無逃逸／注入／越權；修了伺服器快取 HTML 的記憶體風險（改成不快取、每個 connector 前 5 個 app、專屬頻率限制 60／分、body 64 KB）、app 換頁脫離 CSP（第二次 `load` 就移除）、標題用格式字元偽造說明列（去掉 `\p{Cc}\p{Cf}`、`<bdi>`）、停用的 connector、CSP 收 IP／內網名稱。
- 驗證：前端 lint、全套測試（1871 個）、build 全過；`mcp-host.cjs` 桌機＋手機：app 收到結果並畫出、`evil.example.com` 被 CSP 擋下、open-link 經讀者按下才開、`tools/call` 拿到新資料（截圖 6 張進 repo）。第一版截圖在深色主題下 app 背景是白的（iframe 與文件的 color-scheme 不同）→ srcdoc 帶同樣的 color-scheme 修好。cscheck 0 個新診斷。CI 40908396536（5ac216d）、40911186070（cb29805）、**40912954064（4ac017d，審查修正，本輪最後一次含程式碼的 push）**都第一次就綠。`mcp-host.cjs` 最後一段驗證 app 換頁後 iframe 被移除、顯示文字結果。CONTRIBUTING-CONNECTORS 加了 MCP Apps 一節。

## 2026-10-28 09:42 – 第 52 輪（之後才畫好的內容不再把對話推走、Studio 找 MCP server 知道哪些已加過）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。`add_repo` 工具仍不存在，用既有的 clone 切到 `feature/ai-native`。
- **NEXT 00(a)**（D-197）：`MessageArea` 對訊息欄掛 `ResizeObserver`：內容長高但訊息數沒變時，跟著最後的讀者留在最後、從搜尋打開的那則在 `REVEAL_MS` 內留在原位；讀者在對話裡捲動／按下／打字就停（新訊息再開始）。第一版沒有「按下就停」，截圖比對發現測驗卡作答後被捲到最後，補上後截圖回到原樣。
- **NEXT 1 的「模型不知道哪些 server 已加過」**（D-198）：`find_mcp_server` 的文字標出使用者已加入的 server（`IConnectorToolSource.ServerUrlsAsync`、`McpServerUrl.Key`），不再叫他重加。
- **審查**（subagent 唯讀，D-199）：無高／中風險；修了按過確認卡後、回答結束才畫好的部分不跟著到最後（`busy` 結束時重新開始跟）。
- 驗證：前端 lint、全套測試（177 檔 1850 個）、build 全過；`chat.cjs`＋`search.cjs` 重拍 45 張與 repo 比對無 >1% 差異；cscheck 只有已知雜訊（CS1674）。CI 40618636944（D-197）、40618957348（D-198）第一次就綠；40620169850（D-199，4a32c593，本輪最後一次含程式碼的 push）第一次就綠。

## 2026-10-28 07:42 – 第 51 輪（D-193 審查收尾、第十一次巡檢：dashboard 分享與 /admin/listening、IPv6 /64）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠。`add_repo` 工具仍不存在，用既有的 clone 切到 `feature/ai-native`。
- **D-193 審查**（subagent 唯讀）：六個情境都沒有真的錯；修了同一個結果亮著時再按會提早熄、換到訊息數一樣多的對話停在原位（D-194，`MessageArea` 的 `lit`／`unlight` 與 `conversationId`；兩個新測試在舊程式上會失敗）。
- **手機寬與 Studio 抽屜的內容搜尋截圖**：`search.cjs` 加兩個情境，抽屜關掉後仍捲到那則並亮（`history-content-search-*-mobile/studio.png`）。`chat.cjs` 重拍 41 張與 repo 比對無退化。
- **第十一次巡檢**（subagent 唯讀，dashboard 分享、/admin/listening）：無高／中風險；修了匿名看 Anyone dashboard 拿到 widget 整份 plan（只給 `viewBy` 的輸出名稱與種類）、widget 大小與數量上限、題庫分頁夾值（D-195）；匿名限制以 IPv6 /64 計（D-196）。
- 試過把捲動 effect 改成 `useLayoutEffect`（免得閃一格），截圖比對有兩張停在離最後 25px，退回（記在 D-194 與 NEXT 00）。
- 驗證：前端 lint、全套測試（177 檔 1844 個）、build 全過；cscheck 0 個新診斷；CI 40605009822（67c8154f）、40606343504（D-195）、40607812443（D-196，本輪最後一次含程式碼的 push）都第一次就綠（中間一次被後一次 push 取消）。

## 2026-10-28 05:43 – 第 50 輪（對話紀錄搜尋內容、打開後捲到那則訊息）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。`add_repo` 工具仍不存在，用既有的 clone 切到 `feature/ai-native`（`--unshallow`）。
- **NEXT 1 的選做「搜尋訊息內容」**（D-191）：`GET api/chatbot/conversations/search`（`ChatConversationService.SearchAsync`：自己的、該助理的、一則訊息含全部字，最多 20 則、附片段；字 NFKC、最多 5 個、超過 100 字 400；`RateLimitPolicies.ChatSearch`），前端停止打字 350 ms 後查、找到的對話依原順序插入、片段在標題下方並標出搜尋的字（`findConversations`、`highlightParts`）。不需要 migration、不花 AI。
- **審查**（subagent 唯讀）：無高風險；修了標題已找到的佔名額、5 DTU 成本（只搜最近 300 個對話、20 次／分）、舊搜尋的失敗、emoji 在 collation 下符合全部、「C#」找不到位置、片段變成按鈕名稱（D-192）。自己截圖發現片段有「…」時搜尋的字標不出來（NFKC 把 … 變三個字元）→ 逐字折疊。
- **打開內容找到的對話捲到那則訊息並亮一下**（D-193）：結果多帶 `messageId`，`MessageArea` 的 `reveal`（以欄的第 n 個子元素找，不動 `MessageItem`，memo 不受影響）。
- 驗證：前端 lint、全套測試（176 檔 1841 個）、build 全過；cscheck 0 個新診斷（`DbSet.Single` 是已知雜訊）；截圖 `.loop/shots/search.cjs`（搜尋中、內容找到、打開後捲到、只標題），`rename.cjs` 重拍與 repo 比對差異 0.03%（列高不變）。CI 40590848034（05420a95）、40592243852（02eec8f6）、40593209177（a33de762）、**40594358867（8682b3ed，本輪最後一次含程式碼的 push）都第一次就綠**。

## 2026-10-28 03:42 – 第 49 輪（無障礙收尾：對話紀錄的列、選取工具列的鍵盤入口；第十次巡檢：本站 OAuth 授權伺服器）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。`add_repo` 工具仍不存在，用既有的 clone 切到 `feature/ai-native`（完整歷史）。
- **NEXT 0.5(a)**：對話紀錄的列不再是按鈕包按鈕（`ConversationRow`：`li` 裡開啟按鈕＋旁邊的改名／刪除，版面與列高不變，D-186）；鍵盤刪除後焦點到補上來的那一列／上一列／「新對話」（`utils/focusAfterDelete`，D-187）。
- **NEXT 0.5(b)**：選取工具列用 **Alt+A** 進入（以 `code` 判斷）、左右鍵／Home／End 移動、Escape 回原處、一個 Tab 位置，選取停下時隱藏的 status 說出快捷鍵（`selectionShortcut`，D-188）。
- **審查**（subagent）：兩個高風險都在抽屜裡、jsdom 測不出來——刪除鈕停用後抽屜的焦點陷阱把焦點放到 paper，被當成「已離開」；Studio 的工具列在 Modal 外，Alt+A 的焦點被陷阱搶回 → 包住對話欄的元素也算自己的、抽屜開著時工具列掛在抽屜裡；另修再按 Alt+A、選完動作焦點回原處、roving tabindex、觸控的 hover（D-189）。兩個新測試確認在修正前會失敗。
- **第十次巡檢**（subagent 唯讀：`SiteOAuthServer` 與同意頁）：無高、中風險。四個低風險都修（D-190）：過期 token 仍算進 10 個上限（用久了 OAuth 登入一定被拒）、同意頁加 `X-Frame-Options`／`frame-ancestors`（`FrameGuardMiddleware`，只限 `/oauth/authorize`）、名稱去掉 Format 字元與主機用 `IdnHost`（仿冒網域顯示成 punycode）、同意的 POST 每人每分鐘 20 次。
- 驗證：前端 lint、全套測試（1833 個）、build 全過；`rename.cjs` 改用 `li` 找列並多拍 `rename-focus`，與 repo 比對差異 0.03%（第一次列高少 2px、hover 列不亮，已修回）；`selection.cjs` 重拍，Studio 抽屜裡的選取無差異；cscheck 0 個新診斷。CI 40573957756（2a69872c）綠；**40576006479（278f4fed）紅**：抽屜刪除測試檢查了中間狀態（CI 上回合在 act 內就完成了）→ 拿掉那行；**40577752025（44eda73b，含 D-190 後端，本輪最後一次含程式碼的 push）綠**。
- 問題：MUI 焦點陷阱在 jsdom 不會自己動（`document.hasFocus()` 是 false），抽屜相關的焦點要在測試裡自己模擬。

## 2026-10-28 02:35 – 第 48 輪（第八次巡檢：共用 chat 引擎的穩健度；第九次巡檢：新 AI 介面的無障礙）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。`add_repo` 工具仍不存在，直接 clone（完整歷史）。
- **第八次巡檢**（subagent 唯讀：agent loop、取消、半途失敗存了什麼、並行、SSE、/finance 不退化）：無高風險。已修：中——「停下來改問這個」時新訊息與停止的請求賽跑，被停的回答存在新問題之後（歷史順序亂、之後每輪兩則連續 assistant）→ `ChatTurnRegistry.SettleAsync`，新訊息與編輯先停掉並等同一對話的回合存好（最多 5 秒，D-177）；低～中——工具輪數用完時前面那句「我來查一下」被當成回答 → `no_answer`（D-178）；低——停在確認卡上的寫入 chip 打勾 → 有 id 沒結果就是失敗（D-179）；低——只有空白的訊息被收下 → 扣額度前 400（D-180）。
- **第九次巡檢**（subagent 唯讀：鍵盤與螢幕閱讀器）：高——整個 chat 沒有 live region（回答完成、失敗、**確認卡在等你**都聽不到）→ `useChatAnnouncer`（只說狀態改變，D-181）；高——收合 inline 助理焦點掉到 body、Tab 又回輸入框再展開 → 焦點回「開啟」按鈕、Escape 收合（D-182）；高——測驗作答與換題焦點掉 → 跟著題目走、結果放在常駐的 live region（D-183）；中——決定確認卡後焦點掉（D-184）；中／低——現成指令 chip 每個鍵重念、工具 chip 狀態只在 `aria-label`、思考點與進度條沒有名稱（D-185）。未修的寫進 NEXT 0.5。
- 驗證：cscheck 0 個新診斷；前端 lint、全套測試（175 檔 1820 個）、build 全過；`chat.cjs` 重拍與 repo 比對無 >1% 差異（測驗卡已作答的畫面看過：「下一題」有焦點框）；CI 40555167455（1ab31532，後端）綠、**40559188194（6fa95a0a，本輪最後一次含程式碼的 push）綠**。
- 問題：`git pull` 一次卡住 2 分鐘（改用 `GIT_TERMINAL_PROMPT=0 timeout`）；`pkill -f vitest` 又殺掉了自己的 shell（NEXT 早有警告）。

## 2026-10-28 00:35 – 第 47 輪（全站錯誤提示收尾、第七次巡檢：TTS、帳號連線）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠。`add_repo` 工具仍不存在，用既有的 clone 切到 `feature/ai-native`。
- **全站 `console.error` 收尾**（D-172）：麥克風打不開時說原因、對話練習開始失敗時情境還在可以再選、AI 訊息語音播不出來時說（自動播放被擋不算）、登出被拒時選單留著並說。新測試 `useAudioPlayback.test.ts`、`UserProfileMenu.test.tsx`，`useVoiceRecording`／`ConversationTestCard` 各加案例。
- **第七次巡檢**（subagent 唯讀，D-175）：登入登出、帳號連線、匿名端點。修了高風險的 **TTS**（匿名、沒有頻率限制、結果存進沒上限的快取：`TtsCache` 自己的 128 MB `MemoryCache`＋每位址每分鐘 30 次，D-173）與兩個低風險（帳號連線 refresh 一次一個、callback 的 provider 先正規化，D-174）。匿名查字典無限產生新字是產品決定，交給 Jacky（FOR_JACKY 18）。
- **字典的發音播不出來時改用瀏覽器的聲音**（D-176）：被頻率限制或額度擋下時，按「播放」仍聽得到。
- 驗證：前端 lint、全套測試（172 檔 1803 個）、build 全過；截圖 `.loop/shots/practice-errors.cjs` → `practice-start-failed`、`practice-playback-failed`（無頭瀏覽器沒有麥克風，順便拍到麥克風提示）；cscheck 0 個新診斷；CI 40538697557（D-172）、40539824667（D-173）、40540915064（D-174）綠；40542669764（D-176，本輪最後一次含程式碼的 push）綠。

## 2026-10-27 22:55 – 第 46 輪（表格貼進文件、巡檢低風險收尾、送出失敗的提示）

- 開工：master 沒有新 commit；上一輪最後的 CI 綠；沒有 Jacky 的新 commit。`add_repo` 工具仍不存在，clone 是 shallow → `git fetch --unshallow`。
- **表格卡片「複製成文件表格」**（D-168）：HTML＋TSV 一起放剪貼簿，貼到 Word／Gmail／Google 文件是有表頭的表格；另一顆按鈕，原本的「複製表格」不變（試算表會優先讀 HTML）。每個值跳脫、文字格標 Excel 文字格式。截圖 `table-doc-*`（手機 390 寬不擠），`table-export-*` 重拍。
- **D-165 留下的低風險都收掉**：對話練習送出失敗時拿掉那一回合、訊息放回輸入框並說原因，總結失敗也說（D-170）；持倉編輯器與 CSV 匯入照伺服器上限逐列標出、超過 300 筆說要刪幾筆、預覽表加「列」（D-167）；Yahoo chart 代號一律 escape（`YahooChart.SymbolUrl`，D-169：查證 `MarketOverviewProvider` 早已 escape `^`／`=` 而 Yahoo 接受，推翻 D-165 的「不修」）。
- **送出失敗的提示**（D-171，master 既有）：每日挑戰、複習、聽寫原本只有 `console.error`；三頁原本沒有頁面測試，各補一個。
- **審查**（subagent）：無中高風險；兩個低風險已修（等回覆時新打的字在失敗時被蓋掉→保留並說「剛才那句沒有送出」；匯入遠超上限的檔案會畫出幾千列→略過的列最多列 50 筆）。
- 驗證：cscheck 0 個新診斷；前端 lint、全套測試（170 檔 1790 個，之後再加 3 個）、build 全過；CI 40519650380（55d53d1b）、40520090147（Yahoo）、40521219992（D-168）、40521792611、40523119638 都綠，**40524625491（審查修正，本輪最後一次含程式碼的 push）綠**。

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

## 2026-10-26 每日摘要 – 第 24～35 輪（第 53 輪合併；細節見 git log、DECISIONS 與 SHOWCASE 的開發日誌）

- 第 24～26 輪：本站 MCP server 的現成指令（MCP prompts）、Studio 與 app 助理的「/」現成指令（跟著頁面走、Enter 選第一個），CI 的 .NET SDK 暫釘後改回 `10.0.x`（D-110～D-115）。
- 第 27～28 輪：找出我的弱點（`weak_spots`）、`review_quiz` 指定字；語音對話（用說的問、自動朗讀、今日簡報「用聽的」）（D-116～D-121）。
- 第 29～31 輪：/connectors 搜尋 MCP 官方目錄、Studio AI 幫你找 MCP server、接上就「在 Studio AI 試試看」、已加入的標示（D-122～D-128）。
- 第 32～33 輪：Jira 原生 connector（讀票、留言）、Outlook 行事曆（D-130～D-135）。
- 第 34～35 輪：思考摘要、長對話不卡（memo）、失敗的回答「重試」（D-136～D-140）。CI 每輪都綠。

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
