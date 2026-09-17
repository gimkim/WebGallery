# Immediate index reconciliation after thumbnail failure

2026-09-09 07:08 Asia/Bangkok.

- Request: hour-long retry is excessive; failed thumbnails must check source existence and update index immediately.
- Changes: Services/GalleryIndexService.cs, Services/BackgroundThumbnailService.cs, tests/GalleryIndexHarness/Program.cs, AGENTS.md, .agents/PROJECT_NOTES.md. Old fingerprint deferred first; synchronous forced parent refresh removes confirmed missing files or replaces changed metadata with ready-to-run fingerprint. Missing parent also triggers its parent reconciliation. Unavailable roots retain snapshots. Unchanged small/medium failures retry after30 seconds. Startup clamps legacy longer deadlines without deleting ready/cache data. Cooperative preemption is unchanged and not treated as failure.
- Validation: GalleryIndexHarness passes with added missing-file immediate removal, changed-file immediate eligibility, and30-second unchanged-failure tests; existing watcher, source race and unavailable-root retention tests pass. Both Release publish profiles pass.
- Deployment: Windows NAS backup/copy/hash verification completed at web-data/backups/20260909-070801-index-deploy; configurations/state preserved. LAN-pinned HTTPS health ok, read-only live index deadline check performed after startup. No Linux runtime deployment, cache purge or Git push.
- Remaining: these tests verify index/retry behavior, not the original error for every one of the1798 production failures. Persistent read/decode errors can still recur; inaccessible files must not be mistaken for confirmed deletion. Authenticated production UI not tested this turn.
