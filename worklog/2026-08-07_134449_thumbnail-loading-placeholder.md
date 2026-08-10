# Thumbnail loading placeholder

- Date: 2026-08-07 13:44:49 +07:00 (Asia/Bangkok)

## Request and intent

- Correct the Grid loading state shown in the user's screenshot, where deferred image elements appeared as broken images with a small browser icon and filename alt text while waiting for thumbnail generation.
- Keep the bounded visible-first thumbnail pipeline and established pixel theme.

## Cause

- Deferred `<img>` elements intentionally had no `src` until their visibility-driven fetch completed.
- They remained visually exposed, so Chromium rendered its native missing/broken-image presentation and the image alt text during the pending interval.

## Durable concepts established

- A pending thumbnail is a normal application state, not an error, and must use an application-owned placeholder instead of browser broken-image chrome.
- Keep deferred image elements hidden until their actual `load` event succeeds.
- Use a dark navy stepped pixel shimmer while pending and a separate centered pink mark only for genuine terminal fetch/decode failures.
- Folder-cover collages need an independent placeholder for every tile.

## Files changed

- `Views/Gallery/Index.cshtml`
- `wwwroot/js/site.js`
- `wwwroot/css/site.css`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Backup

- Before deployment, backed up the deployed DLL, CSS, and JavaScript to `backup/2026-08-07_134449_pre-thumbnail-placeholder`.

## Validation actually performed

- Inspected the user-provided 2048x999 screenshot and confirmed the native broken-image icon/alt text appeared only in pending image media while the visible-first queue generated thumbnails.
- Added server-rendered `thumbnail-loading` hosts so the placeholder exists before JavaScript executes.
- Added per-tile `thumbnail-slot` wrappers to folder covers and retained the three-image first-tile row-span behavior on the wrapper.
- JavaScript now distinguishes pending, assigned-for-decode, loaded, and failed states; it reveals an image only on `load`, prevents duplicate fetch assignment before decode completes, revokes blob URLs on load/error, and preserves 503 retry/off-screen cancellation behavior.
- `node --check wwwroot/js/site.js`: passed.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.
- Deployed the DLL/PDB, CSS, and JavaScript after `app_offline.htm` caused IIS to release the application lock; removed the offline file afterward.
- SHA-256 matched between publish output and IIS deployment for `WebGallery.dll`, `wwwroot/css/site.css`, and `wwwroot/js/site.js`.
- Live `/Gallery/` returned HTTP 302 to the application login page.
- Live CSS and JavaScript returned the new loading-placeholder, pending-image hiding, loaded-state, and failed-state logic.
- The source directory is not a Git repository, so no Git status or `git diff --check` result is available.

## User-visible result

- Grid and List image cards now show a clean animated pixel-theme placeholder while thumbnail generation is pending instead of looking like missing files.
- A decoded thumbnail fades in only after it is ready to display.
- Folder cover tiles use the same pending treatment independently, and a true failure is visually distinct from normal waiting.

## Remaining manual validation

- The supplied screenshot validates the pre-fix defect. Post-fix interactive browser QA was not run because no current Gallery credential is available to the agent and this project's Codex workaround prohibits In-app Browser use after two prior app exits during browser teardown.
- Refresh the signed-in Gallery on a folder with uncached images and confirm the shimmer transitions to thumbnails without exposing filename alt text or broken-image icons in Grid, List, and folder covers.
