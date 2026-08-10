# Folder-cover browser-cache bypass

- Date: 2026-08-07 15:52:00 +07:00 (Asia/Bangkok)

## Request and intent

- Fix folder-cover tiles that still appeared progressively through the thumbnail queue even when the browser had already cached them.
- Preserve bounded/cancellable network and server generation behavior for genuine cache misses.

## Cause

- A regular file card has one thumbnail, while a folder cover can contain four independent thumbnail URLs.
- All visible URLs, including browser-cache hits, entered the same 12-slot dispatcher. A page with many folders therefore serialized cached cover tiles in visible batches even though no server generation was needed.

## Durable concepts established

- Each visible folder-cover image gets one parallel HTTP-cache-only probe before entering the dispatcher.
- The probe uses same-origin `fetch` with `cache: only-if-cached`, which is forbidden from contacting the network.
- Successful cache hits decode/assign immediately and consume no dispatcher slot.
- HTTP-cache misses (normally a synthetic 504), unsupported browser behavior, and probe errors fall through once to the normal 12-request cancellable dispatcher.
- Only actual network requests remain subject to navigation abort and the server's bounded thumbnail generation queue.

## Files changed

- `wwwroot/js/site.js`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Backup and deployment

- Backed up the previously deployed JavaScript under `backup/2026-08-07_155140_pre-folder-cache-probe`.
- Published Release output to `publish-current`.
- Deployed the published JavaScript to `C:\Web\imagegallery` without restarting the application because only a static asset changed.

## Validation actually performed

- `node --check wwwroot/js/site.js`: passed.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- A focused headless-Chrome harness rendered 30 visible folder-cover images with a controlled Fetch implementation: 10 cache hits and 20 cache misses.
- All 30 cache-only probes started; all 10 hits decoded and reached `thumbnail-loaded` without entering the network dispatcher.
- The 20 misses entered the bounded dispatcher with measured `networkMax=12`.
- Clicking a navigation link aborted all 12 active network requests, producing `active=0` and `aborted=12`.
- The temporary harness file was removed after testing.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.
- Published, deployed, and live JavaScript SHA-256 matched: `4EBE1470585B6F805B5ED125B9B0AFFBDAFD506F57188291C401C6B53F4A1537`.
- Live JavaScript contained the folder cache probe, `only-if-cached`, and one-attempt state handling.
- Live `/Gallery/` returned HTTP 302 to login for an anonymous request after deployment.
- The source directory is not a Git repository, so no Git status or `git diff --check` result is available.

## User-visible result

- Previously downloaded folder-cover tiles can appear together directly from browser cache instead of waiting behind the 12-slot thumbnail queue.
- Tiles not in browser cache continue to load progressively with the existing CPU/network protection.
- Folder navigation remains responsive because real network thumbnail requests are still aborted before navigation.

## Remaining manual validation

- Refresh a large folder page once, wait for all visible covers, enter a child folder, then return. Confirm cached four-tile folder covers appear together rather than in queue order.
- The real deployed JavaScript and controlled Chrome cache-hit/miss behavior were verified, but the authenticated live browser's existing disk-cache contents were not inspected directly.
