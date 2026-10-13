# NL Summarize — Natural-Language Data Analysis Plan

讓 DataAnalysis demo 的 Summarize 多一個「用自然語言問」模式：使用者在 text area 打
「給我 TW 區的 ARR 用 Account Segment 切分」→ 送出 → 後端用 LLM 把它翻成 summarize 參數
並執行 → 直接回 table，使用者再用現有圖表切換鈕自由換圖。

手動模式維持不變（自己設 filters / viewBy / measures / field transforms）。
兩個模式各走各的，**NL 模式不需要回灌編輯器**。

---

## 架構決策

- 後端**自己查 schema**（不信任前端傳來的欄位），單一真相來源、也防 LLM 亂編欄位名。
- **一支 endpoint 同時翻譯 + 執行**，回 `{ plan, rows }`：`plan` 給前端顯示「AI 解讀」小字，
  `rows` 直接餵進現有結果區。一次 round trip，符合「送出就拿到結果」。
- 收費：`NL_SUMMARIZE = 2`。登入者扣、匿名走 demo 預設源不扣（比照 `ExamService.EvaluateSentenceAsync`
  以 `userId` 是否存在判斷）。理由：單次小型 structured-output 調用（in ~2–3k / out ~300 tokens），
  比 `STOCK_ANALYSIS=5` 輕，和 `CLOZE/SENTENCE=2` 同級；DB 查詢不花 token。

---

## 復用的現有積木

| 積木 | 位置 |
|------|------|
| `IOpenAIPromptService.GetCompletionWithSystemPromptAsync<T>()` — NL→強型別物件 | `Services/OpenAI/OpenAIPromptService.cs` |
| `SummarizeRequestDto`（ViewBy/Measures/Filters + DateTrunc/NumericBin/StringGrouping） = LLM 輸出 schema | `DTO/DataAnalysis/DataAnalysisDto.cs` |
| `SummarizeAsync` 執行 + `MapGroupField`/`MapFilter` | `Controllers/DataAnalysisController.cs` |
| schema 解析（engine `FieldListAsync` / `ISessionDataStore.GetSchema`） | controller `TryResolveAsync` / `SessionDataStore` |
| `PromptLoader`（嵌入式 `.txt`） | `Services/Prompt/PromptLoader.cs`、`Prompt/` |
| credit 扣點 `ICreditService.ConsumeCreditsAsync` | 既有 |
| 結果區 table + 圖表切換鈕（table/column/bar/line/pie/stacked） | `components/SummarizeTab.tsx`、`SummarizeChart.tsx` |

---

## 實作更新：先查真實值再組 plan 的 agent（避免 LLM 幻覺 filter 值）

> 初版單次 call 會讓 LLM 自己猜 filter 值（例：使用者打「UK 的營收」，但 demo 的 Region 只有
> APAC/EMEA/NAMER → LLM 幻覺出 "UK" → 篩不到 row）。改成後端自己當 agent，先查真實值再組 plan。

固定步驟的 pipeline（不需 Dify、不需開放式 tool-use loop）：

1. **後端抓 LoV**：查所有字串欄的 distinct values（engine 走 `SummarizeAsync` + Count 當 SELECT DISTINCT，
   快取 5 分鐘；session 走 `GetDistinctValues`）。不重複值 ≤ 300 的欄位整份放進 prompt（總量上限 2000 個值、
   最多 30 欄）；超過 300 的（CustomerName 之類）不放 prompt，但仍會驗證。prompt 不寫死任何資料集的值，
   AI 對照的是當下資料裡真正的代碼。
2. **LLM 產 plan**（`NlSummarizeSystem.txt` → `SummarizeRequestDto`）：給 schema + 真實值清單 + today + 資料最新日期。
   prompt 規定 filter 值**必須**從真實清單複製（中文、全名、同義詞要翻成清單裡的代碼），配不到就略過該 filter。
3. 驗證欄位名 → **驗證篩選值**（`NlPlanValidator.ValidateFilterValues`：不存在的值移除並附中文提示）
   → 有值被移除時**重跑一次**，附上「哪些值不存在」＋可選值（≤ 300 的欄位給整份清單，更大的欄位給
   `SuggestCandidates` 找出的相近值）→ 執行 → 回 `{ plan, interpretation, rows, notes }`。
4. 日期 Between 的結束值若只有日期，執行時會改成「起日 ≤ x < 結束日+1」
   （`DataAnalysisMapping.ExpandDateOnlyRangeEnds`），讓有時間的欄位包含結束日整天。

> 2026-10 之前是兩階段：先問 LLM「要篩選哪些欄」（`NlFilterPlanSystem.txt`），只抓那幾欄的值。
> Phase 1 常把「各產品類別」當篩選、或漏選同義詞所屬的欄（「拉丁美洲」→ Region），Phase 2 就只能自己猜值。
> 現在每欄都直接查，Phase 1 那次呼叫已移除。

共用 helper `BuildNlPlanAsync(fields, query, prompt, distinctFetcher, maxDateFetcher, ct)` 兩條路（engine / session）共用，
engine 只 `TryResolveAsync` 一次（schema + LoV + execute 共用同個 ctx，custom source 不重複建連線）。
credit 在 schema 解析成功後、LLM call 前扣（`TryChargeNlSummarizeAsync`）。

---

## Phase 1 — 後端翻譯 + 執行 endpoint（核心）

1. **新 DTO**（`DataAnalysisDto.cs`）
   ```csharp
   public sealed record NlSummarizeRequestDto
   {
       public QueryContextDto? Context { get; init; }
       public string?          SessionId { get; init; }
       public required string  Query { get; init; }
   }
   ```
