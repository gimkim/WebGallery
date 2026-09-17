# Per-user thumbnail progress snapshot

2026-09-09 05:56 Asia/Bangkok.

- Request: show generated / total / remaining thumbnails per user in Management; retrieve once on page load, no frequent refresh.
- Implementation: one server-side owner-grouped aggregate over indexed non-directory images joined to current owned roots. Generated matches the captured current ThumbnailService.Signature; remaining is total minus generated. Zero-image users show zero. Counts cover small thumbnails only, not videos/medium images; explanatory text identifies indexed snapshot semantics and manual cache deletion caveat. No new client polling, endpoint or filesystem/cache scan.
- Files: Controllers/AdminController.cs, ViewModels/GalleryViewModels.cs, Views/Admin/Index.cshtml, wwwroot/css/site.css, tests/admin-thumbnail-counts.cjs, AGENTS.md, .agents/PROJECT_NOTES.md.
- Validation: Windows-IIS and Linux-Docker Release publish passed. Real local MVC/Edge Management rendering passed; all10 fixture users matched direct SQLite totals/readiness/current signature, including empty users. Summary unchanged over3.5 seconds; no browser errors. User-card screenshot captured and visually inspected. Local server stopped.
- Deployment: Windows NAS publish copied/hash-verified with existing appsettings/web.config/state preserved; backup web-data/backups/20260909-055542-index-deploy. LAN-pinned HTTPS health returned ok. No Linux runtime deploy or Git push; authenticated NAS UI not tested.
- Limitation: values are index snapshots, not a cache-directory audit. Newly undiscovered files or manually deleted cached WebPs can lag until normal index/generation activity reconciles them. Existing service-status polling unrelated to these counters remains unchanged.
