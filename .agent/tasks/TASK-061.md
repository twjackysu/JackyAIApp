# TASK-061：自訂資料來源儲存失敗時，顯示後端給的原因（DECISION-037 的小缺口）

## Owner
Frontend Engineer

## Status
READY

## Priority
LOW（純前端、幾行；#478 之後這個畫面變得更常被擋，使用者卻看不到為什麼）

## Objective
#478 起，後端在儲存自訂資料來源時會檢查連線設定，擋掉會讓伺服器改用自己身分的驗證方式（Integrated Security、Managed Identity、`ActiveDirectoryDefault`…）、`AttachDBFilename`、過長的連線逾時、非 Kusto 網域的叢集位址等，並以 **HTTP 400 ＋ 標準錯誤封包** 回覆具體原因（`DataAnalysisController` 的 `POST/PUT sources` 在 `DataSourceConfigException` 時回 `ErrorCodes.BadRequest`＋`ex.Message`，訊息來自 `DataSourceConfigValidator`，例如 `Authentication=ActiveDirectoryManagedIdentity is not allowed; use SQL login (SqlPassword), ActiveDirectoryPassword or ActiveDirectoryServicePrincipal.`）。

前端 `DataAnalysis/components/SourceManagerDialog.tsx` 的儲存處理是 `catch { setSaveError('儲存失敗 — 請檢查連線資訊後再試一次。') }`（約第 157～159 行）：**不管後端說什麼都只顯示這一句**。使用者貼了 Managed Identity 的連線字串，被擋，卻只看到「請檢查連線資訊」，不知道要換驗證方式。

## Requirements
1. 儲存失敗時，顯示**後端給的訊息**；後端沒給可用訊息時，才用現在那句當 fallback。**用 `apis/utils/errorMessage.ts` 的 `getApiErrorMessage(error, fallback)`**——這是專案規定唯一能把 RTK Query 錯誤轉成顯示文字的方式（`FetchBaseQueryError.data` 不保證是標準封包），不要自己讀 `error.data.error.message`。`fallback` 傳現有那句中文。
2. 網路斷線（`FETCH_ERROR`）時該 helper 會回「Cannot reach the server…」，這是預期行為，不用另外處理。
3. 後端訊息是英文（`DataSourceConfigValidator`），畫面其他部分是中文——**接受這個不一致**，不要在前端做翻譯對照表（驗證規則日後會變，對照表會過期）。若你認為值得在訊息前面加一句中文引導（例如「儲存失敗：」），可以，但訊息本身要原樣顯示。
4. 其他兩個 `儲存失敗` 字串（`Dashboard/ShareDialog.tsx`、`Portfolio/HoldingsEditor.tsx`）**不在本任務範圍**，不要順手改。
5. 不動後端、`.github/workflows/`、`CLAUDE.md`。

## Acceptance Criteria
- 新增前端測試（Vitest + Testing Library，沿用 `SourcePanel.test.tsx` 等 DataAnalysis 現有測試的寫法）：儲存的 mutation 以含 `{ status: 400, data: { error: { code, message: '…' } } }` 的錯誤失敗時，畫面顯示該 `message`；以沒有可用訊息的錯誤（例如 `{ status: 500, data: '<html>…' }`）失敗時，顯示現有的 fallback 句。
- `npm run lint`（0 warnings）、`npm run build`、`npm test` 全過。

## Technical Notes
- PM 於 2026-10-19 讀碼發現這個缺口（DECISION-037）；Tech Lead 讀 `DataAnalysisController`／`DataSourceConfigService` 確認後端確實回 400＋原因（讀碼，沒有實際打過 API）。
- 先檢查 `SourceManagerDialog.tsx` 是否還有其他呼叫（例如「測試連線」或刪除）同樣吞掉錯誤；有的話在 PR 說明提出，不要擴大範圍。
- 流程：開工前先把本檔 `Status` 改 `IN_PROGRESS` 並直接 push 到 master（`ARCHITECTURE.md`「Workflow conventions」；若 session 只能推指定分支，在 Result 註明）；完成後開 PR，`Status` 改 `PR_OPENED`、填 Result。開工前先看 master 是否已有人改掉這段，已有就標 `CANCELLED`。

## Dependencies
無。

## Result
（尚未開工）

## Review
（尚無）
