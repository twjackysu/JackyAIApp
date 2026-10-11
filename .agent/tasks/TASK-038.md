# TASK-038：回落煞車 — 後端盤中預估訊號（MIS）與「訊號已更正」（#431，第二階段）

## Owner
Backend Engineer

## Status
PR_OPENED（2026-10-11 Backend Engineer；待 Tech Lead 技術審查。TASK-041／040 已於 #438／#441 合併，據此接手。**依 Boss 指示，MIS 改經 TWStockLib 2.2.1 讀取**——見 Result 與 PR 區）

## Priority
MEDIUM（PM：第二階段 MEDIUM；Boss 規格將盤中預估列為「建議做法」，回測主結果假設收盤集合競價成交，「隔日成交」版本績效明顯較差）

## Objective
在 `GET /api/portfolio/drawdown-brake` 的 `estimate` 欄位提供盤中預估訊號，讓使用者可在 13:25–13:30 收盤集合競價前下單；收盤正式資料入庫後若狀態與先前預估不同，回報 `estimateCorrected=true`。

## Requirements
1. **先做格式驗證（#431 §5 要求，PM 亦列為 Tech Lead Action）**：`https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch=tse_t00.tw`。確認欄位（`z` 現價、`y` 昨收等）、盤中／盤後 `z` 缺值行為、是否需先取得 cookie。先查 TWStockLib 的 `GetLatestQuoteAsync` 能否直接取得指數；不行再自建 client（走 `ExternalReadOnly`）。以 LiveContract 測試固定格式（只驗 shape）。
2. **預估公式**（#431 §5）：`預估報酬指數 = 昨日正式報酬指數 × (目前加權指數 z ÷ 昨收 y)`；以 `DrawdownBrakeCalculator` 對「正式序列＋預估點」重算，輸出 `estimate = { asOfTime, estimatedClose, state, prevState(=正式 asOfDate 的狀態), drawdownPct }`。`z` 缺值 → `estimate=null`（不猜）。
3. 僅在「平日、台北 09:00 之後、今日正式資料尚未入庫」時計算；今日正式資料入庫後 `estimate=null`。
4. 結果短快取（≈30 秒）、全站共用一份，不隨使用者數放大對 MIS 的請求。
5. **接上重新整理（需求 8，2026-10-11 加入）**：TASK-041 的 `POST …/drawdown-brake/refresh` 在第 3 點的時間窗內同時要求 MIS 預估重新取得——MIS 預估快取（第 4 點的 ≈30 秒）以**全站最短間隔 10 秒**為限（遠高於證交所的 ≥ 2 秒規則），間隔內回現有預估，不打 MIS。`estimate.asOfTime` 就是前端顯示的「預估資料時間」。`result` 列舉不新增值：預估取不到時 `estimate=null`，其餘照 041 的判定。MIS 失敗**不得**讓 `result` 變成 `SourceUnavailable`（那表示正式資料來源失敗）。DTO 快取（60 秒）在預估更新時一併失效。
6. **訊號已更正**：`IMemoryCache`（TTL 2 天）記當日最後一次預估狀態；正式資料入庫後若與之不同 → `estimateCorrected=true`（持續到隔日）。快取遺失時退化為 false（可接受）。

## Acceptance Criteria
- Offline unit tests：預估公式、`z` 缺值、時間窗（週末／09:00 前／正式資料已入庫皆為 null）、快取（同窗內只打一次上游）、更正邏輯（預估 OFFENSE → 正式 DEFENSE ⇒ true；一致 ⇒ false；快取遺失 ⇒ false）。
- LiveContract 測試加入 MIS 指數端點 shape；PR 附實際欄位紀錄；更新根 `CLAUDE.md` Testing Status 來源清單。
- 不新增 DB 表／migration；無 AI；無 `[UsageLimit]`。
- 前端 TASK-037 已能渲染 `estimate`／`estimateCorrected`，**本任務不改前端**。

