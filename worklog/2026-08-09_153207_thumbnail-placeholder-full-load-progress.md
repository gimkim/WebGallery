# Thumbnail placeholder and full-image download progress

- Date: 2026-08-09 15:32:07 +07:00 (Asia/Bangkok)

## Request and intent

- Correct the preceding 2048px viewer-preview implementation.
- Do not create a separate medium preview.
- Reuse the existing Gallery thumbnail as a visibly low-resolution placeholder.
- Show how much of the original has loaded as percentage and loaded bytes / total bytes.

## Correction to prior record

- This worklog supersedes the active implementation described by `2026-08-09_144600_progressive-full-image-preview.md` without rewriting that historical record.
- The dedicated `ViewerPreview` action, `viewer-preview-2048-v1` cache rendition, preview service method, and its test harness source were removed.
- Existing orphaned cache files from the short-lived rendition are not referenced by the application and were not destructively cleared from the shared production cache.

## Behavior established

- The full viewer uses the same fingerprinted 480x360 WebP thumbnail URL already used by the Grid/List card.
- The original is requested with a same-origin `XMLHttpRequest` using Blob response mode.
- The progress overlay reads `Content-Length` as soon as response headers arrive, then displays values such as `37% · 12.4 MB / 33.5 MB` while transfer progress events arrive.
- Once the original Blob has fully downloaded and decoded, its temporary object URL replaces the thumbnail and the progress overlay disappears.
- Navigating to another image or closing the viewer aborts the active XHR, invalidates late callbacks, clears old image loaders, and revokes the old Blob URL.
- If the original fails, the thumbnail remains visible when available and the overlay reports that the full image could not be loaded.
- Gallery thumbnail dispatch remains suspended while the viewer is open and resumes after close.

## Files changed

- `Services/ThumbnailService.cs`
- `Controllers/GalleryController.cs`
- `Views/Gallery/Index.cshtml`
- `wwwroot/js/site.js`
- `wwwroot/css/site.css`
- `wwwroot/css/site-modern.css`
- Removed `tests/ViewerPreviewHarness/ViewerPreviewHarness.csproj`
- Removed `tests/ViewerPreviewHarness/Program.cs`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Validation actually performed

- Confirmed there are zero `ViewerPreview`, `viewerPreview`, or `viewer-preview` references in the active service, controller, view, and JavaScript source files.
- Confirmed the view sends the existing fingerprinted thumbnail URL in `data-viewer-thumbnail`.
- Confirmed the JavaScript contains XHR progress handling, early `Content-Length` reading, human-readable byte formatting, abort handling, generation checks, and Blob URL cleanup.
- Confirmed both Retro and Modern CSS contain the progress overlay selectors; Modern retains its rounded presentation.
- `node --check wwwroot/js/site.js`: passed.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.
- Real authenticated visual progress timing was not exercised because this project's Codex In-app Browser workaround remains active.
- The source workspace is not a Git repository, so Git status and `git diff --check` are unavailable.

## Backup and deployment

- Backed up the previous deployed DLL, symbols, static-web-assets manifest, JavaScript, Retro CSS, Modern CSS, and compressed variants under `backup/2026-08-09_153104_pre-thumbnail-viewer-progress`.
- Used `C:\Web\imagegallery\app_offline.htm` only during the copy window.
- Deployed the published binary/view, JavaScript, both theme stylesheets, static-web-assets manifest, and Brotli/Gzip variants to `C:\Web\imagegallery`.
- Removed `app_offline.htm` after deployment.
- Production SQLite data, users, share records, settings, gallery roots/files, IIS authentication, and existing cache files were not modified.

## Post-deployment validation

- Every copied publish/deployment SHA-256 pair matched.
- Deployed `WebGallery.dll` SHA-256: `DAF9128E08113534FF791C764C6312E9D5E120D473A307B4F4006D7C97CE720E`.
- Deployed `site.js` SHA-256: `92CEFD4426F397D0073FA6F4D04D1F32944E1D306688D3CE372DFF6FACF2BDE9`.
- Public Login returned HTTP 200.
- Anonymous `/Gallery/` returned HTTP 302 to the application Login route.
- Live JavaScript returned HTTP 200 and contains the thumbnail placeholder, XHR, and `Content-Length` logic while containing no old `viewerPreview` reference.
- Live Retro and Modern CSS each returned HTTP 200 and contain the progress-detail styling.
- Confirmed `C:\Web\imagegallery\app_offline.htm` is absent.

## User-visible result

- The existing small thumbnail appears immediately in the full viewer, even if visibly pixelated.
- A bottom overlay shows the original transfer percentage and loaded/total size.
- The viewer swaps to the original only after the complete image has downloaded and decoded.

## Remaining manual validation

- Open a large uncached image through a normal authenticated browser and visually confirm that progress advances rather than jumping directly to 100% on the current network.
- Change images mid-transfer and close/reopen the viewer to confirm cancellation and progress reset visually.
- Verify the progress overlay placement on a narrow mobile viewport in both themes.
