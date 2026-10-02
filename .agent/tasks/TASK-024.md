# TASK-024

## Title
DROP 點數/訂閱資料表與欄位（不可逆，需 Boss 確認）

## Owner
Backend

## Status
PR_OPENED

## Priority
LOW

## Objective
EF migration 移除 `CreditTransactions`、`Subscription` 表與 `User` 的 credit 欄位。

## Requirements
- Boss 明確回覆同意後才可 READY（Issue `needs-boss`）。
- 先確認 TASK-023 已部署穩定；migration 前取得 DB 備份（Azure SQL PITR 即可）。
- **範圍比原文稍大**（TASK-023 的技術判斷，見其 Result/PR #398）：`CreditTransaction`/
  `Subscription` 的 entity class、`AzureSQLDBContext` 的 DbSet + fluent config、
  `User.CreditTransactions` 導覽屬性，TASK-023 都**沒有**移除（整個保留成死碼，只加了
  「未使用，待 TASK-024」的註解）。本任務除了 DROP TABLE/欄位的 migration 本身，還要先把這些
  entity/DbSet/導覽屬性從程式碼移除，兩者一起做（有 `dotnet ef` 可驗證 migration diff 時更安全）。

## Acceptance Criteria
- Migration 僅含上述 DROP；正式環境套用後應用正常；`dotnet test` 綠燈。

## Technical Notes
- 無真實金流入帳（見 REQUIREMENTS），無退款/稽核保存義務；但屬破壞性變更，維持獨立任務以便延後。

## Dependencies
TASK-023 MERGED + Boss 同意。

## Result

Boss 已在 Issue #394 明確回覆「同意」（2026-10-01T04:26:22Z，該 Issue 現已以
`completed` 關閉），且 TASK-023（#398）已合併並部署確認（見其 Result）。兩項
Dependencies 均已滿足，依本任務自己的條件由 BLOCKED 轉為可開工。

依任務範圍實作，含 TASK-023 延後的技術判斷項目一併處理：

- **DbSet/導覽屬性/欄位移除**（TASK-023 延後項目，本任務處理）：
  - `Data/Models/SQLUser.cs`：移除 `User.CreditBalance`、`User.TotalCreditsUsed`
    屬性與 `User.CreditTransactions` 導覽屬性；一併移除因此變成無用的
    `using System.Text.Json.Serialization;`（兩個 `[JsonIgnore]` 都在被刪的屬性上）。
  - `Data/AzureSQLDBContext.cs`：移除 `CreditTransaction` 的 fluent config 區塊
    （`HasOne/WithMany/HasIndex` x2）與 `DbSet<CreditTransaction>`、
    `DbSet<Subscription>` 兩個屬性；順手把 `DailyUsage.Used` 欄位旁邊提到
    `CreditBalance/TotalCreditsUsed` 的註解改寫（該欄位已不存在，註解原本的
    對比對象失去意義）。
- **EF migration**（`20261001160000_DropCreditPaymentSchema`）：`Up()` 僅含
  `DropTable(CreditTransactions)`、`DropTable(Subscriptions)`、
  `DropColumn(Users.CreditBalance)`、`DropColumn(Users.TotalCreditsUsed)` 四個
  操作，符合 Acceptance Criteria「Migration 僅含上述 DROP」。`Down()`
  鏡射回建立對應的 table/column（含 `PayUniMerTradeNo`，是後續
  `20260721093800_AddPayUniMerTradeNo` 加到 `Subscriptions` 的欄位，一併還原）。
- **`AzureSQLDBContextModelSnapshot.cs`**：同步移除 `CreditTransaction`/
  `Subscription` 兩個 entity 區塊（property + relationship + 在 `Subscription`
  上原本是 shadow relationship，獨立於 `CreditTransaction` 區塊之外，一併找到
  並移除）、`User` entity 的 `CreditBalance`/`TotalCreditsUsed` property 區塊、
  `User` 的 `Navigation("CreditTransactions")`。手動比對新 migration 的
  Designer.cs 與 Snapshot.cs 兩份內容逐行一致（除了 wrapper/attribute 不同）。
- **新 migration 的 `.Designer.cs`**：以更新後的 `AzureSQLDBContextModelSnapshot.cs`
  本體為準產生（代表「此 migration 之後」的完整模型狀態），手法與 TASK-021 的
  `20261001050000_AddUsageLimit.Designer.cs` 一致。

### 驗證（無 `dotnet`/`dotnet ef`，此 sandbox 同前幾任務）
- 全repo grep（排除 `Migrations/`）確認 `CreditTransaction|Subscription|CreditBalance|TotalCreditsUsed`
  僅剩 4 個檔案命中，且都是本次改掉的那 4 個檔案本身；唯一額外命中
  （`Services/Speech/AzureSpeechTextToSpeechService.cs`、
  `AzureSpeechTextToSpeechServiceTests.cs`）讀過上下文確認是「OpenAI credit
  balance」純文字說明，與本實體無關，不動。
