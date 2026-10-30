# PROGRESS（新的在最上面）

## 2026-10-31 00:30 – 第 65 輪（合併前最後一輪安全審查：六份 subagent 審查，修 D-237～D-242）

- 開工：鎖不存在；master 沒有新 commit；上一輪最後的 CI（41077408185）綠；沒有 Jacky 的新 commit。`add_repo` 仍不存在，既有 clone 切到 `feature/ai-native`（`--unshallow`）。鎖用 `.loop/lock.py`。
- 照 NEXT 的建議「審查最近的 commit」，六個 general-purpose subagent 各看一塊（不帶我的結論）：MCP app-call（D-201／202／207）、connector 認證與限流（D-223／224／196）、MCP Apps host 前端、本站 MCP server＋OAuth、AI 寫入工具、確認卡與 taint。**都沒有高風險**；修了：
  - **D-237**（中，安全）：`CachedTools.Changed` 沒比 `AppOnly`／`NotForApp`——只給 app 的寫入工具設成自動後，server 改成也給模型，就變成模型不經確認能呼叫。
  - **D-238**：加密 key 換過時「更新認證」對 API key 是死路 → 表單選填 header、伺服器讀不到時要求填（FOR_JACKY 21 已處理）。
  - **D-239**：MCP App 卡片同時最多 4 個呼叫；按鍵防護的時間改在 `useLayoutEffect` 設。
  - **D-240**：site token 記錄使用時間時剛被撤銷就不放行。
  - **D-241**（中，安全）：記憶筆記去掉控制／格式／私用字元（tag block 能讓確認卡只顯示無害句子）；`update_holding` 用 `quantity` 時卡片也說成交價、數字上限一兆；`save_to_dashboard` 建立前先檢查標題、失敗回服務的原因。
  - **D-242**：同一個回覆裡外部讀取之後的寫入也問；`OutsideText` 結果擋自己的換頁；`ModelTurn` 例外時 taint 仍存（finally）。design/06 §8 補「分類是 server 自己說的」。
- 驗證：每次後端改動 cscheck 0 個新診斷；前端 lint／test（186 檔、1945 個）／build 全過；CI 41085554392（5177a8b0）、41087195865（df63afc8）、41088075161（67d5ce57）都第一次就綠，1f2feddf 見下一則或 NEXT §1。
- 接受不改（寫在各 D-xxx）：app 換頁後 `load` 前仍能送訊息、`readerAllowed` 不綁參數（D-208）、錯 token 不限流、token 上限的競態、新 key 存好後舊呼叫的 401、`add_word_to_repository` 卡片說變化形。

## 2026-10-30 23:08 – 第 64 輪（design/08、09 對照完成；審查最近的高風險 commit：工具輪收費的例外、MCP App 連結、connector 名稱）

- 開工：鎖不存在；master 沒有新 commit；上一輪最後的 CI（41054734621）綠；沒有 Jacky 的新 commit（有一個不是迴圈的 branch `claude/nice-allen-szv1lq`，不在範圍內，沒碰）。`add_repo` 仍不存在，既有 clone 切到 `feature/ai-native`（`--unshallow`）。鎖用 `openssl rand`。
- 兩個 Explore subagent 分別對 design/08（記憶）、design/09（MCP Apps host）：
  - **D-232**（中）：MCP App 的 `ui/open-link` 每次都換掉畫面上的網址、「開啟」沒有按壓防護——app 能在讀者伸手時換網址或移動按鈕。現在一次一個（再來的回 `{ isError: true }`），「開啟」與「放進輸入框」在提議出現或 app 改高度後 600 ms 內的按不算。
  - **D-233**：其餘差異都是文件過時或實作更嚴，兩份 design 照實作改寫。
- 三個 general-purpose subagent 審查最近的 commit（不帶我的結論）：
  - **D-234**（中，含安全）：D-226 的 `chargeToolRound` 丟例外會讓整個回合中斷（沒有 done、卡片遺失、**taint 沒記下**）→ `ModelTurn.PaidForAsync` 接住當作額度不夠；工具跑完時已停止就不再收下一輪。
  - **D-235**：D-224（更新認證）、D-228（64 個工具）沒問題；加密 key 換過後的死路寫進 FOR_JACKY 21。
  - **D-236**：D-223 沒問題；connector 名稱（server 自己取的）去掉看不見的字元。d8bc6314（app-call）、05420a95（內容搜尋）沒有 bug，三點接受。
- 驗證：前端 lint、全套測試（186 檔 1943 個）、build 全過；`mcp-host.cjs` 截圖腳本跑過（按「開啟」前等 700 ms 後照常開啟）；cscheck 兩次 0 個新診斷；CI 41075118078（f4ff4407，D-232）、41075940650（22a9eb74，D-234）、41077408185（661dbc20，D-236）都第一次就綠。

