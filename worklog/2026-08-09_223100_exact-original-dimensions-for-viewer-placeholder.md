# Exact original dimensions for Viewer placeholder

- Date: 2026-08-09 22:31:00 +07:00 (Asia/Bangkok)

## Request and intent

- Fix landscape originals becoming slightly smaller when replacing the temporary thumbnail.
- Preserve the already-correct portrait transition.

## Cause

- The previous implementation inferred whether the original was large from whether its generated thumbnail reached 480x360.
- A landscape original such as 1920px wide reaches the 480px thumbnail bound even when the Viewer stage is wider than 1920px.
- The heuristic therefore enlarged the placeholder to the entire wider stage, while the full-image no-upscale rule correctly rendered the 1920px original at native width, producing a visible shrink.

## Behavior established

- On a full-image cache miss, `ViewFile` uses ImageSharp `IdentifyAsync` to read image dimensions before streaming the body.
- EXIF orientations 5–8 swap width/height so the reported dimensions match the auto-oriented thumbnail and browser display.
- `ViewFile` returns `X-Image-Width` and `X-Image-Height` response headers.
- The XHR `HEADERS_RECEIVED` handler reads those dimensions before starting the temporary thumbnail.
- Placeholder dimensions now use the same exact formula as the eventual original: `min(1, availableWidth/originalWidth, availableHeight/originalHeight)`.
- The existing configured-thumbnail-bound calculation remains only as a metadata-failure fallback.

## Files changed

- `Controllers/GalleryController.cs`
- `wwwroot/js/site.js`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Validation actually performed

- `node --check wwwroot/js/site.js`: passed.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors, including the ImageSharp EXIF API usage.
- Static assertions confirmed original-dimension headers are read before thumbnail creation and exact original-width viewport math is present.
- Source review confirmed dimensions are swapped only for EXIF orientations 5–8.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.
- Real authenticated visual comparison was not performed because this project's Codex In-app Browser workaround remains active.
- The source workspace is not a Git repository, so Git status and `git diff --check` are unavailable.

## Backup and deployment

- Backed up the prior deployed DLL, symbols, static-web-assets manifest, JavaScript, and compressed JavaScript under `backup/2026-08-09_223048_pre-original-dimension-headers`.
- Used `C:\Web\imagegallery\app_offline.htm` only during the copy window.
- Deployed the published binary, controller/view assembly, static-web-assets manifest, JavaScript, and Brotli/Gzip variants to `C:\Web\imagegallery`.
- Removed `app_offline.htm` after deployment.
- Production SQLite data, users, shares, settings, gallery roots/files, existing caches, CSS, and IIS configuration were not modified.

## Post-deployment validation

- Every copied publish/deployment SHA-256 pair matched.
- Deployed `WebGallery.dll` SHA-256: `EF61728D1C8494A912D4FA5CFF8A42FE28F43A8DA74213B56C4F69222309236B`.
- Deployed `site.js` SHA-256: `2F8979281F64CE926E8E73E4298E89A8E292C41D09407FA9D21FFAFA0474B1D2`.
- Public Login returned HTTP 200.
- Live JavaScript returned HTTP 200 and contains original-dimension header reading and exact viewport-fit calculation.
- Confirmed `C:\Web\imagegallery\app_offline.htm` is absent.

## User-visible result

- Landscape placeholders no longer over-expand merely because their thumbnail reaches 480px.
- The placeholder and full original use the same exact no-upscale frame, so replacement changes sharpness without shrinking.
- Portrait EXIF orientation remains accounted for.

## Remaining manual validation

- Open uncached landscape images whose native width is just below the Viewer stage and confirm no shrink at replacement.
- Recheck portrait images with EXIF orientation 6 or 8.
- Confirm metadata identification overhead is not noticeable on OneDrive-backed files; only image headers are decoded, not full pixels.
