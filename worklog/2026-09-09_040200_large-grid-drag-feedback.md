# Reduce work while dragging across large folder grids

2026-09-09, Asia/Bangkok.

- Request: dragging a folder becomes very slow after crossing the first folder in a large listing.
- Findings: every dragover removed then re-added the target class, even on the same card. The class also ran a repeating outline-color animation. Native drag preview used the image-heavy live card. No move/upload API is intentionally called during dragover.
- Files: wwwroot/js/file-operations.js, wwwroot/css/site.css, tests/folder-drag-performance.cjs, AGENTS.md, .agents/PROJECT_NOTES.md.
- Fix: idempotent target transition, static highlight, dedicated small text drag ghost, and one writability lookup per page preparation instead of one per card. Existing confirmation, internal/external distinction and drag cleanup retained.
- Validation: real headless Edge2000-card fixture with2000 same-target events measured3999 class mutations before versus1 after; crossing target changes2 classes and leaves exactly1 highlighted item. Handler-only synthetic timing17.8ms before,6.5ms after; no claim of equivalent real-world/NAS speedup. Animation name changed drop-pulse to none. Full local MVC file-mutation browser regression passed, including actual cover-image-origin folder move, zero unintended uploads, file-byte preservation, external folder upload and network retry. Local server stopped.
- Deployment: JS/CSS-only change synchronized to Windows-IIS and Linux-Docker publish trees; Windows NAS backup/copy/hash validation and LAN-pinned health check performed via helper (result in session tools). Existing configurations/state preserved. No C# build needed; no Linux runtime, ThumbService, local C:\Web deployment or Git push.
- Remaining: real user's large NAS listing and hardware frame pacing not measured. Local fixture establishes bounded highlight mutations and functional regression coverage, not proof of all possible sources of drag latency.
