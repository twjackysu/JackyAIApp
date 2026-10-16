# TASK-058：刪除 C# 評分／風險與 `GET /api/finance/score/{code}`，綜合分析快取 key 只剩代號＋日期＋市場（Issue #464 IMP-2 第 3 步）

## Owner
Backend Engineer

## Status
ON_HOLD（2026-10-16 Boss 決定「AI 評分那段留在後端」，與本任務「刪除 C# 評分」的前提衝突；等 Boss 在 (1) 全部留後端、取消 IMP-2 與 (2) 前端另算一份、後端留一份給 AI 之間確認。**確認前不要開工。**）
（原狀態：BLOCKED（等 TASK-057 合併並部署；前端不再讀 `scoring`／`risk` 之後才能刪））

## Priority
MEDIUM

## Objective
Boss 決定（Issue #464，2026-10-16）：**C# 版評分刪除，不要兩套邏輯並存。** TASK-057 把評分／權重／風險移到前端後，後端這些程式就是死碼（`comprehensive-analysis` 的 `scoring`／`risk` 沒人讀），而且仍讓每種旗標／權重組合各佔一份快取。本任務刪掉它們。

## Requirements
1. **刪除**：`Services/Finance/Scoring/`（`StockScoreService`、`IStockScoreService`、`CategoryWeightConfig`）、DI 註冊（`FinanceServiceExtensions`）、`StockScoreResponse`／`CategoryScore`／`RiskAssessment`／`RiskLevel` DTO（若 TASK-056 的黃金測試類別引用它們，**測試類別一起刪**；`stockScoringGolden.json` **保留**，它是前端移植的凍結黃金資料）、`FinanceQueryService.GetStockScoreAsync`、`FinanceController` 的 `GET score/{stockCode}` 與介面 `IFinanceQueryService` 上對應的方法。**這是刪除一個匿名公開端點**——Boss 已於 2026-10-16 同意（「C# 版刪除」）；`technical-indicators/{code}` 與 `chip-analysis/{code}` **不在本任務**（保留，直到 Boss 另行決定）。
2. **`StockAnalysisBuilder`**：移除評分、風險、自訂權重、`OnlyIndicators`／`ExcludeIndicators`、各面向旗標（`WithTechnical/Chip/Fundamental/Scoring/Risk`、`WithCustomWeights`）與 `BuildScoring`。保留 `ForStock`／`ForMarket`／`BuildAsync`：永遠取得並計算**全部面向**（TW：技術＋籌碼＋基本面；US：技術＋基本面＋以內部人交易當籌碼，與現行「全開」時相同），回傳 `indicators`（不過濾）。`StockAnalysisResult` 移除 `Scoring`／`Risk`／`Configuration`；`AnalysisConfiguration` 刪除。
3. **`StockAnalysisRequest`**：縮成 `{ StockCode, Market }`。**舊客戶端多送的欄位（旗標、權重、`OnlyIndicators`…）會被 System.Text.Json 忽略**（預設不拒絕未知欄位——請用一個測試確認這點，不要憑印象）。
4. **`GetComprehensiveAnalysisAsync`**：快取 key 改為 `ComprehensiveAnalysis_{code}_{yyyyMMdd}_{region}`（拿掉 `configHash`）。
5. **AI 趨勢分析的輸入（需要特別注意的連帶影響）**：`BuildQuantitativeContextSafeAsync` 目前用 `WithScoring(true).WithRisk(true)`（C# 預設權重 技術 50%／籌碼 30%／基本面 20%），`FormatQuantitativeContext` 會把「Overall Scoring（總分／方向／建議／各面向分數）」與「Risk Assessment」兩段放進 **AI 個股趨勢分析的 prompt**。評分刪除後這兩段就**拿掉**：AI 仍會收到每個指標的 `Value／Score／Direction／Signal／Reason`（原本就有），只是少了後端預先算好的綜合結論。這是 Tech Lead 的**預設選擇**（保留就得留一份 C# 評分，違反 Boss 的決定）；已在 Issue #464 告知 Boss，若 Boss 反對，**先停下來問，不要自行保留 C# 評分**。請確認 `Prompt/` 下的提示詞沒有引用這兩段的標題（Tech Lead 已 grep：沒有）。
6. 測試：刪除 `StockScoreServiceTests` 與 `StockAnalysisBuilderTests` 中針對評分／風險／旗標／權重的案例；**保留並調整**builder 其餘行為的測試（取資料、US 內部人交易、指標計算呼叫、錯誤處理）。`FinanceControllerMarketIndexHistoryTests` 裡 `new Mock<IStockScoreService>()` 的建構子參數隨之移除。新增：(a) 請求帶舊欄位也能反序列化且結果相同；(b) 兩個不同「舊旗標」的請求共用同一份快取（第二次不再呼叫 provider）；(c) `FormatQuantitativeContext` 的輸出不含 `Overall Scoring`／`Risk Assessment` 但仍含每個指標行。
7. 文件：`CLAUDE.md` 提到評分的地方若有（grep `StockScoreService`／`composite score`）不要自行改——在 PR 說明列出，由 Tech Lead 更新。

## Acceptance Criteria
- `dotnet build`（0 警告新增）、`dotnet test --filter "Category!=LiveContract"` 全過；`grep -rn "StockScoreService\|CategoryWeightConfig\|IStockScoreService\|StockScoreResponse\|RiskAssessment" JackyAIApp.Server JackyAIApp.Server.Tests` 無結果（黃金 JSON 與註解除外）。
- **部署後前端不得壞**：TASK-057 必須已在 `master` 並部署（Status = MERGED 才能開工）；PR 說明確認 `jackyaiapp.client` 內已無任何對 `scoring`／`risk`／`configuration` 欄位或 `/api/finance/score` 的讀取（`grep`）。
- PR 說明回報：被刪除的端點與類別清單、以及「AI 趨勢分析 prompt 少了哪兩段」。

## Technical Notes
- 為什麼不保留 `GET /api/finance/score/{code}`：它**前端與 repo 內都沒人呼叫**（Tech Lead 2026-10-16 grep），而它的實作就是被刪的 C# 評分；保留它＝保留一份評分邏輯。若有外部／MCP 呼叫者，只有 Boss 知道，Boss 已同意刪。
- 收益（誠實面）：後端少 `StockScoreService`（346 行）、builder 的評分／旗標／權重段與一組 DTO；`comprehensive-analysis` 快取不再因設定分裂（同一檔股票全站一份）；比較分頁每檔與量化分頁共用快取。對 server CPU 的影響很小（評分本來就是便宜的純運算）。
- 流程：本檔 `Status` 在 TASK-057 合併並確認部署後由 Tech Lead 改為 `READY`；開工前先把 `Status` 改 `IN_PROGRESS` 並直接 push 到 master（若 session 只能推指定分支，在 Result 註明）；完成後開 PR，`Status` 改 `PR_OPENED`、填 Result。
- 只改後端；不要動前端、`.github/workflows/`、`CLAUDE.md`。

## Dependencies
TASK-057（前端不再讀 `scoring`／`risk`）。間接：TASK-056（黃金 JSON 必須已存在且保留）。

## Result
（Engineer 填寫）

## Review
（Tech Lead 填寫）
