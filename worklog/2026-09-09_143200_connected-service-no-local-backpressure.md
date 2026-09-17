# Do not decode locally for connected remote backpressure

2026-09-09 Asia/Bangkok.

- Request: NAS CPU tracks local background-worker count even while remote connected; investigate/fix local image processing.
- Found code path: remote queue-full and HTTP429/413/422 returnedfalse without setting offline, and ThumbnailService fell through to local decoding. This proves unintended fallback is possible; production CPU profiling was not performed.
- Changes: RemoteResizer retries queue-full/429 remotely with cancellable1-second delay;413/422 raise explicit rejection without offline state or local decode. ThumbnailService rejects unpublished remote output while still online, and queued local jobs recheck recovery before decode and reroute remote. Gallery endpoint maps remote image rejection to422. Updated durable notes and MediumThumbnailHarness.
- Validation: harness verifies background and visible429 remain pending remotely with no local cache output,413/422 create no cache and preserve connected state; offline fallback, remote preemption/dedup/temp cleanup continue passing. Priority/pipeline harness passes. Both Release publish profiles pass.
- Deploy: Windows NAS backup/copy/hash verified at web-data/backups/20260909-143139-index-deploy, persistent configs/state untouched; LAN-pinned HTTPS health ok. No ThumbService deployment or worker setting changed.
- Follow-up question: inspected deployed ThumbService configuration; Workers=4. Source uses semaphore with immediate429 on saturation, no waiting queue. Runtime authenticated health checked separately in session. Gallery concurrency settings do not alter service capacity. No production credential values printed.
