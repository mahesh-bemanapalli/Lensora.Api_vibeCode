-- Run against the Lensora database before deploying the event logging API.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.ApiEventLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ApiEventLogs (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ApiEventLogs PRIMARY KEY,
        TimestampUtc DATETIME2 NOT NULL,
        Level NVARCHAR(16) NOT NULL,
        Category NVARCHAR(256) NOT NULL,
        TraceId NVARCHAR(128) NULL,
        UserId NVARCHAR(128) NULL,
        Method NVARCHAR(16) NULL,
        Route NVARCHAR(512) NULL,
        StatusCode INT NULL,
        DurationMs BIGINT NULL,
        Message NVARCHAR(2048) NOT NULL,
        Details NVARCHAR(max) NULL
    );
    CREATE INDEX IX_ApiEventLogs_TimestampUtc ON dbo.ApiEventLogs(TimestampUtc);
    CREATE INDEX IX_ApiEventLogs_TraceId_Id ON dbo.ApiEventLogs(TraceId, Id);
END;
COMMIT TRANSACTION;
