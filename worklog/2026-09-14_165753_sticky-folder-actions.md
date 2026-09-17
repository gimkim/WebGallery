# Sticky folder actions

- Request: move create folder/upload/share/download ZIP into sticky navigation bar.
- Moved existing actions and write scope in Views/Gallery/Index.cshtml inside gallery-toolbar; preserved authorization conditions, URLs, button types and tokens. CSS adds wrapping action row and hides full actions during collapsed mobile toolbar mode. Updated AGENTS.md.
- Validation: Windows-IIS publish succeeded; NAS deploy hash-verified, config/state retained, backup web-data/backups/20260914-165748-index-deploy. No browser validation performed. Linux publish unchanged.