## Technical Notes
- 預估以價格指數漲跌幅近似報酬指數，除息旺季（約 6–8 月）略低估；訊號貼近門檻時可能與正式值不同 → 由「訊號已更正」承接，**不另做修正**（見 `ARCHITECTURE.md` Trade-offs）。
- MIS 為非公開文件化介面，格式可能變動；失敗一律退化為 `estimate=null`，不得影響正式訊號。
- 沙箱可能連不到 `mis.twse.com.tw`（Tech Lead 實測 403）；格式驗證依 GitHub Actions LiveContract。

## Dependencies
TASK-036 MERGED（#433）。**TASK-041**（手動重新整理端點、DTO 的 `lastCheckedAt`／`generatedAt`、60 秒訊號快取）須先合併。站外提醒（Email／推播）已由 Boss 決定不做（Q2，DECISION-014），**不在本任務範圍**。

## PR
- 原版 #443 — https://github.com/twjackysu/JackyAIApp/pull/443（分支 `claude/exciting-cray-kozet7`；自建 MIS client／parser）
- **現行版（TWStockLib）**：#445 — https://github.com/twjackysu/JackyAIApp/pull/445 ——分支 `claude/exciting-cray-w4l3w6`，以 #443 的全部提交為基底，再加「改經 TWStockLib」的提交與對 master 的 merge；合併它即等於合併 #443。
- 相依（已完成）：TWStockLib「略過快取」參數 https://github.com/twjackysu/TWStockLib/pull/12 ——Boss 已合併，NuGet **2.2.1** 已發佈。

