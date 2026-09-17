# Always-visible API trace

- Date: 2026-09-08, Asia/Bangkok.
- Request: Always display API trace details during loading; remove collapse/expand.
- Files: wwwroot/js/spa-router.js now renders a plain console section/title/pre instead of details/summary. AGENTS.md and .agents/PROJECT_NOTES.md record the superseding UI rule.
- Validation: JS syntax, static markup assertions (trace present, no details disclosure) and Windows-IIS publish passed. No browser visual test repeated for this markup-only change.
- Deployment: Windows NAS, backup `\\Gimkim-nas\c\Users\tatsa\web-data\backups\20260908-232344-index-deploy`. Application copy/hash checks passed; config preserved, persistent data/cache/keys/tools untouched. LAN-pinned TLS health status ok; no maintenance marker remains.
- Result: Compact corner panel shows trace details throughout navigation loading without requiring a click. Existing progress, cancellation and element placeholders unchanged.
