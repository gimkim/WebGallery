# Collection density and folder-picker sorting

- Date/time: 2026-08-10 18:58:08 Asia/Bangkok (UTC+07:00)
- User request: Let the Collections management page adjust items per row, and let the Add folders page sort its folder cards.
- Concepts/rules established: Collections management has one visible folders-per-row slider that changes every collection member grid and persists through the shared Gallery column preference. The collection folder picker sorts directory-only results on the server by Name or modified Date, toggles ascending/descending, and carries that ordering through hierarchical navigation and the Add folders POST/redirect. Collection share creation captures the currently selected density.
- Files changed: `Controllers/CollectionsController.cs`, `ViewModels/GalleryViewModels.cs`, `Views/Collections/Index.cshtml`, `Views/Collections/SelectFolders.cshtml`, `wwwroot/js/site.js`, `wwwroot/css/site.css`, `wwwroot/css/site-modern.css`, `AGENTS.md`, `.agents/PROJECT_NOTES.md`.
- Validation performed:
  - The first attempt to run build and harness commands concurrently collided on `obj/Release/net10.0/WebGallery.dll`; the same checks were rerun sequentially.
  - `dotnet build WebGallery.csproj -c Release` passed with 0 warnings and 0 errors.
  - `dotnet run --project tests\FileSystemVisibilityHarness\FileSystemVisibilityHarness.csproj -c Release` passed.
  - `dotnet run --project tests\LoginAttemptLimiterHarness\LoginAttemptLimiterHarness.csproj -c Release` passed.
  - `node --check wwwroot\js\site.js` passed.
  - `dotnet list WebGallery.csproj package --vulnerable --include-transitive` reported no vulnerable packages.
  - Focused static assertions confirmed the picker sort controls/state fields, Collections density control/share hook, and JavaScript update of every collection grid.
  - `git diff --check` passed with only the repository's normal LF-to-CRLF warnings.
- Deployment: Published Release output to `publish\2026-08-10_185719`, backed up the previous IIS application and database to `backup\2026-08-10_185719_pre-collection-controls`, placed the app offline briefly, and copied the publish output to `C:\Web\imagegallery` while excluding `appsettings.Development.json`. Source-publish and deployed hashes matched. Deployed SHA-256 values: `WebGallery.dll` `B22D14D66F27A5C93A323526E50189C98651A4F7A2C59400A8F6D81579C3833B`; `site.js` `52A67F8F6FEAAD479D0C6B4038C3C220C534CCCD0F11B5E09A3EFA6C3ADC182A`; Retro CSS `4FF0F4AB42ABE98F09B1A8E7C64D886423204E19F63E03BAC86AA8F79EE73BA4`; Modern CSS `807536F1A81B66A4164831931C38CC970988F0D5EA6258EFBBD51F3BFF4C2332`.
- Live verification: Anonymous requests to live `/Gallery/Collections` and `/Gallery/Collections/SelectFolders/1?sort=date&dir=desc` returned the expected HTTP 302 application-login redirects; following Collections reached HTTP 200, live `js/site.js` returned HTTP 200, `app_offline.htm` was absent, and production SQLite `PRAGMA integrity_check` returned `ok`.
- User-visible result: The Collections page now has a Folders per row slider that immediately updates all member grids and remembers the setting. Add folders now offers Name and Date sort buttons with ascending/descending toggles that remain active while browsing and adding.
- Remaining uncertainty: No authenticated interactive browser or visual UI validation was performed because this project continues to avoid the unstable In-app Browser surface. Signed-in interaction and responsive appearance in Retro/Modern remain manual checks.
