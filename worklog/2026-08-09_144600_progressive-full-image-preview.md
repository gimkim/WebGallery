# Progressive full-image preview

- Date: 2026-08-09 14:46:00 +07:00 (Asia/Bangkok)

## Request and intent

- Make the full-image viewer responsive for very large originals by showing a medium preview of about 2048 pixels first, then replacing it with the fully loaded original.

## Concepts and behavior established

- Added a dedicated cached viewer-preview rendition: auto-oriented WebP, aspect ratio preserved, maximum 2048x2048, quality 85.
- Preview generation uses the existing application-wide bounded thumbnail queue at Visible priority and therefore remains subject to the administrator's `ThumbnailConcurrency` limit.
- Preview disk-cache identity uses its own `viewer-preview-2048-v1` algorithm version, source owner/path/length/UTC modification ticks, dimensions, and quality.
- Preview URLs include the preview version and existing source fingerprint, allowing one-year private immutable browser caching while source changes generate a new URL.
- The browser begins preview and original requests concurrently, gives the preview higher fetch priority, displays it once decoded, and replaces it only after the original has loaded and decoded.
- Viewer navigation and close invalidate stale asynchronous results and cancel the old loader elements so a late result cannot overwrite the current image.
- Ordinary Grid/List thumbnail requests are suspended while the full viewer is open and visible thumbnail scheduling resumes on close, keeping the chosen image responsive.
- Existing mouse Fit/1:1, clicked-point zoom, touch pinch/pan/double-tap, download, and previous/next behavior remains in place and is recalculated for the final original after the swap.

## Files changed

- `Services/ThumbnailService.cs`
- `Controllers/GalleryController.cs`
- `Views/Gallery/Index.cshtml`
- `wwwroot/js/site.js`
- `tests/ViewerPreviewHarness/ViewerPreviewHarness.csproj`
- `tests/ViewerPreviewHarness/Program.cs`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Validation actually performed

- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- `node --check wwwroot/js/site.js`: passed.
- The new executable preview harness generated a 3200x2400 JPEG, sent it through the real `ThumbnailService` and running bounded `ThumbnailWorkQueue`, and verified:
  - output dimensions are exactly 2048x1536;
  - the aspect ratio is preserved;
  - the WebP cache file exists;
  - a second request returns the same cache path.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.
- A scripted authenticated/shared live-preview request was attempted but rejected by the shell safety policy before execution. No share token was printed and this end-to-end image request is not claimed as tested.
- The source workspace is not a Git repository, so Git status and `git diff --check` are unavailable.

## Backup and deployment

- Backed up the previously deployed binary, symbols, static-web-assets manifest, and JavaScript assets under `backup/2026-08-09_144300_pre-viewer-preview`.
- Used `C:\Web\imagegallery\app_offline.htm` only for the copy window.
- Deployed `WebGallery.dll`, `WebGallery.pdb`, `WebGallery.staticwebassets.endpoints.json`, and `site.js` with its Brotli/Gzip variants to `C:\Web\imagegallery`.
- Removed `app_offline.htm` after deployment.
- Production SQLite data, users, share records, user roots, gallery files, existing cache files, settings, and IIS authentication were not modified.

## Post-deployment validation

- Published/deployed SHA-256 hashes matched for all copied release files.
- Deployed `WebGallery.dll` SHA-256: `C0B2A99C25D19339F868FD056F50EC0AA899A60F22ACE704D7209998AC039FF2`.
- Deployed `site.js` SHA-256: `017A7C5BD7EA7B9AA892E7F44A905C5F05BA72683C6EE47EFA376B42AE656E5F`.
- Public Login returned HTTP 200.
- Anonymous `/Gallery/` returned HTTP 302 to the application Login route.
- An invalid anonymous `ViewerPreview` request returned HTTP 404, confirming the deployed action is routed and does not expose a missing/private file.
- Live `site.js` returned HTTP 200 and contains the progressive preview loader, stale-load cancellation, and Gallery-thumbnail suspension logic.
- Confirmed `C:\Web\imagegallery\app_offline.htm` is absent after deployment.

## User-visible result

- Opening a large image can show the cached 2048px preview quickly while the original continues loading in the background.
- The preview is replaced in the same viewer after the original is fully decoded; changing images or closing the viewer prevents stale loads from flashing later.
- Reopening the same unchanged image can reuse both server and browser preview caches.

## Remaining manual validation

- Log in through a normal browser, open a genuinely large uncached image over the public connection, and visually confirm the preview-to-original swap has no flash or unexpected zoom reset.
- Repeat with previous/next navigation during an in-progress original load and with pinch/double-tap on mobile.
