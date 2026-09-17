# UGREEN Gallery deployment

## Request / changes
- User requested https://gimgim.ddns.net/Gallery using /mnt/disks/CAMERA and explicitly approved migrating existing users/settings.
- Read project notes, AGENTS, latest benchmark worklog and worklog index. Preserved dirty source work and original Windows deployments.
- Added compose.ugreen.yaml: separate webgallery project/container, nonroot 1654:1654, render group105, /dev/dri, no host published port, external deploy_default network shared with existing proxy. Persistent /volume1/webgallery/{database,keys,cache}, media /mnt/disks/CAMERA mounted read-only at /gallery/CAMERA. Only app directories owned by UID1654.
- Dockerfile now installs Intel media driver and libmfx-gen1.2 for working oneVPL/QSV on NAS. No image thumbnail algorithm changes.
- FileListing workspace nginx.filelisting.conf adds /Gallery -> /Gallery/ redirect and scoped proxy, strips /Gallery before forwarding and sends trusted X-Forwarded-Prefix /Gallery. Only proxy172.18.0.254 and host gimgim.ddns.net accepted. Existing cookie name GimGallery.Auth differs from FileListing.Auth.

## Migration
- SQLite online snapshot from C:/Web/imagegallery-data/gallery.db (newer than available local Gallery backups), 3 users. Integrity check ok. Copied persistent key ring, settings, shares/collections/audit. Source database unchanged.
- On staged copy, root IDs1/2/3 map to /gallery/CAMERA and display CAMERA, retaining owner and root IDs. Legacy RootFolder camera/default values mapped accordingly. Root10 dashcam unchanged/unavailable because no corresponding NAS mount authorized; do not substitute CAMERA for that scope.
- Thumbnail cache not copied: rebuild on demand under /volume1/webgallery/cache. Migration archive owner-protected; database/keys owner-only. No credentials or databases added to source.

## Validation
- Real NAS Linux Docker build/start passed. image d92d64cadfd34cf8165bd6cb5b2a96993a8336a4af73c54d19c19b8935691b47.
- HTTPS /Gallery/health ok; /Gallery/ redirects to /Gallery/Account/Login with correct ReturnUrl; Login and CSS200; FileListing /api/health unchanged/ok. TLS validated without bypass.
- Deployed site.css/site.js SHA256 match workspace; container nonroot identity and read CAMERA/write cache checks passed; Docker healthy; 3 migrated users remain.
- QSV synthetic one-frame encoder test exit0. No authenticated browser, original-image/thumbnail visual QA, or end-to-end video seek testing claimed.

## Deployment / rollback
- Gallery sources and compose at /volume1/webgallery/deploy. Prior proxy configuration backed up /volume1/filelisting/deploy/nginx.before-gallery.conf. nginx -t passed then reload, no FileListing restart needed.
- Stop Gallery with compose down, preserving state. Restore proxy backup and nginx -t/reload to remove route. Existing Windows source data untouched. Do not reimport staged DB on future routine deploys.
- Remaining: user login/browser verification; configure dashcam separately if desired. Public URL now standard443 /Gallery/, not old Windows :45570.
