# Deploy video metadata badges to Windows NAS

- Date: 2026-09-07 02:07:38 +07:00 (Asia/Bangkok)
- Request: Deploy the current WebGallery source, including video metadata badges, to the NAS.

## Concepts and rules established

- The NAS HTTPS binding must be verified with Host/SNI `gimgim.ddns.net`; a LAN-direct check can pin that hostname to `192.168.1.29`.
- Direct HTTPS requests to `gimkim-nas` reach a different HTTP.sys listener and return 404, so they are not a valid WebGallery health check.
- Routine NAS application deployments preserve NAS `appsettings.json`, `web.config`, database, Data Protection keys, thumbnail cache, and media files.

## Files changed

- NAS application binaries and static assets under `\\Gimkim-nas\c\Users\tatsa\web\imagegallery`, copied from the fresh `Windows-IIS` publish except preserved `appsettings.json` and `web.config`.
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog.

## Deployment and validation performed

- Confirmed the exact SMB application root, external `web-data\gallery.db`, and key directory were accessible before deployment.
- Restored `win-x64` and published the current source with the `Windows-IIS` profile successfully.
- Backed up the previous 31-file NAS application to `\\Gimkim-nas\c\Users\tatsa\web-data\backups\20260907-020513-video-badges`.
- Placed `app_offline.htm`, copied the publish output with retry handling, and then moved the offline marker into the backup. The application root no longer contains `app_offline.htm`.
- Preserved NAS `appsettings.json` and `web.config`; their pre/post SHA-256 values matched.
- Compared all 30 publish files except the two preserved settings files against the NAS by relative path and SHA-256: zero missing files and zero mismatches.
- Deployed SHA-256: `WebGallery.dll` `35DAD4E2342C9CF105CD60EEF0234C3969BA1AC2DE2C50C0474AB29807795890`; `site.js` `598384C1DA1ED00D91AA4FA335DFC5CB42C67BD6C4F39F7E851A316D12580224`; `site.css` `9086123D66558AFFE88E23EDFAD1427B03A12F32EDFB7E9BFA430BDFA9842549`.
- With `gimgim.ddns.net:443` pinned to NAS `192.168.1.29`, `/gallery/health` returned HTTP 200 and `{"status":"ok"}`; `/gallery/Account/Login` returned HTTP 200.
- Downloaded live `/gallery/js/site.js` and `/gallery/css/site.css`; both hashes matched the publish output and contained the new video badge queue/style.

## User-visible result

- The Windows NAS is serving the current build with video filename/ffprobe metadata badges available in Gallery Grid and List views.

## Remaining manual validation

- No authenticated browser session was used to visually inspect real NAS video rows or trigger a verified ffprobe badge update in this session.
