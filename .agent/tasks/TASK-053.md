# TASK-053：每日挑戰的當日題目只產生一次並持久化（Issue #464 IMP-1）

## Owner
Backend Engineer

## Status
MERGED

## Priority
HIGH（會讓評分／XP 靜默算錯；改動小、可測、遷移只新增一張表）

## Objective
`DailyChallengeService` 的 `GetChallengeAsync` 與 `SubmitChallengeAsync` 每次都：載入**整個** `Words` 表（連同 Meanings → Definitions／ExampleSentences／Tags，三個集合 Include、沒有 `AsSplitQuery`，產生笛卡兒積）→ 用日期當種子**重新產生**題目。兩個後果：

1. **正確性（Tech Lead 讀碼推論，未實際重現）**：`DailyChallengeQuestionGenerator.Generate` 用**同一個** `Random(seed)`，先為每個合格單字抽一個洗牌 key，之後每題的選項順序與正解位置也從**同一條**亂數流抽。所以只要 GET 與 Submit 之間「合格單字數」變了（任何人查了一個新單字而字典為它新增了一筆有定義的 `Word`；或某字被標成 `DataInvalid`），亂數流位移，**五題的選項順序與 `CorrectIndex` 全部可能不同**；使用者送出的 `selectedIndex` 就被拿去對另一份題目 → 分數與 XP 算錯。字典會隨查詢自動長大（`DictionaryService` 新增字），這不是罕見事件。`LoadEligibleWordsAsync` 也沒有 `OrderBy`，列順序本身不受保證。同一天兩位使用者也可能因此看到不同題目（與「全站同一天題目相同」的設計不符）。
2. **效能**：每位使用者每天至少兩次載入整本字典，字典越大越重。

做法：**當日題目第一次被需要時產生一次、寫進 DB，之後 GET 與 Submit 都讀同一份。**

## Requirements
1. 新 entity `DailyChallengeSet`（放 `Data/Models/`，比照 `DailyChallengeResult` 的風格）：
   - `ChallengeDate`：UTC 日期，**主鍵**（與 `DailyChallengeResult.ChallengeDate` 同語意與同一個日期型別／欄位型別）。
   - `QuestionsJson`：`nvarchar(max)`，序列化後的 `List<DailyChallengeQuestion>`。
   - `CreatedAt`：UTC。
   - `AzureSQLDBContext` 加 `DbSet`，產生一個 EF migration。**Migration 只能含 `CreateTable`（Down 為 `DropTable`）**，不得改動或刪除任何既有資料表／欄位／資料。
2. `DailyChallengeService` 加私有的 `GetOrCreateTodaySetAsync(DateTime today)`：
   - 先以主鍵讀當日那一列；有 → 反序列化後回傳。
   - 沒有 → `LoadEligibleWordsAsync()`（**加 `.AsSplitQuery()` 與 `.OrderBy(w => w.Id)`**，讓載入量與順序確定）→ 字數 `< MinWordsForChallenge` 時回現行的 `BadRequest` 錯誤與同一段訊息，**且不寫入**；否則 `Generate` → 序列化 → 新增一列 → `SaveChangesAsync`。
   - **併發**：兩個請求同時是當日第一個，會有一個在 `SaveChangesAsync` 撞主鍵（`DbUpdateException`）。捕捉後把 tracked 的那個 entity 從 context 移除（`Entry(...).State = Detached`）、重讀並回傳**先寫入者**的那份；不要讓使用者看到 500。
3. `GetChallengeAsync`、`SubmitChallengeAsync` 都改用 `GetOrCreateTodaySetAsync`；Submit 的評分拿持久化的那份題目對答案。其餘邏輯（已完成者回傳舊成績、XP、streak、freeze）**一行不動**。
4. **不改** `DailyChallengeQuestionGenerator` 的演算法與種子、不改 API 契約（`DailyChallengeResponse`／`DailyChallengeSubmitResponse`）、不改前端。
5. 序列化用 `System.Text.Json` 預設即可；`QuestionType` 存字串或整數皆可，但**反序列化後必須與產生時逐欄位相同**（`DailyChallengeQuestion` 有 `required` 成員，注意反序列化器是否受得了）。
6. 不做清除機制（約 365 列／年、每列數 KB）。

