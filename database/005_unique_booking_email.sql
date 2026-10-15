/* Run after 001_create_lensora.sql. One booking email per photographer,
   regardless of event date, package, or booking status. */
USE Lensora;
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF EXISTS (
  SELECT 1
  FROM dbo.Bookings
  GROUP BY PhotographerId, LOWER(LTRIM(RTRIM(ClientEmail)))
  HAVING COUNT(*) > 1
)
BEGIN
  SELECT PhotographerId, LOWER(LTRIM(RTRIM(ClientEmail))) AS NormalizedEmail,
         COUNT(*) AS BookingCount, STRING_AGG(CONVERT(NVARCHAR(MAX), Id), N', ') AS BookingIds
  FROM dbo.Bookings
  GROUP BY PhotographerId, LOWER(LTRIM(RTRIM(ClientEmail)))
  HAVING COUNT(*) > 1;
  THROW 50001, 'Resolve the duplicate booking rows listed above before creating the unique index. No rows were changed.', 1;
END;
ALTER TABLE dbo.Bookings ADD NormalizedClientEmail AS LOWER(LTRIM(RTRIM(ClientEmail))) PERSISTED;
CREATE UNIQUE INDEX UX_Bookings_PhotographerId_NormalizedClientEmail
  ON dbo.Bookings (PhotographerId, NormalizedClientEmail);
COMMIT TRANSACTION;
GO
