# TASK-036：回落煞車 — 後端訊號（報酬指數資料、狀態機、端點、功能開關）（#431，第一階段）

## Owner
Backend Engineer

## Status
PR_OPENED

## Priority
HIGH（Requirement：「0050 + 00631L『回落煞車』」第一階段；PM 標 HIGH）

## Objective
提供全站共用的「回落煞車」市場訊號：每日取得證交所「發行量加權股價報酬指數」收盤值、存入 DB，依 Boss 固定規則從頭重算 OFFENSE／DEFENSE，並以 `GET /api/portfolio/drawdown-brake` 回傳。個人建議（配置／股數）由 TASK-037 前端計算，**本任務不碰使用者持股**。

設計與契約見 `ARCHITECTURE.md`「回落煞車」一節（API 契約以該節為準）。規則原文以 Issue #431 為準；**固定參數不得調整、不得做成設定**（DECISION-013）。

## Requirements
1. **資料表**：`TaiexTotalReturnDaily(TradeDate PK, Close decimal(18,4))` + EF migration（僅加法）。只存已收盤的正式值。
2. **`TaiexTotalReturnClient`**：`GET https://www.twse.com.tw/rwd/zh/TAIEX/MFI94U?date=YYYYMM01&response=json`。
   - 請求間隔 ≥ 3 秒、全域單一 in-flight（`SemaphoreSlim`）；走 `ExternalReadOnly` named client。
   - 解析：民國年（`115/10/08`）、千分位逗號、`--` 視為無資料。**依表頭文字（含「報酬指數」）定位欄位**，找不到唯一欄位 → 記 log、回傳失敗、**不寫入**；`stat` 非 OK／無資料 → 回傳空集合，不丟例外。
   - 遇非 JSON（HTML／限流頁）或非預期格式 → 立即停止本輪，不再連續重試。
3. **`TaiexTotalReturnRefreshService : BackgroundService`**（仿 `TwListedCompanyRefreshService`，注入 `TimeProvider`）：每小時檢查；表空 → 背景回補近 3 年；平日台北時間 14:00 後若最新一筆 < 今日 → 抓當月（有缺口自缺口月起逐月補）；Upsert 冪等；失敗吞掉、下小時重試，不得讓 loop 終止。
4. **`DrawdownBrakeCalculator`**（純函式、無 DB／時間依賴）：輸入依日期排序的收盤序列，輸出每日狀態序列與最後一日的 `close/high252/sma60/drawdown`。規則逐字依 #431 §1：初始 OFFENSE；`up = sma60 != null && close > sma60`（< 60 筆為 false）；`down = close/high252 - 1 < -0.08`（**嚴格小於**）；`up` 優先；皆否維持前日；`high252` 不足 252 筆用現有資料。
5. **端點** `GET /api/portfolio/drawdown-brake`（`[Authorize]`；**不加** `[UsageLimit]`，無 AI 成本），回傳 `ARCHITECTURE.md` 契約的 `DrawdownBrakeDto`：`prevState` ＝ `asOfDate` 前一交易日狀態；`lastSwitchDate`；`distanceToTriggerPct`；`isInitializing`（表空／回補中，其餘欄位 null）；`isStale`；`estimate`／`estimateCorrected` 本任務恆為 `null`／`false`（欄位先存在，供前端與 TASK-038）。
6. **`isStale`**：平日、台北時間 17:00 後、最新資料日 < 今日、且今日**不是**休市日。休市日行事曆優先用 TWSE OpenAPI `holidaySchedule`（先以 LiveContract 驗證存在與格式；快取）；不可用時退而求其次「連續缺 ≥ 2 個平日才標 stale」，並在 PR 說明、回報 Tech Lead。
7. **功能開關（Q1 未決前預設關閉）**：設定 `Features:DrawdownBrake:Audience`＝`Admin`（預設）｜`All`。非資格者呼叫端點 → 403（新增 `ErrorCodes`，訊息中性）；既有使用者資訊回應（與 `isAdmin` 同處，見 `accountApis/type.ts` 的來源 endpoint）新增 `drawdownBrakeEnabled`（= `IsAdmin` 或 Audience=All）。
8. **LiveContract 測試**（`LiveContracts/`，`[Trait("Category","LiveContract")]`，只驗 shape）：MFI94U（整月有資料、日期格式、報酬指數欄可定位、數值可解析）、`holidaySchedule`。更新根 `CLAUDE.md`「Testing Status」的來源清單。**不得自行放寬請求間隔**。

