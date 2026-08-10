# Folder share cover and title navigation

- Date/time: 2026-08-10 22:00:24 Asia/Bangkok (UTC+07:00)
- User request: Let users click the folder image or folder name in a Folder share card to open that folder.
- Concepts/rules established: When the grouped folder still exists and is accessible, both its entire cover/media area and its displayed name are keyboard-accessible private Gallery links to the same folder as Open folder. Cover hover/focus gives theme-appropriate feedback. Missing/unavailable groups remain unlinked so the management page does not knowingly send users to a 404.
- Files changed: `Views/Collections/Index.cshtml`, `wwwroot/css/site.css`, `wwwroot/css/site-modern.css`, `AGENTS.md`, `.agents/PROJECT_NOTES.md`.
- Validation performed:
  - `dotnet build WebGallery.csproj -c Release` passed with 0 warnings and 0 errors, including Razor compilation.
  - `node --check wwwroot\js\site.js` passed.
  - Focused assertions confirmed the cover overlay link, accessible label, folder-name link, and shared folder route.
  - `git diff --check` passed with only normal LF-to-CRLF warnings.
- Deployment: Published Release output to `publish\2026-08-10_220024`, backed up the verified non-empty prior IIS application to `backup\2026-08-10_220024_pre-folder-card-links`, and deployed to `C:\Web\imagegallery` while excluding development settings. Publish/deploy SHA-256 matched for `WebGallery.dll` (`94DD1AAB5EAA9AD114FA619C51EF14B5F4D3122372449E9B20B6AB31390D6ABD`), Retro CSS (`EC7F4E1E657F0C35C4D649AF69AD3B6B70BDA5130A965B4E941A81003731AB32`), and Modern CSS (`20BCFD927CFE006C7C93352C5BE977C4F672B050513E3B3440C8BF66704DCDA9`).
- Live verification: An anonymous request to `https://gimgim.ddns.net:45570/Gallery/Collections` returned HTTP 302 to the expected Login route. `app_offline.htm` was absent afterward.
- User-visible result: The Folder share card now supports the two expected direct navigation targets—the visual folder cover and folder title—in addition to the explicit Open folder button.
- Remaining uncertainty: Authenticated browser interaction and visual focus/hover behavior were not tested because this project continues to avoid the unstable Codex In-app Browser. Both links, unavailable-card behavior, and both themes should be manually checked while signed in.
