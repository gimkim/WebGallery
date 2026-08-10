# Folder cover spacing and touch zoom

- Date: 2026-08-07 15:18:28 +07:00 (Asia/Bangkok)

## Request and intent

- Reduce unused space around folder-cover thumbnails so the folder outline nearly fills the card media area.
- Add mobile full-viewer pinch zoom in/out and double-tap zoom while preserving existing desktop click zoom and image navigation.

## Durable concepts established

- Folder shells use small fixed edge insets rather than percentage width/height gutters. The complete tab and hard shadow must remain inside the media box.
- Touch zoom uses Pointer Events with continuous scaling from Fit through 4x original size and keeps the two-finger midpoint anchored to the same image point.
- A single finger pans a zoomed image. Double-tap zooms around the tapped point and toggles back to Fit.
- Large images double-tap between Fit and 1:1; images that already fit start at native size and double-tap to 2x.
- Touch-generated synthetic clicks are suppressed so a single tap cannot trigger the desktop mouse zoom action.

## Files changed

- `wwwroot/css/site.css`
- `wwwroot/js/site.js`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Backup and deployment

- Backed up the previously deployed CSS and JavaScript under `backup/2026-08-07_151757_pre-folder-cover-touch-zoom`.
- Published Release output to `publish-current`.
- Deployed the published `wwwroot/css/site.css` and `wwwroot/js/site.js` to `C:\Web\imagegallery`.

## Validation actually performed

- `node --check wwwroot/js/site.js`: passed.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- A temporary focused headless-Chrome harness opened the real viewer script with a 1600x1200 test image and dispatched touch Pointer Events. The rendered width changed from 516 px fitted to 1032 px after pinch, returned to 516 px after double-tap, and changed to 1600 px after the next double-tap.
- The same harness measured the folder shell's CSS insets as 8 px left, 24 px top, 13 px right, and 13 px bottom. The visual tab extends upward and the shadow extends right/down, leaving approximately 5-8 px of actual outer clearance while staying inside the media box.
- The temporary harness file was removed after testing.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.
- Published/deployed/live CSS SHA-256 matched: `E1C8F6E9CDE5F2CF10E63FB7BAFAA8C194C4CAEFFF357EBFBDED6AC056F8EB3B`.
- Published/deployed/live JavaScript SHA-256 matched: `BE9AA5199FD533717027AEC6B06C72DEFD05744E2845F0102DA6521BF0065F20`.
- Live CSS contained the fixed folder insets and `touch-action: none`; live JavaScript contained the bounded 4x scale and touch gesture handlers.
- Live `/Gallery/` returned HTTP 302 to the application login page after deployment.
- The source directory is not a Git repository, so no Git status or `git diff --check` result is available.

## User-visible result

- Folder collages occupy substantially more of each Grid and List media area while retaining the recognizable folder tab, border, and shadow.
- Mobile users can pinch continuously to zoom, drag while zoomed, and double-tap the desired point to zoom in or return to Fit.
- Desktop click-at-point Fit/1:1 behavior remains available.

## Remaining manual validation

- Refresh the authenticated Gallery on the user's actual mobile device and confirm pinch speed, double-tap timing, and one-finger panning feel natural in Safari/Chrome for both portrait and landscape images.
- Visually confirm folder-cover spacing at the user's preferred 8-items-per-row density and in List view. The isolated Chrome layout and gesture harness passed, but an authenticated end-to-end mobile session was not run because this project currently avoids the unstable Codex In-app Browser workflow.
