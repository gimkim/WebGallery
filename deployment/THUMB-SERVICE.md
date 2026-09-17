# Thumbnail service

## Current Management controls and shared cache

Management separately saves Remote thumbnail decoding (Full/Faster JPEG), Background thumbnail workers (remote, 0–16), and Concurrent thumbnail jobs (remote, 1–16). Background may not exceed concurrent. Local equivalents remain independent. Changes apply without restart. Remote background defaults on first upgrade to the existing background opt-in; afterward it has its own SQLite setting. Connected background scanning uses remote settings, unavailable scanning uses local settings. ThumbService's server-wide hard ceiling still applies.

Current cache behavior supersedes the older suffix description below: canonical contain-v1 / contain-idct-v1 renditions are shared between remote and local, with no endpoint/backend suffix. Both receive the same common Gallery:ThumbnailQuality encoder value; old ResizerServiceQuality is no longer an output-quality override. Requests retain their selected decoder through fallback so one key cannot mix Full/Fast. Local decoder settings choose quality when remote is disabled; the configured remote decoder chooses new requests when remote is enabled. Existing canonical thumbnails are reused; compatible old same-decoder remote aliases are copied without re-encoding or deleting old files. Ambiguous mixed-decoder old profiles are retained but not promoted. Changing workers, endpoint, availability, or the unused backend's decoder does not invalidate a rendition. The index may recheck disk readiness on a quality switch because it stores one latest signature, but cache hits bypass generation.

Publish separately: `dotnet publish ThumbService/ThumbService.csproj -c Release -o publish/thumb-service`.
Deploy its output to `C:\Users\tatsa\web\ThumbService` as an IIS application, with .NET 10 Hosting Bundle installed and Anonymous Authentication enabled (the application verifies its API key). The app-pool identity needs Read & Execute only; there is no thumbnail storage on the service. Preserve the deployed appsettings.json when updating binaries. The template deliberately has no key and will refuse startup until configured.

Service `ThumbService` settings:

- `ApiKey`: at least 32 cryptographically random characters. Keep only in deployed configuration; use the same key on Gallery. Never commit or paste it in chat.
- `Workers`: hard server-wide concurrent resize limit, 1–16, default 4. Excess work returns 429 before buffering.
- `Quality`: default WebP quality, 1–100, default 78; Gallery sends its independent remote quality explicitly.

Gallery `Gallery` settings in the deployed appsettings.json:

- `ResizerServiceUrl`: `https://192.168.1.30/ThumbService`; blank disables offload.
- `ResizerServiceHost`: blank for the current wildcard IIS binding. Optional Host override exists for named IIS sites.
- `ResizerServiceApiKey`: matching service key.
- `ResizerServiceCertificateSha256`: exact certificate SHA-256 for IP TLS when the certificate is issued to a DNS name. Certificate validity dates are still checked. Update the pin deliberately when renewing/replacing the certificate. Blank uses normal OS certificate validation; no global TLS bypass is used.
- `ResizerServiceQuality`: 78 by default, independent from local ThumbnailQuality.
- `ResizerServiceWorkers`: 4 by default; separate bounded priority queue from local ThumbnailConcurrency. Actual throughput also depends on service hard limit and background producer worker setting.
- `ResizerServiceRetrySeconds`: 30 by default, clamped 5–600.

Management provides Remote thumbnail decoding (Full decode / Faster JPEG reduced IDCT) and Remote workers with Save remote settings. These Gallery-side values persist in SQLite (ResizerServiceDecodeMode and ResizerServiceWorkers) and apply immediately without restart. Gallery:ResizerServiceDecodeMode defaults to full when there is no saved override. Decoder choice is independent from local decoding; local fallback retains local settings. WebP encoder quality remains a configuration/legacy setting and is not what the user means by remote quality. These controls do not edit ThumbService's server-wide hard limit. Each remote mode has its own cache suffix; old recognized mode URLs retain their mode. Worker changes retain active jobs and apply the new cap to subsequent starts.

Restart the affected application after other configuration edits. Unavailable state is process-local and starts unverified after restart. An authenticated health check determines availability; thumbnail requests never reconnect while unavailable. Management shows state and last checked time, with Retry connection now. Background checks run independently, including while no images are requested.

Requests use `X-Thumb-Key`; GET `/health` reports status/workers/quality/modes. POST `/resize?width=480&height=360&mode=full&quality=78` uploads a raw image with Content-Length; `mode=fast` uses JPEG reduced IDCT then Lanczos. Both auto-orient and retain complete aspect ratio. Limits: 128 MiB source, 120 MP image, output bounds 1–2048 each. Unsupported/invalid images return 422; oversized inputs return 413. Those and saturation fall back locally without declaring a connectivity outage. Other service/network failures disable remote attempts until the next health success.

Uploads and output are memory-only in the service. Gallery's existing persistent cache stores the response under an endpoint/quality-aware fingerprint. Existing local/remote cache files are not deleted on deployment; local fallback output is reused after reconnection. A remote/local race shares the same stripe and rechecks the cache before doing work. Unique temporary cache files are atomically moved only if the original length/mtime still match.

Current NAS application is `\\Gimkim-nas\c\Users\tatsa\web\imagegallery`. Preserve its database/cache/keys under web-data. HTTPS verification must retain NAS Host/SNI gimgim.ddns.net; the local ThumbService wildcard binding is independent of the NAS binding.
