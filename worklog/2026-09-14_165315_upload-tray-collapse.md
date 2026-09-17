# Remember upload tray collapse

- Request: collapsible upload progress with remembered last state.
- Changed file-operations.js (stable header button, counts, localStorage preference with fallback; body-only redraw), site.css (compact header/hidden body/focus), AGENTS.md, tests/upload-tray-collapse.cjs.
- Validation: node syntax check passed. Headless Edge fixture using real script/CSS passed click collapse, Enter expand, hidden body, and preference restoration across document reload. This test did not perform actual file transfers. Existing transfer/cancel routines were unchanged.
- Updated Windows publish static assets and deployed via NAS helper; hash verified, configuration retained, backup web-data/backups/20260914-165307-index-deploy. Linux output unchanged. Preference persists in browser; uploads themselves still do not survive document reload.
