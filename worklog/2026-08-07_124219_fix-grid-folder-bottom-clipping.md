# Fix Grid folder bottom clipping

- Date: 2026-08-07 12:42:19 +07:00 (Asia/Bangkok)

## Request and intent

- Correct the Grid folder cover shown in the user's screenshot, where the bottom of the pixel-art folder frame reached the item-information panel and its lower outline was not visible.
- Preserve the newly established folder silhouette and the existing pixel theme.

## Cause

- `.folder-cover` applied percentage padding while `.folder-cover-shell` also used percentage width, height, and top margin inside the fixed 4:3 media area.
- At dense Grid widths, that combined sizing left no reliable clearance between the shell's lower border and the item-information panel.

## Durable concepts established

- Size the Grid folder shell against the unpadded media box and reserve visible clearance around the complete outline.
- Keep Grid and List shell dimensions separate because their media boxes have different proportions and absolute sizes.

## Files changed

- `wwwroot/css/site.css`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Backup

- Before deployment, backed up the deployed CSS to `backup/2026-08-07_124219_pre-folder-grid-fit`.

## Validation actually performed

- Inspected the user-provided 2048x914 screenshot and confirmed the failure occurred where the folder body met the item-information panel in dense Grid mode.
- Removed folder-media padding, reduced the Grid shell to 78% width and 66% height with 6% top margin, and reduced the compact List shell height separately.
- Confirmed the Grid density control permits 2 through 10 columns and the corrected selectors are present in source.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.
- IIS initially retained locks on two runtime DLLs briefly after `app_offline.htm` was created, so the bulk copy reported those two skipped writes. This session changed only CSS, and subsequent SHA-256 verification confirmed both the deployed CSS and application DLL exactly match the publish output. The offline file was removed.
- Live `/Gallery/` returned HTTP 302 to the application login page.
- Live `/Gallery/css/site.css` returned HTTP 200 and contained the corrected 78% width, 66% height, and 6% margin Grid-shell rules.
- The source directory is not a Git repository, so no Git status or `git diff --check` result is available.

## User-visible result

- The complete bottom border and shadow of folder covers now remain above the folder-name panel in Grid view.
- The folder silhouette remains distinct, while the collage has slightly more breathing room on all sides.
- List view retains its own compact folder frame without inheriting the larger Grid dimensions.

## Remaining manual validation

- The supplied screenshot validates the pre-fix defect, but post-fix interactive browser QA was not run because this project's current Codex desktop workaround prohibits In-app Browser use after two prior app exits during browser teardown.
- Refresh the signed-in Gallery and confirm the bottom border at the user's preferred dense Grid setting, especially at 7 through 10 items per row.
