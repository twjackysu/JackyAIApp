# TASK-052：Data Analysis「問 AI」輸入處加一行資料外送告知（DECISION-028／需求 7）

## Owner
Frontend Engineer

## Status
IN_PROGRESS

## Priority
LOW（成本近零、純前端、可逆；但它是 PM 為隱私面補上的唯一一道告知，不要拖太久）

## Objective
目前「問 AI」每次提問都會把 **欄位名稱** 與 **各欄位可選的值** 傳送給 OpenAI（Tech Lead 2026-10-13 審閱 `BuildNlPlanAsync`：來源裡「相異值 ≤300 個」的字串欄位，最多 30 欄、合計 ≤2000 個值，連同使用者的問題一起放進 prompt）。使用者連自己的資料庫／上傳檔案時，低基數欄位若是人名、部門、客戶代碼，這些值也會被送出，但畫面上沒有任何告知（`jackyaiapp.client/src/DataAnalysis/` 內找不到相關文字）。

PM 已決定（DECISION-028，PM 預設、Boss 可撤回）：在「問 AI」輸入處加**一行白話告知**。不是阻擋式同意框、不改功能行為、不做「排除欄位」（那是 Future Consideration）。

## Requirements
1. 在 `DataAnalysis/components/SummarizeTab.tsx` 的 AI 模式面板（`mode === 'ai'` 的 `<Paper>`），**輸入框正下方**加一行**永遠可見**的說明文字。建議用 `TextField` 的 `helperText`（MUI 會自動以 `aria-describedby` 連到輸入框，螢幕閱讀器會讀到）；改用 `Typography variant="caption" color="text.secondary"` 也可以。
2. **不得放進 Tooltip／hover 才出現的元素**（`jackyaiapp.client/CLAUDE.md`「No hover-only tooltips」：觸控裝置看不到）。也不要放進 `InfoPopover`——告知要直接看得到。
3. 文案（可微調語氣，但三件事都要寫到、且須白話）：
   - **會傳送什麼**：你的問題、資料的欄位名稱、各欄位可選的值（例如地區、產品類別）。
   - **傳給誰**：AI 服務（OpenAI）。
   - **不得**暗示資料不會離開本站，也不要寫「不會儲存」「安全」「已去識別化」之類本站無法保證的承諾。
   - 建議文案（起點）：`提問時，會把你的問題、這份資料的欄位名稱與各欄位可選的值（例如地區、產品類別）傳送給 AI 服務（OpenAI）來理解問題。`
4. **文案必須與實際行為一致**：實作前先讀 `JackyAIApp.Server/Controllers/DataAnalysisController.cs` 的 `NlSummarize`／`BuildNlPlanAsync` 與 `Services/…/NlValueSources`、`NlPlanValidator`（含「帶著提示重跑一次」那條路徑），確認 prompt 裡實際有哪些資料；若發現還有別的東西被送出（例如重跑時附上的候選值），把它補進文案或在 PR 說明提出，**不要**只照上面的建議文案貼上。
5. 訪客與已登入者看到**同一段文字**（面板對兩者都顯示；訪客按鈕寫「登入以使用 AI」，告知仍要看得到，因為這是他們決定要不要登入前該知道的事）。
6. 手機寬度（≤400px）下要自動換行，不造成橫向捲動。
7. 不新增依賴、不改 API／後端、不改現有的「例如」chips、最近查詢、按鈕行為。

## Acceptance Criteria
- AI 模式面板（登入與未登入兩種狀態）皆可看到告知文字，且位於輸入框附近（不需點擊或 hover）。
- 新增前端測試（Vitest + Testing Library，沿用 `SourcePanel.test.tsx`／`DetailsTab.test.tsx` 的寫法）：
  - 文字在已登入與未登入時都存在（斷言包含「OpenAI」與「欄位名稱」關鍵字即可，不要逐字比對整句，避免日後改文案就紅）。
  - `SummarizeTab` 屬性很多、直接 render 很繁瑣時，**可以把告知抽成一個很小的元件**（例如 `AiDataNotice`）並測它，再用一個測試確認 `SummarizeTab` 的 AI 模式有渲染它；不要為了測試重構 `SummarizeTab`。
- `npm run lint`（0 warnings）、`npm run build`、`npm test`（或專案現行的 vitest 指令）全過。
- 手機寬度的畫面請在 PR 說明寫明你怎麼確認（截圖或 devtools 400px 檢查皆可）；沒辦法實機就寫「僅依 CSS 判斷、未實機」，不要宣稱已驗證。

## Technical Notes
- 現況：`SummarizeTab.tsx` 約第 337–423 行是 AI 模式面板；`<TextField multiline …>` 在第 351 行附近，下方是 `NL_EXAMPLES` chips，再下方是「最近查詢」與按鈕。
- 這是**告知**，PM 明確**未查證**這一行是否足以符合個資法／OpenAI 使用條款（DECISION-028 Impact）。Engineer 與 Tech Lead 都不要在 PR 或文案裡下合規結論；若 Boss 之後要更強的做法（排除欄位、只有登入者可連自己的來源），那是新的範圍決策，另開任務。
- 預設示範資料（`dbo.Orders`）是虛構資料，風險集中在使用者自己的來源；文案不需要區分來源類型。
- 流程：開工前先把本檔 `Status` 改 `IN_PROGRESS` 並直接 push 到 master（`ARCHITECTURE.md`「Workflow conventions」）；完成後開 PR，PR 描述寫明文案依據的程式碼位置，並把本檔 `Status` 改 `PR_OPENED`、填 Result。
- 只改前端；不要動 `JackyAIApp.Server`、`.github/workflows/`、`CLAUDE.md`。

## Dependencies
無。（與匿名匯出的硬化無關；該項已決定暫不排工，見 `ARCHITECTURE.md` 最後一節。）

## Result
（Engineer 填寫）

## Review
（Tech Lead 審閱後填寫）
