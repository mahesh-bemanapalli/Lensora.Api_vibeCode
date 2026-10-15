-- Run against the Lensora database before deploying the updated notification worker.
-- Also run after restoring an older database snapshot. No rows are deleted.
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.NotificationOutbox', N'U') IS NULL
    THROW 50001, 'NotificationOutbox is missing. Apply 006_booking_crm.sql first.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.NotificationOutbox')
          AND name = N'CK_NotificationOutbox_Status')
        ALTER TABLE dbo.NotificationOutbox DROP CONSTRAINT CK_NotificationOutbox_Status;

    ALTER TABLE dbo.NotificationOutbox WITH CHECK
        ADD CONSTRAINT CK_NotificationOutbox_Status CHECK
        (Status IN (N'Queued', N'Processing', N'Retry', N'Sent', N'Delivered', N'Read', N'Failed', N'Cancelled'));
    ALTER TABLE dbo.NotificationOutbox CHECK CONSTRAINT CK_NotificationOutbox_Status;

    -- One-time retirement of notifications for the discontinued Accepted event.
    -- The predicate makes reruns safe; delivered/sent history is preserved.
    UPDATE NotificationOutbox
    SET Status = N'Cancelled', LastError = N'Accepted notifications are no longer sent.'
    WHERE EventType = N'Accepted'
      AND Status IN (N'Queued', N'Processing', N'Retry', N'Failed');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

select * from [dbo].[NotificationOutbox]
