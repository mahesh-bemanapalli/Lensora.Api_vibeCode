/* Run after 003_add_packages.sql. Review duplicate same-date inquiries before running. */
USE Lensora;
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Bookings')
    AND name = N'UX_Bookings_PhotographerId_NormalizedClientEmail')
  DROP INDEX UX_Bookings_PhotographerId_NormalizedClientEmail ON dbo.Bookings;

IF COL_LENGTH(N'dbo.Bookings', N'NormalizedClientEmail') IS NULL
  ALTER TABLE dbo.Bookings ADD NormalizedClientEmail AS LOWER(LTRIM(RTRIM(ClientEmail))) PERSISTED;

IF EXISTS (
  SELECT 1 FROM dbo.Bookings
  GROUP BY PhotographerId, LOWER(LTRIM(RTRIM(ClientEmail))), EventDate
  HAVING COUNT(*) > 1
)
BEGIN
  SELECT PhotographerId, LOWER(LTRIM(RTRIM(ClientEmail))) AS NormalizedClientEmail,
         EventDate, COUNT(*) AS BookingCount,
         STRING_AGG(CONVERT(NVARCHAR(MAX), Id), N', ') AS BookingIds
  FROM dbo.Bookings
  GROUP BY PhotographerId, LOWER(LTRIM(RTRIM(ClientEmail))), EventDate
  HAVING COUNT(*) > 1;
  THROW 50002, 'Resolve duplicate same-date inquiries shown above. No rows were changed.', 1;
END;

EXEC(N'CREATE UNIQUE INDEX UX_Bookings_Photographer_Email_EventDate
  ON dbo.Bookings (PhotographerId, NormalizedClientEmail, EventDate)');

ALTER TABLE dbo.Bookings ADD
  ClientPhone NVARCHAR(16) NULL,
  WhatsAppOptIn BIT NOT NULL CONSTRAINT DF_Bookings_WhatsAppOptIn DEFAULT 0,
  WhatsAppOptInUtc DATETIME2 NULL,
  WhatsAppConsentVersion NVARCHAR(40) NULL,
  InternalNotes NVARCHAR(4000) NULL,
  FollowUpUtc DATETIME2 NULL;

ALTER TABLE dbo.Bookings DROP CONSTRAINT CK_Bookings_Status;
ALTER TABLE dbo.Bookings ADD CONSTRAINT CK_Bookings_Status
  CHECK (Status IN (N'Pending', N'Accepted', N'Rejected', N'Confirmed'));

CREATE TABLE dbo.NotificationOutbox (
  Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_NotificationOutbox PRIMARY KEY,
  BookingId INT NOT NULL,
  EventType NVARCHAR(40) NOT NULL,
  Channel NVARCHAR(20) NOT NULL,
  Recipient NVARCHAR(320) NOT NULL,
  Status NVARCHAR(20) NOT NULL,
  Attempts INT NOT NULL CONSTRAINT DF_NotificationOutbox_Attempts DEFAULT 0,
  CreatedUtc DATETIME2 NOT NULL,
  NextAttemptUtc DATETIME2 NOT NULL,
  SentUtc DATETIME2 NULL,
  ProviderMessageId NVARCHAR(255) NULL,
  LastError NVARCHAR(1000) NULL,
  CONSTRAINT FK_NotificationOutbox_Bookings FOREIGN KEY (BookingId) REFERENCES dbo.Bookings(Id) ON DELETE CASCADE,
  CONSTRAINT CK_NotificationOutbox_Status CHECK (Status IN (N'Queued', N'Processing', N'Retry', N'Sent', N'Delivered', N'Read', N'Failed', N'Cancelled')),
  CONSTRAINT CK_NotificationOutbox_Channel CHECK (Channel IN (N'Email', N'WhatsApp'))
);
CREATE INDEX IX_NotificationOutbox_Status_NextAttemptUtc ON dbo.NotificationOutbox (Status, NextAttemptUtc);
CREATE INDEX IX_NotificationOutbox_ProviderMessageId ON dbo.NotificationOutbox (ProviderMessageId);
CREATE UNIQUE INDEX UX_NotificationOutbox_Booking_Event_Channel_Recipient
  ON dbo.NotificationOutbox (BookingId, EventType, Channel, Recipient);

COMMIT TRANSACTION;
GO
