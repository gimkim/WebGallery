# Viewer memory LRU and no thumbnail during cache check

- Date: 2026-08-09 15:47:00 +07:00 (Asia/Bangkok)

## Request and intent

- Remove the visible low-resolution thumbnail flash while checking whether a full image is cached.
- Make previous/next switching back to a recently completed full image display the full image immediately.

## Behavior established

- Full-image lookup order is now:
  1. page-lifetime full-image Blob/object-URL LRU;
  2. browser HTTP `only-if-cached` probe;
  3. only after a cache miss, thumbnail placeholder plus XHR progress.
- The thumbnail is not requested or displayed while the browser-cache probe is pending.
- Completed original Blobs remain reusable in a page-lifetime LRU instead of being revoked on every previous/next navigation or Viewer close.
- The LRU retains at most four recent originals and normally at most 256 MB of compressed Blob data. A single large current entry may exceed the byte cap; adding later entries evicts/revokes the oldest entries.
- Access refreshes LRU recency, so switching between recently viewed adjacent images uses their full object URLs directly without another browser-cache probe.
- All retained object URLs are revoked on LRU eviction, replacement, or page hide. Active XHR/cache probes still abort on navigation/close/page hide.

## Files changed

- `wwwroot/js/site.js`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Validation actually performed

- `node --check wwwroot/js/site.js`: passed.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- Static assertions confirmed:
  - the page-memory lookup appears before the browser-cache probe;
  - thumbnail loading appears only inside the cache-miss branch after the probe;
  - the four-entry LRU bound is present;
  - page-hide cleanup revokes the retained cache.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.
- Real authenticated previous/next visual timing was not exercised because this project's Codex In-app Browser workaround remains active.
- The source workspace is not a Git repository, so Git status and `git diff --check` are unavailable.

## Backup and deployment

- Backed up the prior deployed JavaScript and compressed variants under `backup/2026-08-09_154631_pre-viewer-memory-lru`.
- Used `C:\Web\imagegallery\app_offline.htm` only during the JavaScript copy window.
- Deployed `site.js` and its Brotli/Gzip variants to `C:\Web\imagegallery\wwwroot\js`.
- Removed `app_offline.htm` after deployment.
- Production binaries, SQLite data, settings, gallery files, users, shares, cache directories, CSS, and IIS configuration were not modified.

## Post-deployment validation

- Published and deployed hashes matched for all three JavaScript assets.
- Deployed `site.js` SHA-256: `ADCC01FC9F6C6142611BFAFA67EBC30EB6BE4C353F5A296EC0A49A5A2ACEA596`.
- Public Login returned HTTP 200.
- Live JavaScript returned HTTP 200 and contains the LRU, memory lookup, cache-miss-only thumbnail ordering, and page-hide cleanup.
- Confirmed `C:\Web\imagegallery\app_offline.htm` is absent.

## User-visible result

- Returning to one of the four most recently completed originals within the same page displays the full image directly.
- A browser-cache check no longer displays a thumbnail first; the thumbnail and progress appear only when the original is genuinely absent from the usable cache.

## Remaining manual validation

- Load several large images fully, then alternate previous/next and confirm no low-resolution frame appears for the four most recent images.
- Move through more than four large images and confirm older entries fall back through browser cache without showing progress on a hit.
- Check memory use with exceptionally large originals; adjust the four-entry/256-MB bounds if the target browser device needs a lower limit.
