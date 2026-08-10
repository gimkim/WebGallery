# Modern folder join and scroll performance

- Date: 2026-08-07 15:58:59 +07:00 (Asia/Bangkok)

## Request and intent

- Remove the protruding/glitched line at the upper-left join of Modern folder-cover tabs.
- Fix Modern Grid/List scrolling that stuttered while Retro remained smooth.

## Causes

- The Modern tab used a 2px border but retained the earlier `left: -3px` offset, while the shell had a rounded top-left corner. The one-pixel misalignment plus curved join produced the visible protrusion.
- Modern retained several expensive paint effects that Retro disabled or visually avoided: a fixed page gradient, header and sticky-toolbar backdrop blur, per-card selection-control backdrop blur, and large blur-radius shadows on every folder shell.

## Durable concepts established

- The Modern folder shell has a square top-left join and rounded remaining corners (`0 10px 10px 10px`). Its 2px tab border aligns at `left: -2px` and overlaps from `top: -18px`.
- Large Modern galleries must not use fixed backgrounds or repeated per-card backdrop filters.
- Modern header, sticky toolbar, and selection controls use opaque/nearly opaque surfaces with `backdrop-filter: none`.
- Repeated folder-shell/fallback-folder shadows use inexpensive zero-blur offsets; List view uses the same rule.

## Files changed

- `wwwroot/css/site-modern.css`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Backup and deployment

- Backed up the previously deployed Modern CSS under `backup/2026-08-07_155838_pre-modern-folder-scroll-fix`.
- Published Release output to `publish-current`.
- Deployed only the published Modern CSS because no application binary or Retro CSS changed.

## Validation actually performed

- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- A focused headless-Chrome visual harness rendered two representative Modern folder cards. The resulting screenshot was inspected at original resolution: the left shell border and tab aligned continuously, the top-left body join was square, and the previous protruding line was absent.
- Computed/style assertions confirmed `backdrop-filter: none` for the header, sticky toolbar, and selection controls; no fixed background remained; Grid folder shadow used a zero-blur 3px offset and List used a zero-blur 2px offset.
- A 300-folder stress document was loaded in headless Chrome to exercise the revised Modern selectors. Headless virtual-time frame timing was not treated as a real-device performance measurement.
- Temporary HTML harnesses and the generated screenshot were removed after validation.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.
- Published, deployed, and live Modern CSS SHA-256 matched: `D75F026D0B59EA0BA2338C5EBB40F98F4A322D21A389B9728DF1F633760FDA86`.
- Production currently stored `Theme=modern`; the live Login page linked `site-modern.css` and emitted `body.theme-modern`.
- Live Modern CSS contained the corrected radius/offset and performance-safe filter/shadow declarations.
- Live `/Gallery/` returned HTTP 302 to login for an anonymous root request after deployment.
- The source directory is not a Git repository, so no Git status or `git diff --check` result is available.

## User-visible result

- Modern folder covers no longer have a one-pixel protrusion at the upper-left tab/body join.
- Grid and List scrolling avoid the repeated blur and fixed-background repaints that caused Modern to stutter on large pages.
- Modern retains its dark contemporary palette and rounded card/folder appearance; Retro is unchanged.

## Remaining manual validation

- Refresh the active Modern Gallery and scroll the user's same large Grid and List folders. Confirm smoothness now matches Retro on the actual GPU/browser.
- Visually inspect the corrected folder join at 8 items per row and List mode. The focused render was inspected, but the authenticated live large folder was not controlled through the unstable In-app Browser workflow.
