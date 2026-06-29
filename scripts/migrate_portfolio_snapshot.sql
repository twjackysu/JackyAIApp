-- Migration: ReplacePortfolioTransactionsWithHoldingSnapshots
-- Run against Azure SQL DB. Drops old tables, creates new ones, updates EF migration history.
-- WARNING: NetWorthSnapshots and PortfolioTransactions data will be permanently deleted.

BEGIN TRANSACTION;

-- 1. Drop old tables (FK constraints removed automatically)
IF OBJECT_ID('dbo.NetWorthSnapshots', 'U') IS NOT NULL
    DROP TABLE dbo.NetWorthSnapshots;

IF OBJECT_ID('dbo.PortfolioTransactions', 'U') IS NOT NULL
    DROP TABLE dbo.PortfolioTransactions;

-- 2. Create HoldingSnapshots
CREATE TABLE dbo.HoldingSnapshots (
    Id            INT IDENTITY(1,1)      NOT NULL,
    UserId        NVARCHAR(450)          NOT NULL,
    EffectiveDate DATE                   NOT NULL,
    UsdTwdRate    DECIMAL(18,2)          NOT NULL,
    CreatedAt     DATETIME2              NOT NULL,
    CONSTRAINT PK_HoldingSnapshots PRIMARY KEY (Id),
    CONSTRAINT FK_HoldingSnapshots_Users_UserId
        FOREIGN KEY (UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IX_HoldingSnapshots_UserId_EffectiveDate
    ON dbo.HoldingSnapshots (UserId, EffectiveDate);

-- 3. Create HoldingPositions
CREATE TABLE dbo.HoldingPositions (
    Id          INT IDENTITY(1,1)        NOT NULL,
    SnapshotId  INT                      NOT NULL,
    AssetType   INT                      NOT NULL,
    Symbol      NVARCHAR(MAX)            NOT NULL,
    Name        NVARCHAR(MAX)            NOT NULL,
    Market      NVARCHAR(MAX)            NOT NULL,
    Currency    NVARCHAR(MAX)            NOT NULL,
    Quantity    DECIMAL(18,6)            NOT NULL,
    UnitPrice   DECIMAL(18,6)            NULL,
    CostBasis   DECIMAL(18,4)            NULL,
    CONSTRAINT PK_HoldingPositions PRIMARY KEY (Id),
    CONSTRAINT FK_HoldingPositions_HoldingSnapshots_SnapshotId
        FOREIGN KEY (SnapshotId) REFERENCES dbo.HoldingSnapshots(Id) ON DELETE CASCADE
);

CREATE INDEX IX_HoldingPositions_SnapshotId
    ON dbo.HoldingPositions (SnapshotId);

-- 4. Record migration in EF history so dotnet ef won't re-run it
IF NOT EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory
    WHERE MigrationId = '20260629162648_ReplacePortfolioTransactionsWithHoldingSnapshots'
)
BEGIN
    INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES ('20260629162648_ReplacePortfolioTransactionsWithHoldingSnapshots', '9.0.0');
END

COMMIT TRANSACTION;
