# Collection disclosure header UX

- Date/time: 2026-08-10 19:32:49 Asia/Bangkok (UTC+07:00)
- User request: Improve the Collections management UI/UX so expand/collapse is controlled by the whole collection heading area, with a clear visual indication of clickability and current collapsed/expanded state instead of a detached button.
- Concepts/rules established: The collection title/count region is a full-width semantic disclosure button. It exposes `aria-expanded` and `aria-controls`, supports native keyboard activation, shows a CSS chevron that rotates with state, displays explicit Collapsed/Expanded text, and has clear hover/focus treatment. Add folders and Delete remain independent header actions. Expanded content now starts with a Folders section heading and count for clearer hierarchy. Per-collection persistence and force-opening after share creation remain unchanged.
- Files changed: `Views/Collections/Index.cshtml`, `wwwroot/js/site.js`, `wwwroot/css/site.css`, `wwwroot/css/site-modern.css`, `AGENTS.md`, `.agents/PROJECT_NOTES.md`.
- Validation performed:
  - `dotnet build WebGallery.csproj -c Release` passed with 0 warnings and 0 errors, including Razor compilation.
  - `dotnet run --project tests\FileSystemVisibilityHarness\FileSystemVisibilityHarness.csproj -c Release` passed.
  - `dotnet run --project tests\LoginAttemptLimiterHarness\LoginAttemptLimiterHarness.csproj -c Release` passed.
  - `node --check wwwroot\js\site.js` passed.
  - `dotnet list WebGallery.csproj package --vulnerable --include-transitive` reported no vulnerable packages.
  - Focused static assertions confirmed removal of the detached toggle, semantic heading disclosure markup, dynamic visible/accessible state, and hover/focus/chevron rules in both Retro and Modern stylesheets.
  - `git diff --check` passed with only normal LF-to-CRLF warnings.
- Deployment: Published Release output to `publish\2026-08-10_193211`, backed up the previous IIS application and database under `backup\2026-08-10_193211_pre-collection-header-ux`, placed the app offline briefly, and copied the publish output to `C:\Web\imagegallery` while excluding `appsettings.Development.json`. Publish/deploy SHA-256 values matched: `WebGallery.dll` `E0DFB0E99E0DBADFA377B0808D19FE40EA48A458E9C921AEC3A324665292F77B`; `site.js` `008AFC8E86E979958C92C2B63E73AE3F92061423C6A2CE9BE798BCFF24AB52BC`; Retro CSS `659A96EBBA3B0D3A3D1126704AA7CF68527D87B9F2CA04E24A3904E5F15EB799`; Modern CSS `2B69DFD7ED94F364F55859C0722734A84EC109A9B3949B4E910D962B6ECF375A`.
- Live verification: Anonymous live `/Gallery/Collections` returned the expected HTTP 302 application-login redirect and followed to HTTP 200. Live `js/site.js`, Retro CSS, and Modern CSS each returned HTTP 200; `app_offline.htm` was absent; production SQLite `PRAGMA integrity_check` returned `ok`.
- User-visible result: Collection headers now behave like obvious expandable rows: clicking the broad title area toggles the card, the chevron and state label explain whether it is open, and keyboard/focus behavior is explicit. Management actions remain safely separate, while expanded folder content has clearer visual hierarchy.
- Remaining uncertainty: No authenticated interactive browser or visual UI validation was performed because this project continues to avoid the unstable In-app Browser surface. Final signed-in spacing, hover/focus appearance, and responsive behavior in both themes remain manual visual checks.
