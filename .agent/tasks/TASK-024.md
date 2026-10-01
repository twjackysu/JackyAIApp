# TASK-024

## Title
DROP 點數/訂閱資料表與欄位（不可逆，需 Boss 確認）

## Owner
Backend

## Status
BLOCKED

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
## PR
## Review
