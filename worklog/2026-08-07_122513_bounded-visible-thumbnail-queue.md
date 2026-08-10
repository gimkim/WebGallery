# Bounded visible-thumbnail queue, parent return focus, and contained thumbnails

- Date: 2026-08-07 12:25:13 +07:00 (Asia/Bangkok)

## Request and intent

- Implement an application-wide bounded thumbnail queue with at most administrator-configured `X` concurrent image generations.
- Prioritize thumbnails currently visible on screen and cancel unfinished work after the user scrolls those thumbnails out of the viewport.
- When navigating Back to a parent folder, return to and highlight the folder card that the user came from instead of starting at the top.
- Ensure regular image thumbnails in both Grid and List preserve the complete original aspect ratio without cropping.

## Durable concepts established

- Thumbnail cache hits bypass the generation queue. Cache misses enter a singleton dispatcher with separate Visible and Normal FIFO queues, Visible-first scheduling, a 256-job pending limit, and a dynamically adjustable active-job limit.
- `ThumbnailConcurrency` is stored in SQLite `AppSettings`, configurable from Admin from 1 through 16, defaults to 2, and updates the live dispatcher immediately. Lowering it does not kill already active work; it prevents new starts until active work falls below the new limit.
- ImageSharp receives the request cancellation token. Canceled queued jobs are pruned without execution, canceled active jobs stop during decode/resize/encode, and unique temporary WebP names are deleted on cancellation or failure.
- Thumbnail markup uses `data-thumbnail-src`. `IntersectionObserver` requests only visible thumbnails with `X-Thumbnail-Priority: visible`; `AbortController` aborts unfinished requests when they leave the viewport. Queue-full HTTP 503 responses retry only while still visible. Browsers without IntersectionObserver use direct image URLs.
- Parent Back URLs carry a normalized child `focus` path. The parent matches it against a rendered directory card, scrolls that card to the viewport center, and briefly highlights it. Invalid/unmatched values are ignored, and the mechanism applies to private and shared navigation.
- Regular file thumbnails explicitly use `object-fit: contain` and disable hover scaling in Grid and List. Folder-cover collage tiles retain their separate cover behavior.

## Files changed

- `Services/ThumbnailQueueSettings.cs` (new)
- `Services/ThumbnailWorkQueue.cs` (new)
- `Services/ThumbnailService.cs`
- `Models/GalleryOptions.cs`
- `ViewModels/GalleryViewModels.cs`
- `Controllers/GalleryController.cs`
- `Controllers/AdminController.cs`
- `Data/DatabaseInitializer.cs`
- `Program.cs`
- `appsettings.json`
- `appsettings.Development.json`
- `Views/Admin/Index.cshtml`
- `Views/Gallery/Index.cshtml`
- `wwwroot/js/site.js`
- `wwwroot/css/site.css`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Backup

- Before deployment, backed up the deployed DLL, appsettings, JavaScript, and CSS to `backup/2026-08-07_122513_pre-thumbnail-queue`.

## Validation actually performed

- `node --check wwwroot/js/site.js`: passed.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- A separate .NET 10 queue harness instantiated the real `ThumbnailWorkQueue` and passed all assertions:
  - configured concurrency 3 produced measured maximum active concurrency 3 across 12 jobs;
  - a queued Visible job ran before an earlier queued Normal job once the single active slot opened;
  - the 257th waiting job was rejected at the 256-job bound;
  - 256 canceled waiting jobs produced zero operation executions.
- `dotnet list WebGallery.csproj package --vulnerable --include-transitive`: no vulnerable packages reported from the configured NuGet sources.
- Runtime-source language scan still found no Thai application copy.
- Static checks confirmed deferred thumbnail markup, visible-priority header, off-screen abort, parent focus URL/scroll logic, and explicit contain/no-hover-scale rules for regular thumbnails.
- Clean publish completed to `publish-current/` without the backup directory.
- Deployed while `C:\Web\imagegallery\app_offline.htm` was present. SHA-256 hashes matched for `WebGallery.dll`, `appsettings.json`, `wwwroot/js/site.js`, and `wwwroot/css/site.css`; the offline file was removed.
- Live `/Gallery/` returned the expected 302 login redirect; live JavaScript and CSS returned HTTP 200 and contained the new loader, cancellation, focus, and contain rules.
- An active anonymous share page returned HTTP 200 with deferred thumbnail attributes, no native `loading="lazy"` thumbnail URLs, English application copy, and the existing viewer-navigation markup.
- Production SQLite read-only verification returned `ThumbnailConcurrency=2` and retained `AppTitle=Gallery` after application startup initialized the new setting.
- The source directory is not a Git repository, so no Git status or `git diff --check` result is available.

## User-visible result

- Thumbnail generation can no longer exceed the Admin-selected number of simultaneous CPU-heavy jobs, and the waiting queue is bounded.
- Only on-screen thumbnails request work in supported browsers; scrolling away cancels unfinished requests and server generation where possible.
- Admin can change concurrent thumbnail jobs between 1 and 16 without restarting the application.
- Back navigation returns to the child folder card in its parent listing.
- Grid and List file thumbnails show the entire image at its original ratio without crop or hover zoom.

## Remaining manual validation

- Interactive browser QA was not run because the installed Codex build has twice exited during In-app Browser teardown. After signing in, manually verify rapid scrolling on a folder containing uncached large images while watching CPU/network cancellation, changing concurrency from Admin, Grid/List full-ratio thumbnails, and Back returning to the expected folder card in private and shared views.
- Cached thumbnails finish immediately and therefore may complete before a browser abort can reach the server; cancellation is intended for pending or active cache-miss work.
