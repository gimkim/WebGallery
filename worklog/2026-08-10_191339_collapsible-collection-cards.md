# Collapsible collection management cards

- Date/time: 2026-08-10 19:13:39 Asia/Bangkok (UTC+07:00)
- User request: Make each collection on the management page collapsible so a user with many collections does not have to scroll through every folder grid and share-link section.
- Concepts/rules established: Collection cards start collapsed. Their header always keeps the collection name, folder count, Add folders, Delete, and an accessible Expand/Collapse button visible. Expansion is stored independently per collection ID in browser local storage. A collection containing a just-created share link is forced open and its state is saved as expanded so the highlighted URL and Copy button remain visible.
- Files changed: `Views/Collections/Index.cshtml`, `wwwroot/js/site.js`, `wwwroot/css/site.css`, `wwwroot/css/site-modern.css`, `AGENTS.md`, `.agents/PROJECT_NOTES.md`.
- Validation performed:
  - `dotnet build WebGallery.csproj -c Release` passed with 0 warnings and 0 errors, including Razor compilation.
  - `dotnet run --project tests\FileSystemVisibilityHarness\FileSystemVisibilityHarness.csproj -c Release` passed.
  - `dotnet run --project tests\LoginAttemptLimiterHarness\LoginAttemptLimiterHarness.csproj -c Release` passed.
  - `node --check wwwroot\js\site.js` passed.
  - `dotnet list WebGallery.csproj package --vulnerable --include-transitive` reported no vulnerable packages.
  - Focused static assertions confirmed the collapsible markup, per-collection storage key, force-expanded state, and body visibility update.
  - `git diff --check` passed with only normal LF-to-CRLF warnings.
- Deployment: Published Release output to `publish\2026-08-10_191303`, backed up the previous IIS application and database under `backup\2026-08-10_191303_pre-collection-collapse`, placed the app offline briefly, and copied the publish output to `C:\Web\imagegallery` while excluding `appsettings.Development.json`. Publish/deploy SHA-256 values matched: `WebGallery.dll` `C27DAD9D646BE19AFCACC09DF1A34294A4654185B82D60AABD1BF65EC211FFC0`; `site.js` `B24650D779B46D9F3881DE9112BA27A0188BE96FF2B205528340769553C2807A`; Retro CSS `467EDB9EEF775437693722CFD1926C28F3A458AF7D85341F2C891DD2C1066E9C`; Modern CSS `273184ED8A495169B40FA82D1D8580FAE026CE6DF2D4FA86F07CFAC751683E22`.
- Live verification: Anonymous live `/Gallery/Collections` returned the expected HTTP 302 application-login redirect and followed to HTTP 200; live `js/site.js` returned HTTP 200; `app_offline.htm` was absent; production SQLite `PRAGMA integrity_check` returned `ok`.
- User-visible result: Collections are compact by default and can be expanded or collapsed individually without losing each collection's remembered state. Newly created share links still open their collection automatically for immediate copying.
- Remaining uncertainty: No authenticated interactive browser or visual UI validation was performed because this project continues to avoid the unstable In-app Browser surface. Signed-in clicking, keyboard focus, responsive layout, and both themes remain manual visual checks.
