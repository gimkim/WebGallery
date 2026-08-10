# Remove included folders from the collection picker

- Date/time: 2026-08-10 20:10:08 Asia/Bangkok (UTC+07:00)
- User request: On the Add folders page, let an already included folder be unchecked and removed there instead of requiring a return to Collection management.
- Concepts/rules established: An exact/direct collection membership renders as a checked checkbox in the folder picker. Clearing it and applying changes removes that membership while preserving the picker path and sort. New additions and removals are submitted together. A descendant included implicitly by a selected parent is labelled `Included via parent` and remains non-selectable because the current parent-membership model cannot exclude only one descendant.
- Files changed: `Controllers/CollectionsController.cs`, `ViewModels/GalleryViewModels.cs`, `Views/Collections/SelectFolders.cshtml`, `wwwroot/js/site.js`, `wwwroot/css/site.css`, `wwwroot/css/site-modern.css`, `AGENTS.md`, `.agents/PROJECT_NOTES.md`.
- Validation performed:
  - `dotnet build WebGallery.csproj -c Release` passed with 0 warnings and 0 errors, including Razor compilation.
  - `node --check wwwroot\js\site.js` passed.
  - Focused source assertions confirmed direct-membership IDs, visible/kept membership POST fields, inherited-parent labels, and initial-checkbox change tracking.
  - `git diff --check` passed with only the normal LF-to-CRLF working-copy warnings.
- Deployment: Published Release output to `publish\2026-08-10_201008`. Copied the preceding IIS application into `backup\2026-08-10_201008_pre-picker-removal` and verified that the backup was non-empty. Deployed to `C:\Web\imagegallery`, excluded the publish copy of `appsettings.Development.json`, and removed the stale development-settings copy left in the IIS directory; it remains recoverable from the backup. The deployed DLL and JavaScript matched the publish output by SHA-256 (`4FE0DD444F4E5E5EED7D79ECBFF366779B142EB918FB6DBA40437C8852D5F1AA` and `F6BE4EEB22AAD3BA89757EE21FF8586090FC5E89C7EDB108F0927434D6F9FA71`). The deployed Retro CSS also matched its publish output.
- Live verification: An anonymous request to `https://gimgim.ddns.net:45570/Gallery/Collections/SelectFolders/1` returned HTTP 302 to the expected Gallery Login route. `app_offline.htm` was absent after deployment.
- User-visible result: Directly included folders now appear checked in Add folders. The user can clear one or more, optionally select additions on the same page, and use `Apply folder changes` once. The change counter enables the action only when checkbox state differs from the initial state.
- Remaining uncertainty: Authenticated interactive clicking and final visual layout were not tested because this project continues to avoid the unstable Codex In-app Browser. The exact checkbox/remove/add flow should be manually confirmed while signed in.
