# Collection management folder grid

- Date/time: 2026-08-10 18:09:52 Asia/Bangkok (UTC+07:00)
- User request: Replace the text-heavy Collections management folder list with a Grid presentation matching the Gallery view.
- Concepts/rules established: Collection membership is displayed as responsive Gallery-style folder cards with direct-child cover collages, cached visibility-driven thumbnails, folder silhouettes, and the user's saved Gallery column count. Each card retains an accessible Remove action. Missing, hidden, or inaccessible member paths remain visible as dim unavailable cards without a navigation link so stale membership can be removed.
- Files changed: `Controllers/CollectionsController.cs`, `ViewModels/GalleryViewModels.cs`, `Views/Collections/Index.cshtml`, `wwwroot/css/site.css`, `wwwroot/css/site-modern.css`, `wwwroot/js/site.js`, `AGENTS.md`, `.agents/PROJECT_NOTES.md`.
- Validation performed:
  - `dotnet build WebGallery.csproj -c Release` - passed with 0 warnings and 0 errors, including Razor compilation.
  - `dotnet run --project tests\\FileSystemVisibilityHarness\\FileSystemVisibilityHarness.csproj -c Release` - passed.
  - `dotnet run --project tests\\LoginAttemptLimiterHarness\\LoginAttemptLimiterHarness.csproj -c Release` - passed.
  - `node --check wwwroot\\js\\site.js` - passed.
  - `dotnet list WebGallery.csproj package --vulnerable --include-transitive` - no vulnerable packages reported.
  - `git diff --check` - passed; only normal LF-to-CRLF warnings were reported.
- Deployment: Backed up the prior IIS deployment and SQLite database under `backup\\2026-08-10_1809_pre-collection-grid\\`; the pre-deployment database integrity check returned `ok`. Published Release output to a temporary staging directory and copied it to `C:\\Web\\imagegallery` while excluding `appsettings.Development.json` from the copy and preserving external data/key directories. Source/deployed hashes matched for `WebGallery.dll` (`F48BDBEE2C627A834C7DA9641982BA8C1C666FC9540EA5447148699B27829BD5`) and `wwwroot/js/site.js` (`D99A162F577565A2957BBB8A9D0640E17C6992F319B516CB13BFFA445FB1F1D7`). The live Collections endpoint returned the expected anonymous 302 and followed to the Sign in page with HTTP 200; live `site.js` returned HTTP 200, `app_offline.htm` is absent, and production SQLite integrity remains `ok`.
- User-visible result: The Collections management page now shows each member folder as a recognizable image/folder Grid card rather than a path-only text row, while collection deletion, folder removal, link copy/revoke, and link creation remain available.
- Remaining uncertainty: No authenticated interactive browser or visual UI validation was performed because this project still avoids the unstable In-app Browser surface. Card layout, hover/focus behavior, and both themes remain a manual signed-in visual check.
