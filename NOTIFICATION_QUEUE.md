# Notification queue status update

Apply `_vibecode/database/009_notification_cancelled_status.sql` to the application database before deploying the API. Pause notification processing during the migration/deployment to avoid an older worker processing legacy entries concurrently. The script runs in a transaction, validates existing statuses, and rolls back on failure. It preserves every row, adds Cancelled to the status constraint, and retires unsent Accepted notifications once. It is safe to rerun. Fresh databases also permit Cancelled in 006_booking_crm.sql; run migrations in order, including 009 after restoring older snapshots.

The worker no longer runs a cleanup UPDATE every ten seconds. A shared SQL-translatable predicate selects and claims due Queued/Retry entries and Processing entries with expired leases. Accepted events and terminal states are excluded. Existing batching, polling interval and the Status/NextAttemptUtc index remain unchanged.

Status strings are centralized in Domain/NotificationStatus.cs and used by the worker, planner, retry endpoint and WhatsApp webhook. Webhooks do not overwrite Cancelled entries. API/JSON values remain strings with their existing spelling.

Verification: build the API into build-check/event-logging and run build-check/EventLogChecks as described in EVENT_LOGGING.md. Additional queue checks cover due entries, future retries/active leases, legacy events and SQL Server query translation without opening a database connection. The migration itself still needs execution against the target database; these checks do not replace that deployment verification.
