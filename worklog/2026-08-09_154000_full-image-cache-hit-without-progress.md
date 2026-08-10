# Full-image cache hit without progress flash

- Date: 2026-08-09 15:40:00 +07:00 (Asia/Bangkok)

## Request and intent

- When the full original has already finished loading and remains in browser cache, display it directly instead of briefly showing download progress again.

## Behavior established

- Original-view URLs now include the source length/UTC-modification fingerprint already available to the Gallery view.
- Fingerprinted `ViewFile` responses use `Cache-Control: private, max-age=31536000, immutable`; legacy requests without a fingerprint use `private, no-cache`.
- On opening an image, the browser first performs a same-origin `only-if-cached` fetch while leaving the progress overlay hidden.
- A cache hit is read as a Blob, decoded, and displayed as the full original without showing progress.
- A generated 504 cache miss, unsupported cache-only mode, or other probe failure then reveals the progress overlay and starts the existing XHR network transfer.
- The cache-only mode cannot contact the network. The fingerprint prevents an unchanged URL from serving a replaced source file.
- Image navigation and Viewer close abort both the cache probe and XHR and continue to revoke obsolete Blob URLs.

## Files changed

- `Controllers/GalleryController.cs`
- `Views/Gallery/Index.cshtml`
- `wwwroot/js/site.js`
- `tests/BrowserCacheProbeHarness/BrowserCacheProbeHarness.csproj`
- `tests/BrowserCacheProbeHarness/Program.cs`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Validation actually performed

- `node --check wwwroot/js/site.js`: passed.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- Built the dependency-free local browser cache-probe harness with 0 warnings and 0 errors. The harness is designed to verify that a second `only-if-cached` request is served without a second server request.
- Attempted to run the harness with external headless Chrome, but the combined background-server command was rejected by shell safety policy before either server or Chrome launched. Browser execution is therefore not claimed.
- Static review confirmed progress starts only after the cache probe returns no Blob, cached responses never reveal the progress overlay, cache-probe cancellation is wired into Viewer cleanup, and original URLs/responses carry matching fingerprint/cache semantics.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.
- The source workspace is not a Git repository, so Git status and `git diff --check` are unavailable.

## Backup and deployment

- Backed up the previous deployed DLL, symbols, static-web-assets manifest, JavaScript, and compressed JavaScript under `backup/2026-08-09_153855_pre-full-image-cache-probe`.
- Used `C:\Web\imagegallery\app_offline.htm` only during the copy window.
- Deployed the published binary/view, static-web-assets manifest, JavaScript, and Brotli/Gzip variants to `C:\Web\imagegallery`.
- Removed `app_offline.htm` after deployment.
- Production SQLite data, users, shares, settings, user roots, gallery files, existing image cache, CSS, and IIS authentication were not modified.

## Post-deployment validation

- Every copied publish/deployment SHA-256 pair matched.
- Deployed `WebGallery.dll` SHA-256: `A0286AE11136032E268C66B017ABBCB7E84F24D63B5F75931324AC5FE93B2D54`.
- Deployed `site.js` SHA-256: `0BBB0E635101E968D58A40B078FFA5639C3665CDA93304C67C10C902393DC9A6`.
- Public Login returned HTTP 200.
- Anonymous `/Gallery/` returned HTTP 302 to the application Login route.
- Live JavaScript returned HTTP 200 and contains the cache-only probe, cache-hit path, and probe-abort cleanup.
- Confirmed `C:\Web\imagegallery\app_offline.htm` is absent.

## User-visible result

- Reopening the same unchanged full image after it has been cached no longer flashes the progress overlay.
- An uncached image still displays the thumbnail followed by real percentage and loaded/total size.

## Remaining manual validation

- In a normal authenticated browser, open one large image to completion, close it, and reopen it to confirm the second open displays the original without progress.
- Confirm a first-time image still shows progress and replacing a source file produces a new fingerprinted URL.
- The first open after this deployment may download once because previously rendered original URLs did not include the new source fingerprint; subsequent unchanged opens use the stable fingerprinted cache entry.
