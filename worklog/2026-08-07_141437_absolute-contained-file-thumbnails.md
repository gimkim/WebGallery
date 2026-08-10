# Absolute contained file thumbnails

- Date: 2026-08-07 14:14:37 +07:00 (Asia/Bangkok)

## Request and intent

- Deploy a definitive CSS fix for regular portrait thumbnails that remained visually cropped even though the generated WebP was correct.
- Use the reported `GIM_9650-2.jpg` case as the real-media rendering regression.

## Cause

- The regular `<img>` was a relative CSS-grid item inside a media box whose height comes from `aspect-ratio`.
- Percentage `max-height` did not resolve as a definite constraint for that intrinsic grid item. The portrait image was constrained by width, retained a taller rendered height, and the media box's `overflow: hidden` clipped its top/bottom content.
- Server thumbnail generation and cache were not the cause in this case, as the user confirmed.

## Durable concept established

- A regular thumbnail must be an absolute layer against the image-button padding box: `position: absolute; inset: 0; width: 100%; height: 100%; object-fit: contain !important`.
- Remove the later relative-position override for image-button images. Folder collage tiles remain relative and intentionally use their separate cover treatment.

## Files changed

- `wwwroot/css/site.css`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Backup

- Before the CSS correction, backed up deployed CSS to `backup/2026-08-07_141437_pre-intrinsic-thumbnail-fit`.

## Validation actually performed

- Confirmed the live IIS CSS response was current and not served from stale Brotli/Gzip sidecar files; responses carried no content encoding and the live plain CSS matched deployment.
- The first attempted intrinsic auto/max-size correction was deployed but reproduced the crop in an exact Chrome headless rendering. It was not accepted as fixed.
- Located the user's actual source file at `C:\Users\tatsa\OneDrive\camera\2569-04-26 feel at ease\GIM_9650-2.jpg`.
- Built a focused card using the real image and live deployed Gallery CSS. Before the final correction, Chrome rendered it width-constrained and vertically clipped, reproducing the user's failure.
- Changed the image to an absolute 100%-by-100% layer with enforced contain behavior and removed the later relative-position override.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors after the final correction.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.
- Deployed the final CSS and verified its SHA-256 matched publish output (`552E46195D7DA8E95355E430F7094515B74914A2691379DD967CD002E839E545`).
- Live CSS returned the final absolute positioning, inset, and `object-fit: contain !important` rules.
- Re-rendered `GIM_9650-2.jpg` in Chrome headless with the live versioned CSS. The full portrait image fit inside the 4:3 media area with visible side space and no vertical crop; screenshot: `C:\Users\tatsa\AppData\Local\Temp\webgallery-thumbnail-fit-absolute.png`.
- This was a focused real-file/live-CSS rendering test, not an authenticated full-site interaction. The project In-app Browser was not used.
- The source directory is not a Git repository, so no Git status or `git diff --check` result is available.

## User-visible result

- Portrait and landscape regular thumbnails now fit completely inside Grid and List media boxes.
- Empty space appears on the axis that does not fill the box, instead of clipping source content.
- Folder collage tile cropping remains unchanged by design.

## Remaining manual validation

- Refresh the signed-in Gallery and confirm `GIM_9650-2.jpg` matches the verified focused rendering in the real Grid and List views.
