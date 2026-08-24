# Mobile one-row sticky Gallery toolbar

## Date and time

- 2026-08-24 09:27:05 UTC+07:00 (Asia/Bangkok)

## Request / intent

- Reduce the very tall sticky Gallery toolbar shown in the supplied mobile screenshot.
- Follow the clarified requirement: while scrolling, keep only Sort, Grid/List, order, and Download ZIP in one row; hide the other controls behind expand/collapse.

## Concepts and rules established

- At viewports up to 520px, the normal toolbar remains visible before it reaches the sticky position. After scrolling past that position, JavaScript adds `mobile-scroll-compact`.
- Compact mode is one row containing a sort-field select, one-button Grid/List toggle, ascending/descending order link, folder/collection ZIP icon when available, and a chevron.
- Search, type filter, and the full existing toolbar start collapsed in sticky compact mode. The chevron toggles `mobile-controls-expanded`; scrolling back above the toolbar resets it to the full non-sticky presentation.
- File-selection contextual controls continue to replace the normal toolbar and suppress the compact summary while a selection is active.
- The existing ResizeObserver continues measuring toolbar height so the List header stays immediately below either compact or expanded state.

## Files changed

- `Views/Gallery/Index.cshtml`
- `wwwroot/js/site.js`
- `wwwroot/css/site.css`
- `wwwroot/css/site-modern.css`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog.

## Validation actually performed

- Inspected the supplied 591×1280 mobile screenshot; the sticky surface occupied roughly four control rows before the change.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- `node --check wwwroot/js/site.js`: passed.
- Static assertions confirmed both themes define the one-row mobile summary only inside the 520px media rules, collapse the full toolbar by default, and expose expanded state. Razor markup contains Sort, Grid/List, Order, ZIP, and chevron controls; JavaScript contains sticky activation and expansion synchronization.
- `git diff --check` passed apart from expected checkout line-ending notices.
- `dotnet publish WebGallery.csproj -c Release -o publish\2026-08-24_092620`: passed.
- With `app_offline.htm` active, backed up 150 preceding IIS files and production SQLite under `backup\2026-08-24_092620_pre-mobile-compact-toolbar`; the backup database returned `PRAGMA integrity_check = ok`.
- Deployed Release output to `C:\Web\imagegallery`, excluding `appsettings.Development.json`, then removed the maintenance marker.
- Publish/deploy SHA-256 matched for `WebGallery.dll`, `site.js`, and both theme stylesheets. Deployed DLL SHA-256 is `93970765F033BE8EF9F22520AB2E3CF6974AD9D7D9652C1DD7CB4CB1D86AD003`.
- Live Login returned HTTP 200 and Management returned the expected anonymous HTTP 302 redirect. Cache-busted live Modern CSS and `site.js` contained the compact/expanded implementation. Production SQLite remained `integrity_check = ok`.
- Confirmed the deployment contains neither `app_offline.htm` nor `appsettings.Development.json`.

## User-visible result

- Scrolling a mobile Gallery leaves a single compact sticky row rather than the Search, filter, sort, and view controls occupying four full rows.
- Sort field, current Grid/List mode, order, and ZIP download remain immediately available; Search/type and the full controls are one chevron tap away.

## Remaining manual test / uncertainty

- The exact rendered mobile interaction was not visually replayed after deployment because the project-specific Codex in-app-browser crash workaround remains active. Source/build/static and live-delivery checks passed; manually refresh the mobile page and verify compact activation, chevron expansion, Grid/List toggle, order, and ZIP download at that device's browser viewport.
