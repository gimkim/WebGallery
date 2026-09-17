# Compact loading ring

- Request: replace API trace panel with a corner circular progress indicator and Loading.
- Changed wwwroot/js/spa-router.js and wwwroot/css/site.css; documented superseding UI rule in AGENTS.md. Retained cancellation and actionable error feedback, as well as existing element placeholders. Known response lengths drive real progress; unknown lengths show indeterminate motion, respecting reduced motion.
- Validation: node syntax check passed; static assets deployed and hash-verified on Windows NAS, config preserved. Backup web-data/backups/20260909-210956-index-deploy. No real-browser test performed in this session. Linux publish not updated.