## Acceptance Criteria
- 新增測試（沿用 `DailyChallengeQuestionGeneratorTests` 與 repo 內其他 DbContext 測試的建庫方式；`DailyChallengeService` 目前沒有測試）：
  1. 同一天連續兩次 `GetChallengeAsync` 回同一份題目，**即使兩次之間新增了一個合格單字**。
  2. **回歸測試**：GET → 新增一個有定義的單字 → Submit 全部答對 → `Score == QuestionsPerChallenge`。請先在**未修改的 service** 上寫出這個測試並確認它會紅（若在你造得出的種子下剛好不紅，換字庫大小／種子直到會紅；實在造不出穩定的紅，PR 說明寫明原因，但測試仍保留）。
  3. 字數不足 → 回現行錯誤，且 `DailyChallengeSets` 沒有新列。
  4. 主鍵衝突路徑：模擬「第二個請求在第一個寫入後才 SaveChanges」，結果回的是先寫入者的題目、沒有例外。
  5. JSON round trip：`Id`、`Type`、`Prompt`、`Options`（含順序）、`CorrectIndex`、`Explanation`、`WordId`、`Word` 全相等。
- `dotnet test --filter "Category!=LiveContract"` 全過；既有測試**不得為了過關而修改斷言**。
- PR 說明貼上 migration 的 `Up`／`Down`，並說明這是純新增資料表。

## Technical Notes
- **為什麼不用 `IMemoryCache`（Issue #464 的原提案）**：App Service 閒置、重啟、swap 都會清掉記憶體（repo 已為同樣原因有 `PersistentTwHistoricalProvider`）。清掉之後會用「當時的字庫」重新產生，上面的漂移仍會發生，只是機率降低。一列主鍵查詢的成本與現有的 `DailyChallengeResults` 查詢相同，不需要再疊一層記憶體快取。
- **不增加洩漏**：`CorrectIndex` 本來就隨題目傳到前端（`QuestionCard.tsx`），持久化沒有改變這點；伺服器重新產生題目來評分也從來沒有防作弊效果。評分仍留在後端。
- **部署當天的過渡**：部署當下已 GET 但尚未 Submit 的使用者，Submit 會讀到「部署後第一次持久化」的題目，可能與他看到的不同。只發生一次，與現況風險相同，不需處理。
- **遷移在啟動時對正式資料庫執行**（`CLAUDE.md`「Deployment」）；純 `CreateTable` 風險低，但合併即部署，PR 說明請照上面寫清楚。
- **回退方案**（若 Boss 不要新表）：只加 `IMemoryCache`（key＝UTC 日期，到下一個 UTC 午夜到期）＋同樣的 `AsSplitQuery`／`OrderBy`。解決載入量，**不**完全解決漂移（重啟後仍會重新產生）。
- 流程：開工前先把本檔 `Status` 改 `IN_PROGRESS` 並直接 push 到 master（`ARCHITECTURE.md`「Workflow conventions」）；完成後開 PR，並把本檔 `Status` 改 `PR_OPENED`、填 Result。
- 只改後端；不要動前端、`.github/workflows/`、`CLAUDE.md`。

## Dependencies
無。

