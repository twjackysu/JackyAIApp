# TASK-056：綜合分析評分／風險 — 以現有 C# 實作產生「黃金測試資料」（Issue #464 IMP-2 第 1 步）

## Owner
Backend Engineer

## Status
ON_HOLD（2026-10-16 Boss 決定「AI 評分那段留在後端」，與本任務「刪除 C# 評分」的前提衝突；等 Boss 在 (1) 全部留後端、取消 IMP-2 與 (2) 前端另算一份、後端留一份給 AI 之間確認。**確認前不要開工。**）
（原狀態：READY）

## Priority
MEDIUM（阻擋 TASK-057；本身只新增測試與一個 JSON 檔，不改任何正式程式）

## Objective
Boss 在 Issue #464 決定（2026-10-16 20:01 +0800）：**IMP-2 做，C# 版評分刪除，不要兩套邏輯並存。** 做法是三步：

1. **本任務（056）**：用**現有、正在正式環境跑的 C# 實作**，對一組涵蓋邊界的輸入產生「輸入 → 期望輸出」的黃金資料（JSON），固定下來。
2. TASK-057（Frontend）：把評分／權重正規化／方向／建議文字／面向摘要／風險評估移植到 TypeScript，並以這份黃金資料逐筆比對。
3. TASK-058（Backend）：刪除 C# 評分與風險、`GET /api/finance/score/{code}`、請求裡的旗標／權重欄位。

為什麼要先固定黃金資料：C# 刪掉後就沒有任何「正確答案」可以對照；而這些是會顯示在畫面上的**投資建議文字與分數**，移植錯一個閾值或一個進位（見下方「地雷」）使用者看不出來，只會靜默地拿到不同的建議。黃金資料必須由**真正的生產程式路徑**產生，不是工程師另寫一份「期望值」。

## Requirements
1. 新增測試類別（例如 `JackyAIApp.Server.Tests/Services/Finance/Scoring/StockScoringGoldenFixtureTests.cs`）。每個案例**經由真正的 `StockAnalysisBuilder.BuildAsync`** 產生期望輸出——沿用 `StockAnalysisBuilderTests` 的 mock 方式（`IIndicatorEngine.CalculateAll` 回傳該案例的指標清單；TW 區域；providers 回最小的 `MarketData`）。**不要**在測試裡複製 `BuildScoring` 的步驟（它是 private，複製等於另寫一份）。
2. 每個案例包含：
   - `indicators`：輸入的指標清單（`IndicatorResult`，欄位 `name/category/value/subValues/signal/direction/score/reason`）。
   - `config`：`includeTechnical/includeChip/includeFundamental/includeScoring/includeRisk` 與 `technicalWeight/chipWeight/fundamentalWeight`（前端永遠送三個權重，所以案例都三個都給）。
   - `expected`：`StockAnalysisResult` 的 `indicators`（**過濾後**、含順序）、`scoring`（`overallScore/overallDirection/recommendation/categoryScores[]`，各項含 `category/score/weight/weightedScore/direction/summary/indicatorCount`；`scoring` 可為 `null`）、`risk`（`level/factors[]/divergenceScore`，可為 `null`）。