## Acceptance Criteria
- Offline unit tests（`dotnet test --filter "Category!=LiveContract"` 全綠）：
  - 狀態機：同日 up 與 down 皆成立 → OFFENSE；皆不成立 → 維持前日；< 60 筆 → up=false；`dd` 恰為 −8.0% **不算** down；序列 < 252 筆；252 窗口排除更早的高點；同一序列重算結果一致（無隱藏狀態）。
  - **真實資料驗收**：以 Boss 提供的回測用報酬指數序列（2003-01 ~ 2026-10-08，檔案 `JackyAIApp.Server.Tests/Fixtures/TaiexTotalReturn.csv`）重算，2026-10-08 狀態為 OFFENSE、`drawdownPct` ≈ −1%（±0.5 個百分點）；**同一序列只取最後 3 年重算，最終狀態必須相同**。若 Boss 另附 Python 逐日狀態輸出，加逐日比對測試。
  - 解析器：民國年、千分位、`--`、`stat` 非 OK、找不到「報酬指數」欄、重複欄 → 皆有測試，後兩者不寫入。
  - RefreshService（假 `TimeProvider`、假 client）：週末／14:00 前不抓；表空走 3 年回補；Upsert 冪等；失敗不終止 loop；遇非預期格式停止本輪。
  - 端點：未登入 401；非資格者 403；`Audience=All` 一般使用者 200；`isInitializing`；`isStale`（含休市日不誤報）；`prevState`／`lastSwitchDate` 正確。
- LiveContract 測試已加入（本地／PR CI 不跑；以 `contract-tests.yml` 手動 dispatch 驗證通過後在 PR 附結果）。
- migration 僅新增資料表；無 AI 呼叫；無 `[UsageLimit]`。
- PR 附上 MFI94U 實際回傳的欄位名稱（取自 LiveContract 執行結果），作為格式驗證紀錄。

## Technical Notes
- **本沙箱／可能的 Engineer 環境無法連 `twse.com.tw`（egress 403，Tech Lead 已實測）**：格式驗證以 GitHub Actions `contract-tests.yml` 為準；單元測試用手寫的、符合 #431 §6 描述的樣本 JSON。若你的環境也連不到，請在 PR 明講「格式僅由 LiveContract 驗證」，不要猜欄位。
- **Fixture 來源**：已在 #431 請 Boss 提供回測用報酬指數 CSV（`date,close`）。**拿到前不要阻塞**：先完成其餘 AC，真實資料驗收測試在 CSV 到位後補上（Tech Lead 審查時會檢查）。備案：Boss 同意後，可加一個僅 `workflow_dispatch` 的一次性 workflow 從證交所抓取（≥3 秒間隔）。
- 現有 Polly（`AddReadOnlyResilience`）對 GET 會重試；注意重試不得繞過 ≥ 2 秒間隔，也不得在被限流時加劇。
- 時間一律用固定 UTC+8（台灣無夏令時間），沿用 `TwTodayOverlayMarketDataProvider.HasTodayTwQuote` 的作法與 `TimeProvider`。
- Migration 隨啟動執行；加法表可安全部署（見根 `CLAUDE.md` Deployment 說明）。
- 範圍外（不要做）：使用者設定表、站外通知、盤中預估（TASK-038）、任何對 Portfolio 估值／分割的修改。

## Dependencies
無（#431 規格與 DECISION-013 已定）。前端 TASK-037 以本任務的 API 契約平行開發。

## PR
https://github.com/twjackysu/JackyAIApp/pull/433（branch `claude/exciting-cray-ij5f2w`）

## Result
**已實作（PR #433）。** 離線測試 `--filter "Category!=LiveContract"`（Release）：893 通過、2 略過、0 失敗。

### 與任務書不同／需 Tech Lead 確認
1. **MFI94U 用專用 named client `TaiexTotalReturn`，未掛 `AddReadOnlyResilience()`**（任務書：走 `ExternalReadOnly`）。理由：Polly 會在 ~1s／~2s 後重送失敗的 GET，違反 ≥3 秒間隔且會加劇限流（Technical Notes 已點出）；重試交給每小時 refresh loop。有測試證明此 client 無 retry handler。
2. **回補由新到舊**（當月先、往回補到 3 年前）。`isInitializing` ＝ 表空或已存歷史跨度 < 730 天（#431 §1「至少 2 年」）；回補中途失敗，下一輪從缺口續補。
3. **休市日**：優先 TWSE OpenAPI `holidaySchedule`（`https://openapi.twse.com.tw/v1/holidaySchedule/holidaySchedule`）；不可用／不涵蓋該年／讀不懂 → 「連續缺 ≥ 2 個平日才標 stale」（已實作、有測試）。此端點格式與「開始交易日／最後交易日」列的處理原為假設，已由 LiveContract 驗證相符。

