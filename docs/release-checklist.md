# Release: templates, SuperAdmin and photographer media library

The UI and API currently have separate Git repositories and share one API.
This release does not split the React application. Review and merge the API first,
then the UI. The UI's production deployment follows `main`; the API's Azure pipeline
follows `master`. Feature-branch pushes verify builds without deploying production.

## Database before API deployment

Back up the target SQL database. Apply any missing existing migrations in numeric
order, then `database/011_photographer_template.sql`,
`database/012_template_assignment_audit.sql` and `database/013_media_library.sql`.
The scripts use `USE Lensora`; verify the target server and database before running.
Scripts are included in this API repository and do not run automatically in CI.
They add Editorial defaults, audited design assignment, media ownership/hash/size
tracking, a 5,000,000,000-byte default allowance, and existing-image references.
No existing media files are deleted by these migrations.

## Deployment configuration

Keep JWT signing keys, database credentials, SMTP credentials and Cloudinary API
secrets in Azure App Service configuration. appsettings.json in Git contains empty
secret placeholders; the developer's populated local file must not be committed.
Set `ConnectionStrings__Lensora` or `ConnectionStrings__DefaultConnection`,
`Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`, Cloudinary settings and any SMTP/WhatsApp
settings required by the existing notification flow. Allow the deployed UI origin
through `Cors__AllowedOrigins__0`. Accept upload request bodies up to 51 MB through
any reverse proxy. Storage usage counts original managed files, not bandwidth or
derived Cloudinary renditions.

## SuperAdmin bootstrap

Verify the intended user account in the production database, then update only that
account's `Users.Role` to exactly `SuperAdmin`. Do not copy a local user ID into a
production update. Sign out and sign in afterward. Registration and platform event
logs now require SuperAdmin and verify the current database role. Photographers can
manage only their own studio records; they cannot assign designs or register users.
New registrations remain Photographer accounts with Editorial.

## Smoke checks after API then UI deployment

- Sign in as SuperAdmin, open Photographers and preview both designs.
- Assign a design; confirm the public URL and chosen design after reload.
- Register a photographer and confirm the SuperAdmin session is preserved.
- Sign in as that photographer; confirm its dashboard brand opens its own slug.
- Open Media library and sync legacy images until no unknown sizes remain.
- Upload an image, upload identical contents under another filename, and confirm
  the second request reuses the asset and does not increase usage.
- Confirm library selection works below each image upload control.
- Verify photographer access to platform design, registration and event logs is
  denied, while public profiles and enquiry forms work without sign-in.

## Verification

`dotnet build Lensora.Api.csproj --configuration Release`

Against a development database after migrations, from this API repository:

`dotnet run --project verification/PlatformDesignChecks/PlatformDesignChecks.csproj`

`dotnet run --project verification/MediaLibraryChecks/MediaLibraryChecks.csproj`

These checks use local appsettings, user secrets and environment variables, create
and remove temporary database records, and use a fake provider for media uploads.
CI builds the checks without running them against a production database. No cloud
upload or database migration is performed by the GitHub verification workflows.

Known boundaries: no self-service role management, refresh tokens, password reset,
MFA or library deletion. Exact-byte duplicate detection does not match recompressed
or edited images. Already-existing physical duplicate cloud assets are counted and
not deleted. Missing provider assets require administrator reconciliation.
