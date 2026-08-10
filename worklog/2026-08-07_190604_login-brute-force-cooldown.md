# Login brute-force rate limit and cooldown

- Date: 2026-08-07 19:06:04 +07:00 (Asia/Bangkok)

## Request and intent

- Add rate limiting and a visible cooldown to the application Login page to reduce brute-force and password-spraying attacks.
- Keep the response generic so it does not reveal whether a username exists.

## Security design

- ASP.NET Identity now explicitly locks a real account after 5 failed password attempts for 15 minutes. This state is persisted in SQLite and survives application restarts.
- A singleton in-memory sliding-window limiter adds two independent controls:
  - 5 failures per normalized username over 15 minutes, including nonexistent usernames.
  - 12 failures per direct client IP over 5 minutes.
- Limiter keys are SHA-256 hashes rather than raw usernames/IPs and the private memory cache is bounded to 10,000 entries with expiry.
- Buckets use per-entry synchronization so concurrent failed requests do not lose increments.
- Successful authentication clears the matching username and IP buckets; Identity also resets its persisted failed count.
- A blocked POST returns the normal themed Login view with HTTP 429, a numeric `Retry-After` response header, and an English countdown. The response does not state whether the username or IP rule fired.
- The IP key uses `HttpContext.Connection.RemoteIpAddress`. A future reverse proxy must configure and trust forwarded headers explicitly before changing this behavior; arbitrary incoming forwarding headers must not be trusted.

## Files changed

- `Services/LoginAttemptLimiter.cs`
- `Program.cs`
- `Controllers/AccountController.cs`
- `ViewModels/GalleryViewModels.cs`
- `Views/Account/Login.cshtml`
- `wwwroot/js/site.js`
- `wwwroot/css/site.css`
- `wwwroot/css/site-modern.css`
- `WebGallery.csproj`
- `tests/LoginAttemptLimiterHarness/LoginAttemptLimiterHarness.csproj`
- `tests/LoginAttemptLimiterHarness/Program.cs`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Test infrastructure note

- The first web build after adding the nested console harness failed because the Web SDK's default compile glob included `tests/**/*.cs`. `WebGallery.csproj` was corrected to exclude all test source/content from the production application and publish output.
- This was a test-project isolation issue, not an application-source compile error. The corrected web build passed and `publish-current/tests` does not exist.

## Validation actually performed

- Initial `dotnet build WebGallery.csproj -c Release` after the application changes: passed with 0 warnings and 0 errors.
- An attempted disposable HTTP form-post test was rejected by the shell safety policy before the local test server started. No request or credential attempt was made by that command, so it is not counted as HTTP-flow validation.
- Added and ran the dependency-free `LoginAttemptLimiterHarness`. It passed assertions for:
  - attempts 1-4 remaining available and attempt 5 starting a 900-second username cooldown;
  - a different username remaining available before the IP threshold;
  - attempt 12 from one IP starting a 300-second IP cooldown;
  - IP expiry after 5 minutes while the username cooldown remains active across another IP;
  - username expiry after 15 minutes;
  - successful-login reset behavior;
  - concurrent failures preserving every increment.
- Final `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.
- Static review confirmed the controller checks cooldown before password verification, records failed attempts, handles Identity lockout, returns 429/`Retry-After`, and renders the generic countdown markup.

## Backup and deployment

- Backed up the previously deployed binaries and affected static assets under `backup/2026-08-07_190511_pre-login-rate-limit`.
- Used `C:\Web\imagegallery\app_offline.htm` only during the copy window, deployed the published binary and JS/CSS plus compressed variants, then removed the offline file.
- Did not overwrite production configuration, SQLite data, thumbnail cache, user roots, or gallery content.

## Post-deployment validation

- Published and deployed SHA-256 hashes matched for:
  - `WebGallery.dll`: `26B8CDA831FF2BF1453A73FA7BA0C62628FC8AA2A3AA486133E7E4EDB46C8A41`
  - `site.js`: `AA4F9DB02B9C1BFB6098D569AFA8840B90312F2D3B811538B2FE853EE68F8FB8`
  - Retro CSS: `D5899D5E5CF370D29C59CB041C0A2689B233D6227EF5AB07767E3094C2A7CD7D`
  - Modern CSS: `1E62A20A7F956C2296EA8ECC4514D9E4E756EBC8576FB3B4D4E97863C388417A`
- Confirmed the test harness was excluded from publish output and `app_offline.htm` was absent.
- The live Login endpoint returned HTTP 200 and contained the username form plus anti-forgery token.
- Live JavaScript contained the cooldown updater and live Modern CSS contained the countdown layout.
- Anonymous `/Gallery/` returned HTTP 302 to the application Login route.
- Failed passwords were deliberately not submitted to production, so no real account or production IP bucket was consumed during validation.
- The source directory is not a Git repository, so no Git status or `git diff --check` result is available.

## User-visible result

- Five failed attempts against one username produce a 15-minute Login cooldown.
- Twelve failures from one IP across usernames produce a 5-minute cooldown.
- The Login page remains themed, shows a live countdown, and returns a standards-friendly 429 response with `Retry-After`.

## Remaining manual validation

- If desired, use a disposable non-admin account on the live site to confirm the complete POST-to-429 browser flow and allow/revoke the test account afterward.
- When adding Cloudflare Tunnel or another reverse proxy, revisit trusted forwarded-header configuration so IP limiting uses the authenticated proxy-provided client IP rather than the proxy connection address.
