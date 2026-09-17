# Folder-click placeholder paint delay

- Request: Investigate/fix folder clicks stalling before placeholders appear while Back responds quickly.
- Date: 2026-09-08, Asia/Bangkok.
- Cause confirmed in browser fixture: showPlaceholders interleaved computed-style reads and class/style writes for every item, forcing repeated style recalculation. A large source folder therefore does more synchronous work before feedback than a small source folder. Fast API response processing could also precede the first painted feedback frame.
- Fix: read all computed positions first, then apply positioning/classes. Add abort-aware double-animation-frame paint opportunity before API fetch, with120ms fallback for suspended RAF in background tabs. Preserve cancellation, dimensions and no minimum loading duration.
- Files: wwwroot/js/spa-router.js, tests/navigation-paint.cjs, AGENTS.md, .agents/PROJECT_NOTES.md, this worklog.
- Measurement: real headless Edge,1200-card fixture, same stylesheet/router: before handler80.1ms / next paint opportunity249.5ms /1203 style recalculations; after handler6.5ms /172.5–176.2ms paint opportunity /3 style recalculations. Regression asserts fewer than20 recalculations and zero fetches before first paint opportunity. These timings measure an isolated local fixture, not the user's NAS/client machine.
- Validation: full local SPA browser suite passed navigation/login/history/middle-click/confirmation/cancel and density5/10 geometry checks. JS syntax and Windows-IIS publish passed. Local fixture server PID34220 stopped.
- Deployment: Windows NAS only; backup `\\Gimkim-nas\c\Users\tatsa\web-data\backups\20260908-233059-index-deploy`. Published app hashes checked, original config preserved, data/cache/keys/tools not replaced. LAN-pinned TLS health status ok; maintenance marker absent.
- Remaining: user-specific folder/client performance and production authenticated UI not measured. The fix removes the proven style-thrashing source of pre-placeholder delay; it does not promise zero render time or faster server indexing.
