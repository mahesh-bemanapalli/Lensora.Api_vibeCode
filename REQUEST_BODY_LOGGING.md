# Request-body logging

New request completion events capture allowlisted JSON fields when HTTP status is 400 or greater. Internal notes are included. Use the same trace ID to find the related exception event. Successful requests are excluded by default.

Configuration (Azure environment variable names):
- EventLogs__CaptureRequestBodies: true by default; false disables capture.
- EventLogs__CaptureSuccessfulRequestBodies: false by default; true includes successful requests.

Authentication, webhook, unmatched routes, and non-JSON/multipart bodies are excluded. Unknown fields, including credentials and customer contact fields, are omitted. Nested values are omitted. Bodies over 16 KB and malformed JSON are skipped. Strings are limited to 2,000 characters and the serialized field collection to 6,500 characters. The capture status identifies omissions due to size. Reading the body preserves it for model binding.

Notes and free-text fields may contain personal information supplied by users. All authenticated photographers currently have access to all event logs, including captured notes. Existing log retention applies. Only future events receive request bodies; existing events are unchanged. No database migration is needed because the existing Details column stores the JSON.

Deploy the API and frontend. Keep environment-specific credentials out of appsettings.json. The UI shows the request body in a separate section of the event modal. This supplements EVENT_LOGGING.md's original body-exclusion description.
