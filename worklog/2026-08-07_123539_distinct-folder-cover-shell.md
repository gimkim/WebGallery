# Distinct folder cover shell

- Date: 2026-08-07 12:35:39 +07:00 (Asia/Bangkok)

## Request and intent

- Make folders visually distinct from image files instead of presenting a bare four-thumbnail collage.
- Keep folder covers consistent with the established dark pixel/retro theme.

## Durable concepts established

- A directory cover collage must sit inside a recognizable folder silhouette rather than filling the media area by itself.
- The folder treatment uses a top tab, yellow pixel outline, dark-gold body, hard offset shadow, and a subtly folder-colored card border.
- The compact List view keeps the same folder identity at a smaller scale.
- Regular file thumbnails and the existing direct-child-only cover-image selection behavior remain unchanged.

## Files changed

- `Views/Gallery/Index.cshtml`
- `wwwroot/css/site.css`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Backup

- Before deployment, backed up the deployed DLL and CSS to `backup/2026-08-07_123539_pre-folder-shell`.

## Validation actually performed

- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- `dotnet list WebGallery.csproj package --vulnerable --include-transitive`: no vulnerable packages reported from the configured NuGet sources.
- Static source inspection confirmed directory cards use `directory-card`, collage images are nested under `folder-cover-shell` and `folder-cover-grid`, and Grid/List CSS defines the folder tab, outline, body, and shadow.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.
- Deployed while `C:\Web\imagegallery\app_offline.htm` was present; the offline file was removed afterward.
- SHA-256 matched between publish output and IIS deployment for `WebGallery.dll` and `wwwroot/css/site.css`.
- Live `/Gallery/` returned HTTP 302 to the application login page.
- Live `/Gallery/css/site.css` returned HTTP 200 and contained the new folder-shell, folder-tab, and directory-card selectors.
- The source directory is not a Git repository, so no Git status or `git diff --check` result is available.

## User-visible result

- Folder cover photos now appear inside a clear pixel-art folder frame with a tab instead of resembling an ordinary four-image thumbnail.
- Folder cards have a muted yellow border that becomes bright yellow on hover, preserving the existing theme while improving recognition.
- List view displays a compact version of the same folder frame.

## Remaining manual validation

- Interactive browser QA was not run because this project's current Codex desktop workaround prohibits In-app Browser use after two prior app exits during browser teardown.
- After signing in, manually confirm folder-shell sizing with one, two, three, and four cover images in both Grid and List views, including narrow/mobile layouts.
