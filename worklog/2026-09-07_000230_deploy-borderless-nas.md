# Deploy unified borderless UI to NAS

- Date: 2026-09-07 00:02 Asia/Bangkok.
- Request: deploy the redesigned gallery to NAS.
- Published Windows-IIS Release into a fresh publish/windows-iis-borderless directory successfully.
- Backed up the existing NAS application to `\\Gimkim-nas\c\Users\tatsa\web-data\backups\20260907-000130-borderless`.
- Used app_offline.htm during deployment to `\\Gimkim-nas\c\Users\tatsa\web\imagegallery`. Copy retried the briefly locked DLL successfully. Removed the obsolete site-modern.css (recoverable from backup) and offline marker afterward.
- Preserved appsettings.json and web.config; before/after SHA-256 hashes identical. No production databases, keys or cache copied or replaced.
- Verified deployed DLL, static asset manifest, CSS and JS hashes match publish. Live /gallery/health returned 200 and status ok; /gallery returned the login page with photo-gallery body and versioned unified stylesheet. Public /gallery/css/site.css returned 200 and matched published SHA-256. An initial probe at /css/site.css returned 404 because the application is mounted at /gallery; verified the actual stylesheet path rendered by the page instead.
- No authenticated live Gallery browser validation in this deployment session; prior source session has isolated rendering fixture checks, not production UI proof.
