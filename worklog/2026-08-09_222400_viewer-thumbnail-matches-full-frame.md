# Viewer thumbnail matches the full-image frame

- Date: 2026-08-09 22:24:00 +07:00 (Asia/Bangkok)

## Request and intent

- When an uncached original uses the Gallery thumbnail as its temporary Viewer image, keep the placeholder in the same frame size as the eventual fitted original.
- Make the transition feel like resolution becoming sharper instead of the image changing dimensions.

## Behavior established

- Each image Viewer button now carries the configured thumbnail width and height from `GalleryOptions`.
- After the temporary thumbnail decodes, JavaScript checks whether its natural WebP dimensions reach either configured thumbnail bound.
- Reaching a bound means ImageSharp reduced a larger source. The Viewer therefore upscales that thumbnail, preserving aspect ratio, to the maximum rectangle that fits the available stage.
- The eventual large original uses the same aspect-preserving available-stage fit, so the placeholder and final frame have equal dimensions apart from unavoidable pixel rounding.
- If the thumbnail is below both configured bounds, it is treated as a genuinely small original and remains at native size, preserving the full-viewer no-upscale rule.
- Placeholder mode disables Fit/1:1 zoom and recalculates the same fitted frame when the viewport changes. Full-image zoom behavior resumes after the original replaces it.

## Files changed

- `Views/Gallery/Index.cshtml`
- `wwwroot/js/site.js`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Validation actually performed

- `node --check wwwroot/js/site.js`: passed.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- Static review confirmed the view emits both configured thumbnail bounds and only the thumbnail display path calls `setViewerPlaceholderSize`.
- Checked representative calculations:
  - a 480x320 thumbnail in a 1000x700 stage becomes 1000x667, matching a fitted 6000x4000 original;
  - a 240x360 portrait in the same stage becomes 467x700, matching its same-ratio large original;
  - a 300x200 thumbnail below both bounds stays 300x200.
- Confirmed viewport resize dispatches back to placeholder sizing while placeholder mode is active.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.
- Real authenticated visual transition testing was not performed because this project's Codex In-app Browser workaround remains active.
- The source workspace is not a Git repository, so Git status and `git diff --check` are unavailable.

## Backup and deployment

- Backed up the previous deployed DLL, symbols, static-web-assets manifest, JavaScript, and compressed JavaScript under `backup/2026-08-09_222244_pre-viewer-placeholder-frame`.
- Used `C:\Web\imagegallery\app_offline.htm` only during the copy window.
- Deployed the published binary/view, static-web-assets manifest, JavaScript, and Brotli/Gzip variants to `C:\Web\imagegallery`.
- Removed `app_offline.htm` after deployment.
- Production SQLite data, users, shares, settings, gallery files, existing caches, CSS, and IIS configuration were not modified.

## Post-deployment validation

- Every copied publish/deployment SHA-256 pair matched.
- Deployed `WebGallery.dll` SHA-256: `9DCD998CCADB29F8BECE92B8864BEDB197C34A9E0E0886426BDEDBC53C738D8A`.
- Deployed `site.js` SHA-256: `9F21FC807FD64C39B50C82711A11DA852250CF9EA2903F21EA625635EB61C7C0`.
- Public Login returned HTTP 200.
- Anonymous `/Gallery/` returned HTTP 302 to the application Login route.
- Live JavaScript returned HTTP 200 and contains placeholder sizing, reduced-source bound detection, and resize handling.
- Confirmed `C:\Web\imagegallery\app_offline.htm` is absent.

## User-visible result

- On an original cache miss, the intentionally blurry thumbnail already occupies the fitted full-image frame.
- When the original finishes decoding, the frame stays stable and only image detail becomes sharp.

## Remaining manual validation

- Open uncached landscape, portrait, square, and panoramic originals and visually confirm there is no size jump at replacement.
- Verify a genuinely small image below 480x360 remains at native size throughout.
- Check the stable frame during mobile orientation changes in both Retro and Modern themes.
