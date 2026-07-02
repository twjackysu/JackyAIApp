-- ============================================================
-- Migration: add ExamAttempts table (per-user, per-word exam history)
-- Feeds the learning loop: exam outcomes update spaced-repetition scheduling.
-- Run this script against the [JackyAI] database in SSMS.
-- ============================================================
USE [JackyAI];
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE object_id = OBJECT_ID(N'[dbo].[ExamAttempts]')
)
BEGIN
    CREATE TABLE [dbo].[ExamAttempts] (
        [Id]         INT IDENTITY(1,1)  NOT NULL  CONSTRAINT PK_ExamAttempts PRIMARY KEY,
        [UserId]     NVARCHAR(450)      NOT NULL,
        [WordId]     NVARCHAR(450)      NOT NULL,
        [ExamType]   NVARCHAR(40)       NOT NULL,   -- Cloze | Translation | Sentence
        [IsCorrect]  BIT                NOT NULL,
        [Score]      INT                NULL,        -- 0-100 for graded types (Sentence)
        [CreatedAt]  DATETIME2(7)       NOT NULL  DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_ExamAttempts_Users
            FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id])
            ON DELETE CASCADE,
        CONSTRAINT FK_ExamAttempts_Words
            FOREIGN KEY ([WordId]) REFERENCES [dbo].[Words]([Id])
            ON DELETE NO ACTION
    );

    -- Per-word history lookups and recent-activity queries
    CREATE INDEX IX_ExamAttempts_UserId_WordId
        ON [dbo].[ExamAttempts] ([UserId], [WordId]);
    CREATE INDEX IX_ExamAttempts_UserId_CreatedAt
        ON [dbo].[ExamAttempts] ([UserId], [CreatedAt]);

    PRINT 'Table [dbo].[ExamAttempts] created successfully.';
END
ELSE
BEGIN
    PRINT 'Table [dbo].[ExamAttempts] already exists — skipped.';
END
GO
