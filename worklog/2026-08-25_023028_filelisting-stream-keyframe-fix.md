# FileListing stream-copy keyframe fix

## Date and time

- 2026-08-25 02:30:28 UTC+07:00 (Asia/Bangkok)

## Request / intent

- Inspect the streaming-logic update in `C:\Users\tatsa\Documents\FileListing` and apply the relevant change to WebGallery.
- Preserve WebGallery's private/share authorization boundary and existing player behavior while synchronizing the common MediaService logic.

## Concepts and rules established

- FileListing commit `aa1034f` changed only server-side `MediaService` keyframe seek/cut precision; its player JavaScript did not require a corresponding port.
- H.264/HEVC stream-copy response cursors remain on keyframe PTS for the presentation timeline, but FFmpeg input seek, residual output seek, and duration calculations use keyframe DTS.
- Preserve ffprobe timestamps to six decimal places. Millisecond rounding can seek past a random-access packet.
- Start residual output seek 1 ms before the selected DTS and retain the 1 ms end guard before the next DTS so adjacent independent fragments neither omit nor duplicate their boundary video packet.

## Files changed

- `Services/MediaService.cs`
- `tests/MediaServiceHarness/Program.cs`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog.

## Validation actually performed

- Compared current FileListing `MediaService.cs` at commit `aa1034f` with WebGallery. After the port, no timing-related difference remained for the start guard, DTS input selection, residual seek, or six-decimal timestamp formatting; remaining differences are application namespace/options/exception/text adaptations.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- `dotnet build tests\MediaServiceHarness\MediaServiceHarness.csproj -c Release`: passed with 0 warnings and 0 errors.
- Extended MediaServiceHarness to accept a starting time and segment count, verify cursor progress, and feed every generated fragment to ffprobe to confirm it contains a video stream.
- The first pipe-based ffprobe run exposed the expected early-stdin-close behavior after ffprobe had identified the stream table. The harness now tolerates that pipe closure but still requires ffprobe exit code 0 and an explicit video stream result.
- Star Detective Precure episode 01 smoke from 60 seconds generated three `copy-h264` fragments spanning source starts `52.135417`, `72.989583`, and `83.416667`; all three contained video and advanced to `93.84375`. This crosses the FileListing regression boundary that previously produced an audio-only fragment.
- Minions and Monsters (H.264 with B-frames and E-AC-3 audio) generated three `copy-h264` fragments from 0 through `30.739`; all contained video and advanced their cursors.
- FileSystemVisibilityHarness, LoginAttemptLimiterHarness, and ShareAuditHarness passed. BrowserCacheProbeHarness was started but is an interactive cache-only server rather than a self-completing test; it was stopped and is not claimed as passed.
- `git diff --check`: passed apart from expected checkout line-ending notices.
- `dotnet publish WebGallery.csproj -c Release -o publish\2026-08-25_023028`: passed.
- With `app_offline.htm` active, backed up 149 preceding IIS files and production SQLite under `backup\2026-08-25_023028_pre-filelisting-stream-fix`; backup `PRAGMA integrity_check = ok`.
- Deployed the Release output to `C:\Web\imagegallery`, excluding `appsettings.Development.json`, then removed the maintenance marker.
- Publish/deploy SHA-256 matched for `WebGallery.dll`, `WebGallery.pdb`, and `video-player.js`. Deployed DLL SHA-256 is `83BD44047B6A6980FAC63FA72D05B01255CD6E662F2B90888D2C8EE85F2A1C88`.
- Confirmed external FFmpeg/ffprobe remain present. Production SQLite remained `integrity_check = ok`; the deployment contains neither `app_offline.htm` nor `appsettings.Development.json`.
- Live Login returned HTTP 200 and Management returned the expected anonymous HTTP 302 redirect.

## User-visible result

- Direct H.264 streaming no longer risks freezing/repeating because an independently appended segment lost its first video packet or reused the next segment's first packet at a keyframe boundary.
- B-frame sources now seek from decode-timeline timestamps without changing the player's presentation-timeline cursor behavior.

## Remaining manual test / uncertainty

- Actual authenticated playback through the deployed Gallery UI was not performed. The regression test exercised the exact WebGallery MediaService build and production FFmpeg/ffprobe against the same two real source classes used for the FileListing fix, while live checks verified deployment delivery and application availability.
