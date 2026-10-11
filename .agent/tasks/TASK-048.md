# TASK-048：以測試鎖定 00685L 的曝險倍數為 2（#431，DECISION-021 / PM 假設 A29）

## Owner
Backend Engineer

## Status
MERGED（PR #446，Boss 於 2026-10-11 合併；Tech Lead 事後審閱見 Review）

## Priority
LOW（僅測試；不阻擋 TASK-047，但 TASK-047 的「正 2 類別」依賴此倍數為 2，鎖住它能在日後有人改動 `PortfolioExposure` 時立即失敗）

## Objective
PM 的假設 A29：「00685L 的曝險倍數為 2（讀程式：台股代碼尾碼 L 判為 2 倍，未執行驗證，請 Tech Lead 以測試鎖定）」。Tech Lead 讀 `PortfolioExposure.Factor` 的結論是：名稱「群益臺灣加權正2」含「正2」→ 2，名稱缺失時代碼尾碼 `L` → 2，兩條規則都成立。本任務只新增測試，把這個結論變成會失敗的檢查。

## Requirements
在 `JackyAIApp.Server.Tests/Services/Portfolio/PortfolioExposureTests.cs` 既有的兩個 Theory 加入 InlineData（**不改產品程式碼**）：
1. `Factor_NameBasedClassification_TakesPriorityOverSuffix` 加：`("群益臺灣加權正2", "00685L", "TW", 2)`。
2. 代碼尾碼 fallback 的 Theory（`"00757L"` 那組，名稱為空時只靠尾碼）加：`("00685L", 2)`。
3. 若 `DrawdownBrake` 或 Portfolio 前端契約沒有任何測試涵蓋「`exposureFactor` 對 `00685L` 的回傳」，不要新增前端測試（TASK-047 負責前端）。

## Acceptance Criteria
- `dotnet test --filter "Category!=LiveContract"` 全過，新增 2 個資料列。
- 刻意把 `PortfolioExposure.Factor` 的 `正2` 規則與 `L` 尾碼規則暫時各改壞一次，確認對應的新資料列會失敗（變異驗證，改回後再提交；PR 描述寫一行結果）。
- 只動測試檔，無產品程式碼、migration、API 變更。

## Technical Notes
- 名稱以 TWSE 現行簡稱「群益臺灣加權正2」為準；若實際簡稱不同，請以 PR 描述註明來源，並仍保留尾碼 fallback 那一列。
- 沙箱可能沒有 .NET：先前 Engineer 以 `apt` 安裝 `dotnet-sdk-10.0` 成功。

## Dependencies
無。可與 TASK-047、TASK-038 平行。

## PR
- #446 — https://github.com/twjackysu/JackyAIApp/pull/446（分支 `claude/exciting-cray-ub9i2y`，僅動 `PortfolioExposureTests.cs`）

## Result
- 兩個 Theory 各加一列：`("群益臺灣加權正2", "00685L", "TW", 2)`、`("00685L", 2)`。無產品程式碼、migration、API 變更；依 Requirements 第 3 點未新增前端測試。
- `dotnet test --filter "Category!=LiveContract"`：1181 通過、0 失敗（沙箱以 `apt` 安裝 `dotnet-sdk-10.0` 後執行）。
- 變異驗證（改壞後皆已 `git checkout` 還原產品檔）：
  - 只改壞 `L` 尾碼規則（`return 2m` → `1m`）→ 新列 `("00685L", 2)` 失敗（連同 `00757L`／`00757l` 共 3 列）。
  - 只改壞 `正2` 規則（拿掉 `name.Contains("正2")`）→ **全部通過**：`00685L` 為 TW 股票且尾碼 `L`，尾碼 fallback 同樣得 2，兩條規則互為備援（既有 `00631L` 列同理）。因此驗收條件「各改壞一次，對應新列會失敗」對 `正2` 規則這一半**無法成立**，不是測試寫錯。
  - 兩條同時改壞 → 名稱列與尾碼列都失敗（共 5 列）。
- 結論：「00685L → 2」這個結果已被鎖住（任一條規則單獨失效時由另一條擋住；兩條都失效才會失敗）。若要單獨鎖住 `正2` 名稱規則，需另一列「名稱含正2、代碼不帶 `L`」——規格外，建議由 Tech Lead 決定是否另開任務，本 PR 未擴大範圍。
- 名稱「群益臺灣加權正2」取自任務文字，沙箱無外網，未連 TWSE 驗證現行簡稱；尾碼 fallback 列不依賴名稱。

## Review
**Tech Lead 事後審閱（2026-10-11，排程執行第 4 次；#446 在審閱前已被 Boss 合併）**
- diff 僅 `PortfolioExposureTests.cs` 兩列 InlineData，與任務書一致；無產品程式碼、migration、API 變更。CI（run 655／656）成功。
- **Engineer 提出的問題＝「單獨鎖住 `正2` 名稱規則是否另開任務」——Tech Lead 決定：不開。**
  - 這是我任務書驗收條件的過度規範：DECISION-021 / A29 依賴的是「00685L → 2」這個**結果**，不是由哪一條規則產生。結果已被鎖住（任一條規則單獨失效時由另一條擋住；兩條都失效才會失敗）。
  - 要單獨鎖名稱規則，需要一個「名稱含正2、代碼不帶 L」的標的；台股 L 尾碼本來就是槓桿 ETF 的編碼，現實中沒有這種標的，只會是人工湊的測試資料。成本（日後維護）大於價值。
  - 分類：**Technical Debt，不排**。
- 變異驗證的誠實揭露（只改壞 `正2` 規則時全部通過）**接受**，這比硬湊一列讓變異失敗更正確。
- 名稱「群益臺灣加權正2」未連 TWSE 驗證現行簡稱：尾碼 fallback 列不依賴名稱，**不影響結論**。
- 結論：無須追加修正。TASK-047 對 00685L 倍數＝2 的依賴現在有會失敗的檢查。