### 未能完成／待補
- **本環境連不到 twse.com.tw**（egress 403）：改以 `contract-tests.yml` 手動 dispatch 驗證，**已通過（55/55）**。第一次 run 抓到真實 bug：MFI94U 日期欄表頭是 `日　期`（含全形空白 U+3000），已修（表頭比對前去除所有空白）。**MFI94U 實際格式**：`fields = [日　期 | 發行量加權股價報酬指數]`，第一列 `["115/09/01","108,395.72"]`。**holidaySchedule 實際格式**：27 列（僅 2026），`{Name, Date(民國 7 碼, 如 1150101), Weekday, Description}`；「開始交易日／最後交易日」列為交易日，與實作相符。細節見 PR #433 留言。
- **Boss 的回測 CSV 尚未提供**：`Fixtures/TaiexTotalReturn.csv` 的兩個驗收測試已寫好，檔案不存在時回報為 **Skipped**；檔案放進 `JackyAIApp.Server.Tests/Fixtures/` 即自動啟用。替代驗證：`Fixtures/DrawdownBrakeGolden.csv`（#431 §7 參考實作的獨立轉寫，1500 日、50 次切換）逐日比對一致。
- 流程：依 Workflow conventions 應把 `IN_PROGRESS` 推到 master；本 session 僅限推 feature branch，所以標記在本分支。


## Review
2026-10-11 Tech Lead 審查 PR #433（head `e338e50`）：**技術審查通過 → READY FOR BOSS REVIEW**（不代表核准合併；task 的 Status 欄由 Engineer 分支維護，這裡不動以免合併衝突）。

- **正確性**：`DrawdownBrakeCalculator` 逐字對應 #431 §1（`up` 優先、`down` 嚴格 `<`、<60 筆 `up=false`、`high252` 不足 252 用現有資料、`decimal` 精確運算、參數為 `const`）。Tech Lead 另以獨立的純 Python 版 Boss 參考規則重放 `Fixtures/DrawdownBrakeGolden.csv`：1500 日 **0 筆不一致**、50 次切換、553 日防守，與 PR 所述一致。
- **資料層**：解析器依表頭定位、找不到／重複／無法解析一律整月失敗不寫入；LiveContract 在 Actions 上抓到真實表頭 `日　期`（U+3000）並已修，是本 PR 最有價值的驗證。MFI94U 真實欄位：`[日　期 | 發行量加權股價報酬指數]`。今日 14:00 前的值與未來日期不入庫。
- **對 task 的偏離——皆接受**：(1) MFI94U 用專用 named client、**不掛** `AddReadOnlyResilience`（Polly 會在 1–2 秒重送，違反 ≥3 秒間隔且加劇限流；重試改由每小時 refresh；有測試證明）；(2) 回補**由新到舊**，使 `isInitializing`（歷史跨度 <730 天）可精確判定、失敗可從缺口續補；(3) 休市日用 `holidaySchedule`（LiveContract 驗證格式：27 列、僅涵蓋 2026、「開始／最後交易日」列為交易日），不可用或不涵蓋該年時退回「連續缺 ≥2 個平日」。
- **安全／成本**：`[Authorize]` ＋ 功能開關（預設 Admin；打錯值視為 Admin，不會誤開）、403 對應 `ErrorCodes.DrawdownBrakeNotEnabled=1040301`（→ HTTP 403，有測試）；無 AI、無 `[UsageLimit]`；migration 僅新增資料表。
- **測試**：Release 離線 897 通過／2 略過／0 失敗；PR CI（`build-and-test-server`、`lint-and-build-client`）綠；變異測試覆蓋規則誤改。
- **未完成的驗收（需 Boss）**：AC「真實資料驗收」（2026-10-08 為 OFFENSE、`drawdownPct`≈−1%、最後 3 年重算同狀態）的兩個測試因缺 Boss 的 `Fixtures/TaiexTotalReturn.csv` 而 **Skipped**（非 Passed）。替代驗證＝golden 逐日比對（驗證狀態機邏輯，但不驗證 MFI94U 真實資料與 3 年窗口）。**緩解**：功能目前僅 admin 可見，部署後請 Boss 以 admin 呼叫端點，確認 `state=OFFENSE`、`drawdownPct` 約 −1%（最新交易日為 2026-10-09 或之後，數字會略不同）；CSV 仍建議補上作為回歸測試。
- **非阻擋觀察（部署後確認，不另開 task）**：(a) 實際確認 MFI94U 當日值的公布時間；若晚於 `StaleAfter=17:00`，「資料未更新」會每天短暫誤報，屆時調整 `DrawdownBrakeService.StaleAfter`／`TaiexTotalReturnRefreshService.PublishedAfter` 常數即可。(b) 缺口補抓只在平日 14:00 後觸發；若週末重啟且前一個平日資料缺漏，要到下個平日 14:00 才補（影響小，列為 Technical Debt）。(c) 首次部署後約 2 分鐘內端點回 `isInitializing=true`，屬預期。
- **QA**：本功能僅 admin 可見，且離線／golden／LiveContract 已涵蓋；不另派 QA，改由 Boss 部署後 smoke check（見上）。
