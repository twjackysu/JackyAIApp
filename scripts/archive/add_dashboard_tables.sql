-- ============================================================
-- Migration: add Dashboards + DashboardWidgets tables
-- A dashboard is a user-owned, named collection of saved DataAnalysis widgets.
-- Each widget stores a re-runnable summarize plan (JSON) + viz type + grid box.
-- Run this script against the [JackyAI] database in SSMS.
-- ============================================================
USE [JackyAI];
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE object_id = OBJECT_ID(N'[dbo].[Dashboards]')
)
BEGIN
    CREATE TABLE [dbo].[Dashboards] (
        [Id]         UNIQUEIDENTIFIER   NOT NULL  CONSTRAINT PK_Dashboards PRIMARY KEY,
        [UserId]     NVARCHAR(450)      NOT NULL,
        [Name]       NVARCHAR(200)      NOT NULL,
        [CreatedAt]  DATETIMEOFFSET(7)  NOT NULL  DEFAULT SYSUTCDATETIME(),
        [UpdatedAt]  DATETIMEOFFSET(7)  NULL,

        CONSTRAINT FK_Dashboards_Users
            FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id])
            ON DELETE CASCADE
    );
    CREATE INDEX IX_Dashboards_UserId ON [dbo].[Dashboards] ([UserId]);

    PRINT 'Table [dbo].[Dashboards] created successfully.';
END
ELSE
BEGIN
    PRINT 'Table [dbo].[Dashboards] already exists — skipped.';
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE object_id = OBJECT_ID(N'[dbo].[DashboardWidgets]')
)
BEGIN
    CREATE TABLE [dbo].[DashboardWidgets] (
        [Id]           UNIQUEIDENTIFIER   NOT NULL  CONSTRAINT PK_DashboardWidgets PRIMARY KEY,
        [DashboardId]  UNIQUEIDENTIFIER   NOT NULL,
        [Title]        NVARCHAR(200)      NULL,
        [VizType]      NVARCHAR(40)       NOT NULL,   -- VizId: column | kpi | treemap | ...
        [PlanJson]     NVARCHAR(MAX)      NOT NULL,   -- serialized SummarizeRequest (context+viewBy+measures+filters)
        [X]            INT                NOT NULL  DEFAULT 0,
        [Y]            INT                NOT NULL  DEFAULT 0,
        [W]            INT                NOT NULL  DEFAULT 6,
        [H]            INT                NOT NULL  DEFAULT 8,
        [CreatedAt]    DATETIMEOFFSET(7)  NOT NULL  DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_DashboardWidgets_Dashboards
            FOREIGN KEY ([DashboardId]) REFERENCES [dbo].[Dashboards]([Id])
            ON DELETE CASCADE
    );
    CREATE INDEX IX_DashboardWidgets_DashboardId
        ON [dbo].[DashboardWidgets] ([DashboardId]);

    PRINT 'Table [dbo].[DashboardWidgets] created successfully.';
END
ELSE
BEGIN
    PRINT 'Table [dbo].[DashboardWidgets] already exists — skipped.';
END
GO
