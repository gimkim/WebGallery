# Search Enter must not submit selected downloads

2026-09-09, Asia/Bangkok.

- Request/cause: the local search input resides inside selection-form, whose action is DownloadSelected. Native Enter implicitly submitted that unrelated form.
- Files: wwwroot/js/site.js, tests/gallery-search-enter.cjs, AGENTS.md.
- Fix: prevent default Enter on the search input outside IME composition, reapply its local filter, and retain explicit download submission. Listener remains under the existing SPA page scope.
- Validation: node syntax passed. Real headless Edge using local isolated MVC fixture navigated via SPA, tested matching/no-match/empty search with Enter and verified unchanged URL and zero DownloadSelected requests. Selecting readme.txt and explicitly clicking Download selected produced the expected download. No browser JS errors. Local server stopped.
- Deployment: static-only site.js synchronized into both existing publish profiles; Windows NAS backup/copy/hash verification and LAN-pinned health check performed through the deployment helper (outcome in session tools). No C# build was required for this JavaScript-only fix. NAS config/database/cache/keys untouched; no Linux runtime/ThumbService/local IIS deploy or Git push. Authenticated production-browser test remains unperformed.
