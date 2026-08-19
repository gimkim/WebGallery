# Stable density control with step buttons

## Date and time

2026-08-19 17:32:21 +07:00 (Asia/Bangkok)

## Request / intent

Stop the Density toolbar/container from shifting while the items-per-row slider changes the Grid, and add easy `−` and `+` controls for one-step changes without dragging.

## Concepts and rules established

- The observed movement comes from density changes altering page height, which can make the vertical scrollbar appear or disappear and resize/reflow the containing toolbar.
- Reserve a stable root scrollbar gutter in both themes.
- Keep each component of the density control at a fixed flex size, including a two-character numeric output for values 2–10.
- Step buttons use the existing range `input` event path so persistence and every Gallery/Collection grid update remain identical to dragging.

## Files changed

- `Views/Gallery/Index.cshtml`
- `Views/Collections/Index.cshtml`
- `Views/Collections/SelectFolders.cshtml`
  - Added accessible `−`/`+` step buttons around each Density range.
- `wwwroot/js/site.js`
  - Added one-column stepping and automatic disabled states at min/max, dispatching the existing range update event.
- `wwwroot/css/site.css`
- `wwwroot/css/site-modern.css`
  - Reserved a stable scrollbar gutter and fixed the density label/range/buttons/output sizing.
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
  - Recorded the durable no-shift and shared-update-path rules.

## Validation actually performed

- `node --check wwwroot/js/site.js` passed.
- `dotnet build WebGallery.csproj -c Release --no-restore` passed with 0 warnings and 0 errors.
- Static assertions confirmed `scrollbar-gutter: stable`, fixed range sizing, and step-button styling in both Retro and Modern stylesheets.
- Static assertions confirmed exactly two density step buttons in Gallery, Collections, and the collection folder picker.
- `git diff --check` passed with only line-ending conversion notices.
- Release publish completed at `publish/20260819_173221`.
- The previous IIS deployment was backed up to `backup/20260819_173221_pre-density-control` (146 files).
- Release output was copied to `C:\Web\imagegallery` with `appsettings.Development.json` excluded; publish/deploy SHA-256 matched for the application DLL, JavaScript, and both theme stylesheets.
- An anonymous live request to `/Gallery/` returned the expected HTTP 302 application Login redirect.
- Public `site.js`, `site.css`, and `site-modern.css` returned HTTP 200 with SHA-256 hashes matching the deployed Release output.
- Confirmed deployment cleanup left neither `app_offline.htm` nor `appsettings.Development.json`.

## User-visible result

Changing density no longer changes available viewport width when the vertical scrollbar appears or disappears, preventing the containing toolbar from reflowing sideways. Users can drag the slider or press `−`/`+` to change one item per row at a time; buttons disable at 2 and 10.

## Remaining manual validation / uncertainty

- Authenticated visual validation while repeatedly moving between densities that cross the page-scroll threshold remains manual for both themes under the project's in-app-browser workaround.
