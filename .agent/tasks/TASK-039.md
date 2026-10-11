# TASK-039：回落煞車 — 啟用 Boss 回測資料驗收測試（#431）

## Owner
Backend Engineer

## Status
PR_OPENED

## Priority
HIGH（小任務；結束 TASK-036 遺留的「驗收測試 Skipped」，PM 驗收條件第 1 項）

## Objective
Boss 已把官方報酬指數快照放進 repo（`回測數據/tw_backtest/data/taiex_tri.csv`，2003-01-02 ~ 2026-10-08，5850 列）。把它變成後端測試 fixture，讓 `DrawdownBrakeCalculatorTests` 中兩個原本 Skipped 的驗收測試真正執行，並固定一組由獨立實作得到的期望值。

## Requirements
1. 將 `回測數據/tw_backtest/data/taiex_tri.csv` **逐字複製**為 `JackyAIApp.Server.Tests/Fixtures/TaiexTotalReturn.csv`。表頭 `date,tri` 不必改：`LoadCloses` 只取「第一欄可解析為日期」的列，表頭自動略過；csproj 已有 `Fixtures\**\*.csv` 複製規則。
   - **測試不得引用 `回測數據/` 路徑**（非 ASCII 路徑，不會複製到輸出目錄，也不應讓測試依賴 Boss 的分析資料夾）。
2. 在測試註解標明來源與完整性：repo 內檔案（LF，`.gitattributes` 的 `* text eol=lf` 正規化後）SHA256 前 16 碼 `97d1f46d43f1f6bf`；Boss `README.md` 寫的 `e9f95b791f9b9e7c` 是 Windows CRLF 版，資料相同（Tech Lead 已驗證：還原 CRLF 後雜湊一致）。
3. 新增一個測試，固定以下期望值（Tech Lead 以獨立的純 Python 實作，依 DECISION-013 規則逐日重放得到；與 Boss 的 `bt.drawdown_brake`／`_hyst` 逐條對照語意一致：`sma` 不足 60 日 → `up=False`、`high252` 用 `min_periods=1`、`up` 優先、`dd < -0.08` 嚴格小於、起始 OFFENSE）：
   | 項目 | 期望值 |
   |---|---|
   | 筆數／區間 | 5850 筆，2003-01-02 ~ 2026-10-08 |
   | 2026-10-08 | `OFFENSE`、Close `114031.08`、High252 `115208.19`、Sma60 `105544.3732`（四捨五入到 4 位）、Drawdown ≈ −1.0217% |
   | 全段狀態切換次數 | 132 |
   | DEFENSE／OFFENSE 天數 | 1344／4506 |
   | 前四次切換 | 2003-02-10→DEFENSE、2003-04-16→OFFENSE、2003-04-17→DEFENSE、2003-04-18→OFFENSE |
   | 最近六次切換 | 2026-03-31→DEFENSE、04-01→OFFENSE、07-17→DEFENSE、07-21→OFFENSE、07-28→DEFENSE、08-05→OFFENSE |
   | 序列最大回撤（對 252 日高） | ≈ −53.59% |
4. 既有的 `Compute_BossBacktestSeries_EndsOffenseNearTheHigh`、`..._LastThreeYearsAloneReachTheSameState` 啟用後應通過（Tech Lead 已用同一資料驗證：只取最後 3 年得到相同 state／High252／Sma60）。
5. 更新 `DrawdownBrakeCalculatorTests` 檔頭「Skipped until Fixtures/TaiexTotalReturn.csv exists」的註解。

## Acceptance Criteria
- `dotnet test --filter "Category!=LiveContract"`（Release）全綠，且上述驗收測試**顯示通過、不是 Skipped**。
- **不得**為了讓測試通過而改 `DrawdownBrakeCalculator` 或固定參數（`DdTrigger`／`ReenterSma`／`LookbackHigh`，DECISION-013）。若任何期望值不符：**停下，回報 Tech Lead**（可能是資料或規格問題，不是調整斷言的理由）。

## Technical Notes
- 不新增資料表、不動 Calculator、不動前端。
- 根 `CLAUDE.md` 不必更新（沒有新來源）。
- 這個 fixture 約 140 KB，可接受；不要把整個 `回測數據/` 搬進測試專案。

## Dependencies
TASK-036 MERGED（#433）。

## PR
#435（branch `claude/exciting-cray-omzm67`，2026-10-11）

## Result
2026-10-11 Backend Engineer：
- 新增 `JackyAIApp.Server.Tests/Fixtures/TaiexTotalReturn.csv`（逐字複製；LF 版 SHA256 前 16 碼 `97d1f46d43f1f6bf`，與任務相符）。
- `DrawdownBrakeCalculatorTests`：更新檔頭註解與來源／完整性說明；新增 `Compute_BossBacktestSeries_MatchesTheIndependentReplay`（筆數、2026-10-08 各指標、DEFENSE／OFFENSE 天數、最大回撤）與 `Compute_BossBacktestSeries_SwitchesStatesOnTheExpectedDates`（132 次、前四次、最近六次）。未動 `DrawdownBrakeCalculator` 與固定參數。
- **驗證**：以獨立純 Python 重放該 CSV，TASK 表格每一項都吻合（Sma60 未四捨五入值為 105544.37316…，不在進位邊界）。**本環境無 .NET SDK 且 `builds.dotnet.microsoft.com` 被出口政策擋下，`dotnet test` 未執行**；C# 編譯與測試通過與否由 PR #435 的 CI 驗證。
- **CI 結果（head `490754d`，run 38110799015）**：`build-and-test-server` ✅（`Total tests: 901`、`Passed: 901`，摘要無 Skipped 行 → Boss 回測驗收測試已實際執行並通過）、`lint-and-build-client` ✅。尚待 Tech Lead 技術審查。

## Review
（待填）
