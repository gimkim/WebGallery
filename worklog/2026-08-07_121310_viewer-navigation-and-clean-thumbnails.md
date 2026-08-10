# Full-viewer image navigation and clean thumbnails

- Date: 2026-08-07 12:13:10 +07:00 (Asia/Bangkok)

## Request and intent

- Add previous/next image navigation to the full-image viewer using both keyboard Left/Right arrows and slim clickable controls at the viewer edges.
- Remove the scanline pattern that appeared over image thumbnails and folder-cover images.

## Durable concepts established

- Viewer navigation uses only image buttons rendered for the current folder, preserving the page's current sorted order and skipping folders and non-image files.
- Previous/next navigation wraps cyclically. Both edge controls are hidden unless at least two images are present.
- Changing viewer images resets original-size zoom and scroll, loads the new original, then applies the normal fit-to-viewport behavior.
- The FileListing-inspired theme keeps its navy pixel-grid background but does not place a foreground scanline overlay across gallery media.

## Files changed

- `wwwroot/js/site.js`
- `wwwroot/css/site.css`
- `Views/Gallery/Index.cshtml`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Backup

- Before deployment, backed up the deployed `WebGallery.dll`, `wwwroot/js/site.js`, and `wwwroot/css/site.css` to `backup/2026-08-07_121310_pre-viewer-navigation`.

## Validation actually performed

- `node --check wwwroot/js/site.js`: passed.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- `dotnet list WebGallery.csproj package --vulnerable --include-transitive`: no vulnerable packages reported from the configured NuGet sources.
- Runtime-source language scan still found no Thai application copy.
- Static checks confirmed Left/Right keyboard branches, cyclic modulo navigation, slim 32-pixel desktop controls, responsive 28-pixel mobile controls, and removal of the `body::before` foreground scanline overlay.
- Clean publish completed to `publish-current/`.
- Deployed while `C:\Web\imagegallery\app_offline.htm` was present. SHA-256 hashes matched for `WebGallery.dll`, `wwwroot/js/site.js`, and `wwwroot/css/site.css`; the offline file was then removed.
- Live `/Gallery/` continued to return the expected 302 login redirect; live JavaScript and CSS returned HTTP 200.
- Direct inspection of live assets confirmed keyboard/cyclic navigation, slim viewer controls, and absence of the foreground scanline selector.
- The source directory is not a Git repository, so no Git status or `git diff --check` result is available.

## User-visible result

- The full viewer now has slim previous/next arrow controls at its left and right edges and responds to keyboard Left/Right arrows.
- Navigation wraps between the first and last image and resets each newly displayed image to fitted view.
- Thumbnails and folder covers no longer have scanlines drawn over them.

## Remaining manual validation

- Interactive browser QA was not run because the installed Codex build has twice exited during In-app Browser teardown. After signing in, manually verify mouse and keyboard navigation with one image, two images, multiple sorted images, zoomed images, portrait/landscape originals, and mobile-width controls; visually confirm thumbnails and folder covers are free of scanline artifacts.
