# Keep newly created share link visible

- Date: 2026-08-07 16:13:45 +07:00 (Asia/Bangkok)

## Request and intent

- After Create share link succeeds, keep the Share panel expanded instead of collapsing it during the redirect.
- Show the newly created URL in a clear box with its Copy button immediately available.

## Implementation

- `CreateShare` now retains the newly saved `ShareLink` entity, then places its database ID and an open-panel flag in TempData before redirecting.
- The Gallery view peeks those one-request values, marks the corresponding URL row, and emits an open-after-create flag on the Share panel.
- Gallery JavaScript removes the panel's `hidden` state when that flag is present. A normal visit still begins with the panel collapsed.
- Added theme-specific highlight styling for the new URL row in both Retro and Modern.

## Files changed

- `Controllers/GalleryController.cs`
- `Views/Gallery/Index.cshtml`
- `wwwroot/js/site.js`
- `wwwroot/css/site.css`
- `wwwroot/css/site-modern.css`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Backup and deployment

- Backed up the previously deployed affected binaries and static assets under `backup/2026-08-07_161301_pre-share-result-panel`.
- Published Release output to `publish-current`.
- Used IIS `app_offline.htm` only during the file-copy window, deployed the new binary plus the changed JavaScript/CSS and compressed static variants, then removed `app_offline.htm`.
- Preserved production configuration and persistent database/cache paths; no production share records were created or modified during deployment verification.

## Validation actually performed

- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.
- Static assertions confirmed that the controller writes `OpenSharePanel` and `CreatedShareLinkId`, the Razor view emits the open flag and highlights the matching row, and JavaScript unhides only panels marked after creation.
- Attempted to start a disposable authenticated integration flow against a temporary SQLite database, but the shell safety policy rejected the command before execution. No temporary server, database, or test share link was created; this is not counted as a completed flow test.
- Published and deployed SHA-256 hashes matched for `WebGallery.dll`, `site.js`, `site.css`, and `site-modern.css`.
- The live JavaScript contained the open-after-create selector and unhide operation. Both live theme stylesheets contained `.share-row-created`.
- The public Login endpoint returned HTTP 200. Anonymous `/Gallery/` returned HTTP 302 to `/Gallery/Account/Login?ReturnUrl=%2FGallery%2F` after deployment.
- Confirmed that `C:\Web\imagegallery\app_offline.htm` was removed after deployment.
- The source directory is not a Git repository, so no Git status or `git diff --check` result is available.

## User-visible result

- Creating a share link returns to the Gallery with the Share panel still open.
- The newly created URL row is visually highlighted and its Copy button can be used immediately.
- Opening the same Gallery normally later does not force the Share panel open.

## Remaining manual validation

- While signed in to the live Gallery, create one disposable share link and confirm that the expanded panel, highlighted URL, and Copy/Copied feedback work in the user's browser. Revoke the disposable link afterward if it is not needed.
