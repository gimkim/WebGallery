# Delete migrated duplicate cache

- Request: delete redundant legacy cache when shared cache already exists.
- Changed ThumbnailService migration to validate retained WebP before deleting the exact legacy fingerprint under shared lock; invalid shared content retains source. Added deleted counter in ThumbnailCacheMigration and Management view/polling; updated AGENTS.md and harness.
- Tests: MediumThumbnailHarness passed, including valid shared destination deleting only old duplicate, corrupt shared destination retaining old cache, migration idempotence and existing concurrency/fallback tests. Windows publish passed.
- NAS deployment hash-verified, preserving config/state; backup web-data/backups/20260910-040853-index-deploy. Background scan starts after startup delay. No production deletion count or authenticated browser UI verification claimed. Deleted derived caches are not trashed; retained shared thumbnail remains, originals untouched and can regenerate cache. Linux publish not updated.
