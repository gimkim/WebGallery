# Persist authentication keys across deployment

- Date: 2026-08-10 15:07:38 +07:00 (Asia/Bangkok)

## Request and intent

- Prevent users from being forced to sign in again after every IIS deployment or application restart.

## Cause

- The authentication cookie was configured for 14 days with sliding expiration, but the application had no explicit persistent ASP.NET Core Data Protection key ring.
- A deployment/app-pool restart could therefore leave the `GimGallery.Auth` cookie encrypted with a key that the restarted process could not load.

## Implementation

- Added `Gallery:DataProtectionKeysPath` to `GalleryOptions` and production/development configuration.
- Production default is `C:\Web\imagegallery-data\keys`; development defaults to `App_Data/keys`.
- Registered filesystem-backed Data Protection with application name `WebGallery` before Identity registration.
- Updated the Windows/IIS installation guide and durable agent notes to retain the key directory and grant the IIS worker Modify access.
- The key directory is outside the deployment folder, so replacing published binaries does not replace the key ring.

## Files changed

- `Program.cs`
- `Models/GalleryOptions.cs`
- `appsettings.json`
- `appsettings.Development.json`
- `deployment/INSTALL-WINDOWS11-IIS.txt`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Validation actually performed

- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- `node --check wwwroot/js/site.js`: passed.
- `dotnet run --project tests/LoginAttemptLimiterHarness/LoginAttemptLimiterHarness.csproj -c Release`: passed.
- `dotnet list WebGallery.csproj package --vulnerable --include-transitive`: no vulnerable packages were reported by the configured sources.
- Isolated restart test: first start created one persistent XML key, login succeeded, the server was stopped, the same database/key directory was used for a second start, and the original cookie reached Gallery with HTTP 200 without another login.
- Backed up 50 overwritten deployment files under `backup/2026-08-10_150635_pre-persistent-dpkeys/deploy`.
- Put IIS offline during the copy window and made a consistent SQLite backup at `backup/2026-08-10_150635_pre-persistent-dpkeys/gallery.db`; integrity check returned `ok`.
- Created `C:\Web\imagegallery-data\keys` and granted `IIS_IUSRS` Modify access with inherited child permissions.
- Published and copied 50 files; every publish/deployment SHA-256 pair matched. Deployed `WebGallery.dll` SHA-256: `C62EDC47FA3F1DD70D68AC8A9A60A5E5F87A00AFA7DA259AA11B5E84E4B35864`.
- Public Login returned HTTP 200 after deployment, the production key directory contains one generated XML key, production SQLite integrity is `ok`, deployed settings match source, and `app_offline.htm` is absent.
- No interactive browser/UI validation was performed; this change is authentication state and deployment behavior validated by isolated HTTP and live HTTP checks.

## User-visible result

- After signing in once following this deployment, normal IIS app-pool restarts and future deployments on this machine retain the authentication cookie until its configured 14-day sliding lifetime expires.
- Cookies issued before the persistent key ring was introduced cannot be migrated automatically and may require one final sign-in after this first deployment.

## Source publication

- The fix is ready to be committed and pushed to the private `gimkim/WebGallery` repository on `main` after this worklog is staged.

## Remaining uncertainty

- A real browser session from before/after deployment was not available for visual confirmation. The isolated cookie-preservation test exercises the same ASP.NET Core cookie/Data Protection path.
