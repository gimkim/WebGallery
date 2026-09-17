# Large folder API limit

- Request: large image folders fail with Page exceeds64MiB safety limit.
- Identified client transport guard and unbounded per-folder Razor rendering into JSON. Kept guard; bounded ordinary/private/folder-share rendering to500 items/page after sorting, page query clamped, focus selects containing page. Added top/bottom pager and explicit page-local search/selection guidance. Full folder ZIP unchanged.
- Changed Gallery controller/viewmodel/Razor/CSS, AGENTS.md and tests/folder-pagination-browser.cjs. Added501 ignored local fixture files in App_Data/spa-smoke/content/paging-fixture, leaving production media untouched.
- Validation: build and both publish profiles passed. Real isolated ASP.NET/headless Edge test:500+1 item pagination, SPA Next/Back, focus page selection, API response1053065 bytes. Test uses text rows, not actual production large image folder; authenticated NAS folder not tested. Local runtime stopped. Full listing still materialized from index; collection-root/selection-share rendering unaffected.
- NAS deployment uses existing state-preserving helper. No DB/cache resets or thumbnail identity changes. Linux publish refreshed but container not deployed.