## Result
- **PR**：https://github.com/twjackysu/JackyAIApp/pull/465（分支 `claude/exciting-cray-lrijyv`）。後端 6 個檔案＋測試：新增 `Data/Models/DailyChallengeSet.cs`、`AzureSQLDBContext`（`DbSet`）、migration `20261016043749_AddDailyChallengeSet`（`.cs`／`.Designer.cs`／snapshot）、`DailyChallengeService.cs`、新增 `DailyChallengeServiceTests.cs`。
- **實作**：`GetOrCreateTodaySetAsync(today)` 先以主鍵讀當日一列，沒有才 `LoadEligibleWordsAsync()`（已加 `AsSplitQuery`＋`OrderBy(Id)`）→ 字數不足回原 `BadRequest`／原訊息且不寫入 → `Generate` → 序列化 → insert。`DbUpdateException` 時把自己的 entity 設 `Detached`、重讀並回傳先寫入者的那份；重讀不到列代表不是主鍵衝突，原例外照丟。GET／Submit 都改用它；其餘邏輯與 `DailyChallengeQuestionGenerator`、API 契約、前端都沒動。
- **Migration**：只有 `CreateTable "DailyChallengeSets"`（`ChallengeDate datetime2` PK、`QuestionsJson nvarchar(max)`、`CreatedAt datetime2`），`Down` 為 `DropTable`；snapshot diff 只多一段新 entity。Up／Down 已貼在 PR 說明。
- **測試**：新增 11 個（SQLite shared-cache，真的主鍵衝突）。**先在未修改的 service 上跑過：7 紅 3 綠**，回歸測試 `Submit_AfterAWordIsAdded_StillGradesAgainstTheSetTheUserSaw` 為 `Expected: 5, Actual: 2`，連續兩次 GET 的選項順序不同。修改後 `dotnet test --filter "Category!=LiveContract"`：**1308 通過、0 失敗**；既有測試沒有改任何斷言。.NET 10.0.112 是我在沙箱用 `apt` 裝的（原本沒有 `dotnet`）。
- **已知限制**：(1) 回歸測試的種子是今天的 UTC 日期（service 直接用 `DateTime.UtcNow`，沒為此加 `TimeProvider`）；位移後五題全相同的機率可忽略但不是 0，UTC 午夜前後跑也可能跨日。(2) Submit 在字數不足時現在回 `BadRequest`（過去會用不足 20 字的字庫硬產生題目）；GET 同條件本來就是 `BadRequest`，前端拿不到題目，所以實務上不會遇到。(3) 部署當天已 GET 未 Submit 的使用者仍有一次性風險（任務書已註明不處理）。
- **未驗證**：沒有對真正的 Azure SQL 跑 migration 或併發（沙箱連不到）；併發路徑只在 SQLite 證明（主鍵衝突拋 `DbUpdateException`，SQL Server 的重複鍵也是 `DbUpdateException`，但我沒有實測）。
- **流程偏離**：本次 session 只能推到指定分支，`IN_PROGRESS` 與 `PR_OPENED` 都在 `claude/exciting-cray-lrijyv` 上，沒有照 Workflow conventions 推到 master。
- **TASK-054**：同為 READY、獨立，這次沒有動（一次一個 PR；等 #465 有結果再做）。

## Review
（Tech Lead 事後審閱，2026-10-16 第 13 次排程；Boss 於 2026-10-16 17:13 +0800 直接合併為 #465，`cc4b588`，`Build and deploy` run 554 成功）

**結論：無須追加修正。** 逐行讀了 `DailyChallengeService.cs`、`DailyChallengeSet.cs`、migration 與 snapshot 的差異：

- **Migration**：`AddDailyChallengeSet` 只有 `CreateTable "DailyChallengeSets"`（`ChallengeDate datetime2` PK、`QuestionsJson nvarchar(max)`、`CreatedAt datetime2`），`Down` 為 `DropTable`；snapshot 只多一段新 entity。符合「純新增資料表」。
- **邏輯**：GET 與 Submit 都走 `GetOrCreateTodaySetAsync`；先以主鍵讀、沒有才 `LoadEligibleWordsAsync`（已 `AsSplitQuery`＋`OrderBy(Id)`）→ 字數不足回原 `BadRequest` 且不寫入 → `Generate` → 序列化 → insert。`DbUpdateException` 時把自己的 entity 設 `Detached`、重讀先寫入者的那份，重讀不到就 `throw;`（不是主鍵衝突則原例外照丟）。Submit 的其餘邏輯（已完成者、XP、streak、freeze）沒動。
- **兩點我確認過、無問題**：(1) 任務書擔心的 `required` 成員反序列化——有 round trip 測試覆蓋（Engineer 的 Result 與測試清單）；(2) 衝突路徑 `Detached` 後同一個 request 之後的 `SaveChanges`（Submit 寫成績）不會重試 insert。
- **已知取捨（沿用 Engineer 的說明）**：Submit 在字數不足時從「用不足 20 字的字庫硬產題」變成 `BadRequest`，與 GET 一致；前端在該情形本來就拿不到題目。回歸測試的種子是當天 UTC 日期（跨午夜跑有極小機率不穩，Engineer 已記）。
- **未驗證**：沒有對真正的 Azure SQL 跑 migration／併發（沙箱連不到）；部署成功只代表 Actions 結論，不代表 migration 在正式庫已套用。請 Boss 方便時查 `SELECT MigrationId FROM __EFMigrationsHistory WHERE MigrationId LIKE '%AddDailyChallengeSet'`（應有一列）；第一位開 `/daily` 的使用者之後 `SELECT * FROM DailyChallengeSets` 應有當日一列。部署當天已 GET 未 Submit 的使用者有一次性風險（任務書已註明）。
