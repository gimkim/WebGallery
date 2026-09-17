# Sticky breadcrumbs and action icons

- Request: place navigation before a smaller search in the sticky toolbar, add themed action icons.
- Changed Views/Gallery/Index.cshtml (moved existing breadcrumb markup without changing href/drop attributes), wwwroot/css/site.css (responsive breadcrumb/search sizing and outline icon styling), wwwroot/js/site.js (scoped icon decoration), AGENTS.md (durable rule).
- Validation: Windows-IIS Release publish and node --check succeeded. NAS deploy hash verification passed, config preserved, backup web-data/backups/20260909-203409-index-deploy. Health returned ok.
- Mobile compact mode retained. No authenticated browser/UI validation performed; long breadcrumbs and mobile visual layout still need manual checking. Linux publish not refreshed.
