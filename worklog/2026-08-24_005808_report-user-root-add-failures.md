# Report user-root add failures

## Date and time

- 2026-08-24 00:58:08 UTC+07:00 (Asia/Bangkok)

## Request / intent

- Fix Management so adding a Gallery folder to a user never fails silently.
- Show whether the cause is filesystem permission, a missing/invalid path, another Windows I/O problem, a duplicate assignment, or a database-save problem.

## Concepts and rules established

- Validate a root with `File.GetAttributes` before enumerating it. Unlike `Directory.Exists`, this preserves access-denied and other OS exceptions instead of reducing them to a misleading not-found result.
- Distinguish missing folder, file-not-folder, access denial/security policy, invalid/unsupported path, and other Windows I/O messages.
- Catch and log unexpected add/save exceptions. Translate common SQLite busy/locked, read-only, disk-full, and constraint codes into actionable Management messages.
- Redirect back to the affected user's anchored card and render the cause beside that user's Gallery folders form, while retaining the global error alert as a fallback.

## Files changed

- `Controllers/AdminController.cs`
- `Views/Admin/Index.cshtml`
- `wwwroot/css/site.css`
- `wwwroot/css/site-modern.css`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog.

## Validation actually performed

- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- FileSystem visibility/multi-root harness passed.
- `git diff --check`: passed apart from expected checkout line-ending notices.
- Browserless authenticated Development HTTP test posted `AddUserRoot` with a deliberately nonexistent path. The final Management response was HTTP 200, contained `Folder not added`, contained the exact missing-folder cause, rendered `admin-root-error` beside the targeted user card, and created zero `UserRoots` rows for the invalid path.
- `dotnet publish WebGallery.csproj -c Release -o publish\2026-08-24_005715`: passed.
- With `app_offline.htm` active, backed up 150 preceding IIS files and production SQLite to `backup\2026-08-24_005715_pre-root-error-feedback`; the backup database returned `PRAGMA integrity_check = ok`.
- Deployed Release output to `C:\Web\imagegallery`, excluding `appsettings.Development.json`, then removed the maintenance marker.
- Publish/deploy SHA-256 matched for `WebGallery.dll` and both theme stylesheets. Deployed DLL SHA-256 is `A2C6B6222D0FC42CEE796E1D9A8ADBB05386962689DDB5EB52D0F41E8D85E923`.
- Live Login returned HTTP 200 and live Management returned the expected anonymous HTTP 302 redirect. Production SQLite remained `integrity_check = ok` with 3 existing user-root rows.
- Confirmed the deployment contains neither `app_offline.htm` nor `appsettings.Development.json`.

## User-visible result

- A failed Add folder submission returns to the same user card and shows a prominent explanation directly above its Gallery folders list.
- Permission errors advise granting the IIS application pool identity Read & Execute permission; filesystem and database failures show their distinct cause instead of disappearing or falling through to a generic error page.
- Successful additions now confirm the target username and normalized physical path.

## Remaining manual test / uncertainty

- No real folder ACL was changed to force an IIS access-denied response. That branch is covered by explicit exception handling but was not triggered against a live protected folder.
- The Management UI was not visually inspected in a browser because the project-specific Codex in-app-browser crash workaround remains active. The actual rendered HTML response and target-card alert were verified programmatically on the Development server.