## Result
- **新增**（`Services/Portfolio/DrawdownBrake/`）：`MisTaiexClient`（經 **TWStockLib 2.2.1**：`GetRealtimeQuoteAsync("t00", MarketType.TSE, bypassCache: true)`；每次呼叫自開 scope；5 秒上限；代號須為 `t00`、單日漲跌 > ±15% 視為壞資料；`MisIndexQuote` 是驗證過的值，一律「無讀數」而非例外）、`MisTaiexQuoteSource`（singleton；全站一份讀數，檢視 ≤ 30 秒、重新整理 ≤ 10 秒；單飛；失敗也保存）、`DrawdownBrakeEstimator`（純函式：昨日正式收盤 × z÷y，再交給既有 `DrawdownBrakeCalculator`）。
- **`DrawdownBrakeService`**：預估疊加在正式訊號（60 秒快取不動）之上，依「(正式快取項目, MIS 讀數)」各自快取；時間窗＝平日、台北 09:00 起、今日正式資料未入庫；基準日檢查（已存最新日與今天之間的每個平日必須是已知休市日，否則不給預估）；`estimateCorrected` 由 `IMemoryCache`（TTL 2 天）記錄當日最後一次預估狀態，正式入庫後比對。`IDrawdownBrakeService.GetAfterEstimateRefreshAsync` 供 `POST …/refresh` 使用；`result` 完全由正式資料決定（MIS 失敗不變成 `SourceUnavailable`，列舉未新增值）。
- **MIS 經 TWStockLib 讀取（Boss 指示：不自己造輪子）**。原版（#443）曾自建 client／parser，理由：(a) 任務書指的 `GetLatestQuoteAsync` 是 app 的包裝，會先查上市櫃清單、失敗改打 `otc_t00.tw`、共用個股的 10 秒快取；(b) 需要 `d`／`t` 驗證是今天的讀數；(c) 無法在沙箱驗證 lib 的 parser 吃不吃得下 `t00`；以及一個**真正擋住**的原因——**lib 內建固定 30 秒報價快取、無法繞過**，與需求 5「手動重新整理最短 10 秒」衝突。處理：(1) lib 加通用的 `bypassCache` 參數（twstocklib#12，已合併，2.2.1）；(2) 直接用 lib 的 `IStockMarketService`（不經 app 的包裝）；(3) 時間改取 `tlong`（lib 的 `SyncTime`，UTC 值、Kind 未指定）→ 台北日期即 `TradeDate`，與實測的 `d`／`t` 吻合；(4) 以 Actions 實測記錄的**真實欄位集**餵 lib 的 parser，並有端到端測試（真的 lib parser＋快取＋假 fetcher）證明每次呼叫都真的送到 MIS。刪除 `MisTaiexParser`。`IMisTaiexClient`／`MisIndexQuote` 保留為薄的防腐層（lib 的 `StockQuote` 全欄位可為 null、帶大量個股欄位，且 `IStockMarketService` 是 scoped）。
- **DTO**：`DrawdownBrakeDto.WithEstimate`（`MemberwiseClone`，共用複本不被改動）；`DrawdownBrakeEstimateDto`、`Estimate`／`EstimateCorrected` 補文件註解。`CacheTtl.Portfolio` 新增 `DrawdownBrakeEstimateQuote`（30 秒）、`DrawdownBrakeEstimateRefresh`（10 秒）、`DrawdownBrakeEstimateRecord`（2 天）。
- **LiveContract**：`TaiexContractTests.Mis_TaiexIndex_HasTheFieldsTheEstimateReads`（只驗 shape；`z` 為 `-` 時容許，因排程剛好在開盤瞬間）；改版後多驗「TWStockLib 的實際路徑」（lib 的裸 GET，無自訂 User-Agent，與 app 的預設 HttpClient 一致）能讀出同一份資料——**這一段尚未在 Actions 跑過**；根 `CLAUDE.md` Testing Status 來源清單已補 MIS（註明經 TWStockLib）。
- 測試：原版 1208 通過；**改經 TWStockLib 後 `dotnet test --filter "Category!=LiveContract"` 1179 通過、0 失敗**（刪除自建 parser 的測試，新增 client 的單元測試與端到端測試）；原版的 29 個變異皆被抓到，改版新增的 client 邏輯另做 10 項變異：7 項被斷言抓到、「拿掉逾時」使測試永不結束（被卡死抓到）、其餘 2 項為等價變異（不 dispose scope、`last<=0` 檢查被後面的 ±15% 檢查涵蓋）。
- **MIS 格式已驗證（2026-10-11，GitHub Actions `contract-tests.yml` run 38140029229，由本分支手動 dispatch；`Mis_TaiexIndex_HasTheFieldsTheEstimateReads` 通過，且是真的跑到解析器〔`z` 為數字〕，不是逾時略過）**。實際紀錄：
  - 請求：`GET https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch=tse_t00.tw&json=1&delay=0&_=<ms>`，**不需要 cookie／先訪問任何頁面**（GitHub runner 直接成功）。
  - 回應 `msgArray[0]` 的欄位：`@ | pid | ^ | key | c | # | d | % | ch | tlong | h | i | it | l | m | n | o | r | ex | t | y | z`，**全部是字串**。解析器用到的 `c`＝`t00`、`ch`＝`t00.tw`、`d`＝`20261008`、`t`＝`13:31:00`、`tlong`＝`1791437460000`（＝台北 13:31:00）、`y`＝`49806.37`（昨收）、`z`＝`49313.44`（現價）、`n`＝`發行量加權股價指數`，與假設一致。另有 `rtcode`＝`0000`、`rtmessage`＝`OK`、`userDelay`＝`5000`、`queryTime.sysTime`（台北時間）。
  - 該次在週日跑，MIS 回的是最後一個交易日（週四 10-08）的收盤值，`d` 為該交易日、不是今天——正是「讀數日期 ≠ 今天 → 無預估」要擋的情況。
  - **仍未實測**：盤中（`z` 隨時間更新、開盤前 `z` 為 `-`）與平日休市日的行為；排程在 01:00 UTC＝台北 09:00 開盤瞬間，測試已容許 `z` 為 `-`。
- **請 Tech Lead 更新 `ARCHITECTURE.md`**（我依規則未動）：契約區「`estimate` 第二階段前恆為 null」、第二階段章節的 BLOCKED 標示、Cache 盤點表的「MIS 預估（未做）」列；並視需要把「疊加在正式訊號之外」「基準日檢查」「未用 TWStockLib 的理由」寫入。

## Review
（待填）
