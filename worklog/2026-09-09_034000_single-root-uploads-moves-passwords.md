# Single-root file management and password lifecycle

Date: 2026-09-09, Asia/Bangkok (UTC+07:00).

## Request and decisions

- Replace multiple roots/virtual-root selection with one root per user, preserving existing data and identities.
- Add safe folder creation, file/folder drag-and-drop and picker upload, segmented retry, persistent SPA batch progress/cancellation, immediate small image thumbnail generation, named folder ZIP labels, selected-file moves with confirmation/tree picker, forced password change and administrator-issued one-time password links.
- Follow-up: staging must use unique application-specific names, not reserve the generic .part extension. Ordinary user-file.part is supported and tested.
- No-overwrite policy; only signed-in owners may mutate files. Shared views remain read/download-only. No blanket filesystem permission changes.

## Files

- Models/ApplicationUser.cs; ViewModels/GalleryViewModels.cs and PasswordChangeViewModel.cs; Data/DatabaseInitializer.cs; Program.cs.
- Controllers/AccountController.cs, AdminController.cs, GalleryController.cs, CollectionsController.cs, FilesController.cs.
- Services/WritePaths.cs, UploadSessions.cs, FileSystemService.cs.
- Views/Account/ChangePassword.cshtml and ResetPassword.cshtml; Views/Admin/Index.cshtml, Gallery/Index.cshtml, Collections/SelectFolders.cshtml, Shared/_Layout.cshtml.
- wwwroot/js/file-operations.js and spa-router.js; wwwroot/css/site.css.
- tests/file-mutations-browser.cjs, UploadSafetyHarness; adapted spa-browser.cjs and spa-folder-cache.cjs for direct-root Home.
- compose.yaml, deployment/docker.env.example, Windows/Linux install guides, AGENTS.md and .agents/PROJECT_NOTES.md.

## Implementation

The owner-unique root index preserves existing UserRoot IDs/paths and refuses unexpected multi-root data without deleting it. Existing user password flags migrate false; new users default true. Per-request enforcement checks the flag/security stamp; password change verifies the current password, and Identity reset tokens expire after one hour and invalidate on successful use.

The upload queue is document-scoped and survives SPA navigation. Two active files across batches, 4 MiB append segments, duplicate offset acknowledgment and five transient retries. Final publication copies to a uniquely named pending file on the destination filesystem, revalidates the root, then renames without overwrite. A private journal allows24-hour cleanup of abandoned final copies; the cleanup tick is five minutes. Pending files are specifically ignored by listings/index/ZIP/direct serving, not by generic extension. Small thumbnails are requested through the existing queue at completion. Media write/delete ACLs remain an administrator responsibility.

## Validation actually performed

- Release build passed with zero warnings/errors; both Windows-IIS and Linux-Docker publish passed. Linux publish is artifact validation, not a live Linux runtime test.
- UploadSafetyHarness passed portable/reserved names (including Windows device aliases), traversal rejection, unique staging recognition, expiry/restart-journal cleanup and preservation of ordinary .part files.
- Real headless Edge + isolated local MVC fixture passed direct-root Home, invalid names/root scope/CSRF rejection, exact SHA-256 of a file over4 MiB across multiple segments, duplicate-segment acknowledgment, incomplete completion rejection, no overwrite, cancel and move.
- Browser UI passed create-folder, picker upload, injected connection-reset retry, persistent progress during SPA Management navigation, native selected-card drag with confirmation/cancel, lazy tree expansion to current folder, same-destination prohibition, move confirmation, controlled directory-drop entry recursion including empty folders/highlight, image upload thumbnail completion and WebP response.
- Password tests passed new-user forced change, server write gate, successful change clearing the checkbox, admin reset link creation, successful reset and denial of replay. Browser JS errors list remained empty.
- Existing SPA and folder-cache browser regressions passed, including Back/Forward, middle-click new tab, unchanged DOM retention and stale response suppression.
- A move-dialog screenshot was inspected. Native-looking new buttons were corrected to use the shared button classes. Final class-only changes compiled in the publish outputs.
- Initial test run accidentally requested Download without mode and attempted a huge buffer diff, exhausting the Node test runner heap. The test now checks status and hashes; missing mode also now safely fails access resolution instead of throwing. No production content or Codex application state was changed by this failed test.

## NAS deployment and state

- Read-only preflight: three roots, no multi-root owners,22 share links,2 collections. Root metadata SHA-256 before/after migration: 7d55e0dcbe2fed18f321fb7601d62ca8ccd03ae2ebd521de76a3c800433bab53.
- Windows NAS backup/copy/hash verification completed: web-data/backups/20260909-033745-index-deploy. appsettings.json/web.config, database, keys, cache and tools preserved.
- LAN-pinned HTTPS /gallery/health returned status ok. Read-only postflight verified RequirePasswordChange column, owner-unique root index, identical root metadata hash and unchanged share/collection counts.
- A final portable reserved-name hardening patch for a spaced device alias (CON .txt) passed UploadSafetyHarness and is being republished; any subsequent deployment outcome is recorded in the session tool evidence.
- No Git push, ThumbService deployment, local C:\Web deployment or Linux-container update.

## Remaining boundaries

No authenticated production upload/move was performed against real user media. NAS pool write permissions, mobile OS-specific drag/drop, actual multi-gigabyte transfers, and live Linux filesystem behavior remain manual checks. File handles/progress are retained only in the current document: reload or server restart requires restarting incomplete uploads, while abandoned staging is cleaned. Failed/partial batch moves keep completed files; existing file-share paths are not rewritten on move. Isolated test files/accounts were retained only in local fixtures for reproducibility.
