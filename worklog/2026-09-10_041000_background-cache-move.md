# Background cache move

- Request: migrate legacy caches in background using rename/move, show Management progress.
- Changed ThumbnailService (shared-stripe no-overwrite migration and lazy move), new ThumbnailCacheMigration hosted service (bounded index paging, daily repeat), Program registration, Admin endpoint/view and scoped JS polling, regression harness and AGENTS.md.
- Both indexed signatures handled; source bytes are not decoded. Existing shared destination means legacy duplicate retained, never overwritten/deleted. Unknown signature/path hashes remain untouched. Progress is per indexed rendition check, with separate outcome counts. Rescans restart safely; counters are process-local.
- Validation: Windows publish passed. Harness verified legacy source disappeared after move and rerun returned shared, plus existing cross-user cache regressions. NAS deployed with state-preserving helper. No authenticated Management browser validation performed.
