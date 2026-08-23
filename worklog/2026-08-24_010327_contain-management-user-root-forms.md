# Contain Management user-root forms

## Date and time

- 2026-08-24 01:03:27 UTC+07:00 (Asia/Bangkok)

## Request / intent

- Fix the Management user cards shown in the supplied screenshot: Gallery-folder inputs and buttons were wider than each card and overlapped adjacent users.
- Preserve the responsive three-card Management layout and equivalent Retro/Modern behavior.

## Concepts and rules established

- A root form must size from its user card, not impose desktop minimum widths on the card. Folder name and physical path use two `minmax(0, ...)` tracks; both stack at the existing narrow breakpoint.
- Save/Remove/Add controls live in a separate full-width action row that can wrap without increasing the form's intrinsic width.
- User cards, root containers, labels, and inputs explicitly allow shrinking with `min-width: 0`; inputs remain at `width/max-width: 100%`, long errors wrap, and the card clips any unexpected paint overflow as a final containment guard.
- Functional layout rules remain identical in both theme stylesheets.

## Files changed

- `Views/Admin/Index.cshtml`
- `wwwroot/css/site.css`
- `wwwroot/css/site-modern.css`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog.

## Validation actually performed

- Inspected the supplied 1782×791 screenshot and confirmed the old root grid required at least `10rem + 16rem + Save + Remove`, exceeding the approximately 400px card content width.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- Static layout assertions passed in both theme files: flexible zero-minimum field tracks are present, old fixed-minimum tracks are absent, cards contain overflow, and root inputs may shrink to the available width.
- Razor source contains action groups for both edit and add forms. `git diff --check` passed apart from expected checkout line-ending notices.
- `dotnet publish WebGallery.csproj -c Release -o publish\2026-08-24_010227`: passed.
- While `app_offline.htm` was active, backed up 150 preceding IIS files and production SQLite under `backup\2026-08-24_010227_pre-management-card-containment`; the backup database returned `PRAGMA integrity_check = ok`.
- Deployed Release output to `C:\Web\imagegallery`, excluding `appsettings.Development.json`, and removed the maintenance marker.
- Publish/deploy SHA-256 matched for `WebGallery.dll` and both theme stylesheets. Deployed DLL SHA-256 is `A459178059AF8647F427FAA59EB77305F63F3E812BA311B35A7857FD4C77FEF6`.
- Live Login returned HTTP 200 and Management returned the expected anonymous HTTP 302 redirect. A cache-busted request to live Modern CSS contained the new card containment, flexible field tracks, shrinkable inputs, wrapping action row, and narrow breakpoint. Production SQLite remained `integrity_check = ok`.
- Confirmed the deployment contains neither `app_offline.htm` nor `appsettings.Development.json`.

## User-visible result

- Each user's root rows remain inside that user's card instead of extending across neighboring cards.
- Folder name and path share the available width; Save/Remove or Add sit on their own contained action row and wrap on narrow screens.
- Long paths and failure messages can no longer determine the width of the whole Management grid.

## Remaining manual test / uncertainty

- The rendered Management page was not visually re-opened after deployment because the project-specific Codex in-app-browser crash workaround remains active. Source structure, Release compilation, static responsive invariants, deployed hashes, and live CSS delivery were verified; the user should refresh the existing browser page to confirm the visual result at their exact viewport and zoom.
