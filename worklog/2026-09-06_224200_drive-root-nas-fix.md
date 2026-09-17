# Windows volume-root browsing fix

- Date: 2026-09-06 22:42 UTC+07:00.
- Request: fix physical drive root browsing and deploy to the Windows NAS.
- Changed `Services/FileSystemService.cs` to preserve absolute volume roots using Path.TrimEndingDirectorySeparator. Hidden/System attributes are ignored only for Windows filesystem roots after a successful attribute lookup; children retain existing filtering and ownership/symlink scope checks.
- Changed `tests/FileSystemVisibilityHarness/Program.cs` to cover volume root and child resolution, volume visibility, and directory-card availability. Existing hidden/system, metadata, ownership and link tests also passed.
- Updated AGENTS.md with the volume-root invariant.
- Validation: filesystem visibility harness passed; Windows-IIS publish passed; deployed application hashes matched publish output (excluding preserved settings/web.config).
- Deployment: backed up the existing app to `\\Gimkim-nas\c\Users\tatsa\web-data\backups\20260906-224049-drive-root-app`, briefly used app_offline.htm, copied publish files, then removed app_offline.htm. No database/settings replacement. Confirmed root 1 still maps to U:\.
- Live validation: HTTPS `/gallery/health` returned status ok and `/gallery/Account/Login` returned HTTP 200 after restart.
- Remaining verification: authenticated browsing of U:\ on NAS needs the user's session; the local volume regression ran successfully, but no authenticated NAS UI test was performed.
