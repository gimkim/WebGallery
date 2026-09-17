# Windows NAS imagegallery redeployment

## Date and time

- 2026-09-06 22:24:51 UTC+07:00 (Asia/Bangkok)

## Request / intent

- Redeploy WebGallery to the corrected Windows NAS application root at `\\Gimkim-nas\c\Users\tatsa\web\imagegallery`.
- Copy the current production database, application settings, Data Protection keys, and external media tools required by that deployment.

## Concepts and rules established

- The Windows NAS application root is `C:\Users\tatsa\web\imagegallery`, not its parent `C:\Users\tatsa\web`.
- NAS persistent state remains outside the replaceable publish root at `C:\Users\tatsa\web-data`; media tools remain at `C:\Users\tatsa\web-tools`; the empty default content root remains at `C:\Users\tatsa\gallery-content`.
- Preserve the parent web root and sibling IIS applications. Verify the NAS subapplication and its endpoint independently from the file transfer.

## Files changed

- Published the current source with the checked-in `Windows-IIS` profile.
- Copied the complete IIS publish output to `\\Gimkim-nas\c\Users\tatsa\web\imagegallery`.
- Deployed a NAS-specific `appsettings.json` pointing database/cache/keys/default content/FFmpeg paths to the NAS-local `C:\Users\tatsa` directories.
- Before replacing `web-data\gallery.db`, copied the previous NAS database to `web-data\backups\2026-09-06_2222_gallery.db`.
- Copied a new consistent snapshot of `C:\Web\imagegallery-data\gallery.db` to the NAS and retained the existing Data Protection key.
- Confirmed the already copied `ffmpeg.exe` and `ffprobe.exe` were unchanged and present under `web-tools`.
- Granted `BUILTIN\IIS_IUSRS` Read & Execute on the application/tools/content paths and Modify on `web-data` recursively.
- Updated `.agents/PROJECT_NOTES.md` with the corrected NAS deployment layout and live-verification boundary.

## Validation actually performed

- `dotnet publish .\WebGallery.csproj -p:PublishProfile=Windows-IIS`: passed.
- Created the source database snapshot with SQLite `.backup`; `PRAGMA integrity_check` returned `ok`.
- Compared all 32 deployed application files recursively by relative path and SHA-256: zero differences.
- Parsed the deployed `appsettings.json` successfully as JSON.
- Source/NAS SHA-256 matched for `gallery.db`, the Data Protection key, `ffmpeg.exe`, and `ffprobe.exe`.
- `WebGallery.dll` SHA-256 is `21A937A2DBEF2C29639BC6B47DA52A0F82F44DACE48EDD512833AAFD2549B5A4`; deployed `gallery.db` SHA-256 is `10BF74A7841ABB550F32B9A9AF9A6A38B863FB48AF312F3C9600D987C400505A`.
- The pre-replacement NAS database passed `integrity_check`. Its logical dump differed from the source only in `sqlite_sequence` for `UserRoots` (`76` on NAS versus `73` at source); all table content rows and counts matched. The complete old file remains in the timestamped backup.
- ACL operations processed all intended files with zero failures.

## User-visible result

- WebGallery is now placed in the corrected `imagegallery` subdirectory with the migrated user/share database, settings, cookie key ring, and media tools available at the configured NAS-local paths.
- The earlier parent-root deployment was already absent before this redeployment; no parent or sibling files were deleted.

## Remaining manual test / uncertainty

- The NAS still filters HTTP/HTTPS and common alternate web ports from this workstation. Both HTTP and HTTPS requests to `/imagegallery/health` timed out, so live IIS startup and browser behavior were not verified.
- IIS on the NAS must define `C:\Users\tatsa\web\imagegallery` as an application and allow the intended inbound port through Windows Firewall before endpoint validation can pass.
- Existing `UserRoots` still reference source-machine paths that do not exist on the NAS. Remap them in Management after the final NAS media paths are known.
