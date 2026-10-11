# TASK-041：回落煞車 — 手動「重新整理資料」端點、資料時間欄位、訊號短快取（需求 8，#431）

## Owner
Backend Engineer

## Status
READY

## Priority
MEDIUM（PM 需求 8；第二階段必備，第一階段也有價值：收盤後指數公布時間不確定，「資料未更新」時可自行重試。不阻擋已合併的 #433）

## Objective
讓使用者能看到「資料有多新」並手動觸發重新取資料，且不放大對證交所的請求：全站共用一個冷卻，請求量與使用者數無關。同時為訊號加一層 60 秒的記憶體快取，降低每次請求讀 DB 的成本（Boss 擔心計算量；設計與數字見 `ARCHITECTURE.md`「回落煞車 — 重新整理、cache 盤點、可觀測性」）。

## Requirements
1. **新端點** `POST /api/portfolio/drawdown-brake/refresh`（與 GET 同一個 `IDrawdownBrakeAccess` 把關，不符資格回 `DrawdownBrakeNotEnabled`）。回 200：
   ```
   { result: "Updated" | "NoNewData" | "SourceUnavailable" | "Throttled",
     retryAfterSeconds: number | null,      // 只有 Throttled 有值
     signal: DrawdownBrakeDto }             // 最新訊號
   ```
   - `Throttled` 也回 **200**（不是 429：429 在本站專指 UsageLimit，前端/中介層會特別處理；冷卻是正常狀態）。
2. **`TaiexTotalReturnRefreshService.TryRefreshNowAsync(ct)`**（供端點呼叫）：
   - **全站單一冷卻 60 秒**，從「最近一次嘗試結束」起算，**含每小時背景輪詢**的嘗試。冷卻中直接回 `Throttled`（帶剩餘秒數），**不打證交所**。
   - 單飛（single-flight）：同時多人按，只有一個真的去抓，其餘看到冷卻。
   - **只做「往前補」**（`PlanMonths` 的 forward 部分，從已存最新日的月份到當月），**不做 backfill**。
   - 短路：若「不可能有新資料」（週末，或平日台北 14:00 前且已存最新日 ≥ 前一個平日）直接回 `NoNewData`，不打證交所。休市日仍會打一次（可接受；有冷卻上限）。
   - 結果判定：有新增／變更列 → `Updated`；成功但沒有新日 → `NoNewData`；`client.FetchMonthAsync` 回 Failure → `SourceUnavailable`（沿用「失敗即停止這一輪」）。
   - 沿用 `TaiexTotalReturnClient` 的 ≥3 秒間隔與單一 in-flight；**不新增 Polly retry**（見該類別註解）。
3. **DTO 新增兩欄**（`DrawdownBrakeDto`，同步 `ARCHITECTURE.md` 契約與前端型別）：
   - `LastCheckedAt`（UTC，可 null）：伺服器最後一次**成功向證交所取得可解析回應**的時間（背景或手動皆算）。記憶體保存，重啟後到第一次成功前為 null。
   - `GeneratedAt`（UTC）：這份訊號的計算時間（快取填入時間）。
4. **訊號短快取**：`GET drawdown-brake` 的 `DrawdownBrakeDto` 以 `IMemoryCache` 快取 **60 秒**，全站共用一份、單飛。refresh 服務在 `UpsertAsync` 有變更時、以及手動重新整理成功後**主動失效**該快取。快取命中不重算、不讀 DB。
5. 日誌：每次手動重新整理與其結果以 `LogInformation` 記一行（結果、是否打了證交所）。**不要**記使用者 ID 以外的使用者資料；結構化欄位名稱留給 TASK-040 統一（本任務只要有 log 即可）。

## Acceptance Criteria
- Offline 單元測試（`FakeTimeProvider`／既有 fake client 模式）：冷卻內第二次呼叫 → `Throttled` 且 client 呼叫次數不增；週末與 14:00 前短路 → `NoNewData` 且 client 未被呼叫；成功有新日 → `Updated` 且快取失效（下一次 GET 看到新日）；Failure → `SourceUnavailable` 且不更新 `LastCheckedAt`；背景輪詢與手動共用冷卻；並行 10 次呼叫只有 1 次打 client。
- 快取：60 秒內重複 GET 只讀一次 store；失效後重算。
- 端點：非 admin（Audience=Admin）→ 403 契約不變；Audience=All 時任何登入者可呼叫；未登入 401。
- `dotnet test --filter "Category!=LiveContract"` 全綠；無 migration、無 AI、無 `[UsageLimit]`。
- 更新 `ARCHITECTURE.md` 契約區的 DTO 欄位（Tech Lead 審查時核對）。

## Technical Notes
- 為什麼全站冷卻而不是每人冷卻：證交所的負載必須與使用者數無關；每人冷卻在 100 位使用者時仍可能每分鐘 100 次。代價是 A 按完後 B 在 60 秒內只會看到 `Throttled`，但看到的 `signal` 仍是最新的（剛被 A 更新過），體驗上不吃虧。
- 時間一律固定 UTC+8 與 `TimeProvider`（沿用既有作法）。
- 選項（技術債，非本任務）：背景輪詢在休市日仍每小時打一次當月；可用既有 `ITwseHolidayCalendar` 跳過。
- 盤中預估的重新整理（TASK-038）會接在同一個端點上，請保留 `result` 列舉的擴充空間，但**本任務不要**加 MIS。

## Dependencies
TASK-036 MERGED。**TASK-038 與 TASK-043（前端）依賴本任務**。

## PR
（待填）

## Result
（待填）

## Review
（待填）
