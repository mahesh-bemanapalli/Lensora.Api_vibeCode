# Event logging

Authenticated users can open `/admin/event-logs` in the frontend. All authenticated accounts currently see all events; there is intentionally no photographer ownership or role filter in this first version. Enforce the future role policy on `EventLogsController`, not just the page.

## Deployment

1. Run `_vibecode/database/008_api_event_logs.sql` against the application's SQL Server database. The script is repeatable and creates `dbo.ApiEventLogs`. It does not run automatically at application startup.
2. Deploy the API and frontend together. The API database identity needs SELECT, INSERT and DELETE permission on this table.
3. Sign in, open Event Logs, perform an API action, then refresh. Filter by the returned `X-Request-ID` or the error response's `traceId` to see related events.

No Azure-specific connection is needed for the page. It uses the existing Lensora SQL connection. Existing Application Insights integration remains available.

Set `EventLogs__RetentionDays` (or `EventLogs:RetentionDays` in configuration) to override the default 30-day retention, bounded to 1–365 days. Cleanup runs at most hourly while events are being persisted. The UI offers the last hour, 24 hours, 7 days, or 30 days. The API also accepts `fromUtc` and `toUtc` ISO timestamps.

## Captured data

Every request reaching the middleware produces a completion event, including authentication failures, validation failures and unmatched routes. Requests use route templates, status, duration, internal user ID and trace ID. Reading the log API also produces a request event.

Lensora application `ILogger` events at Information and above are captured, including existing notification worker events and controller errors. Named property values are allowed explicitly in `EventLogProvider`; message templates are stored rather than rendered messages. Notification and booking IDs from logging scopes are included. Add structured events to a workflow when more detail than its request summary is needed.

The database viewer excludes request/response bodies, headers, query strings, raw URL values, raw exception messages, and arbitrary properties. Exceptions show their type, root cause type and method-only stack frames. The allowlist includes safe IDs, status and operation metadata. This policy applies to the new event store; existing logging providers retain their own configuration.

## Reliability limits

A bounded in-memory queue keeps database writes off request execution. Events are inserted in batches of up to 100. A failed batch or full queue writes sanitized JSON to stderr rather than blocking or failing requests. These fallback events are not replayed into SQL and will not appear on the page. A hard process crash can lose queued events. This is a diagnostic log, not a durable financial/audit ledger. Database unavailability also makes the page unavailable; retain an independent sink such as Application Insights for those incidents.

## Verification

Build the API, then run the dependency-free smoke harness using its offline NuGet config:

```powershell
dotnet build Lensora.Api.csproj --no-restore --output build-check/event-logging
dotnet restore build-check/EventLogChecks/EventLogChecks.csproj --configfile build-check/EventLogChecks/NuGet.Config
dotnet run --project build-check/EventLogChecks/EventLogChecks.csproj --no-restore --no-launch-profile
```

The harness checks sanitized properties, scope correlation, request status coverage, exceptions, the authorization attribute and queue overflow. It does not exercise SQL persistence or a deployed authenticated session.

From the UI directory:

```powershell
npm run build -- --configLoader native --outDir event-logs-build-check
```

Production acceptance: verify anonymous access returns 401; authenticated access returns stored events; issue a successful and failed request and find their traces; check notification events; verify retention and database failure behavior in a non-production environment.
