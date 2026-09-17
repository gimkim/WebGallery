# Video metadata badges

- Date: 2026-09-07 01:55:32 +07:00 (Asia/Bangkok)
- Request: Show small highlighted details on each video item, combining filename inference such as season, episode, resolution, and codec with metadata read from the actual video file.

## Concepts and rules established

- Render filename-derived badges in the initial HTML so listing a folder never waits for ffprobe.
- Inspect only video items near the viewport at low browser priority, with at most three requests active and off-screen work canceled.
- Verified ffprobe facts replace conflicting filename guesses and use a distinct verified accent. Failed inspection leaves the useful filename guesses in place.
- The metadata-badge endpoint uses the same private/share/file scope authorization as playback but does not count background inspection as a share View event.
- Fingerprinted successful responses are private immutable browser-cache entries and reuse the existing source-fingerprinted MediaService probe cache.

## Files changed

- `Services/VideoMetadataBadges.cs`
- `Controllers/GalleryController.cs`
- `Views/Gallery/Index.cshtml`
- `wwwroot/css/site.css`
- `wwwroot/js/site.js`
- `tests/VideoMetadataBadgesHarness/VideoMetadataBadgesHarness.csproj`
- `tests/VideoMetadataBadgesHarness/Program.cs`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`

## Validation performed

- `dotnet run --project tests/VideoMetadataBadgesHarness/VideoMetadataBadgesHarness.csproj -c Release`: passed filename parsing for the supplied `S01E01.1080p...AAC2.0.H.264.MSubs` style and verified metadata conflict replacement.
- Generated a temporary 1280x720 H.264/AAC fixture with the configured FFmpeg, then ran `MediaServiceHarness` against the configured ffprobe: metadata and one eight-second stream-copy H.264 segment passed. The fixture was moved out of the workspace afterward.
- `node --check wwwroot/js/site.js`: passed.
- `dotnet build WebGallery.csproj -c Release --no-restore`: passed with zero warnings and zero errors before profile publishing.
- Restored and published `Windows-IIS`: passed to `publish/windows-iis`.
- Restored and published `Linux-Docker`: passed to `publish/linux-docker`.
- Publish-content checks passed: Windows contains `web.config` but no Docker/development settings; Linux contains `appsettings.Docker.json` but no `web.config` or development settings.
- `git diff --check`: passed; only existing line-ending conversion warnings were reported.

## User-visible result

- Video cards and List rows immediately show compact inferred badges such as `Season 1`, `Episode 1`, `1080p`, `H.264`, `WEB-DL`, `AAC`, `2.0 audio`, and subtitle/language hints when present in the filename.
- As a video approaches the viewport, verified file metadata can replace quality/codec/audio facts and add duration or subtitle count without blocking the folder response.

## Remaining manual validation

- Real Grid/List visual behavior, long badge overflow, scrolling cancellation, and private/unlisted behavior were not browser-tested in this session because this project's Codex in-app-browser workaround remains active.
- Source publish outputs were produced, but neither the IIS deployment directory nor the live public/NAS application was updated in this session.
