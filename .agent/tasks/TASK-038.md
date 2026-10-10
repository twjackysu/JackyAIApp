# TASK-038：回落煞車 — 後端盤中預估訊號（MIS）與「訊號已更正」（#431，第二階段）

## Owner
Backend Engineer

## Status
BLOCKED（待 TASK-036 合併；站外提醒另依 Boss 對 Q2 的決定另案）

## Priority
MEDIUM（PM：第二階段 MEDIUM；Boss 規格將盤中預估列為「建議做法」，回測主結果假設收盤集合競價成交，「隔日成交」版本績效明顯較差）

## Objective
在 `GET /api/portfolio/drawdown-brake` 的 `estimate` 欄位提供盤中預估訊號，讓使用者可在 13:25–13:30 收盤集合競價前下單；收盤正式資料入庫後若狀態與先前預估不同，回報 `estimateCorrected=true`。

## Requirements
1. **先做格式驗證（#431 §5 要求，PM 亦列為 Tech Lead Action）**：`https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch=tse_t00.tw`。確認欄位（`z` 現價、`y` 昨收等）、盤中／盤後 `z` 缺值行為、是否需先取得 cookie。先查 TWStockLib 的 `GetLatestQuoteAsync` 能否直接取得指數；不行再自建 client（走 `ExternalReadOnly`）。以 LiveContract 測試固定格式（只驗 shape）。
2. **預估公式**（#431 §5）：`預估報酬指數 = 昨日正式報酬指數 × (目前加權指數 z ÷ 昨收 y)`；以 `DrawdownBrakeCalculator` 對「正式序列＋預估點」重算，輸出 `estimate = { asOfTime, estimatedClose, state, prevState(=正式 asOfDate 的狀態), drawdownPct }`。`z` 缺值 → `estimate=null`（不猜）。
3. 僅在「平日、台北 09:00 之後、今日正式資料尚未入庫」時計算；今日正式資料入庫後 `estimate=null`。
4. 結果短快取（≈30 秒）、全站共用一份，不隨使用者數放大對 MIS 的請求。
5. **訊號已更正**：`IMemoryCache`（TTL 2 天）記當日最後一次預估狀態；正式資料入庫後若與之不同 → `estimateCorrected=true`（持續到隔日）。快取遺失時退化為 false（可接受）。

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
TASK-036 MERGED。站外提醒（Email／推播）屬 Q2 決定，**不在本任務範圍**。

## PR
（待填）

## Result
（待填）

## Review
（待填）
