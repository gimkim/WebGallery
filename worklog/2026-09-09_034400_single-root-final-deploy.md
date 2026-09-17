# Final single-root/file-management deployment

2026-09-09, Asia/Bangkok. Completion of 2026-09-09_034000_single-root-uploads-moves-passwords.md.

- Final additional changes: reserved Windows device basenames also reject spaces before an extension (CON .txt); shared root ZIP/breadcrumb display uses the root's display name, not its internal marker; active upload rows remain visible even beyond the first100 history entries.
- UploadSafetyHarness passed after the reserved-name change. Both publish profiles passed after shared-name changes. The final active-row-only JavaScript adjustment passed node syntax validation and was copied into both publish trees before deploying.
- Windows NAS application backup/copy/hash verification completed at web-data/backups/20260909-034155-index-deploy, then final static-row adjustment at web-data/backups/20260909-034300-index-deploy. Configuration/persistent state preserved. LAN-pinned HTTPS /gallery/health returned {status:ok} after each deployment.
- Source, both publish trees and Windows NAS now include the final file-operations.js. No Linux runtime deployment, ThumbService changes, Git push or broad ACL changes.
- Main worklog records all local API/browser tests and NAS root/share/collection preservation checks. Actual production-media writes, multi-gigabyte runs and Linux runtime validation remain unperformed. The final active-row adjustment is syntax/deploy verified, not a new full browser test.
