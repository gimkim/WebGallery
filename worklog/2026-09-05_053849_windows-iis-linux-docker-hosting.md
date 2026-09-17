# Windows IIS and Linux Docker hosting profiles

## Date and time

- 2026-09-05 05:38:49 UTC+07:00 (Asia/Bangkok)

## Request / intent

- Keep the existing Windows/IIS hosting model working.
- Make the same WebGallery source deployable in a Linux x64 Docker container.
- Check in repeatable publish profiles for both targets.

## Concepts and rules established

- `appsettings.json` remains the Windows/IIS baseline. Docker selects `appsettings.Docker.json` with `ASPNETCORE_ENVIRONMENT=Docker` and overrides state, media, and tool paths without changing IIS defaults.
- Writable database, Data Protection keys, and thumbnail cache are external persistent bind mounts. Original media is mounted read-only, with each NAS drive/path assigned a unique container path.
- Reverse-proxy headers are disabled by default. Enabling them requires an explicit public-host allowlist and accepts For/Proto/Host/Prefix only from configured proxy IPs/CIDRs; wildcard public hosts are rejected.
- Filesystem path comparison follows the host: case-insensitive on Windows and case-sensitive on Linux. Linux dotfiles are hidden. Reparse/symbolic-link children are not listed, used as covers, or included in ZIPs, and direct resolution rejects a link target outside its assigned root.
- Media concurrency and encoder threads are configurable and clamped. Windows keeps 16 total jobs / 2 Quick Sync jobs / 16 encoder threads; Docker defaults to 4 / 2 / 4. FFmpeg probes use `NUL` on Windows and `/dev/null` on Linux.

## Files changed

- Runtime/configuration: `Program.cs`, `Models/GalleryOptions.cs`, `Models/HostingOptions.cs`, `appsettings.json`, `appsettings.Docker.json`.
- Cross-platform filesystem/access behavior: `Services/FileSystemService.cs`, `Services/ShareAuditService.cs`, `Controllers/GalleryController.cs`, `Controllers/CollectionsController.cs`, `Controllers/AdminController.cs`.
- Cross-platform media limits/probes: `Services/MediaService.cs`.
- Publish/container assets: `WebGallery.csproj`, `Properties/PublishProfiles/Windows-IIS.pubxml`, `Properties/PublishProfiles/Linux-Docker.pubxml`, `Dockerfile`, `.dockerignore`, `compose.yaml`, `compose.intel-qsv.yaml`, `deployment/docker.env.example`.
- Documentation: `README.md`, `deployment/INSTALL-WINDOWS11-IIS.txt`, `deployment/INSTALL-DOCKER-LINUX.md`, `AGENTS.md`, `.agents/PROJECT_NOTES.md`, `.gitignore`, and this worklog.
- Test coverage: `tests/FileSystemVisibilityHarness/Program.cs`.

## Validation actually performed

- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- `dotnet publish WebGallery.csproj -p:PublishProfile=Windows-IIS`: passed and produced framework-dependent `win-x64` output under `publish/windows-iis`.
- `dotnet publish WebGallery.csproj -p:PublishProfile=Linux-Docker`: passed and produced framework-dependent `linux-x64` output under `publish/linux-docker`.
- Windows artifact inventory contains `web.config`, `INSTALL-WINDOWS11-IIS.txt`, and `e_sqlite3.dll`; it contains neither Docker/development settings nor runtime database/bootstrap files.
- Linux artifact inventory contains `appsettings.Docker.json`, `INSTALL-DOCKER-LINUX.md`, and an ELF `libe_sqlite3.so`; it contains neither `web.config`, development settings, nor runtime database/bootstrap files.
- Final Windows and Linux `WebGallery.dll` SHA-256 values were `21A937A2DBEF2C29639BC6B47DA52A0F82F44DACE48EDD512833AAFD2549B5A4` and `FDBF820DEB1F206AB394D023DF444232E4748AB2F4367CF34AE5EF481A356CFB` respectively.
- Started the final Windows publish against an isolated temporary database/cache/key/content root with HTTPS redirection disabled for local HTTP. `GET /health` returned HTTP 200 with `{"status":"ok"}` and `GET /Account/Login` returned HTTP 200 with the login form; the process then shut down normally.
- `FileSystemVisibilityHarness` passed, including host-specific path casing and an available Windows symbolic-link escape regression check.
- `LoginAttemptLimiterHarness` and `ShareAuditHarness` passed.
- Both base Compose configuration and the Intel Quick Sync overlay parsed successfully with `docker compose ... config --quiet` using the example environment file.
- Both JSON settings files parsed successfully. `git diff --check` reported only expected checkout line-ending notices and no whitespace errors.
- A final attempt to republish both different RIDs sequentially with `--no-restore` produced `NETSDK1047` because the shared local assets file contained only the other RID. Re-running the documented profile commands with their normal per-RID restore passed for both outputs. The Dockerfile's `--no-restore` remains valid because its preceding restore explicitly targets the same `linux-x64` RID.

## User-visible result

- The project now has repeatable `Windows-IIS` and `Linux-Docker` publish commands and isolated outputs.
- Existing IIS paths, HTTPS behavior, resource limits, and `web.config` remain the Windows defaults.
- Linux Docker can persist state on NAS SSD storage, mount media from multiple drives read-only, run without root, use an optional Intel render device, and work safely behind a configured HTTPS reverse proxy.

## Remaining manual test / uncertainty

- No files were deployed to `C:\Web\imagegallery`, and the public IIS site was not changed or checked in this session.
- Docker Desktop's Linux engine was not running, and the registered WSL distribution could not start because this machine lacks the required WSL2/virtualization configuration. Therefore the Docker image was not built or run here; Linux validation is cross-RID publish, ELF/native-asset inspection, and Compose parsing rather than an actual Linux container runtime test.
- The target NAS still needs real host paths, directory ownership, proxy IP/CIDR, public hostname, and optionally `/dev/dri` render-group ID filled into its external environment file before first start.
- No browser/UI validation was performed because this change is hosting/runtime configuration rather than a presentation change, and the project's existing Codex in-app-browser crash workaround remains in effect.
