# Thumbnail browser cache and folder-grid throughput

- Date: 2026-08-07 15:41:06 +07:00 (Asia/Bangkok)

## Request and intent

- Fix large folder grids whose folder-cover thumbnails appeared slowly in a four-request sequence.
- Ensure returning from a child folder reuses already downloaded thumbnails instead of visibly loading them again.

## Cause

- The browser thumbnail dispatcher was capped at four requests even for WebPs already present in the server disk cache.
- Thumbnail responses did not explicitly allow durable private browser caching.
- Thumbnail URLs included the thumbnail algorithm version but not the source file's modification/size identity, so a long immutable browser lifetime would previously have risked stale images.

## Durable concepts established

- Visible thumbnail HTTP concurrency is 12. Server ImageSharp generation remains independently bounded by the Admin `ThumbnailConcurrency` setting, so this does not raise the CPU generation limit.
- All active thumbnail fetches are still synchronously aborted before navigation or form submission, preserving navigation responsiveness despite the higher read concurrency.
- Every regular thumbnail and folder-cover tile URL includes the algorithm version plus a source fingerprint made from UTC modification ticks and source length.
- Fingerprinted responses use `Cache-Control: private, max-age=31536000, immutable`. Legacy requests without a fingerprint use `private, no-cache`.
- A source change or thumbnail-algorithm version bump creates a new URL, while Back/repeat visits can reuse unchanged WebPs directly from the browser cache.

## Files changed

- `ViewModels/GalleryViewModels.cs`
- `Services/FileSystemService.cs`
- `Views/Gallery/Index.cshtml`
- `Controllers/GalleryController.cs`
- `wwwroot/js/site.js`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Backup and deployment

- Backed up the previously deployed DLL, PDB, and JavaScript under `backup/2026-08-07_154015_pre-thumbnail-browser-cache`.
- Published Release output to `publish-current`.
- Used `app_offline.htm` briefly while replacing DLL/PDB, removed it, and deployed the published JavaScript.

## Validation actually performed

- `node --check wwwroot/js/site.js`: passed.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- A focused headless-Chrome dispatcher harness presented 30 thumbnails with deliberately unresolved fetches. It measured `max=12`; clicking a navigation link aborted all 12 requests and measured `active=0`, `aborted=12`.
- The temporary harness file was removed after testing.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.
- An authenticated request to the deployed Gallery returned HTTP 200 and exposed a real `data-thumbnail-src` containing `stamp=`.
- Requesting that real deployed thumbnail returned HTTP 200 with `Cache-Control: private, max-age=31536000, immutable`.
- Published/deployed DLL SHA-256 matched: `BBD557194D92ECA3F437E5B7567EBC56825FF908B6F2B8709AF03FC5A7EF9530`.
- Published/deployed/live JavaScript SHA-256 matched: `1DFA180D90953A7633992325607387E19C92FDCFBBF380AA0189DD0348C4AF60`.
- Live `/Gallery/` returned HTTP 302 to login for an anonymous request after deployment.
- The source directory is not a Git repository, so no Git status or `git diff --check` result is available.

## User-visible result

- Large visible folder-cover grids can request up to 12 tiles concurrently instead of revealing cached tiles in batches of four.
- Once a fingerprinted thumbnail has been downloaded, returning to the folder or revisiting it can use the browser's immutable private cache without revalidation.
- New or modified source images still receive new thumbnail URLs and cannot be confused with the old browser entry.
- First-time thumbnail generation speed remains limited by the administrator-selected ImageSharp concurrency to protect CPU; the change accelerates cached delivery without bypassing that safety limit.

## Remaining manual validation

- Refresh the authenticated large-folder page once so it receives fingerprinted URLs, wait for visible covers to finish, enter a child folder, and press Back. Confirm the covers reappear immediately from browser cache.
- On a first visit with genuinely uncached source images, generation may still appear progressively according to Admin `ThumbnailConcurrency`; increase that setting only if the host has CPU capacity.
