# Share auto-open once

- Request: fix Share modal opening automatically when entering folders.
- Cause found: OpenSharePanel used TempData.Peek, retaining the true flag across requests; raw SPA snapshots could retain transient modal state.
- Changed Gallery Razor to consume flag; site.js consumes client trigger; spa-router sanitizes transient modal state only when needed. Updated AGENTS.md.
- Validation: Windows publish passed, router syntax passed. NAS deployed/hash verified with preserved config, backup web-data/backups/20260914-171116-index-deploy. No end-to-end creation/navigation browser regression run this session. Existing old browser snapshots require a document reload to discard them. Linux output unchanged.
