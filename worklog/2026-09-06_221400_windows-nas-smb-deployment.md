# Windows NAS SMB deployment

## Date and time

- 2026-09-06 22:14:00 UTC+07:00 (Asia/Bangkok)

## Request / intent

- Deploy the current WebGallery source to the Windows NAS web root at `\\Gimkim-nas\c\Users\tatsa\web`.
- Copy the existing production SQLite database and settings needed to preserve users, shares, application settings, and authentication cookies.

## Concepts and rules established

- The NAS application root is `C:\Users\tatsa\web`.
- Persistent runtime state is kept outside that application root at `C:\Users\tatsa\web-data`; FFmpeg tools are kept at `C:\Users\tatsa\web-tools`; the empty default content root is `C:\Users\tatsa\gallery-content`.
- The existing `index.html` redirect for the separately hosted `/api/` application was preserved. WebGallery's `web.config` uses `inheritInChildApplications="false"`, so the deployment did not remove or overwrite that child application.
- Existing database user-root mappings were preserved exactly. Some source-machine paths do not currently exist on the NAS and must be remapped through Management when the NAS media locations are known.

## Files changed

- Source history: added this worklog only. Existing dirty source changes were not rewritten or reverted.
- NAS application files: copied the complete `Windows-IIS` publish output to `\\Gimkim-nas\c\Users\tatsa\web`.
- NAS deployment settings: `\\Gimkim-nas\c\Users\tatsa\web\appsettings.json` uses NAS-local paths under `C:\Users\tatsa` for database, cache, Data Protection keys, default content, FFmpeg, and ffprobe.
- NAS persistent state: copied a consistent SQLite snapshot to `\\Gimkim-nas\c\Users\tatsa\web-data\gallery.db` and the existing Data Protection key to `web-data\keys`.
- NAS media tools: copied `ffmpeg.exe` and `ffprobe.exe` to `\\Gimkim-nas\c\Users\tatsa\web-tools`.
- Granted `BUILTIN\IIS_IUSRS` Modify on `web-data` and Read & Execute on `web-tools` and `gallery-content`.
- Did not copy `bootstrap-admin.txt` because the migrated database is already initialized and the file contains bootstrap credentials. Thumbnail cache was not copied because it is replaceable.

## Validation actually performed

- `dotnet publish .\WebGallery.csproj -p:PublishProfile=Windows-IIS`: passed.
- SQLite `.backup` created the deployment snapshot while the source database remained available; `PRAGMA integrity_check` returned `ok` before transfer.
- Compared 32 published application files recursively between staging and the NAS by relative path and SHA-256: zero differences.
- NAS `appsettings.json` parsed successfully as JSON.
- SHA-256 matched source and NAS copies for `gallery.db`, the Data Protection key, `ffmpeg.exe`, and `ffprobe.exe`.
- NAS hashes: `WebGallery.dll` `21A937A2DBEF2C29639BC6B47DA52A0F82F44DACE48EDD512833AAFD2549B5A4`; `gallery.db` `10BF74A7841ABB550F32B9A9AF9A6A38B863FB48AF312F3C9600D987C400505A`.
- Confirmed the NAS has ASP.NET Core runtime 10.0.11, IIS, and ASP.NET Core Module V2 installed.
- Confirmed the resulting IIS ACLs on the deployed state/tool paths.

## User-visible result

- WebGallery binaries, settings, user/share database, authentication key ring, and media tools are present on the Windows NAS in the intended locations.
- Database and key state are separated from the application root so a later application publish does not overwrite them.

## Remaining manual test / uncertainty

- The NAS exposes SMB but filters HTTP/HTTPS and WinRM from this workstation. `http://192.168.1.29/health` timed out, and remote IIS service control was denied, so the live NAS process and browser UI could not be verified or started from this session.
- The IIS site/application still needs to be confirmed locally on the NAS as pointing to `C:\Users\tatsa\web`, and Windows Firewall must allow the intended HTTP/HTTPS port before LAN access can be verified.
- Existing user roots still reference source-machine locations such as `C:\Users\tatsa\OneDrive\camera`, `C:\Users\tatsa\Desktop\Dashcam`, and `C:\Web\gallery-content`; these locations do not currently exist on the NAS and were not guessed or rewritten.
- SQLite created temporary zero/empty journal sidecars next to the staging snapshot; two harmless sidecar files were copied into the NAS application root by the first transfer. They are not used because the configured database is under `web-data`.
