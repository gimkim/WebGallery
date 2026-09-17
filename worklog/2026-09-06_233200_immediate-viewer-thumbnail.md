# Immediate enlarged viewer placeholder

- Request: make full-image loading feel responsive by displaying an enlarged thumbnail immediately.
- Updated wwwroot/js/site.js to reuse loaded grid thumbnails before HTTP cache probing/headers, fetch thumbnails when not loaded, stretch to viewport fit preserving aspect ratio, refine sizing from original metadata, and protect against late results after navigation/full-image completion. Memory-cached originals still display directly. Added the overriding preference in AGENTS.md.
- Validation: node syntax check and Windows-IIS publish passed. NAS deployed after backup to web-data/backups/viewer-preview-20260906, using app_offline.htm and preserving settings/database. Offline marker removed. Live health returned ok and source/NAS JavaScript hashes matched.
- Both themes share this JS; no stylesheet changes. No interactive browser test was performed; visual loading/gesture verification remains manual.
