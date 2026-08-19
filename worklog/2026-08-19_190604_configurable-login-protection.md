# Configurable login protection

## Date and time

2026-08-19 19:06:04 +07:00 (Asia/Bangkok)

## Request / intent

Allow administrators to configure login attempt limits in Management, including when progressive delay starts, how much delay is added after each further failure, and cooldown behavior.

## Concepts and rules established

- Management owns six bounded login-protection settings: progressive username delay threshold/increment, username cooldown threshold/duration, and direct-client-IP cooldown threshold/duration.
- Defaults are delay after 3 failures with 2 seconds added per further failure, username cooldown after 5 failures for 15 minutes, and IP cooldown after 12 failures for 5 minutes.
- Settings are persisted in SQLite and update one immutable runtime snapshot immediately after a successful save; no IIS restart is required.
- Progressive delay returns HTTP 429 with `Retry-After` rather than sleeping and tying up a request thread.
- Known-account cooldown is also persisted through ASP.NET Identity `LockoutEnd`, while hashed in-memory username/IP buckets protect nonexistent names and password-spray attempts. Existing persistent lockouts retain their recorded expiry after settings change.

## Files changed

- `Services/LoginSecuritySettings.cs`: added bounded defaults, validation, and thread-safe runtime snapshot.
- `Services/LoginAttemptLimiter.cs`: made thresholds/durations dynamic and added progressive username delay plus exact cooldown start tracking.
- `Controllers/AccountController.cs`: applied the runtime threshold to explicit Identity failure recording and persistent account lockout.
- `Controllers/AdminController.cs`, `ViewModels/GalleryViewModels.cs`, `Views/Admin/Index.cshtml`: loaded, displayed, validated, saved, and immediately applied all six settings.
- `Data/DatabaseInitializer.cs`, `Program.cs`: seeded/repaired SQLite settings, initialized the runtime snapshot, and replaced Identity's fixed built-in threshold with the configured controller path.
- `wwwroot/css/site.css`, `wwwroot/css/site-modern.css`: styled the Login protection subsection consistently in both themes.
- `tests/LoginAttemptLimiterHarness/*`: covered default progressive delays, exact cooldowns, live runtime setting changes, concurrency, reset, and invalid configuration.
- `AGENTS.md`, `.agents/PROJECT_NOTES.md`: recorded the durable security architecture and defaults.

## Validation actually performed

- `dotnet build WebGallery.csproj -c Release` passed with 0 warnings and 0 errors.
- `tests/LoginAttemptLimiterHarness` passed, including default/custom delay and cooldown behavior.
- `tests/FileSystemVisibilityHarness` and `tests/ShareAuditHarness` passed.
- `git diff --check` passed with only the repository's existing line-ending conversion notices.
- The browser-cache probe project was identified as a persistent browser test server rather than a self-test; its process was stopped and no browser claim is made.
- Release publish completed at `publish/20260819_190522`.
- The preceding IIS application (146 files) and a SQLite online backup with `PRAGMA integrity_check = ok` were stored under `backup/20260819_190522_pre-login-settings`.
- Release output was copied to `C:\Web\imagegallery` with `appsettings.Development.json` excluded. Publish/deploy SHA-256 matched for `WebGallery.dll` and both theme stylesheets.
- The live `/Gallery/Admin` boundary returned the expected HTTP 302 to the application Login route, and live Login returned HTTP 200.
- Production startup created all six login settings with the intended defaults; production SQLite integrity remained `ok`.
- Confirmed the deployed application contains neither `app_offline.htm` nor `appsettings.Development.json`.

## User-visible result

Management > System and users now includes a Login protection section. Saving valid values changes new login checks immediately. Repeated failures receive the existing themed countdown response, with configurable progressive delay and username/IP cooldown behavior.

## Remaining manual validation / uncertainty

- Authenticated visual inspection of the Management form in both themes remains manual under the project's in-app-browser workaround.
- A deliberate failed-login test was not run against the production administrator account to avoid creating a real account lockout. The limiter behavior was instead exercised in the focused harness.
