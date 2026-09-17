# Additive selection toolbar

- Request: preserve all primary header/menu controls and their positions when selecting files; show selection tools as an additional row.
- Changes: site.js no longer hides primary toolbar on selection. site.css no longer hides mobile compact summary on selection; desktop uses stable minmax/auto grid columns, selection spans both below. AGENTS records additive selection rule. Added tests/selection-toolbar-layout.cjs.
- Validation: headless Edge against isolated local app at 1600,1000,390px verified primary main/actions/compact-summary bounding boxes unchanged within1px before/after selection; extra row appears and Clear hides it; no runtime errors. Windows-IIS publish and diff whitespace check passed.
- Deployment: NAS preserving script copied and hash-verified application; config retained; backup web-data/backups/20260917-212936-index-deploy. HTTPS SNI-pinned health returned ok. No database/cache changes or Git push. Local test server stopped. Production authenticated UI not separately inspected.
