-- ============================================================
-- Migration: dashboard sharing (Phase 3)
-- Adds Dashboards.Visibility and a DashboardShares table (restricted viewers).
-- Run this script against the [JackyAI] database in SSMS.
-- ============================================================
USE [JackyAI];
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[Dashboards]') AND name = N'Visibility'
)
BEGIN
    ALTER TABLE [dbo].[Dashboards]
        ADD [Visibility] NVARCHAR(20) NOT NULL CONSTRAINT DF_Dashboards_Visibility DEFAULT N'Private';
    PRINT 'Column [Dashboards].[Visibility] added.';
END
ELSE
BEGIN
    PRINT 'Column [Dashboards].[Visibility] already exists — skipped.';
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE object_id = OBJECT_ID(N'[dbo].[DashboardShares]')
)
BEGIN
    CREATE TABLE [dbo].[DashboardShares] (
        [Id]           UNIQUEIDENTIFIER   NOT NULL  CONSTRAINT PK_DashboardShares PRIMARY KEY,
        [DashboardId]  UNIQUEIDENTIFIER   NOT NULL,
        [UserId]       NVARCHAR(450)      NOT NULL,
        [CreatedAt]    DATETIMEOFFSET(7)  NOT NULL  DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_DashboardShares_Dashboards
            FOREIGN KEY ([DashboardId]) REFERENCES [dbo].[Dashboards]([Id])
            ON DELETE CASCADE,
        CONSTRAINT FK_DashboardShares_Users
            FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id])
            ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX IX_DashboardShares_DashboardId_UserId
        ON [dbo].[DashboardShares] ([DashboardId], [UserId]);

    PRINT 'Table [dbo].[DashboardShares] created successfully.';
END
ELSE
BEGIN
    PRINT 'Table [dbo].[DashboardShares] already exists — skipped.';
END
GO
