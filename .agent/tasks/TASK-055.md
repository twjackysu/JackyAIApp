# TASK-055：台股代號形狀的輸入（含 5～6 碼 ETF）直接當代號，不經 AI（需求 6 / Stock Intelligence）

## Owner
Backend Engineer

## Status
PR_OPENED

## Priority
MEDIUM（PM 於 REQUIREMENTS「Stock Intelligence 自動趨勢線」需求 6 評為 MEDIUM；改動極小）

## Objective
`StockCodeResolver.ResolveStockCodeAsync` 的 `IsValidStockCode` 只接受**恰好 4 位數字**。讀碼結論（Tech Lead 2026-10-16，沙箱連不到正式環境與 OpenAI，**未實機**）：

- 輸入 `00878`／`00919`／`006208`／`00631L` 這類 5～6 碼（可帶尾端字母）的代號 → 不是「有效代號」→ 比對上市公司清單（只有 4 碼公司，沒有 ETF）→ `FindExactCompanyMatchAsync` **無條件送一次 OpenAI**（前 100 家公司清單、提示詞要求「只返回 4 位數字」）→ 多半回 `NONE` → 回空字串 → `comprehensive-analysis`／`price-bars` 回「Unable to resolve stock code」。
- 結果：熱門 ETF 在 Stock Intelligence **查不到**（Boss 自己的 Portfolio 就持有 00631L、00685L），而且**每次查都花一次 OpenAI**（匿名也一樣，全站沒有 rate limiter）。
- 前端 `detectMarket`（`^\d{4,6}[A-Za-z]?$`）與後端 `MarketDataProviderFactory.TwStockPattern`（同一個 regex）**早已把這類字串視為台股代號**，只有解析器沒跟上。

做法：解析器對「長得就是台股代號」的輸入直接回傳（trim＋大寫），不呼叫 AI、不取公司清單。

## Requirements
1. 在 `ResolveStockCodeAsync` 最前面：`var input = userInput?.Trim()`；若符合 `^\d{4,6}[A-Za-z]?$`（**與 `MarketDataProviderFactory.TwStockPattern` 同一個規則——請共用一份定義，不要再複製第二個 regex**；例如把它改成 `internal static` 或抽一個小的 `TwStockCode` 輔助），直接回傳 `input.ToUpperInvariant()`（`00631l` → `00631L`，TWStockLib 的 ETF 代號是大寫）。
2. **AI 回傳值的驗證維持嚴格 4 碼**（`FindExactCompanyMatchAsync`／`ResolveWithAIAsync` 裡對 LLM 結果的 `IsValidStockCode`）：公司清單只有 4 碼上市公司，LLM 回 5～6 碼等於幻覺。請把「使用者輸入的形狀判斷」與「LLM 結果驗證」分成兩個名字清楚的方法，不要共用一個。
3. 名稱輸入（`台積電`、`TSMC`…）的路徑、快取、提示詞、日誌**一行不改**。
4. 不改 `IStockCodeResolver` 介面、任何 controller／DTO／前端。

## Acceptance Criteria
- `StockCodeResolverTests` 新增（沿用該檔的 mock 方式）：
  1. `00878`、`00919`、`006208`、`00631L`、`00631l`、`2330`、`  2330  `（前後空白）→ 回傳正規化後的代號（`00631L`、`2330`），**且 `IHttpClientFactory` 建出的 client 沒有發出任何請求、`IOpenAIService` 沒有被呼叫**（用 Moq `Verify(..., Times.Never)`）。
  2. 名稱輸入（既有測試）行為不變、既有測試**不改斷言**。
  3. 長得像代號但不合規則的輸入（`123`、`1234567`、`23A0`、空字串）**不**被當作代號（走原路徑）。
- `dotnet test --filter "Category!=LiveContract"` 全過。
- PR 說明回報兩件**查證**（讀碼即可，不需要連網；查不到就寫「不確定」，不要猜）：
  1. 輸入一個**不存在**的代號（例如 `99999`、`9999`）時 `comprehensive-analysis` 目前回什麼（錯誤？空指標的成功回應？）——這是既有行為（4 碼不存在的代號今天就走同一條路），**本任務不改**，只回報，讓 PM 決定要不要另開「明確訊息」任務。
  2. 5～6 碼 ETF 通過解析後，各資料提供者（`TWStockLibHistoricalProvider`／`TWSEChipDataProvider`／`TWSEFundamentalDataProvider`）依代號比對的方式，ETF 預期有哪些資料、哪些會是空的（讀碼推論；TWSE 的籌碼／基本面資料集以「公司代號」比對，ETF 多半查不到，應是優雅地空值而非例外——請確認沒有哪個 provider 會因找不到列而丟例外讓整個分析失敗）。

