# Stabilize folder shell position

- Date: 2026-08-07 14:04:58 +07:00 (Asia/Bangkok)

## Request and intent

- Correct folder covers that still reached or disappeared behind the item-information panel only for some image combinations.

## Cause and durable rule

- The folder shell remained a relative CSS-grid item with percentage height. Intrinsic dimensions from some loaded collage images could contribute an automatic minimum size and shift/enlarge that grid item.
- A folder shell must be absolutely positioned at an explicit percentage position and size inside the fixed 4:3 media box, with zero minimum dimensions. Only the inner collage grid clips overflow; image intrinsic dimensions must not affect shell geometry.

## Files changed

- `wwwroot/css/site.css`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Backup

- Before deployment, backed up deployed CSS to `backup/2026-08-07_140458_pre-fixed-folder-shell-layout`.

## Validation actually performed

- Inspected the user's screenshot and correlated inconsistent shell positions with content-sized grid behavior.
- Changed the Grid shell to absolute `left: 50%`, `top: 53%`, 78% width, and 66% height with `min-width/min-height: 0` and centered translation.
- Kept a separate compact List position at `top: 54%`, 72% width, and 54% height.
- Added inner collage overflow clipping while leaving the outer tab visible.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- `node --check wwwroot/js/site.js`: passed; JavaScript was not changed.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.
- Deployed CSS to IIS and verified its SHA-256 matched publish output (`2BCEDDA40CBEFF063A3F5C4FE582C764BC6575FE1C5C960237134BCA28321B8C` at that deployment step).
- A planned anonymous headless share screenshot could not run because no active unlisted share remained. No token or access setting was changed.
- The source directory is not a Git repository, so no Git status or `git diff --check` result is available.

## User-visible result

- Folder shell position and size no longer depend on portrait/landscape thumbnail intrinsic sizes, preventing individual covers from dropping into the name panel.

## Remaining manual validation

- Refresh the signed-in Grid and confirm all folder bottom borders remain at the same height across 1-to-4-image covers and dense 8-item rows.

