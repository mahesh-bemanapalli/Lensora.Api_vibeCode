/* Run after 006_booking_crm.sql. Existing bookings keep an unknown agreed amount. */
USE Lensora;
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.Bookings', N'AgreedAmount') IS NULL
  ALTER TABLE dbo.Bookings ADD AgreedAmount DECIMAL(12,2) NULL;

IF COL_LENGTH(N'dbo.Bookings', N'AmountReceived') IS NULL
  ALTER TABLE dbo.Bookings ADD AmountReceived DECIMAL(12,2) NOT NULL
    CONSTRAINT DF_Bookings_AmountReceived DEFAULT (0);

IF COL_LENGTH(N'dbo.Bookings', N'Currency') IS NULL
  ALTER TABLE dbo.Bookings ADD Currency NVARCHAR(3) NOT NULL
    CONSTRAINT DF_Bookings_Currency DEFAULT (N'INR');
GO

/* This batch compiles after SQL Server has registered the new columns. */
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
  WHERE parent_object_id = OBJECT_ID(N'dbo.Bookings')
    AND name = N'CK_Bookings_Financials')
  ALTER TABLE dbo.Bookings ADD CONSTRAINT CK_Bookings_Financials CHECK (
  (AgreedAmount IS NULL OR AgreedAmount >= 0)
  AND AmountReceived >= 0
  AND (AgreedAmount IS NOT NULL OR AmountReceived = 0)
  AND (AgreedAmount IS NULL OR AmountReceived <= AgreedAmount)
    AND Currency COLLATE Latin1_General_BIN2 LIKE N'[A-Z][A-Z][A-Z]'
  );

COMMIT TRANSACTION;
GO
