# Hide retry while connected

- Request: do not show Retry connection now while Thumbnail service is Connected.
- Files: Views/Admin/Index.cshtml, Controllers/AdminController.cs, wwwroot/js/site.js, tests/resizer-management.cjs, AGENTS.md.
- Changes: initial server-rendered hidden form when connected/disabled; status API canRetry boolean updates visibility on each existing3-second poll. Unavailable/checking state can retry. Authentication and antiforgery unchanged.
- Validation: Release build passed. Headless Edge isolated Management test confirmed hidden while actually connected, visible after simulated unavailable status response, and hidden again after real retry/reconnect; existing settings and antiforgery tests passed with no page errors. Production service was not stopped. Local fixture server stopped after test.
- Deployment: Windows-IIS publish and Windows NAS backup/copy/hash workflow; preserve NAS configs, database, cache, keys and tools. No ThumbService/Linux/C:\Web deployment or Git push. NAS health/backup outcome recorded by session tools.
