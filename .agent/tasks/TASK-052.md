# TASK-052：Data Analysis「問 AI」輸入處加一行資料外送告知（DECISION-028／需求 7）

## Owner
Frontend Engineer

## Status
MERGED（PR #462，Boss 於 2026-10-15 17:59 +0800 合併，merge commit `86475c8`；`Build and deploy` run 552 成功）

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
- **PR**：https://github.com/twjackysu/JackyAIApp/pull/462（分支 `claude/nice-allen-jlxwiw`）。純前端，3 個檔案：`DataAnalysis/constants.ts`（`AI_DATA_NOTICE`）、`DataAnalysis/components/SummarizeTab.tsx`（輸入框 `helperText`）、新增 `SummarizeTab.test.tsx`。
- **實作**：AI 模式面板輸入框正下方的 `helperText`，永遠可見、訪客與已登入者同文，沒有放進 Tooltip／InfoPopover；MUI 自動以 `aria-describedby` 連到輸入框。
- **文案依據**（`DataAnalysisController.BuildNlPlanAsync`）：除了建議句的「問題、欄位名稱、各欄位可選的值」，prompt 還帶**日期欄位的最新日期**（`dateBlock`／`NlDateRanges`），而「帶著提示重跑一次」那條路徑會再附上**從資料搜出的相近值，包含太大而沒列進第一次 prompt 的欄位**（`FindCandidatesAsync`→`SearchAsync`→`[CORRECTION]`）。兩項都補進文案；沒有「不會儲存」「安全」之類的承諾，也沒有下合規結論。
- **測試**：新增 `SummarizeTab.test.tsx`（登入／未登入都含「OpenAI」與「欄位名稱」；告知為 `…-helper-text` 且輸入框 `aria-describedby` 指向它）。暫時拿掉 `helperText` 時 3 個測試全失敗，還原後通過。本機實際執行：`npm run lint`（0 warnings）、`npm run build`、`npm test`（62 檔／682 測試）全過。
- **手機寬度**：僅依 CSS 判斷、未實機（`fullWidth` TextField 內的區塊 `<p>`，中文可任意字元換行）。
- **流程偏離**：本次 session 只能推到指定分支，`IN_PROGRESS` 與 `PR_OPENED` 都在 `claude/nice-allen-jlxwiw` 上，沒有照 Workflow conventions 推到 master。
- **請 Tech Lead 決定**：文案第二句（重跑時附相近值）是否保留；刪掉會讓文案與實際行為不一致。

## Review
**Tech Lead 審閱（2026-10-15，PR #462，head `c64b2b3`）— 技術上可接受 → READY FOR BOSS REVIEW**（不代表核准合併，合併由 Boss 決定）

- **文案對照 `DataAnalysisController.BuildNlPlanAsync`（我逐行讀過，結論：文案與實際送出的內容一致）**：
  - 「你的問題」＝`GeneratePlanAsync(query)` 的 user message。
  - 「欄位名稱」＝`fieldList`（全部欄位的 `名稱(型別)`，`AVAILABLE COLUMNS`）。
  - 「各欄位可選的值」＝`lovBlock`（相異值 ≤300 的字串欄位、最多 30 欄、合計 ≤2000 個值）。
  - 「最新日期」＝`dateBlock`（最多 3 個日期欄位的 MAX，加上 `NlDateRanges.Describe` 算出的相對區間）。**Engineer 發現的、任務建議句沒寫的一項，核對屬實。**
  - 「值找不到時，再附上相近的值重問一次」＝`result.Rejected.Count > 0` 才走的重跑；太大而沒列進第一次 prompt 的欄位，由 `FindCandidatesAsync`→`valueSource.SearchAsync` 取最多 `MaxRetryCandidates`（20）個真實值放進 `[CORRECTION]`——**這確實是第一次 prompt 沒有的資料（例如客戶名稱），不寫會讓文案少報**。Engineer 的「保留第二句」判斷正確，我同意保留。
  - 此外 prompt 還含靜態系統提示與 `TODAY`（日期），不屬資料，不需寫。查詢結果的資料列**不會**送給 OpenAI（`GetCompletionWithSystemPromptAsync` 是整個管線唯一的 OpenAI 呼叫點）。
  - 訪客／已登入者、資料庫／上傳檔案（`SessionNlValueSource`）兩條來源走同一個 `BuildNlPlanAsync`，所以一段文案涵蓋全部來源。