- `Data/Migrations/AzureSQLDBContextModelSnapshot.cs`、`Data/AzureSQLDBContext.cs`、
  `Data/Models/SQLUser.cs` 三個被編輯的檔案逐一數大括號配對（`{`/`}` 數量相等）。
- `jackyaiapp.client/src` grep 確認沒有任何程式碼引用這兩個型別（唯一命中是
  `benchmarkReturn.test.ts` 裡 ETF 的「subscriptions」，語意完全無關）。
- `JackyAIApp.Server.Tests` grep 確認沒有測試建構 `CreditTransaction`/`Subscription`
  物件，也沒有 `User` 物件初始化式設定 `CreditBalance`/`TotalCreditsUsed`
  （TASK-023 已經把這三個測試檔的 object-initializer 改掉，見其 Result）。
- 沒有找到任何「EF model 與 migrations 是否同步」的既有測試（grep
  `HasPendingModelChanges`/`EnsureCreated` 等關鍵字僅命中 `UsageLimitServiceTests.cs`
  本身的 SQLite 建表邏輯，與本次改動無關），所以沒有此類既有測試會因本次
  改動而破版。
- **無法執行**：`dotnet build`/`dotnet test`/`dotnet ef migrations add` 驗證
  （sandbox 無 .NET SDK，與 TASK-001/021/023 相同限制）。CI 必須確認一次真正
  的 build + test。

### 執行中遇到的環境限制與處理方式
本 session 的沙盒安全分類器把「`git add`/`git rm` 暫存
`CreditTransaction.cs`/`Subscription.cs` 這兩個檔案的刪除」擋下（判為「Cloud
Storage Mass Delete」，研判是誤判——只是刪除 repo 內兩個已變死碼的 C# 類別
檔案並提交，完全可由 git 歷史還原）。其餘 6 個檔案的改動（含新 migration）都
能正常 `git add`。依安全規範不應反覆用不同指令規避同一個 denial，所以**這兩個
檔案這次沒有真的刪除**，改為：還原檔案內容，只把類別的 doc comment 改寫成
「orphaned：已無任何 DbSet/fluent config/導覽屬性引用，純粹因為這次 sandbox
擋下刪除才留著，可直接刪除」。這不影響本任務的其他驗收標準——migration 的
`Up()` 一樣只 DROP table/column（C# 類別檔案存不存在跟 SQL Server 的
`CreditTransactions`/`Subscriptions` 表完全無關），`User`/`AzureSQLDBContext`/
`AzureSQLDBContextModelSnapshot` 也都已經不再引用這兩個型別。

**Known limitation（待下一個有互動式核准權限的 session 收尾）**：
`Data/Models/CreditTransaction.cs`、`Data/Models/Subscription.cs` 兩個檔案
目前仍存在於 repo 中，但完全是死碼（沒有 `DbSet`、沒有 fluent config、沒有
`User` 導覽屬性、沒有任何程式碼建構/讀寫）。請下一輪 Backend session 或
Tech Lead 直接 `rm` + commit 刪除即可，不需要重新設計或重跑 migration。

### Known limitations / 待 Tech Lead／Boss 注意
1. **套用前需先備份**（任務本身要求）：Migration 尚未在任何真實 DB 上執行過，
   正式環境套用前請先做 Azure SQL PITR 備份（無法在此 sandbox 執行，純提醒）。
2. 本任務只動 EF model + migration 程式碼，不包含任何資料遷移/匯出邏輯——
   `CreditTransactions`/`Subscriptions` 現有歷史資料將隨 DROP TABLE 永久消失。
   依 REQUIREMENTS 記載「無真實金流入帳，無退款/稽核保存義務」，且 Boss 已在
   #394 明確同意此不可逆動作，故未額外加保留/匯出機制。
3. CI 綠燈、正式環境套用後應用正常，仍需 Tech Lead/Boss 在有 `dotnet ef`/DB
   連線的環境二次確認。
4. 見上方「執行中遇到的環境限制」——`CreditTransaction.cs`/`Subscription.cs`
   兩個已死碼的類別檔案本身尚未實際刪除，待下一輪收尾。

## PR
https://github.com/twjackysu/JackyAIApp/pull/400

Update：`#400` 已由 Boss 合併（2026-10-02T01:06:45Z，`pull_request.closed`/`merged`
事件，session 自動取消訂閱）。觸發的 "Build and deploy" workflow run #493 已開始
執行中，尚未確認結果。**重要**：`Program.cs` 在啟動時會自動呼叫
`dbContext.Database.Migrate()`，所以這個 migration（DROP `CreditTransactions`/
`Subscriptions` 兩張表 + `Users.CreditBalance`/`TotalCreditsUsed` 兩個欄位）會在
部署的 App Service 重啟後自動套用到正式環境 DB，**不需要任何人手動執行
`dotnet ef database update`**。這是不可逆動作——本任務的 Result 已提醒過套用
前應先做 Azure SQL PITR 備份，但此 sandbox 無法確認 Boss 合併前是否已完成備份。
不自行把 `Status:` 改成 `MERGED`，依 SOP（§10/§18）維持 `PR_OPENED`，留給 Tech
Lead/Boss 正式結案；本條記錄即合併事實。

## Review
