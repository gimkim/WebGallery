# Independent Folders and Share links disclosures

- Date/time: 2026-08-10 19:38:53 Asia/Bangkok (UTC+07:00)
- User request: Inside every collection, make the Folders and Share links areas independently collapsible.
- Concepts/rules established: The existing whole-collection disclosure remains. Inside an expanded collection, Folders and Share links are separate semantic disclosure sections with full-width buttons, chevrons, item counts, visible Expanded/Collapsed state, native keyboard behavior, and independent browser persistence keyed by collection ID and section name. Both sections start expanded on first use. Creating a share link force-opens both its parent collection and the Share links subsection so the new highlighted URL remains immediately visible.
- Files changed: `Views/Collections/Index.cshtml`, `wwwroot/js/site.js`, `wwwroot/css/site.css`, `wwwroot/css/site-modern.css`, `AGENTS.md`, `.agents/PROJECT_NOTES.md`.
- Validation performed:
  - `dotnet build WebGallery.csproj -c Release` passed with 0 warnings and 0 errors, including Razor compilation.
  - `dotnet run --project tests\FileSystemVisibilityHarness\FileSystemVisibilityHarness.csproj -c Release` passed.
  - `dotnet run --project tests\LoginAttemptLimiterHarness\LoginAttemptLimiterHarness.csproj -c Release` passed.
  - `node --check wwwroot\js\site.js` passed.
  - `dotnet list WebGallery.csproj package --vulnerable --include-transitive` reported no vulnerable packages.
  - Focused static assertions confirmed both subsection keys/markup, per-section storage state, and subsection hover/focus/collapsed styles in Retro and Modern.
  - `git diff --check` passed with only normal LF-to-CRLF warnings.
- Deployment: Published Release output to `publish\2026-08-10_193817`, backed up the previous IIS application and database under `backup\2026-08-10_193817_pre-collection-subsections`, placed the app offline briefly, and copied the publish output to `C:\Web\imagegallery` while excluding `appsettings.Development.json`. Publish/deploy SHA-256 values matched: `WebGallery.dll` `0FCF7DEBC2FD2FFEBAF5F315D038006231F58BE3B17118F08FCD389A6F02C4C1`; `site.js` `F5510FCCF13A18D397B8998BD7B6B045ECCC3240DE8C92CF0A8671B981761438`; Retro CSS `A87A3F66B925E6B708FC96DFDBD71626A730783547DE4DC2EB71F436B15F2E83`; Modern CSS `0ABE9A5078DC2C974197B5C608F54D4A066A4C9305732E5B77641B778FCFC850`.
- Live verification: Anonymous live `/Gallery/Collections` returned the expected HTTP 302 application-login redirect and followed to HTTP 200. Live `js/site.js`, Retro CSS, and Modern CSS each returned HTTP 200; `app_offline.htm` was absent; production SQLite `PRAGMA integrity_check` returned `ok`.
- User-visible result: A user can keep one collection open while independently hiding its folder grid or its share-link management area, reducing page length without losing access to the other section. Each subsection remembers its own state.
- Remaining uncertainty: No authenticated interactive browser or visual UI validation was performed because this project continues to avoid the unstable In-app Browser surface. Signed-in toggle behavior, focus styling, layout at narrow widths, and both themes remain manual visual checks.