- **實作**：`helperText={AI_DATA_NOTICE}` 在 `mode === 'ai'` 的 `<Paper>` 內，沒有 `isLoggedIn` 條件，故訪客與已登入者同文；不在 Tooltip／InfoPopover 內；不改 API、不新增依賴、不動 chips／最近查詢／按鈕。文案沒有「不會儲存」「安全」「已去識別化」之類承諾，也沒有合規結論——符合任務書。
- **測試**：`SummarizeTab.test.tsx` 3 個測試（登入／未登入含「OpenAI」「欄位名稱」；告知為 `…-helper-text` 且 textarea 的 `aria-describedby` 指向它）。只比關鍵字、不比整句。CI run 683 兩個 job（`lint-and-build-client`、`build-and-test-server`）皆綠；PR 描述寫明本機 lint／build／test（62 檔／682 測試）全過，並做過變異驗證。**我沒有在沙箱重跑前端測試**（沒有 `node_modules`），以 CI 為準。
- **範圍**：PR 改動 5 個檔案——3 個前端、`TASK-052.md`、`PROJECT_STATUS.md`；沒有動 `JackyAIApp.Server`、`.github/workflows/`、`CLAUDE.md`。流程偏離（`IN_PROGRESS` 沒推 master，因為 session 只能推指定分支）已在 PR 與 Result 說明，可接受。
- **未驗證／限制（誠實列出）**：(1) 手機 ≤400px 的畫面：Engineer 寫「僅依 CSS 判斷、未實機」，我也沒有渲染過；`fullWidth` 的 `TextField` 的 `helperText` 是區塊 `<p>`，中文可任意字元換行，**預期**不會橫向捲動，請 Boss 部署後在手機看一眼（與既有的「Exam 選單 ≤400px」一併）。(2) 文案較長（兩句約 90 字），在輸入框下方會佔 2～4 行；這是為了與行為一致的取捨，若 Boss 嫌長，刪第二句會讓文案少報，**我不建議**。(3) 這只是告知，PM 與我都沒有查證它是否足以符合個資法／OpenAI 條款（DECISION-028 Impact）。
- **維護不變式**：日後只要改 `BuildNlPlanAsync` 放進 prompt 的內容（新增區塊、改重跑附帶的資料、改欄位／值的上限邏輯），**必須同步改 `AI_DATA_NOTICE`**。`constants.ts` 的註解已寫明；沒有自動比對的測試（與 `SitePages.cs`／`apps.tsx` 同類的人工對應）。
- **衝突提醒**：PR #462 與 master 目前無衝突（`mergeable_state: clean`）。我這次刻意**沒有**動 `PROJECT_STATUS.md` 與本檔的 `Status`／`Result`，避免 Boss 合併時出現「兩邊都在檔尾追加」的衝突；PR 合併後我再補狀態與把 Status 改 `MERGED`。

**合併後確認（Tech Lead，2026-10-15 第 11 次排程）**：Boss 於 2026-10-15 09:59 UTC（17:59 +0800）合併 PR #462，merge commit `86475c8`；其 parent 為 master `4162a56` 與 PR head `c64b2b3`——**合併進去的就是我審閱的 head，審閱後沒有新提交**。`Build and deploy` run 552（push 觸發）成功，正式環境＝`86475c8`。Status → MERGED。仍未驗證：手機 ≤400px 的畫面（Boss 部署後目視，非阻擋）；合規 PM 與我都未查證，不下結論。
