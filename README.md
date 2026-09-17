# WebGallery

ASP.NET Core 10 gallery for filesystem-backed image and video collections. Administrators can assign zero or more named filesystem roots to each user; those roots appear as folders on that user's private home. SQLite stores users, roots, roles, revocable folder/file/collection share links, collections, audit logs, and system settings. Original files remain on disk; WebP thumbnails are generated into a separate cache. Single-image and single-video links open directly in the existing full viewer while remaining scoped to exactly that file. Video playback uses FFmpeg/ffprobe with direct H.264/HEVC streaming where supported and on-demand transcoding fallback.

## Hosting targets

- Windows 11 / IIS keeps the existing paths and `/Gallery` deployment model.
- Linux x64 / Docker uses Kestrel in the official .NET 10 Ubuntu image, installs FFmpeg in the image, and bind-mounts persistent data plus read-only media paths.
- The same source and database schema run on both targets. Existing Windows user-root paths must be edited to their Linux container paths after moving a database.

## First login

On a new database the application creates the `admin` account with a random password. On IIS, read it from `C:\Web\imagegallery-data\bootstrap-admin.txt`. In Docker, read `/data/database/bootstrap-admin.txt`. Sign in, change the password in Management, and then remove the credential file.

## Development

```powershell
dotnet restore
dotnet run
```

Development data stays under the ignored `App_Data` directory.

## Publish profiles

```powershell
dotnet publish WebGallery.csproj -p:PublishProfile=Windows-IIS
dotnet publish WebGallery.csproj -p:PublishProfile=Linux-Docker
```

The outputs are `publish\windows-iis` and `publish\linux-docker`. Both are framework-dependent .NET 10 x64 publishes. The IIS output contains `web.config` and the Windows guide but omits Docker/development settings. The Linux output contains `appsettings.Docker.json` and the Docker guide but omits `web.config` and development settings.

For Docker, use `Dockerfile`, `compose.yaml`, and `deployment/docker.env.example`; see `deployment/INSTALL-DOCKER-LINUX.md`. The default container paths are:

- SQLite: `/data/database/gallery.db`
- Data Protection keys: `/data/keys`
- Thumbnail cache: `/data/cache`
- Mounted gallery roots: `/gallery/...`
- FFmpeg / ffprobe: `/usr/bin/ffmpeg` and `/usr/bin/ffprobe`

The application exposes anonymous `GET /health` for container and reverse-proxy health checks.

## Windows / IIS production

The Windows publish expects the ASP.NET Core Hosting Bundle for .NET 10 on IIS. Preserve `C:\Web\imagegallery-data` across deployments because it holds the SQLite database, cookie-encryption keys, and thumbnail cache.

Video playback also requires `ffmpeg.exe` and `ffprobe.exe`. Production defaults to `C:\Web\imagegallery-tools`; paths can be changed with `Gallery:FfmpegPath` and `Gallery:FfprobePath` in `appsettings.json`.
