# Mobile toolbar scroll hysteresis

## Date and time

- 2026-08-24 10:33:31 UTC+07:00 (Asia/Bangkok)

## Request / intent

- Stop the mobile Gallery toolbar from rapidly alternating between its full and compact states while scrolling.
- Prevent the resulting feedback loop from moving the page back toward the top.

## Concepts and rules established

- Mobile compact/full transitions must not share one scroll threshold because changing the toolbar height can cause browser scroll anchoring to cross that same threshold again.
- Enter compact mode only while scrolling downward at least 96 pixels beyond the toolbar's document position.
- Return to full mode only while scrolling upward past a separate exit point 24 pixels before the toolbar's document position.
- Ignore scroll changes for 650 milliseconds after a transition, then rebase the stored scroll position once before accepting another transition. This prevents layout-driven scroll changes from being mistaken for user direction.
- Keep the toolbar out of scroll-anchor selection in both Retro and Modern themes.

## Files changed

- `wwwroot/js/site.js`
- `wwwroot/css/site.css`
- `wwwroot/css/site-modern.css`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog.

## Validation actually performed

- `node --check wwwroot/js/site.js`: passed.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- `git diff --check`: passed apart from expected checkout line-ending notices.
- Inspected the source diff to confirm the requestAnimationFrame callback does not accidentally pass its timestamp as the initialization flag.
- `dotnet publish WebGallery.csproj -c Release -o publish\2026-08-24_103331`: passed.
- With `app_offline.htm` active, backed up 149 preceding IIS files and the production SQLite database under `backup\2026-08-24_103331_pre-mobile-toolbar-hysteresis`; the backup database returned `PRAGMA integrity_check = ok`.
- Deployed Release output to `C:\Web\imagegallery`, excluding `appsettings.Development.json`, then removed the maintenance marker.
- Publish/deploy SHA-256 matched for `WebGallery.dll`, `site.js`, and both theme stylesheets. Deployed DLL SHA-256 is `10784A71BC47981CD2DB6F1C339D2CE555A36B9D950647D9F006FC7B0ECD6EFE`.
- Live Login returned HTTP 200 and Management returned the expected anonymous HTTP 302 redirect. Cache-busted live JavaScript contained the enter threshold, transition guard, and scroll rebase; both live theme stylesheets contained the scroll-anchor rule.
- Production SQLite remained `integrity_check = ok`; deployment contains neither `app_offline.htm` nor `appsettings.Development.json`.

## User-visible result

- The compact toolbar no longer immediately expands again merely because its own height change altered the browser's scroll position.
- A meaningful downward/upward scroll distance and direction now separates the two states, with a short transition debounce to prevent rapid oscillation.

## Remaining manual test / uncertainty

- The exact touch-scroll interaction was not visually replayed after deployment because the project-specific Codex in-app-browser crash workaround remains active. Source/build/static and live-delivery checks passed; manually refresh the mobile page and verify downward collapse, continued scrolling, and expansion only after returning toward the toolbar's original position.
