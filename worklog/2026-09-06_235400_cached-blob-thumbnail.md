# Preview while cached originals decode

- Request: thumbnails still absent while waiting for full images; fix and redeploy NAS.
- Found live JavaScript matched prior deployment but memory Blob cache hits returned before starting the thumbnail. Prefetched large originals can still require slow decode despite being cached.
- Moved thumbnail start before the memory cache branch in wwwroot/js/site.js. Added tests/viewer-placeholder.cjs exercising the actual extracted showViewerImage function with deferred decode for memory Blob, HTTP cache and network paths. All three confirm thumbnail source is visible until full image readiness, then replaced.
- Node regression passed, syntax check and Windows-IIS publish passed. NAS backup at web-data/backups/20260906-235318-viewer-blob-preview. Deployed with temporary app_offline.htm, preserving database and config; removed offline marker.
- Live health ok, public JS contains fix and deployed/source hashes match. No authenticated real-browser visual reproduction; regression is a mocked asynchronous state test, not rendering evidence.
