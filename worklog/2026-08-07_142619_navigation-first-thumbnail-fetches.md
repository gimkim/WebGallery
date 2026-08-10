# Navigation-first thumbnail fetches

- Date: 2026-08-07 14:26:19 +07:00 (Asia/Bangkok)

## Request and intent

- Prevent folder navigation and other page actions from waiting behind a large set of thumbnail requests while thumbnails are being generated.
- Treat thumbnail loading as background/low-priority activity compared with user-initiated navigation, forms, and downloads.

## Cause

- Server generation was already bounded to the administrator-selected concurrency, but every visible image immediately opened its own HTTP `fetch` and marked it high priority.
- Requests waiting inside the server thumbnail queue still occupied browser same-origin request capacity. On HTTP/1.1, enough waiting thumbnail requests could leave no connection available for the next navigation request.
- Off-screen cancellation alone did not guarantee immediate cancellation before the browser began a same-origin navigation.

## Durable concepts established

- Bound browser-side thumbnail HTTP concurrency separately from server-side ImageSharp generation concurrency.
- Allow at most four simultaneous thumbnail fetches and mark them `priority: low`, leaving request capacity for interactive traffic.
- Before an unmodified link action or form submission, synchronously remove queued thumbnail requests and abort active requests. Cancel without resumption on `pagehide`.
- If an action such as a download leaves the current document active, resume visible thumbnail scheduling after a short delay.
- The visible header remains appropriate inside the thumbnail-only server queue; it prioritizes on-screen thumbnails over other thumbnails, not over page/controller requests.

## Files changed

- `wwwroot/js/site.js`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Backup

- Before deployment, backed up deployed JavaScript to `backup/2026-08-07_142619_pre-navigation-first-thumbnails`.

## Validation actually performed

- Added a client dispatcher with a four-request active bound, lazy invalidation of no-longer-visible queued entries, low fetch priority, and pumping after completion/cancellation.
- Preserved visible-only IntersectionObserver behavior, server 503 retry, blob URL cleanup, pending/decode/loaded/error states, and off-screen AbortController cancellation.
- Changed the no-IntersectionObserver fallback to use the same bounded dispatcher instead of assigning every image URL at once.
- Added capture-phase cancellation for normal link clicks and form submissions plus non-resuming `pagehide` cancellation.
- `node --check wwwroot/js/site.js`: passed.
- A focused Chrome harness rendered eight visible deferred thumbnails with fetches intentionally held open. It measured `max=4`; clicking a navigation link aborted all four active requests and measured `active=0`, `aborted=4` before the test ended.
- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- `dotnet publish WebGallery.csproj -c Release -o publish-current`: passed.
- Deployed `wwwroot/js/site.js` to IIS and verified SHA-256 matched publish output (`42B7138248DE37F9DC217B2574E31E4686ED3A19F9A223A7D7048B4321739128`).
- Live JavaScript returned the four-request cap, low priority, cancellation function, and pagehide handler.
- Live `/Gallery/` returned HTTP 302 to the login page after deployment.
- The source directory is not a Git repository, so no Git status or `git diff --check` result is available.

## User-visible result

- Thumbnail traffic no longer opens an unbounded number of waiting HTTP requests.
- Clicking a folder, sort option, navigation link, download action, or submitting a form cancels thumbnail traffic first so the interactive request can start immediately.
- If the current page remains open after a download, visible thumbnails resume automatically rather than staying permanently paused.

## Remaining manual validation

- After refreshing the signed-in Gallery, open a folder containing many uncached large images and immediately enter another folder while placeholders are active. Confirm navigation begins without waiting for the thumbnail queue to drain.
- The focused browser harness validates client request bounding/cancellation, but an authenticated end-to-end timing reproduction against the user's exact folder was not run because the project does not use the unstable In-app Browser workflow.
