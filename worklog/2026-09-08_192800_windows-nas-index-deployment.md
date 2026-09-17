# Windows NAS index integration and deployment

- Date: 2026-09-08 evening, Asia/Bangkok.
- Request: Deploy the index/background-thumbnail work. User corrected the target to Windows NAS 192.168.1.29, application `\\Gimkim-nas\c\Users\tatsa\web\imagegallery`, persistent `web-data`, existing media tools `web-tools`.
- Correction to 2026-09-08_191700_index-deployment-blocked.md: SSH failure was not a blocker for this Windows target. SMB access succeeded; no Linux deployment was attempted.

## Implementation

- Connected GalleryIndexService and additive SQLite schema initialization to the running application. Configured-folder lists/covers use indexed metadata; an uncatalogued folder scans once and normal visits request a background direct-child refresh. A separate hosted service discovers all roots incrementally, watches filesystem changes, coalesces requested folders, and reconciles every 24 hours (Management setting 1–168h).
- Retain last good data on incomplete/unavailable directory reads. Source endpoints still use live root/owner/share authorization. Indexed subtree deletion uses ordinal substring equality rather than SQLite case-insensitive LIKE. Exclude app cache, keys and database/WAL/SHM from indexing/watch-triggered work.
- Background workers consume batches of up to 64 pending indexed images, bounded by configured workers and the shared total generation limit. Revalidate physical path and fingerprint before work. Reuse cached WebPs, defer failing entries, and condition readiness updates on owner/path/length/mtime/settings so stale completion cannot mark newer content ready. On-demand completion also updates readiness. Zero workers remains disabled.
- Added Management index status, reconciliation interval, Rebuild index and Refresh folder index. Existing pages are not dynamically DOM-replaced; reopen/reload displays the newest index. Rebuild rechecks metadata while retaining thumbnail readiness; cache files manually removed regenerate on demand, not through an unconditional periodic cache sweep.
- Files: Program.cs, Data/DatabaseInitializer.cs, Data/GalleryDbContext.cs, Models/GalleryIndexEntry.cs, Services/GalleryIndexService.cs, FileSystemService.cs, BackgroundThumbnailService.cs, ThumbnailService.cs, Controllers/AdminController.cs, Views/Admin/Index.cshtml, tests/GalleryIndexHarness, deployment/Deploy-WindowsNas.ps1, deployment/maintenance.html, AGENTS.md, .agents/PROJECT_NOTES.md and this worklog. Preserved unrelated pre-existing dirty changes.

## Validation

- Release build passed with zero warnings/errors.
- GalleryIndexHarness passed real SQLite and filesystem checks: first scan, indexed cached read, direct refresh, filesystem watcher event, owner boundary, pending-only generation, changed-fingerprint race, descendant removal and unavailable-root snapshot preservation.
- Existing BackgroundThumbnailHarness, FileSystemVisibilityHarness and RootMigrationHarness passed.
- Windows-IIS publish passed. No Linux deployment or authenticated UI test performed.

## Deployment and recovery

- Added repeatable Windows NAS deployment helper: backup app, place temporary maintenance page, wait for database handle release, back up DB/WAL/SHM/key state, copy changed published files only, verify SHA-256, preserve appsettings.json/web.config, remove only owned maintenance marker. Does not modify web-tools or cache.
- First helper attempt encountered PowerShell compatibility/locked native-file handling; live health remained ok after maintenance cleanup. Adjusted relative-path calculation and skip unchanged files; retried using pwsh successfully.
- Successful backup: `\\Gimkim-nas\c\Users\tatsa\web-data\backups\20260908-192452-index-deploy` (app + stopped database/key snapshot).
- Deployed DLL SHA-256: A476CF2A4BFE3F5E0033DF02B8F8125773BE8B70704DE447CEAF16DD3DDDED1E. All copied publish files verified; original config hashes retained. No production DB replacement; startup added derived index tables.
- Verified TLS with gimgim.ddns.net Host/SNI pinned to 192.168.1.29:443: /gallery/health returned status ok; /gallery/Account/Login HTTP200. No app_offline.htm remains.
- Read-only SQLite inspection confirmed 3 users, 3 roots and 21 shares both before and after. New live index reached 25,129 entries and 1,170 folders at inspection, demonstrating live indexing rather than build-only evidence. Two folder rows were scheduled for unavailable-folder retry; not claimed resolved.
- BackgroundThumbnailWorkers is absent in existing settings, so effective default is 0. User can enable it in Management; no worker count was silently changed during deployment.

## Remaining boundaries

- Initial indexing continues as the application runs. Windows IIS idle/recycling rules still govern hosted service lifetime.
- No authenticated gallery/player/Management browser test or NAS performance benchmark was run. Automatically patching already-open pages while preserving scroll/selection is not implemented in this deployment.
- No Git push, local C:\Web deployment, Linux container update, source-media modification, cache deletion or tools replacement.

- Diagnostic cleanup: the read-only SMB SQLite inspector printed all results but lingered during exit; stopped only its identified local harness/dotnet processes (4908/35448). It performed SELECTs only. Rechecked NAS health afterwards: status ok. Prefer a server-local read or stopped snapshot for future database inspection.
