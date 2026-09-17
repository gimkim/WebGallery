# ThumbService configurable16 workers

2026-09-09 Asia/Bangkok.

- Request: accept16 concurrent jobs, configurable in appsettings.
- Changed source ThumbService/appsettings.json and fallback default in Program.cs from4 to16; existing1–16 clamp retained. Deployed C:\Users\tatsa\web\ThumbService\appsettings.json Workers changed to16 with all other values/API key preserved. Backed up previous config outside service webroot under ThumbService-backups.
- Restarted only ThumbService using temporary app_offline.htm created and removed by this session. Authenticated runtime /ThumbService/health returned status ok, workers16, quality78. No production secrets output. No binary replacement needed for this config-only deployed change; source fallback change not separately built.
- Gallery/NAS not redeployed; no cache/state deletion. Service retains immediate429 beyond16 active jobs, not an unbounded waiting queue. No concurrent-load benchmark performed.
