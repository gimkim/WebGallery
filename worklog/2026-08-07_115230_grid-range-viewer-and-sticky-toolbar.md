# Grid range selection, click-to-zoom viewer, and top-sticky toolbar

- Date: 2026-08-07 11:52:30 +07:00 (Asia/Bangkok)

## Request and intent

- Report the two Codex desktop exits to the Codex application team.
- In Grid view, make Shift+click select an anchor-to-clicked range while clearing old selections; retain Ctrl+Shift+click as additive range selection.
- Correct the full-image viewer so oversized images fit the available height as well as width, remove the separate `1:1` button, and toggle fitted/original size by clicking the image.
- Let the site header scroll out of view and keep the Gallery toolbar stuck directly to the top edge afterward.

## Durable concepts established

- Shift+click replaces prior selection with its range; Ctrl+Shift+click preserves old selection and adds its range.
- Viewer fit size is calculated from the stage's actual content width and height. Small images remain at native size; clicking an oversized image toggles between fitted and original `1:1` pixels.
- The site header is not sticky. The Gallery toolbar uses `top: 0` and becomes the topmost persistent row after the header scrolls away.
- Codex crash reports must omit raw logs when they contain local paths or task metadata; provide sanitized evidence and offer the raw evidence privately if requested.

## Files changed

- `wwwroot/js/site.js`
- `wwwroot/css/site.css`
- `Views/Gallery/Index.cshtml`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## External report

- Searched the existing `openai/codex` issues before reporting and found issue `#36645`, which matches the Browser Use teardown symptom.
- Added sanitized evidence for Codex Desktop `26.803.5235.0` to that existing issue instead of opening a duplicate. GitHub returned comment ID `5212538982`.
- The comment includes both AppX-container destruction/relaunch timelines, the correlated stale-DOM and ResizeObserver messages, and the absence of WER, crash-dump, OOM, or explicit fatal markers. It excludes usernames, local paths, task contents, and raw logs.

## Validation actually performed

- `node --check wwwroot/js/site.js`: passed.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- `dotnet list WebGallery.csproj package --vulnerable --include-transitive`: no vulnerable packages reported from the configured NuGet sources.
- Static source checks confirmed the Shift range branch clears selection only when Ctrl is not held, the image owns the zoom click handler, the removed `viewer-zoom` element is no longer referenced, the toolbar is `top: 0`, and the header is non-sticky.
- `dotnet publish WebGallery.csproj -c Release --no-build -o publish`: passed.
- Deployed the publish output to `C:\Web\imagegallery` while `app_offline.htm` was present. SHA-256 hashes matched for `WebGallery.dll`, `wwwroot/js/site.js`, and `wwwroot/css/site.css`; the offline file was removed.
- Live anonymous `https://gimgim.ddns.net:45570/Gallery/` returned 302 to the application login page; deployed JavaScript and CSS each returned HTTP 200.
- Direct inspection of the live JavaScript confirmed the Shift range, replace-vs-add, click-to-zoom, and height-fit expressions; direct inspection of the live CSS confirmed the non-sticky header and `top: 0` toolbar rules.
- The source directory is not a Git repository, so no Git status or `git diff --check` result is available.

## User-visible result

- Shift+click now selects only the contiguous range; Ctrl+Shift+click adds a contiguous range to existing selections.
- Oversized originals are explicitly sized to fit the viewer's available width and height. Clicking the image switches to 1:1 and clicking again returns to fit; no zoom button is shown.
- Scrolling removes the Gallery/login header from view while the control toolbar remains at the viewport's top edge.

## Remaining manual validation

- Interactive browser validation was deliberately not run because this Codex build has twice exited during In-app Browser teardown. After signing in, manually check Shift/Ctrl+Shift gestures, an oversized portrait and landscape image, click-to-zoom scrolling, a smaller-than-screen image, and desktop/mobile sticky behavior against the user's real collection.
