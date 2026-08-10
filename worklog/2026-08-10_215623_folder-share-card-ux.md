# Folder share grouped card UX

- Date/time: 2026-08-10 21:56:23 Asia/Bangkok (UTC+07:00)
- User request: Improve the recently added Folder share links UX/UI, then additionally show a folder thumbnail when available and arrange the result as semi-grid cards.
- Concepts/rules established: Group active standalone links case-insensitively by their normalized folder path so folder name/path/cover/Open folder appear once while each URL retains independent Copy, settings, metrics, Activity, and Revoke controls. Render folder groups as responsive semi-grid cards with the Gallery folder silhouette and up to four direct-child cover thumbnails; use the existing fingerprinted visibility-driven thumbnail loader/cache rather than eager image requests. The entire Folder share links section is a semantic disclosure, starts expanded, and persists its state under `gim-folder-shares-expanded`.
- Files changed: `Controllers/CollectionsController.cs`, `ViewModels/GalleryViewModels.cs`, `Views/Collections/Index.cshtml`, `wwwroot/js/site.js`, `wwwroot/css/site.css`, `wwwroot/css/site-modern.css`, `AGENTS.md`, `.agents/PROJECT_NOTES.md`.
- Validation performed:
  - `dotnet build WebGallery.csproj -c Release` passed with 0 warnings and 0 errors, including Razor compilation.
  - `node --check wwwroot\js\site.js` passed.
  - `dotnet run --project tests\ShareAuditHarness\ShareAuditHarness.csproj -c Release` passed.
  - Focused assertions confirmed grouped models, cover collage markup, deferred thumbnail URLs, group/link hierarchy, disclosure controls, and saved-state key.
  - `git diff --check` passed with only normal LF-to-CRLF warnings.
- Deployment: Published Release output to `publish\2026-08-10_215623`, backed up the verified non-empty prior IIS application to `backup\2026-08-10_215623_pre-folder-share-ux`, and deployed to `C:\Web\imagegallery` while excluding development settings. Publish/deploy SHA-256 matched for `WebGallery.dll` (`EA25C157D1B8731BE79E814AAF1C745469C377E997442DC0DDA0DBF0E55ECDFB`), `site.js` (`0E75C5350757F68DDBE758B9B062E7BB5B900E9FE4B890BB948DF68B1240D0FB`), Retro CSS (`0E0E8C12A9DB8DA28BE4A6BC142AAC0260A8C3B8BCD32EA456A33C8C2E7AC450`), and Modern CSS (`66B5AC7582DAEEEFEE16C0D74AFCB5366EB197BC1F9D65F1D3B0FC1BDDD3DD23`).
- Live verification: An anonymous request to `https://gimgim.ddns.net:45570/Gallery/Collections` returned HTTP 302 to the expected Login route. `app_offline.htm` and `appsettings.Development.json` were absent afterward.
- User-visible result: Folder share links are now organized as compact folder-centric cards instead of repetitive link-centric rows. Each card provides visual folder context through its cover collage, scales into a semi-grid on wide screens and a stacked card on mobile, and can be hidden with one remembered disclosure control.
- Remaining uncertainty: Authenticated visual/interaction testing was not performed because this project continues to avoid the unstable Codex In-app Browser. Actual cover loading, multi-link grouping, two-column breakpoints, long URL/path wrapping, Copy/Activity/Revoke actions, disclosure persistence, and both themes should be manually checked while signed in.
