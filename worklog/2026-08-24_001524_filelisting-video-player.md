# FileListing video player integration

## Request / intent

- Inspect `C:\Users\tatsa\Documents\FileListing` and bring its video-player behavior into WebGallery so video files can be opened in Full View.
- Keep WebGallery's private-user and scoped unlisted-share access rules, deployment separation, durable notes, and append-only worklog requirements.

## Implementation

- Added video-file classification and a Videos filter/count to Gallery Grid/List rendering.
- Video recognition mirrors FileListing's common container set, including MP4/MKV/MOV/AVI/WebM/TS/M2TS and older 3GP/FLV/OGV/RM/VOB/ASF/DivX/OGM containers.
- Added a full-window video dialog with direct download, audio-track selection, text-subtitle selection/size, keyboard controls, preparation progress, and responsive Retro/Modern styling.
- Ported FileListing's MSE client pipeline, including segmented seek/buffering, cancellation of stale work, HEVC capability fallback, SourceBuffer quota recovery, track preference persistence, and subtitle preparation progress.
- Ported FileListing's `MediaService`: ffprobe metadata, H.264/AAC copy where browser-compatible, continuous HEVC support, and on-demand NVENC -> Intel Quick Sync -> libx264 fallback. The service keeps bounded media/QSV job slots and returns fragments from memory without temporary renditions.
- Added private/share-scoped MVC endpoints for metadata, subtitles/progress, fragmented segments, normal streams, and continuous HEVC. A successful shared video metadata request records a View audit event.
- Added external `Gallery:FfmpegPath` and `Gallery:FfprobePath` settings. Production defaults to `C:\Web\imagegallery-tools`; binaries remain outside Git and the application publish tree.
- Updated `AGENTS.md`, `.agents/PROJECT_NOTES.md`, README, and Windows/IIS installation instructions.

## Files changed

- `Controllers/GalleryController.cs`
- `Models/GalleryOptions.cs`
- `Program.cs`
- `Services/FileSystemService.cs`
- `Services/MediaModels.cs`
- `Services/MediaService.cs`
- `ViewModels/GalleryViewModels.cs`
- `Views/Gallery/Index.cshtml`
- `Views/Shared/_Layout.cshtml`
- `wwwroot/js/video-player.js`
- `wwwroot/css/site.css`
- `wwwroot/css/site-modern.css`
- `appsettings.json`
- `README.md`
- `deployment/INSTALL-WINDOWS11-IIS.txt`
- `tests/MediaServiceHarness/*`
- Agent notes and this worklog.

## Validation actually performed

- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- `node --check wwwroot/js/video-player.js`: passed.
- `git diff --check`: passed; only expected LF-to-CRLF checkout warnings were printed.
- Existing FileSystem visibility, LoginAttemptLimiter, and ShareAudit harnesses all passed.
- `MediaServiceHarness` ran against a local 19 MB H.264 MP4 fixture using the FileListing FFmpeg 9.0 full build: ffprobe returned H.264 and 17.042 seconds; the player generated a 2-second `copy-h264` fragment of 2,264,200 bytes with next start 2 seconds.
- `dotnet publish WebGallery.csproj -c Release -o publish\2026-08-24_001337`: passed.
- Backed up the previous IIS application plus production SQLite files to `backup\2026-08-24_001337_pre-video-player` while `app_offline.htm` was active.
- Copied FFmpeg 9.0 full-build `ffmpeg.exe`/`ffprobe.exe` to `C:\Web\imagegallery-tools` and granted inherited `IIS_IUSRS` Read & Execute.
- Deployed the publish output to `C:\Web\imagegallery`, excluding `appsettings.Development.json`.
- Final publish/deploy SHA-256 matched for `WebGallery.dll` (`81206A98FCA4B92C561EBC7C45F5A1714BCF086A67BDC82A721F44BB98AC21D4`), `video-player.js`, Retro CSS, and Modern CSS.
- Live `https://gimgim.ddns.net:45570/Gallery/Account/Login` returned HTTP 200 and referenced the versioned video-player script.
- Live `/Gallery/js/video-player.js` returned HTTP 200 (78,207 bytes) and contained the MSE pipeline.
- Confirmed the IIS directory contains neither `app_offline.htm` nor `appsettings.Development.json` after deployment.
- Public-repository safety check found no tracked database/bootstrap/development-settings/key/publish/backup/ZIP/FFmpeg files and no private-key, GitHub-token, API-key, or client-secret pattern in the source candidate. Implementation commit `4f3ef03` was pushed to `origin/main` and the remote ref matched locally.

## User-visible result

- Video cards now have a clear Play control and can open a Full View player without downloading the original first.
- Compatible video is copied directly; unsupported codecs are transcoded only for the current buffer/seek range using FileListing's acceleration/fallback logic.
- Video playback is available under both private galleries and correctly scoped unlisted folder/collection shares.

## Remaining manual test / uncertainty

- Interactive playback UI was not tested in a browser because this project's Codex in-app-browser crash workaround remains active. Manually verify one H.264 file, one HEVC/non-H.264 fallback file, seeking, audio/subtitle switching, mobile controls, and a valid unlisted-share video.
- The harness verified the direct-copy branch. Hardware encoder availability and software fallback retain FileListing's tested logic but were not forced on this source file during this session.