2. **新 endpoint** `POST /api/dataanalysis/summarize/nl`（`DataAnalysisController`）
   1. 解析 schema：有 `SessionId` → `_sessionStore.GetSchema`；否則 `TryResolveAsync` + `FieldListAsync`。
   2. 取 maxDate（供「上一季」之類相對日期），可選。
   3. 組 system prompt = 靜態 SKILL 模板（`PromptLoader`）+ 動態注入欄位清單（name + canonicalType）+ 今天日期/maxDate。
   4. `GetCompletionWithSystemPromptAsync<SummarizeRequestDto>(systemPrompt, query, model)`。
      - model 建議 `NewModels.GPT_5_MINI`（discriminated-union 較可靠）；可先試 nano 省錢。
   5. **驗證層**：
      - ViewBy / Measures / Filters 的 `FieldName` 必須 ∈ schema 欄位（擋幻覺欄位）。
      - aggregation / operator / granularity enum 合法。
      - ViewBy、Measures 至少各一；不合法回友善錯誤（讓前端顯示「AI 看不懂…請換個說法」）。
   6. **執行**：把驗證過的 DTO 走和 `Summarize`/`SessionSummarize` 同一條路執行。
   7. 回 `{ plan: SummarizeRequestDto, rows, interpretation? }`。
      - `interpretation` 可由後端用欄位組一句人讀字串（"Grouping by Account Segment, summing ARR, filtered to Region = TW"），或前端自組。
3. **credit**：登入者執行前 `ConsumeCreditsAsync(userId, CreditCosts.NlSummarize, "nl_summarize", ...)`；匿名（預設源）不扣。
   - `CreditCosts.cs` 加 `public const ulong NlSummarize = 2;`
   - `creditCosts.ts` 加 `NL_SUMMARIZE: 2`

> 重構提醒：`MapGroupField`/`MapFilter`/`NullIfEmpty` 目前是 controller 的 private static。
> NL endpoint 也要把 DTO→engine model，建議把這段抽成共用 helper（或直接複用，避免重複）。

---

## Phase 2 — SKILL / system prompt（成敗關鍵）

新檔 `Prompt/DataAnalysis/NlSummarizeSystem.txt`（記得設為 EmbeddedResource，比照現有 Prompt/*.txt）。
要教 LLM：

- **輸出 JSON schema**：`SummarizeRequestDto` 形狀，含三種 GroupField 子型別的 discriminator
  （`Type` = "Plain" | "DateTrunc" | "NumericBin" | "StringGrouping"）。
- **GroupField 選型**：
  - 日期欄 +「按月/季/年/週」→ `DateTrunc` + `Granularity`（Year/Quarter/Month/Week/Day/Hour）。
  - 數值欄 +「價格區間/年齡層」→ `NumericBin` + `Bins`。
  - 類別欄需合併 →`StringGrouping` + `Groups`。
  - 其餘 → `Plain`。
- **措辭 → aggregation**：total/sum→Sum、how many/count→Count、average→Average、max/min→Max/Min、
  unique/distinct→DistinctCount。
- **措辭 → filter operator**：only/is→In or Equals、exclude/not→NotIn、more than→GreaterThan、
  less than→LessThan、between→Between、contains→Contains。
- **動態段**（每次注入）：可用欄位清單（名 + canonicalType）、今天日期、maxDate。
- **3–5 組 few-shot**，含一組需要 DateTrunc、一組需要 filter、一組「TW 區 ARR by Account Segment」。
- 規則：只能用清單內欄位；看不懂就回空 ViewBy（讓後端驗證層擋下並回友善錯誤）。

---

## Phase 3 — 前端模式切換 + NL 輸入

- `SummarizeTab.tsx` 最上方加 `ToggleButtonGroup`：**手動 ⇄ 問 AI**。
- **問 AI 模式**：
  - 多行 `TextField`（placeholder 範例：「給我 TW 區的 ARR 用 Account Segment 切分」）。
  - 範例 chips 一鍵帶入（「各區營收」「上季前十大客戶」「各產品類別平均單價」）。
  - 「Generate」按鈕（loading 時 disable）。
  - 送出 → 新 `useNlSummarizeMutation`（`dataAnalysisApis`）→ 拿到 `{ plan, rows }`。
  - 顯示 `interpretation` 小字（AI 解讀，建立信任）。
  - rows 直接進**現有結果區**（table + 圖表切換鈕，全部現成，不用改）。
  - 402 → 沿用既有 `openCreditModal` 流程。
- **手動模式**：完全維持現狀。
- 切模式時清掉另一邊的結果/錯誤即可（不需共用 state）。

新 API（`dataAnalysisApis/index.ts`）：
```ts
nlSummarize: builder.mutation<
  ApiOkResponse<{ plan: SummarizeRequest; rows: Record<string, unknown>[]; interpretation?: string }>,
  { context?: QueryContextDto; sessionId?: string; query: string }
>({
  query: (body) => ({ url: 'summarize/nl', method: 'POST', body }),
}),
```

---

## 驗收

- [ ] `dotnet test` 全綠、`dotnet build` 無誤
- [ ] 預設源（匿名）打「給我各區營收」→ 出 table，不扣點
- [ ] 登入者用 NL → 扣 2 credits；credit 不足 → 402 → 開購買 modal
- [ ] NL 結果可用圖表切換鈕換成 column/pie 等
- [ ] 故意打不存在的欄位 → 友善錯誤、不 500
- [ ] 手動模式行為與現在完全一致
- [ ] `eslint` + `tsc` + vite build 全過

## 風險

- LLM 幻覺欄位名 → Phase 1 驗證層擋。
- discriminated union 產錯型 → prompt few-shot + 驗證層。
- 相對日期（「上一季」）→ prompt 注入 today/maxDate；V1 若不穩可先不支援，要求使用者講絕對區間。
