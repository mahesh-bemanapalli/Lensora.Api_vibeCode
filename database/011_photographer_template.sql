/* Persist each photographer's public website template. Safe to run again. */
USE Lensora;
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.Photographers', N'TemplateKey') IS NULL
  ALTER TABLE dbo.Photographers ADD TemplateKey NVARCHAR(40) NOT NULL
    CONSTRAINT DF_Photographers_TemplateKey DEFAULT (N'editorial') WITH VALUES;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
  WHERE parent_object_id = OBJECT_ID(N'dbo.Photographers')
    AND name = N'CK_Photographers_TemplateKey')
  EXEC(N'ALTER TABLE dbo.Photographers WITH CHECK ADD CONSTRAINT CK_Photographers_TemplateKey
    CHECK (TemplateKey COLLATE Latin1_General_100_BIN2 IN (N''editorial'', N''minimal-gallery'') AND DATALENGTH(TemplateKey) = DATALENGTH(RTRIM(TemplateKey)))');

COMMIT TRANSACTION;
GO
