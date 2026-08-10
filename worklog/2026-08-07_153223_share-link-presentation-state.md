# Share-link presentation state

- Date: 2026-08-07 15:32:23 +07:00 (Asia/Bangkok)

## Request and intent

- Make a newly created unlisted share link open with the same sort order, Grid items per row, and Grid/List mode that the owner was using when the link was created.

## Durable concepts established

- Presentation state is stored per share link, not globally: normalized sort field/direction, items per row in the 2-10 range, and `grid`/`list` mode.
- Managed share URLs include `sort`, `dir`, `itemsPerRow`, and `view` query values.
- Explicit presentation values in a received share URL override the recipient's existing localStorage on that render. The existing controls then persist those values, so later navigation and recipient changes continue normally.
- Existing share rows receive Name Ascending, 8 per row, Grid defaults during the additive schema upgrade.
- Empty folders still restore and capture the browser's Grid/List and items-per-row preferences even though they do not render `#gallery-items`.

## Files changed

- `Models/ShareLink.cs`
- `ViewModels/GalleryViewModels.cs`
- `Controllers/GalleryController.cs`
- `Data/DatabaseInitializer.cs`
- `Views/Gallery/Index.cshtml`
- `wwwroot/js/site.js`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Backup and deployment

- Created `backup/2026-08-07_153043_pre-share-presentation`.
- Backed up the deployed DLL, PDB, JavaScript, and a consistent SQLite backup made with the SQLite backup API.
- The backup database passed `PRAGMA integrity_check`.
- Published Release output to `publish-current`.
- Used `app_offline.htm` briefly while replacing the application DLL/PDB, then removed it and deployed the published JavaScript.

## Validation actually performed

- `node --check wwwroot/js/site.js`: passed.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- A focused headless-Chrome harness started with conflicting localStorage (`3`, List) and explicit share-link state (`9`, Grid). The initial controls resolved to 9/Grid. After changing the controls, the create-share form submitted 6/List.
- A second focused headless-Chrome harness omitted `#gallery-items` to reproduce an empty folder. Stored 7/List preferences were restored and submitted as 7/List.
- Temporary harness files were removed after testing.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.
- After deployment, application startup added `Sort`, `Direction`, `ItemsPerRow`, and `ViewMode` to the production `ShareLinks` table. Production `PRAGMA integrity_check` returned `ok`.
- Published and deployed DLL SHA-256 matched: `9CCBBDDF766F91870465F7B75592573B07291C3E501BBA19ACFEA4EDFF9B1CD0`.
- Published, deployed, and live JavaScript SHA-256 matched: `C4DADF656D35AD4BE661A7BE9C8F021337718103691449D42BFBD21B50EC853C`.
- Live JavaScript contained explicit share-state preference handling and create-share form capture.
- Live `/Gallery/` returned HTTP 302 to the application login page after deployment.
- The production database currently had no active share row available for a non-destructive anonymous end-to-end link request.
- The source directory is not a Git repository, so no Git status or `git diff --check` result is available.

## User-visible result

- Creating a share link while viewing, for example, Date Descending / 6 per row / List stores that state on that specific link.
- Copying that managed link and opening it in another browser starts with Date Descending / 6 per row / List, regardless of the recipient's previously saved Gallery layout.
- Recipients can still change Grid/List and density afterward; their browser remembers their latest choice for subsequent navigation.

## Remaining manual validation

- While authenticated, choose a distinctive combination such as Size Descending / 5 per row / List, create a new link, copy it into a private/incognito browser, and confirm the first shared-folder render matches all four values.
- The form/state precedence and empty-folder paths were exercised in focused Chrome harnesses, but an authenticated create-link flow was not automated because no signed-in external browser session was used.
