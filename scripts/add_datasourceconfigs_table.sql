-- ============================================================
-- Migration: add DataSourceConfigs table (per-user)
-- Run this script against the [JackyAI] database in SSMS.
-- ============================================================
USE [JackyAI];
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE object_id = OBJECT_ID(N'[dbo].[DataSourceConfigs]')
)
BEGIN
    CREATE TABLE [dbo].[DataSourceConfigs] (
        [Id]               UNIQUEIDENTIFIER    NOT NULL  CONSTRAINT PK_DataSourceConfigs PRIMARY KEY,
        [UserId]           NVARCHAR(450)       NOT NULL,
        [Name]             NVARCHAR(100)       NOT NULL,
        [ConnectorType]    INT                 NOT NULL,   -- 0 = MsSql, 1 = Adx
        [ConfigJson]       NVARCHAR(MAX)       NOT NULL,
        [DefaultTableName] NVARCHAR(200)       NULL,
        [CreatedAt]        DATETIMEOFFSET(7)   NOT NULL  DEFAULT SYSDATETIMEOFFSET(),
        [UpdatedAt]        DATETIMEOFFSET(7)   NULL,

        CONSTRAINT FK_DataSourceConfigs_Users
            FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id])
            ON DELETE CASCADE
    );

    -- Index for fast per-user listing
    CREATE INDEX IX_DataSourceConfigs_UserId
        ON [dbo].[DataSourceConfigs] ([UserId]);

    PRINT 'Table [dbo].[DataSourceConfigs] created successfully.';
END
ELSE
BEGIN
    PRINT 'Table [dbo].[DataSourceConfigs] already exists — skipped.';
END
GO
