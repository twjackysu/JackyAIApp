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

## Acceptance Criteria
- Migration 僅含上述 DROP；正式環境套用後應用正常；`dotnet test` 綠燈。

## Technical Notes
- 無真實金流入帳（見 REQUIREMENTS），無退款/稽核保存義務；但屬破壞性變更，維持獨立任務以便延後。

## Dependencies
TASK-023 MERGED + Boss 同意。

## Result
## PR
## Review
