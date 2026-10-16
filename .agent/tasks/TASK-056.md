# TASK-056：綜合分析評分／風險 — 以現有 C# 實作產生「黃金測試資料」（Issue #464 IMP-2 第 1 步）

## Owner
Backend Engineer

## Status
ON_HOLD（2026-10-16 Boss 決定「AI 評分那段留在後端」，與本任務「刪除 C# 評分」的前提衝突；等 Boss 在 (1) 全部留後端、取消 IMP-2 與 (2) 前端另算一份、後端留一份給 AI 之間確認。**確認前不要開工。**）
（原狀態：READY）
（Backend 備註：本任務在 ON_HOLD 公告**之前**（我開工時 master＝`55e50db`，任務為 READY）已完成並開出 **PR #468**；暫停公告在我工作期間才進 master。PR 已改為 draft，**請勿在 Boss 決定前合併**。(1) 取消 IMP-2 → 關閉 PR #468、本任務取消；(2) 前端另算一份、後端留一份給 AI → 本任務「照原樣做」，PR #468 可直接審閱，不需修改。程式碼只新增測試與 JSON，沒有碰正式程式。）

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
**PR**：https://github.com/twjackysu/JackyAIApp/pull/468（分支 `claude/exciting-cray-fs6zh3`；draft，等 Boss 決定 IMP-2 (1)／(2) 後再審——見上方 Status 備註）

### 做了什麼
- 新增 `JackyAIApp.Server.Tests/Services/Finance/Scoring/StockScoringGoldenFixtureTests.cs` 與 `jackyaiapp.client/src/Finance/utils/__fixtures__/stockScoringGolden.json`（473,531 bytes）。**不修改任何正式程式碼**（沒有把 `private` 改成 `internal`）。
- 每個案例經由真正的 `StockAnalysisBuilder.BuildAsync` 產生（呼叫順序同 `FinanceQueryService.GetComprehensiveAnalysisAsync`，只 mock 資料提供者與 `IIndicatorEngine.CalculateAll`）；序列化用 API 設定；decimal 原樣輸出；一案例一行。
- 預設比對、`UPDATE_GOLDEN=1` 才寫檔；找不到 repo 根目錄／缺檔／內容不同各有明確失敗訊息（內容不同時指出第一個不同的案例 id）。另一個測試把各地雷的**手算**預期值釘在對應案例上。
- 案例共 215 個：手工 125（多於「約 60」，每個地雷獨立一案，id 對應見 PR 說明）＋ 確定性隨機 90（xorshift64、固定種子）。

### 驗證（實際執行）
- `dotnet test --filter "Category!=LiveContract"`：1346 通過、0 失敗（原 1344 ＋ 新增 2）。
- `UPDATE_GOLDEN=1` 連跑兩次 `cmp` 相同（確定性）。
- 變異檢查：任務書要求的兩個（`>= 60`→`> 60`、`MidpointRounding.AwayFromZero`）皆轉紅；另外 43 個（閾值 `>`/`>=`、`F1`→`F2`、`Min`→`Max`、少算 Strong、總分用未進位加總、面向順序、權重正規化、各旗標被忽略、文字…）**45/45 全部轉紅**；手動改 JSON 也轉紅。

### 與任務書的差異（請 Tech Lead 知悉）
1. **隨機案例 90 個，不是 ~300**：每個指標在輸入與輸出各寫一次（約 260 bytes），300 個會超過 1 MB。依 Requirement 7「超過就減少隨機案例數」下修，並把長度分佈改成偏短（70% 為 0–8 個指標）。若 Tech Lead 要更多隨機案例，需要先放寬 500 KB 的上限。
2. 手工案例 125 個而非 ~60（合計僅約 250 KB）。
3. `meta.sourceCommit` 寫的是產生當時的 master tip `55e50db`；評分三個來源檔最後被改動於 `0d03215`（PR #437），另列於 `scoringSourcesLastChangedIn`。
4. 流程：本次排程 session 只能推指定分支，`IN_PROGRESS`／`PR_OPENED` 都在 `claude/exciting-cray-fs6zh3` 上（只動 `.agent/**`），沒有直接推 master。

### 給 TASK-057 的提醒
- 風險因子的 `F1` 是 **decimal 遇 `.x5` 往遠離 0 進位**，與 JS `toFixed(1)` 不同（RSI `80.05`：C# `"80.1"`，JS `"80.0"`）；`Math.Round(decimal, 1)` 是 half-even。兩者在 TS 都不能直接用 double 運算。案例：`risk-rsi-80_05-…`、`risk-rsi-80_35-…`、`risk-rsi-19_95-…`、`risk-volume-3_05-…`、`risk-pledge-30_05-…`、`round-*`、`cat-avg-60_05-…`、`gap-uses-rounded-scores-80_05-vs-50`。
- `categoryScores` 的順序是各面向第一次出現的順序（依賴 `Dictionary` 列舉順序，非規格保證，但現行行為如此）。
- 黃金資料是 C# 現行行為而非「正確」行為（含 `includeScoring=false` 時風險少「各面向分數差距大」、`score` 不限 0–100），要原樣保留。

## Review
（Tech Lead 填寫）
