# Shared thumbnail cache and migration

- Request: share physical-file renditions across accounts and reuse old cache regardless of which current account requests first.
- Changed ThumbnailService identity/legacy promotion, GalleryIndexService owner lookup and cross-owner readiness writes, MediumThumbnailHarness cross-owner concurrency regression, AGENTS.md.
- Migration is lazy on shared misses: probe existing account IDs with the source fingerprint, copy matching WebP atomically under shared stripe, preserve originals and source recheck. No full media scan or cache deletion. Endpoint authorization unchanged. Old cache from removed accounts or differently cased historical path spelling may not be discoverable from hashes alone.
- Windows publish passed. Medium harness passed including cross-owner same-path concurrency and small/medium separation, remote backpressure/preemption/fallback tests. Full production legacy migration behavior not separately exercised; no browser test. NAS deployment performed with existing state-preserving helper. Linux output not updated.
