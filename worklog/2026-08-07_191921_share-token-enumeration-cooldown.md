# Share-token enumeration cooldown

- Date: 2026-08-07 19:19:21 +07:00 (Asia/Bangkok)

## Request and intent

- Add cooldown protection against automated guessing of public unlisted share tokens.
- Start cooldown after 20 invalid token attempts while preserving normal valid-share behavior.

## Security behavior

- The public `Gallery/Share` entry point uses a dedicated, bounded in-memory sliding-window limiter keyed by a SHA-256 hash of the direct client IP.
- Missing, malformed, unknown, and revoked tokens count as invalid.
- Tokens must have the generated format of exactly 48 hexadecimal characters before SQLite is queried.
- Invalid attempts 1-19 return the existing HTTP 404 response so individual failures do not disclose additional token information.
- Attempt 20 within 5 minutes starts a 5-minute cooldown. Requests during cooldown are rejected before SQLite lookup.
- Cooldown responses use the themed HTTP 429 page with a numeric `Retry-After` header and the shared live countdown component.
- A valid token is not counted. A valid token paired with a path outside its allowed folder scope remains a normal 404 and is not treated as token guessing.
- Once an IP is in cooldown, even a valid token is temporarily paused until the five-minute window expires. This avoids database-oracle bypass during an active abuse window.
- The limiter is bounded to 10,000 expiring IP buckets and synchronizes concurrent updates so parallel attempts cannot lose increments.

## Files changed

- `Services/InvalidShareTokenLimiter.cs`
- `Program.cs`
- `Controllers/GalleryController.cs`
- `ViewModels/GalleryViewModels.cs`
- `Views/Gallery/ShareCooldown.cshtml`
- `Views/Account/Login.cshtml`
- `wwwroot/js/site.js`
- `wwwroot/css/site.css`
- `wwwroot/css/site-modern.css`
- `tests/LoginAttemptLimiterHarness/LoginAttemptLimiterHarness.csproj`
- `tests/LoginAttemptLimiterHarness/Program.cs`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## UI reuse

- Generalized the Login-only countdown selectors/classes to `data-cooldown`, `data-cooldown-value`, and `.cooldown-alert`.
- Login behavior remains unchanged; both Login and invalid-share cooldown pages now use the same English countdown logic and theme-compatible layout.

## Validation actually performed

- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- Extended and ran the dependency-free security limiter harness. It passed assertions that:
  - invalid share attempts 1-19 do not activate cooldown;
  - attempt 20 activates cooldown;
  - initial `RetryAfterSeconds` is 300;
  - a different IP remains unaffected;
  - cooldown expires after 5 minutes;
  - 20 parallel invalid attempts retain all increments and activate cooldown.
- The existing Login limiter assertions also passed after the countdown UI was generalized.
- An attempted local HTTP test of 20 invalid Share requests was rejected by shell safety policy before the local server started. No Share request was sent by that command, so the controller-to-view HTTP flow was not claimed as executed.
- Static review confirmed cooldown is checked before token-format validation/database access, invalid or revoked records are counted, valid out-of-scope paths remain ordinary 404 responses, and the 429 page sets `Retry-After`.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.

## Backup and deployment

- Backed up the previously deployed binaries and affected static assets under `backup/2026-08-07_191841_pre-share-token-rate-limit`.
- Used `C:\Web\imagegallery\app_offline.htm` only during the copy window, deployed the published binary and JS/CSS plus compressed variants, then removed the offline file.
- Production configuration, SQLite data, share records, thumbnail cache, user roots, and gallery content were not modified.

## Post-deployment validation

- Published and deployed SHA-256 hashes matched for:
  - `WebGallery.dll`: `DC16E7798228C9396716719C6DDB358D355065161CF9BA0A60FB6EFC67E595F3`
  - `site.js`: `DEB240259A0FF5EB062AB016D4BE15A67DFBCC46F3D6B5EE2EF41EE678D1C17C`
  - Retro CSS: `B158B4EC06B91036AFDF24F3F73AA0D07C3699FCBEB51B4AB844F9870A607C32`
  - Modern CSS: `94127CB516295ABF3CA0C5419C6E8EF00C931EB4F96EF9CBDD29531E51939D08`
- Confirmed test sources were excluded from publish output and `app_offline.htm` was absent.
- The live Login endpoint returned HTTP 200. Live JavaScript contained the generalized countdown selector and live Modern CSS contained `.cooldown-alert`.
- Anonymous `/Gallery/` returned HTTP 302 to the application Login route.
- Production was deliberately not sent invalid Share tokens, so no real public-IP limiter bucket was consumed during validation.
- The source directory is not a Git repository, so no Git status or `git diff --check` result is available.

## User-visible result

- Automated share-token guessing from one IP is slowed after 20 invalid attempts in five minutes.
- A blocked requester sees a themed five-minute countdown instead of continuing to query the database.
- Normal valid share URLs are unaffected unless the same IP has already entered an active abuse cooldown.

## Remaining manual validation

- If desired, run the complete 19x404 then 1x429 sequence from a disposable external IP or a controlled local environment whose tooling permits repeated HTTP probes.
- If a trusted reverse proxy or Cloudflare Tunnel is added, configure forwarded headers explicitly; otherwise all external requests may share the proxy IP limiter bucket.
