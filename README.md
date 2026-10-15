# Lensora API

ASP.NET Core API shared by Lensora's public photography sites and authenticated
studio/platform management screens. This repository includes the SQL migration
scripts, deployment checklist and development integration checks for this release.

Read `docs/release-checklist.md` before deployment, and `docs/media-library.md` for
storage accounting, supported media, quota enforcement and reconciliation.

Set database, JWT, email and Cloudinary credentials through environment variables,
Azure App Service configuration or local .NET user secrets; never commit credentials.
Run `dotnet run --launch-profile https` for local development. For first setup, apply
the appropriate scripts in `database/` to the target SQL Server database. Bootstrap
the first verified SuperAdmin account directly in the database before using the
protected registration screen.

Verification: `dotnet build Lensora.Api.csproj --configuration Release`.
Integration checks are under `verification/` and run against development databases
only. They create and clean up temporary records; media uploads use a fake provider.
