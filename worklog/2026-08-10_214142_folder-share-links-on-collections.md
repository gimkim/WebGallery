# Aggregate folder share links on Collections

- Date/time: 2026-08-10 21:41:42 Asia/Bangkok (UTC+07:00)
- User request: Show single-folder share links on the Collections page after the collection list.
- Concepts/rules established: Collections remains the central collection editor, and now ends with a separate Folder share links section containing every active non-collection share owned by the signed-in user. This supplements rather than removes Gallery's per-folder Share panel. Each row identifies the folder/path and exposes Open folder, URL/Copy, creation time, captured Grid/List/sort/per-row settings, activity totals/link, and owner-scoped Revoke. Revoked links are excluded.
- Files changed: `Controllers/CollectionsController.cs`, `ViewModels/GalleryViewModels.cs`, `Views/Collections/Index.cshtml`, `wwwroot/css/site.css`, `wwwroot/css/site-modern.css`, `AGENTS.md`, `.agents/PROJECT_NOTES.md`.
- Validation performed:
  - `dotnet build WebGallery.csproj -c Release` passed with 0 warnings and 0 errors, including Razor compilation.
  - `dotnet run --project tests\ShareAuditHarness\ShareAuditHarness.csproj -c Release` passed.
  - `node --check wwwroot\js\site.js` passed.
  - Focused source assertions confirmed the owner/non-collection/non-revoked query, combined audit-summary loading, the trailing rendered section, and owner-scoped Revoke without the former collection-only restriction.
  - `git diff --check` passed with only normal LF-to-CRLF warnings.
- Deployment: Published Release output to `publish\2026-08-10_214142`, backed up the verified non-empty preceding IIS application to `backup\2026-08-10_214142_pre-folder-share-aggregation`, and deployed to `C:\Web\imagegallery` while excluding development settings. Publish/deploy SHA-256 matched for `WebGallery.dll` (`8CCB8A424CAE6107F4EACC4D496F0470E9A7BBEBB9ADFA83D28FCAD97F4656DF`), Retro CSS (`9E6EAE2E8766D62C376EA9959E286A348F236ED66B1256EE20E3A2E56F6E0F68`), and Modern CSS (`418E63CAF408DAD2DC75EE22B383D81A37AAE411BBE955FCAD5E702BEF83B6C1`).
- Live verification: An anonymous request to `https://gimgim.ddns.net:45570/Gallery/Collections` returned HTTP 302 to the expected Login route. `app_offline.htm` and `appsettings.Development.json` were absent afterward.
- User-visible result: The Collections page now provides one consolidated place to review and manage both Collection share links and all individual Folder share links, with the Folder links placed after all Collections as requested.
- Remaining uncertainty: Authenticated visual and interaction testing was not performed because this project continues to avoid the unstable Codex In-app Browser. Actual link count/order, Copy/Open/Activity/Revoke actions, long folder-path wrapping, and both themes should be manually checked while signed in.
