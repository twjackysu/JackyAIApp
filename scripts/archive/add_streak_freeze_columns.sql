-- ============================================================
-- Migration: add streak-freeze columns to Users
-- Forgiving streaks — one missed day per week is forgiven instead of resetting.
-- Run this script against the [JackyAI] database in SSMS.
-- ============================================================
USE [JackyAI];
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[Users]') AND name = 'StreakFreezesAvailable'
)
BEGIN
    ALTER TABLE [dbo].[Users]
        ADD [StreakFreezesAvailable] INT NOT NULL CONSTRAINT DF_Users_StreakFreezes DEFAULT(1);
    PRINT 'Column [StreakFreezesAvailable] added to [dbo].[Users].';
END
ELSE
BEGIN
    PRINT 'Column [StreakFreezesAvailable] already exists — skipped.';
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[Users]') AND name = 'LastFreezeGrantDate'
)
BEGIN
    ALTER TABLE [dbo].[Users]
        ADD [LastFreezeGrantDate] DATETIME2(7) NULL;
    PRINT 'Column [LastFreezeGrantDate] added to [dbo].[Users].';
END
ELSE
BEGIN
    PRINT 'Column [LastFreezeGrantDate] already exists — skipped.';
END
GO
