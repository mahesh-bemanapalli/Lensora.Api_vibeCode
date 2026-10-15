# Photographer media library

Apply `database/013_media_library.sql` before deploying the updated API. The local
migration has been applied. Every photographer starts with 5,000,000,000 bytes
(5 decimal GB). `Photographers.StorageLimitBytes` can be adjusted by the database
administrator per account; there is no photographer-accessible limit setter.

The dashboard's Media library (`/admin/media`) shows original-file bytes used,
remaining allowance, pending reservations, and any unmeasured legacy assets.
Images, videos, audio and PDF documents remain in Cloudinary; SQL stores ownership,
URL, public ID, SHA-256 content hash, original byte size and processing status.
The metric excludes Cloudinary transformations, bandwidth and externally hosted
links; it is an application storage allowance, not the provider's full billing meter.

Supported uploads: JPG, PNG, GIF, WebP (20 MB each); MP4, MOV, WebM, MP3, WAV, OGG,
and PDF (50 MB each). Content signatures and extensions are checked. The library
supports uploading multiple files sequentially, previews, search and type filters.
Only image selection is enabled in website image fields; storing a video does not
add a video section to the public templates.

Existing profile, portfolio, gear and package upload forms use the same backend.
The image-library picker reuses existing assets without uploading. Uploading an
identical file with another filename returns the original asset; the same image
can be referenced in several website sections without consuming storage twice.
Edited/recompressed images have different contents and count as separate files.
Ownership and quota checks run in the API, not just the browser.

Quota reservations are saved before calling Cloudinary. SQL application locks
serialize quota and duplicate checks per photographer across API processes. Pending
uploads count toward usage, preventing parallel requests from exceeding the limit.
Failed/uncertain uploads retain their reservation. After 10 minutes, Sync existing
media either reconciles the remote asset, or marks an absent upload Retryable.
Reupload the exact same file to reuse that reservation. Upload calls have a
five-minute deadline. No cloud asset or library deletion is exposed in this version.
Removing an image from a portfolio or package does not free cloud storage.

Migration 013 imports already-referenced image URLs and public IDs. Cloudinary
sizes remain unknown until **Sync existing media** queries the provider. For small
legacy originals it also streams the original to calculate SHA-256. New uploads
are blocked while managed assets have unknown sizes. External URLs without public
IDs are displayed separately and excluded from managed storage. Unreferenced uploads
from before this feature cannot be reliably attributed to a photographer and are
not automatically imported. Existing physical duplicates are counted as stored;
new uploads reuse their matching hash without deleting old files.

Sync processes up to ten eligible records per request. Repeat it for larger
libraries. Assets missing from the provider remain unresolved and need administrator
investigation; do not remove their accounting until remote storage is verified.

API endpoints (authenticated, current photographer only):

- `GET /api/admin/media`: items and usage summary.
- `POST /api/admin/media/files`: multipart `file`, all supported media.
- `POST /api/admin/media/images`: backward-compatible image-only endpoint.
- `POST /api/admin/media/sync`: reconcile legacy and older pending assets.

Quota overflow returns HTTP 413. Pending duplicate or unmeasured legacy storage
returns HTTP 409. An existing ready duplicate returns HTTP 200 with
`isDuplicate: true`. Upload responses retain `url` and `publicId` for existing clients.
Configure any reverse proxy to allow 51 MB request bodies; the API's media endpoint
sets that multipart limit explicitly.

Run `dotnet run --project verification/MediaLibraryChecks/MediaLibraryChecks.csproj` from this API
workspace root against a development database. Checks create and remove temporary
photographer records and use a fake provider; they do not upload to Cloudinary.
