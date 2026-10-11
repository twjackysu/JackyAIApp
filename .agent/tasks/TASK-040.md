# TASK-040：回落煞車 — 可觀測性（需求 9，#431）

## Owner
Backend Engineer

## Status
BLOCKED（待 TASK-041 合併，兩者改同一批檔案；041 先做）

## Priority
MEDIUM（PM 需求 9：`Features:DrawdownBrake:Audience` 改為 `All` 之前必備，不阻擋已合併的 #433）

## Objective
讓 Boss 不讀程式碼就能在 Azure Application Insights 查到：此功能被看幾次、幾位不重複使用者、後端回應時間、對證交所的請求與失敗、cache 命中、手動重新整理次數、「資料未更新」次數。**不記錄任何持股、股數、金額**（本功能的伺服器端本來就收不到這些：個人建議在瀏覽器計算）。

## Requirements
後端已有 `AddOpenTelemetry().UseAzureMonitor(...)`（`Program.cs`），HTTP request 與對外 HttpClient 呼叫是**自動收集**的（`requests`／`dependencies` 表）：回應時間、錯誤率、對 `www.twse.com.tw` 的請求數／失敗數／耗時**不需要寫任何程式**。只補下列自訂部分：

1. **Meter**：`System.Diagnostics.Metrics.Meter("JackyAIApp.DrawdownBrake")`，並在 `Program.cs` 以 `.WithMetrics(m => m.AddMeter("JackyAIApp.DrawdownBrake"))` 註冊（否則不會送到 Azure Monitor）。
   | Instrument | 型別 | Tags | 記錄時機 |
   |---|---|---|---|
   | `drawdownbrake.views` | Counter<long> | `outcome` = `ok｜initializing｜stale｜forbidden`；`cache` = `hit｜miss` | 每次 `GET drawdown-brake` |
   | `drawdownbrake.refresh` | Counter<long> | `result` = `updated｜no_new_data｜source_unavailable｜throttled` | 每次 `POST …/refresh` |
   | `drawdownbrake.compute.duration` | Histogram<double>（ms） | — | 僅快取 miss 時：讀 DB＋重算的耗時 |
2. **不重複使用者**：每次 GET 以 `LogInformation("DrawdownBrake view {Outcome} {UserKey}", …)` 記一行，`UserKey` ＝ `SHA256(userId + ":" + UTC日期 + ":" + 每個行程啟動時隨機產生的 salt)` 的前 12 個十六進位字元。
   - 這是**每日、每行程**的假名：只能算「當日不重複人數」，不能跨日追蹤，也無法還原 Google ID（隱私最小化；DECISION-010 的匿名原則）。重啟會讓同一人當天被算兩次（部署時才發生；觀察用途可接受，文件註明）。
   - 不得把 `userId`、email、持股、金額寫進任何 log／metric。
3. 確認 `ILogger` 的 Information 層級會送到 App Insights（`appsettings` 的 log filter 沒有把 `JackyAIApp.*` 擋在 Warning 以上；若有擋，只放行這個 category）。
4. 在 `ARCHITECTURE.md` 的「可觀測性」小節（Tech Lead 已寫好初稿 KQL）核對欄位名稱，**首批資料進來後**修正 KQL（Engineer 沒有 App Insights 存取時，在 PR 註明「KQL 未實測」，由 Boss 部署後貼查詢驗證）。

## Acceptance Criteria
- 單元測試：用 `MeterListener` 驗證各 instrument 的名稱與 tag 組合；`UserKey` 同日同人相同、不同日不同、不含 `userId` 子字串；快取命中／未命中、各 `outcome`、各 refresh `result` 都有被記錄。
- 沒有任何新的 log／metric 含持股、股數、金額、email、Google ID（review 時逐一檢查）。
- `dotnet test --filter "Category!=LiveContract"` 全綠；無 migration、無新增付費服務（沿用既有 Application Insights）。

## Technical Notes
- 「資料未更新出現次數」＝ `drawdownbrake.views{outcome=stale}`（以被看到的次數計）；若要「發生幾次」改看 `GeneratedAt`／log，不另加 instrument。
- 開放給全部使用者的建議門檻見 `ARCHITECTURE.md`（Tech Lead 提議、Boss 決定）；**本任務不做任何自動開關**。
- 不做 Admin 儀表板：Azure Portal → Application Insights → Logs 貼 KQL 即可；若 Boss 之後想要固定圖表，另開任務做 Workbook。

## Dependencies
TASK-041（快取與 refresh 結果在那裡產生）。

## PR
（待填）

## Result
（待填）

## Review
（待填）