## 2026-10-30 19:42 – 第 63 輪（對照 design/06、04、03 補齊落差：工具輪收費、看不見的字元、64 個工具上限、手動 OAuth 的 Redirect URI）

- 開工：鎖不存在；master 沒有新 commit；上一輪最後的 CI（41041520459）綠；沒有 Jacky 的新 commit。`add_repo` 仍不存在，既有 clone 切到 `feature/ai-native`。鎖用 `od` 產生 run_id。
- 照第 61、62 輪的作法，請 Explore subagent 逐條對 design/06（安全）、design/04（共用 chat）、design/03（connector 架構），只修中風險或小落差：
  - **D-226**（中）：design/06 §6 的 `ChatToolRound` 從沒實作，一則訊息 5 單位能跑 8 次請求。現在第 4 次起每次請求前先收 1 單位；額度不夠就送最後一次請求（`tool_choice: none`＋`OutOfBudgetNote`），用已讀到的結果回答。/finance 不受影響。
  - **D-227**：MCP 工具的標題與說明去掉控制字元與格式字元（含 Unicode tag 區塊，`McpToolClient.Plain`）；截斷不切在 surrogate pair 中間。
  - **D-228**（中）：每輪最多 64 個 connector 工具（原本最多約 1,000 個）。`SafeHttpTests` 補 AAAA 指到內網的案例。
  - **D-231**（demo 卡關點）：授權伺服器要手動建 OAuth app 時，新增對話框顯示要填的 Redirect URI（可複製）。
  - 其餘差異記錄接受：D-229（design/06）、D-230（design/04）、D-231 內（design/03；工具清單不會自己每小時重讀，寫進 FOR_JACKY 20）。
- 驗證：cscheck 每次都 0 個新診斷；前端 lint、全套測試（1939 個）、build 全過；CI 41051468980（33027395，D-226／D-227）、41052059373（f0671913，D-228）第一次就綠；41054734621（23aa89a7，D-231）第一次就綠。

## 2026-10-30 17:42 – 第 62 輪（授權過期時一鍵回到重新授權：失敗呼叫的「前往重新連線」卡片、API key／token 直接更新認證、原生 connector 卡片捲到帳號連線）

- 開工：鎖不存在；master 沒有新 commit；上一輪最後的 CI（41028152017）綠；沒有 Jacky 的新 commit。`add_repo` 仍不存在，既有 clone 切到 `feature/ai-native`（`--unshallow`）。鎖用 `openssl rand` 產生 run_id。
- **D-223**（NEXT 第 61 輪的 (c)）：MCP connector 被拒絕認證時，失敗的工具呼叫附一張「重新連線「X」」連結卡（`ConnectorToolSource.ReconnectCard`，`/connectors?connector=<id>`）。規則改成失敗的呼叫**只能帶本站路徑的連結卡**（伺服器 `ClientToolRunner.WayForwardOf`＋`ToolCardGuard.IsSiteLink`，前端 `wayForwardCard`）。順帶發現：Jira 的 `ConnectCard` 與 `update_holding` 失敗時的卡片原本寫了卻一直被「失敗不送卡片」吃掉，現在會出現。/connectors 讀 `?connector=` 打開該 connector 的詳情一次（`connectorToOpen`），Studio 抽屜讓開；等待授權時「重新授權」是主要按鈕。
- **D-224**：API key／bearer token 的 connector 原本只能移除重加 → `PUT installations/{id}/credentials`（`UpdateCredentialsAsync`，ConnectorProbe 頻率限制）＋詳情抽屜「更新認證」→ `CredentialsForm`「儲存並測試」。
- **D-225**：原生 connector（Google 日曆、Outlook、Jira）的「先連線」卡片改連 `/connectors#accounts`（`ConnectorProviders.AccountsPath`），頁面讀到後捲到帳號連線一次（`ACCOUNTS_ANCHOR`），Studio 抽屜讓開。
- 截圖：新腳本 `.loop/shots/reconnect.cjs`（Studio 的失敗卡片 → 按下去 → /connectors 詳情；bearer 的更新認證），6 張新截圖放進 SHOWCASE 第 6 步。
- 驗證：cscheck 兩次都 0 個新診斷；前端 lint、全套測試（186 檔 1934 個）、build 全過；CI 41037377396（c360d50d，D-223 後端＋前端）、41037911711（c3fbe3b0）第一次就綠，41039615668（e1c04e29，D-224）綠；41041520459（ad67e606，D-225）第一次就綠。

## 2026-10-30 15:43 – 第 61 輪（對照 design/05 補齊落差：訪客的 Studio AI、工具耗時、標題列的頁面、我能用的工具、connector 的最後測試時間、抽屜拉寬）

