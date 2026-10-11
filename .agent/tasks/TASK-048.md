# TASK-048：以測試鎖定 00685L 的曝險倍數為 2（#431，DECISION-021 / PM 假設 A29）

## Owner
Backend Engineer

## Status
READY

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
（待填）

## Result
（待填）

## Review
（待填）