## Technical Notes
- **範圍控制**：ETF **名稱**搜尋（輸入「元大高股息」）不在本任務——公司清單 `t187ap03_L` 不含 ETF，要支援需要另一份 ETF 清單，屬 PM 的優先序（Future Work）。
- **為什麼不是單純放寬 `IsValidStockCode`**：那樣 LLM 回 `NONE`／幻覺 5 碼字串的驗證也會被放寬；且「代號形狀的輸入」根本不該進 AI。
- 順帶效果：全站唯一的匿名 AI 暴露面少掉最可能的觸發情境（Tech Lead 2026-10-16 記錄的技術債 (a)）；名稱輸入的 AI 暴露面不變，仍是技術債。
- 與 TASK-056／057／058（IMP-2）**互不相干，可並行**；兩邊都不會碰同一個檔案（IMP-2 的 058 會動 `FinanceQueryService.cs`，本任務不動它）。
- 流程：開工前先把本檔 `Status` 改 `IN_PROGRESS` 並直接 push 到 master（`ARCHITECTURE.md`「Workflow conventions」；若 session 只能推指定分支，照 TASK-053／054 的做法在 Result 註明）；完成後開 PR，把 `Status` 改 `PR_OPENED`、填 Result。
- 只改後端；不要動前端、`.github/workflows/`、`CLAUDE.md`。

## Dependencies
無。

## Result
**PR：** https://github.com/twjackysu/JackyAIApp/pull/467（分支 `claude/exciting-cray-2whj9h`）

**實作**
- 新增 `Services/Finance/TwStockCode.cs`（`internal static`）：台股代號形狀 `^\d{4,6}[A-Za-z]?$` 的唯一定義，`TryNormalize` 回傳 trim＋大寫後的代號。`MarketDataProviderFactory` 的 `TwStockPattern` 已刪，改用它（同一個規則，沒有第二份 regex）。
- `StockCodeResolver.ResolveStockCodeAsync` 最前面命中形狀就直接回傳，不取公司清單、不呼叫 AI；原 `IsValidStockCode` 改名 `IsFourDigitCompanyCode`，只驗證 LLM 回傳值（仍嚴格 4 碼）。名稱輸入路徑、快取、提示詞、日誌未改；介面／controller／DTO／前端未動。

**測試**（本機實際執行）：`dotnet test --filter "Category!=LiveContract"` → 1358 通過／0 失敗；`StockCodeResolverTests` 3 → 17（代號形狀 7 例：HTTP 0 請求、OpenAI 零呼叫；非代號形狀 4 例：走原路徑；LLM 回 5～6 碼被拒 3 例）。既有斷言未改，無新增編譯警告。

**兩項查證**（讀碼，未實機；完整說明在 PR 描述）
1. 不存在的代號（`9999`／`99999`）：`comprehensive-analysis` 回**成功**但近乎全空（`indicators: []`、`latestClose: null`、`companyName: ""`、`dataRange: "N/A"`、`scoring/risk: null`），並以 12 小時快取。相鄰觀察：真實代號遇上游暫時故障也會得到空結果並被快取 12 小時（分析層不檢查是否有資料）；5～6 碼不存在字串現在會到達 TWSE／TWStockLib，成本由 OpenAI 轉為 TWSE 呼叫。本任務未改，留給 PM 決定是否另開「明確訊息」任務。
2. 5～6 碼 ETF：價格／技術指標／公司名稱預期都有（`TwListedCompanyService` 清單含 ETF）；籌碼的董監持股、大股東查不到，融資券／借券／當沖與基本面的 `BWIBBU_d` 是否含 ETF 不確定；月營收、EPS 多半查不到。所有 provider 皆有 try/catch 或回 null，**沒有任何一個會因查無列而丟例外**；最差是空 `ChipData`／`null` 基本面，對應指標被跳過。

**Known issues / 給 Tech Lead**
- `\d` 會匹配全形數字（沿用原 `TwStockPattern` 行為，為維持「同一個規則」未改）。
- `IStockCodeResolver` 的 XML 註解仍寫 4-digit，已與行為不符；依第 4 點未動，請決定是否順手改。
- 本 session 只能推指定分支，認領（`IN_PROGRESS`）未能直接推 master，隨本 PR 帶入。

## Review
（Tech Lead 填寫）
