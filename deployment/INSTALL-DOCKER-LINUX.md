# WebGallery on Linux Docker

## File management permissions

Each user now has one root and opens it directly. Existing root IDs, share links and collections are preserved.
To enable create-folder, upload and move, set `WEBGALLERY_MEDIA_READ_ONLY=false` in the compose environment and grant the container UID write/delete access only to the intended media folders. The application filesystem remains read-only. For the UGREEN-specific compose file, change only the intended media mount from `:ro` to `:rw`. This is not required for browsing/downloads. Existing deployments are not made writable automatically.

Uploads use 4 MiB segments with offset-based retries, two active browser transfers, and private cache staging named `webgallery-upload-<UUID>.wg-upload-segment`. A publication file `.webgallery-upload-<UUID>.pending` is hidden from gallery/index/ZIP access until the final rename. Expired staging and validated publication journals are cleaned after 24 hours (checked every five minutes). Ordinary `.part` files are not ignored. Keep sufficient free space for staging plus the final copy. Network retries survive SPA navigation in the same tab, not browser reload or application restart; abandoned files are cleaned safely. No automatic overwrite is performed.

New accounts default to changing their password on first login. Existing accounts retain their current flag; Management can enable it. One-time reset links expire after one hour. Preserve Data Protection keys and send reset links privately.

This deployment runs the same .NET 10 application as Windows/IIS. The container image is Linux x64, installs FFmpeg, runs as the unprivileged `app` user, and keeps all writable state outside the container.

## 1. Prepare persistent folders

Put the database, Data Protection keys, and thumbnail cache on persistent NAS storage. SSD-backed storage is recommended for these paths; original media can remain on HDD volumes.

```sh
mkdir -p /volume1/docker/webgallery/database
mkdir -p /volume1/docker/webgallery/keys
mkdir -p /volume1/docker/webgallery/cache
chown -R 1654:1654 /volume1/docker/webgallery
```

The official .NET image currently uses UID/GID 1654 for the `app` user. Confirm it for the image before deployment if your platform remaps container users. Back up `database` and `keys`; `cache` is disposable and can be regenerated.

## 2. Configure bind mounts

Copy `deployment/docker.env.example` to a persistent location such as `/volume1/docker/webgallery/webgallery.env`, then edit it. Every host media path is mounted read-only at a unique container path. Add or remove media mounts in `compose.yaml` as needed:

```yaml
- type: bind
  source: /volume3/archive
  target: /gallery/archive
  read_only: true
```

After first sign-in, add `/gallery/media1`, `/gallery/media2`, and any additional mount targets as separate user roots in Management. If an existing Windows database is moved to Linux, edit each existing root to its new `/gallery/...` path instead of deleting and recreating it; this preserves root IDs, shares, and collection memberships.

## 3. Build and start

Run from the source directory:

```sh
docker compose --env-file /volume1/docker/webgallery/webgallery.env build
docker compose --env-file /volume1/docker/webgallery/webgallery.env up -d
docker compose --env-file /volume1/docker/webgallery/webgallery.env ps
curl http://127.0.0.1:8080/health
```

For Intel Quick Sync, determine the render-device group ID and add the hardware overlay:

```sh
stat -c '%g' /dev/dri/renderD128
docker compose --env-file /volume1/docker/webgallery/webgallery.env \
  -f compose.yaml -f compose.intel-qsv.yaml up -d
```

If the device or driver is unavailable, WebGallery automatically falls back to software transcoding. The Docker defaults limit total media jobs to 4, Quick Sync jobs to 2, and software encoder threads to 4; override `Gallery__MaxConcurrentMediaJobs`, `Gallery__MaxConcurrentQuickSyncJobs`, or `Gallery__MediaEncoderThreads` only after measuring the host.

## 4. Reverse proxy and HTTPS

The base compose file binds Kestrel to `127.0.0.1:8080`. Put the NAS, Nginx, Caddy, or Cloudflare reverse proxy in front of it for public HTTPS. The proxy must replace, not append untrusted client values for `X-Forwarded-For`, `X-Forwarded-Proto`, and `X-Forwarded-Host`.

Set these values in the environment file:

```text
WEBGALLERY_PROXY_ENABLED=true
WEBGALLERY_PROXY_IP=172.18.0.2
WEBGALLERY_PUBLIC_HOST=gallery.example.com
WEBGALLERY_HTTPS_REDIRECT=true
```

Use either the exact proxy IP or its smallest stable CIDR in `WEBGALLERY_PROXY_NETWORK`; do not trust every network. `WEBGALLERY_PUBLIC_HOST` must not include a port. Forwarded headers are rejected unless the direct peer matches a configured trusted proxy/network, so generated share URLs and audit IPs cannot be spoofed by arbitrary requests.

For a sub-path proxy such as `/Gallery`, have the trusted proxy send `X-Forwarded-Prefix: /Gallery`. A dedicated host such as `gallery.example.com` is simpler and recommended.

## 5. First login and updates

On a new database, read the generated password from:

```text
/volume1/docker/webgallery/database/bootstrap-admin.txt
```

Sign in as `admin`, change the password in Management, then delete that file. To update, back up `database` and `keys`, build the new image, and recreate the container. Never remove the persistent bind mounts during an update.

The health endpoint is `/health`. View logs with `docker logs webgallery`.
