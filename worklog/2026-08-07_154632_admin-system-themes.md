# Administrator-selectable system themes

- Date: 2026-08-07 15:46:32 +07:00 (Asia/Bangkok)

## Request and intent

- Add a system-wide theme choice to Admin Settings.
- Preserve the currently deployed appearance as `Retro`.
- Add a second `Modern` appearance.

## Durable concepts established

- The selected theme is stored in SQLite as the `Theme` app setting with normalized values `retro` or `modern`.
- Missing or invalid stored values fall back to `retro`, so upgrading does not unexpectedly change the live site's appearance.
- Retro loads `site.css` and remains the exact established FileListing-inspired pixel interface.
- Modern loads `site-modern.css`: the shared functional/base CSS without the 300-line Retro override block, with Segoe UI-style typography, GitHub-dark colors, subtle fixed radial gradients, rounded cards/folder shells, and soft shadows.
- Theme changes presentation only. Layout behavior, responsive rules, thumbnail sizing/loading, selection, sharing, and the full viewer remain shared requirements and must be maintained in both stylesheets.

## Files changed

- `ViewModels/GalleryViewModels.cs`
- `Data/DatabaseInitializer.cs`
- `Controllers/AdminController.cs`
- `Views/Admin/Index.cshtml`
- `Views/Shared/_Layout.cshtml`
- `wwwroot/css/site.css`
- `wwwroot/css/site-modern.css` (new)
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Backup and deployment

- Created `backup/2026-08-07_154541_pre-system-theme`.
- Backed up deployed DLL/PDB, Retro CSS, any prior Modern CSS, and a consistent SQLite backup made with the SQLite backup API.
- The backup database passed `PRAGMA integrity_check`.
- Published Release output to `publish-current`.
- Used `app_offline.htm` briefly while replacing DLL/PDB, removed it, and deployed both theme stylesheets.

## Validation actually performed

- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- A focused headless-Chrome theme harness rendered representative card, folder, and Admin select elements under each real stylesheet.
- Retro computed Cascadia Mono, 0 px card/folder radii, and the existing hard 4 px offset shadow.
- Modern computed the Segoe UI font stack, 15 px card radius, 10 px folder-shell radius, and non-pixel styling.
- Both styles computed a 42 px minimum height for the new Admin Theme select.
- The temporary harness file was removed after testing.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.
- After deployment, startup created `AppSettings.Theme=retro`; production SQLite `PRAGMA integrity_check` returned `ok`.
- An authenticated deployed Admin request returned HTTP 200, contained the Theme select with Retro selected, emitted `body.theme-retro`, and linked Retro CSS rather than Modern CSS.
- Live `site-modern.css` returned successfully and contained the intended Modern typography, gradients, and rounded folder styling.
- Published/deployed DLL SHA-256 matched: `845C5F58757B3ED7586968672CA01B183573FD5B2EA49035C2AB9AF3CDDB6942`.
- Published/deployed Retro CSS SHA-256 matched: `349C5977033E298A5F1492FEED2C6AECF011EFB3DB1970CF8724E7B851670FD5`.
- Published/deployed/live Modern CSS SHA-256 matched: `EFA04B9308E33FA6EAC28E1AE798D739838AB2AA8D6EDF704B07E4C71E6FBFC7`.
- Live `/Gallery/` returned HTTP 302 to login for an anonymous request after deployment.
- The source directory is not a Git repository, so no Git status or `git diff --check` result is available.

## User-visible result

- Admin > System now offers Retro and Modern in a Theme dropdown.
- Saving Modern applies the contemporary appearance system-wide on the next response; saving Retro restores the current pixel appearance.
- The deployment remains on Retro until the administrator explicitly chooses Modern and saves.

## Remaining manual validation

- In Admin > System, select Modern and Save, then inspect Gallery Grid/List, Share, Login, Admin, and the full-image viewer on desktop and mobile. Select Retro and Save to compare or restore the existing appearance.
- The real deployed default/Retro path and both isolated stylesheets were verified, but the production setting was not temporarily switched to Modern during automated validation to avoid changing the live UI for active users.