3. 序列化**使用 API 實際的設定**（camelCase、列舉輸出字串；`Program.cs` 有 `JsonStringEnumConverter`），讓 JSON 與前端實際收到的形狀一致。
4. 輸出到 **`jackyaiapp.client/src/Finance/utils/__fixtures__/stockScoringGolden.json`**（該目錄已有其他 fixture）。換行用 `\n`（repo 的 `.gitattributes` 會轉 LF）。檔案頂部欄位註明：由哪個 commit 的 C# 產生、用途（TASK-057 的移植黃金資料；C# 刪除後凍結）。
5. 測試的行為：預設是**比對**（重新用現有程式產生，與已提交的 JSON 逐字相等，否則失敗並提示如何更新）；設定環境變數（例如 `UPDATE_GOLDEN=1`）時才**寫檔**。找不到 repo 根目錄（含 `jackyaiapp.client` 的上層）時給出明確失敗訊息，不要靜默略過。
6. 案例集（**案例必須由測試內的確定性程式產生，隨機部分請自備小型 LCG／xorshift，不要用 `System.Random`**——避免不同 .NET 版本序列不同）：
   - **手工邊界案例（約 60 個）**，至少涵蓋下列「地雷」，每個各自一案或併入合理案例，`id` 要看得出測的是哪一個：
     - 空指標清單；只有單一面向；三面向交錯排列（鎖定 `categoryScores` 的順序＝各面向**第一次出現**的順序）；基本面排在最前面。
     - 面向平均剛好在 80／60／40／20 與略低於（例如三個指標 80,80,79）→ 方向閾值。**面向方向用的是未進位的平均**。
     - 面向平均落在 `.25`／`.75`（四個指標、總分不被 4 整除）→ `Math.Round(decimal, 1)` 是**銀行家進位（half-even）**，不是四捨五入（62.25 → 62.2，不是 62.3）。權重相乘後落在 `.x5` 的案例也要有（如平均 62.25 × 權重 0.2）。
     - 加權總分：`overallScore = Σ(各面向「已進位」的 weightedScore)`，再拿這個總和判方向（80／60／40／20 恰好等於邊界的案例，如三個進位後加總正好 60.0）。
     - 權重：預設 0.2／0.3／0.5；0.5／0.3／0.2；其中一個為 0；**三個全為 0**（`GetNormalizedWeights` 在總和為 0 時**不正規化**，加權分全 0）；某面向沒有指標（權重重新分配給剩下的面向）；總和不為 1。
     - 旗標：各面向關閉（指標被過濾、面向不存在）；**`includeScoring=false` 時 `risk` 仍會算，但 `categoryScores` 當空清單傳入，所以風險因子「各面向分數差距大」不會出現**（現行怪癖，TASK-057 要原樣保留）；`includeRisk=false` → `risk` 為 `null`；全部面向關閉 → `scoring`、`risk` 皆 `null`。
     - 風險：`RSI` 值恰為 80／80.1／20／19.9（`> 80`、`< 20`，文字用 `F1`）；`VolumeRatio` 的 `subValues.TodayVsAvg20` 恰為 3／3.1；`DirectorPledge` 值恰為 30／30.1；分歧分數（`min(多,空) / 總數 × 200`）剛好等於 30／50／60 的案例（`>30`、`>60`、`>50` 都是嚴格大於）；面向分數差距恰為 30／30.1（用**已進位**的面向分數）；`factors` 個數 0／1／2／3／4＋ 各一案（對應 Low／Medium／High／VeryHigh）；分歧分數 >50 但 factors 少於 3 → `High`。
     - 建議文字：五種方向各一；技術面看多＋籌碼面看空（出貨風險句）、技術面看空＋籌碼面看多（主力佈局句）；只有技術面（沒有籌碼面，不加註）。
     - `Direction` 五種值都出現；`reason`／`signal` 含中文與特殊字元。
   - **確定性隨機案例（約 300 個）**：每案 0～30 個指標，面向、`score`（0–100 整數）、`direction`、`value` 隨機；`config` 在旗標組合與「5% 為單位」的權重間隨機。固定種子。
7. 檔案大小控制在約 500 KB 以內（超過就減少隨機案例數）。

## Acceptance Criteria
- 測試在 CI（`dotnet test --filter "Category!=LiveContract"`）通過，且會在 JSON 被手動改動、或 C# 評分邏輯被改動時**失敗**（PR 說明貼出你做過的兩個變異檢查：例如把 `DetermineOverallDirection` 的 `>= 60` 改成 `> 60`、把 `MidpointRounding` 預設改掉，各自讓測試轉紅，然後還原）。
- 已提交 `stockScoringGolden.json`；`UPDATE_GOLDEN=1` 重新產生後 `git diff` 為空（確定性）。
- **不修改任何正式程式碼**（`Services/`、`Controllers/`、`DTO/`）。若為了測試需要把某個 `private` 改成 `internal`，先在 PR 說明提出理由——預期不需要。
- PR 說明列出案例總數、各「地雷」對應的案例 `id`，以及 JSON 檔大小。

## Technical Notes
- 為什麼是 JSON 檔放在前端目錄、而不是兩邊各寫一份期望值：Boss 的原則是單一實作；這份檔案是 C# 的最後一份「證詞」，之後由前端測試永遠對照它。C# 在 TASK-058 刪除時，**測試類別一起刪，JSON 留著**。
- 這些閾值與文字目前**只存在於** `StockScoreService.cs`（`ComputeCategoryScore`、`DetermineOverallDirection`、`GenerateRecommendation`、`AssessRisk`、`GenerateCategorySummary`）與 `StockAnalysisBuilder.BuildScoring`／`BuildAsync`（過濾與 `risk = AssessRisk(filtered, scoring?.CategoryScores ?? [])`）。移植者會逐行對照這兩個檔案，你的案例要讓「抄錯任何一行」都至少有一案轉紅。
- 十進位／浮點的差異是移植最大的風險：C# 用 `decimal`（28 位有效數字、half-even 的 `Math.Round`），JS 用 double。黃金資料裡的 `score`／`weightedScore`／`divergenceScore` 以**原樣數字**輸出；`weight`（正規化後的權重，可能是 `0.2000000000000000000000000000` 這類）也原樣輸出，前端比對時會以容差處理它——不要在這邊四捨五入。
- 流程：開工前先把本檔 `Status` 改 `IN_PROGRESS` 並直接 push 到 master（若 session 只能推指定分支，照 TASK-053／054 的做法在 Result 註明）；完成後開 PR，把 `Status` 改 `PR_OPENED`、填 Result。
- 只改後端測試與上述 JSON；不要動前端程式、`.github/workflows/`、`CLAUDE.md`。

## Dependencies
無（阻擋 TASK-057）。

## Result
（Engineer 填寫）

## Review
（Tech Lead 填寫）
