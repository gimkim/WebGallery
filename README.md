# Gim Gallery

ASP.NET Core 10 gallery for filesystem-backed image and video collections. SQLite stores users, roles, unlisted share links, collections, audit logs, and system settings. Original files remain on disk; WebP thumbnails are generated into a separate cache. Video playback uses FFmpeg/ffprobe with direct H.264/HEVC streaming where supported and on-demand transcoding fallback.

## Workspaces

- Source: `C:\Users\tatsa\source\webgallery`
- IIS deployment: `C:\Web\imagegallery`
- Application URL: `https://gimgim.ddns.net:45570/Gallery`
- Persistent production data: `C:\Web\imagegallery-data`
- Default gallery content root: `C:\Web\gallery-content`

## First login

On a new database the application creates the `admin` account with a random password. Read it from `C:\Web\imagegallery-data\bootstrap-admin.txt`, sign in, change the admin password from the Admin page, and then remove the credential file.

## Development

```powershell
dotnet restore
dotnet run
```

Development data stays under the ignored `App_Data` directory.

## Production publish

```powershell
dotnet publish -c Release -o publish
```

The published application expects the ASP.NET Core Hosting Bundle for .NET 10 on IIS. Preserve `C:\Web\imagegallery-data` across deployments because it holds the SQLite database and thumbnail cache.

Video playback also requires `ffmpeg.exe` and `ffprobe.exe`. Production defaults to `C:\Web\imagegallery-tools`; paths can be changed with `Gallery:FfmpegPath` and `Gallery:FfprobePath` in `appsettings.json`.