- 開工：鎖不存在；master 沒有新 commit（master 是 branch 的祖先，可乾淨合併）；上一輪最後的 CI（41002751763）綠；沒有 Jacky 的新 commit。`add_repo` 仍不存在，既有 clone 切到 `feature/ai-native`（`--unshallow`）。鎖用 python 的 `secrets` 產生 run_id。
- 功能已收手，這輪先找「設計說了、實作沒做」的落差：發現 design/05 §1 寫訪客按下按鈕要看到「登入後可使用」，實作卻整個不顯示 → **D-217**（`StudioGuest`、`StudioFab`）。再請 Explore subagent 逐條對 design/05，找到 6 個落差，做了其中 4 個：**D-218**（工具 chip 耗時，伺服器早就送 `durationMs`）、**D-219**（標題列「全站助理・在「複習」」，`studioPageLabel`）、**D-220**（歡迎畫面「我能用的工具」，`AiChatConfig.welcomeFooter`＋`StudioToolsRow`＋`usableConnectors`）、**D-221**（/connectors 卡片的最後測試時間，`lastTestedText`）、**D-222**（抽屜左緣拖曳拉寬 440～640，`DrawerResizeHandle`＋`useDrawerWidth`）。
- 截圖巡檢 `chat.cjs`、`connectors.cjs` 時抓到：dashboard 清單讀到 `data: null` 時 D-219 的新程式讓 dashboard 頁空白（預設參數不接 null）→ 改傳 `boards ?? []`（D-219 內）。只有假資料會這樣，但已修。更新 27＋5 張截圖，新增 `studio-guest-*`、`studio-welcome-tools-*`；新腳本 `.loop/shots/guest.cjs`、`welcome-tools.cjs`。
- 驗證：前端 lint、全套測試（185 檔 1920 個）、build 全過；CI 41023032098（5ba724cc，D-217）、41025166406（033fe94a，D-218／D-219）、41026238162（699d7f55，D-220）、41027176896（6f91e990，D-221）都第一次就綠；41028152017（9258d442，D-222）綠。新腳本 `.loop/shots/wide.cjs`。只動前端，沒跑 cscheck。

## 2026-10-30 13:42 – 第 60 輪（SHOWCASE 第 7 步的安全截圖；精華版第 4 步一支腳本走完）

- 開工：鎖不存在；master 沒有新 commit；上一輪最後的 CI（41002751763）綠；沒有 Jacky 的新 commit。`add_repo` 仍不存在，既有 clone 切到 `feature/ai-native`（`--unshallow`）。鎖用 `od` 產生 run_id（`xxd` 不存在）。
- **NEXT (1)**：新增 `.loop/shots/tainted-go.cjs`：首頁「AI 今日簡報」→ 同一個對話「帶我去複習」，第二次請求照 `ClientToolRunner.NavigateOf` 的行為不帶 `navigate`（對話已 tainted），回答照 `NotMovedNote` 請讀者自己按。桌機與手機都確認網址仍是 `/`、連結卡在；`studio-tainted-go-desktop` 放進 SHOWCASE 第 7 步。
- **NEXT (2)**：新增 `.loop/shots/mcp-flow.cjs`，把 `find-mcp.cjs`＋`try-connector.cjs` 合成一條連續流程（建議 → 卡片「加入」→ 對話框「使用 Weather」→ probe →「連線」→ 完成步驟的「在 Studio AI 試試看」→ 新對話用 Weather 回答），十張截圖逐張看過、畫面與精華版第 4 步的描述一致；兩張放進第 6 步。第一次跑頁面空白，原因是假資料的 `category`／`health` 不在型別裡，修假資料即可。
- 順手確認 NEXT 0 的 `MaskMsSql` 已修（`MaskPassword`），從清單刪除。
- 驗證：沒有改程式碼（只有 `.loop/` 與文件，全部 `[skip ci]`），所以沒有新的 CI；最後一次含程式碼的 CI 41002751763 綠。

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

## 2026-10-27 每日摘要 – 第 36～46 輪（第 64 輪合併；細節見 git log、DECISIONS 與 SHOWCASE 的開發日誌）

- 第 36～40 輪（功能）：AI 替對話取標題、搜尋對話標題（D-141～D-143）；手動改名（D-144～D-146）；匯出對話成 Markdown（D-147～D-149）；表格卡片複製到試算表／下載 CSV（防公式注入）、按欄名排序（D-150～D-152）、一鍵畫成圖（D-153、D-154）。中途截圖巡檢沒有退化。
- 第 41～46 輪（合併前巡檢一到六）：MCP、上傳檔案與 chat 附件、巡檢未修項目、dashboard 與資料分析的 AI 工具、自訂資料源（D-163、D-164）、Job Finder、Portfolio、English 的 AI 批改（D-155～D-171）；表格貼進文件（D-168）、送出失敗的提示。
- CI：每輪最後一次含程式碼的 push 都綠；紅過的都是新測試自己的設定（第 43、44 輪，見 NEXT §1）。

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
