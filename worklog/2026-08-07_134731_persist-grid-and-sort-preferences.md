# Persist Grid and sort preferences

- Date: 2026-08-07 13:47:31 +07:00 (Asia/Bangkok)

## Request and intent

- Change the first-use Grid density default to 8 items per row.
- Remember the user's selected sorting and items-per-row values across folder navigation and later visits.

## Durable concepts established

- The default applies only when the browser has no saved Grid density; an existing user-selected value remains authoritative.
- Grid/List choice and Grid density stay in browser local storage because they are presentation-only client preferences.
- Sort field/direction affect server-side filesystem enumeration, so they are persisted as normalized application-scoped cookies and restored by both private and shared Gallery actions when query parameters are omitted.
- Only `name`, `size`, or `date` and `asc` or `desc` are accepted for persistence.

## Files changed

- `Models/GalleryOptions.cs`
- `ViewModels/GalleryViewModels.cs`
- `Controllers/GalleryController.cs`
- `appsettings.json`
- `appsettings.Development.json`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Backup

- Before deployment, backed up the deployed DLL and application settings to `backup/2026-08-07_134731_pre-persist-gallery-preferences`.

## Validation actually performed

- Confirmed the existing JavaScript restores `gim-gallery-columns` from local storage for values 2 through 10 and writes every slider change back to that key.
- Changed model, view-model fallback, production settings, and development settings to `DefaultItemsPerRow=8`.
- Added normalized sort restoration/persistence using `gallery-sort` and `gallery-sort-direction` cookies with HttpOnly, Essential, SameSite=Lax, one-year lifetime, HTTPS Secure behavior, and the current application PathBase.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- `node --check wwwroot/js/site.js`: passed; JavaScript was inspected but not changed in this session.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.
- Deployed the DLL/PDB and `appsettings.json` after `app_offline.htm` caused IIS to release the application lock; removed the offline file afterward.
- SHA-256 matched between publish output and IIS deployment for `WebGallery.dll` and `appsettings.json`.
- A dedicated .NET 10 live HTTP harness used an existing active unlisted share without printing its token and verified:
  - an explicit `size`/`desc` request persisted both cookie values;
  - both cookies were scoped to `/Gallery`;
  - the following request without `sort` or `dir` rendered Size descending as the active sort;
  - the rendered Grid CSS variable and range input both defaulted to 8.
- Live `/Gallery/` returned HTTP 302 to the application login page after deployment.
- The source directory is not a Git repository, so no Git status or `git diff --check` result is available.

## User-visible result

- New/unsaved Grid sessions start at 8 items per row.
- Moving the per-row slider continues to remember the user's value and overrides the default on subsequent pages and visits.
- The last selected Name/Size/Date and ascending/descending sort is restored when entering Gallery or an unlisted share without an explicit sort query.

## Remaining manual validation

- Interactive browser QA was not run because this project's Codex workaround prohibits In-app Browser use after two prior app exits during browser teardown.
- In the signed-in browser, choose a non-default Grid density and Date descending, navigate through child/parent folders, then open `/Gallery/` again and confirm both preferences remain. Existing local-storage density values intentionally remain unchanged rather than being forcibly reset to 8.
