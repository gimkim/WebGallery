# Point-focused image zoom and viewer download icon

- Date: 2026-08-07 11:58:13 +07:00 (Asia/Bangkok)

## Request and intent

- When clicking a fitted image to zoom to original size, keep the clicked location under the pointer instead of merely switching to an unpositioned full-size image.
- Add a download icon to the Download action in the full-image viewer.

## Durable concepts established

- Before entering `1:1`, capture the clicked point as an X/Y ratio within the fitted image and its viewport position. After the original-size layout completes, calculate viewer scrolling so the same image point remains at that viewport position.
- In original-size mode, center the image on an axis that does not overflow; align it to the scroll origin only on axes that do overflow.
- The full-viewer Download action includes a compact inline SVG icon and label without an external asset or package.

## Files changed

- `wwwroot/js/site.js`
- `wwwroot/css/site.css`
- `Views/Gallery/Index.cshtml`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Validation actually performed

- `node --check wwwroot/js/site.js`: passed.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- `dotnet list WebGallery.csproj package --vulnerable --include-transitive`: no vulnerable packages reported from the configured NuGet sources.
- Static inspection confirmed that the live zoom code records fitted-image X/Y ratios, waits for the original-size layout, and updates both `scrollLeft` and `scrollTop` relative to the original pointer position.
- Static markup/CSS inspection confirmed the viewer Download action contains the inline SVG icon and the icon uses `currentColor` styling.
- `dotnet publish WebGallery.csproj -c Release --no-build -o publish`: passed.
- Deployed the publish output to `C:\Web\imagegallery` while `app_offline.htm` was present. SHA-256 hashes matched for `WebGallery.dll`, `wwwroot/js/site.js`, and `wwwroot/css/site.css`; the offline file was removed.
- Live anonymous `https://gimgim.ddns.net:45570/Gallery/` returned 302 to the application login page; deployed JavaScript and CSS returned HTTP 200.
- Direct live-asset inspection found the point-ratio/scroll calculation and the download-icon CSS in the served files.
- The source directory is not a Git repository, so no Git status or `git diff --check` result is available.

## User-visible result

- Clicking a location on a fitted oversized image now zooms to `1:1` around that location. Clicking the original-size image again returns to fitted view.
- Portrait or panoramic originals remain centered on any non-scrollable axis.
- The viewer Download action now displays a download icon beside its label.

## Remaining manual validation

- Interactive browser validation was not run because the installed Codex build has twice exited during In-app Browser teardown. After signing in, manually click the center, corners, and edges of both landscape and portrait oversized images and verify the intended point remains under the pointer; also visually check the icon at desktop and mobile widths.
