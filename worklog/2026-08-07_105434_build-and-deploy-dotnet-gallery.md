# Build and Deploy the .NET Gallery

- Date: 2026-08-07 10:54:34 +07:00
- Session intent: Design, implement, test, and deploy a multi-user filesystem-backed gallery with per-folder access control.

## Request

Build a web gallery and assess .NET 10 and SQLite. Each user needs a configurable start folder. The gallery must support folders, Grid/List views, adjustable Grid density, sorting, WebP thumbnail cache, full-image viewing, individual downloads, extension icons for non-images, streaming folder ZIP downloads, private/public/unlisted access, administrator settings, and user administration.

## Concepts and rules established

- Selected .NET 10 LTS with ASP.NET Core MVC and ASP.NET Core Identity.
- Selected SQLite for users, roles, rules, tokens, and settings only; media remains on the filesystem.
- Added strict root containment for all filesystem paths.
- Added nearest-ancestor access inheritance and scoped/revocable unlisted tokens.
- Added on-demand WebP cache invalidated by source metadata.
- Added direct response-stream ZIP creation with no temporary ZIP and no traversal into reparse-point directories.
- Persistent data lives outside the replaceable deployment directory.
- Bootstrap credentials are randomly generated outside source control.

## Files changed

- Added the ASP.NET Core project, configuration, production `web.config`, and launch settings.
- Added Identity/SQLite entities and database initialization.
- Added account, gallery, file delivery, share/access, streaming ZIP, and admin controllers.
- Added filesystem and thumbnail services.
- Added Razor views for login, gallery, full-image viewer, and system/user administration.
- Added responsive styling, Grid/List persistence, column controls, copy actions, and viewer behavior.
- Added `.gitignore` and `README.md`.
- Updated `AGENTS.md` and `.agents/PROJECT_NOTES.md` with durable architecture and deployment rules.
- Published 48 files to `C:\Web\imagegallery`.

## Validation performed

- Confirmed .NET SDK 10.0.302, ASP.NET Core Runtime 10.0.10, and IIS ASP.NET Core Module V2 17.0.22116.0 are installed.
- `dotnet build -c Release --no-restore` passed with 0 warnings and 0 errors.
- `dotnet list package --vulnerable --include-transitive` reported no vulnerable packages after pinning the corrected SQLite native package.
- Fresh Development startup created the SQLite schema and a random bootstrap credential; login and Admin returned HTTP 200.
- Authenticated gallery and Admin pages returned HTTP 200; a default private public route returned HTTP 404.
- Real JPEG integration test generated a 12,238-byte WebP thumbnail, served the 542,091-byte original JPEG, and created a readable streamed ZIP containing `sample.jpg`.
- Unlisted link returned HTTP 200, attempts outside its linked folder returned HTTP 404, and the same unlisted folder through the public route returned HTTP 404.
- Browser visual QA covered login, desktop Grid/List gallery, access-panel state, full-image viewer loading a 1920x1200 image, Admin page, and a 390-pixel mobile viewport. Mobile had no horizontal overflow after responsive fixes.
- Production published DLL was run directly: production database/content folders were created, generated admin credentials worked, and Admin returned HTTP 200.
- Re-published and copied 48 files to `C:\Web\imagegallery`; SHA-256 comparison found 0 mismatches.
- IIS logs and a local detailed request confirmed the public endpoint currently returns IIS `401.2` because Basic Authentication runs before ASP.NET Core.

## User-visible result

The complete gallery application is present in source and deployed to the configured IIS application directory. Production database, cache location, default content root, and initial admin credential file have been created.

## Remaining manual tests or uncertainty

- Public endpoint verification is blocked until an administrator enables Anonymous Authentication and disables Basic Authentication specifically for the `/Gallery` IIS application.
- After changing IIS authentication, verify `https://gimgim.ddns.net:45570/Gallery`, sign in with the generated credential in `C:\Web\imagegallery-data\bootstrap-admin.txt`, change the password, and remove that credential file.
- Configure real user root folders and perform a manual regression with the user's actual image/file corpus and a large nested ZIP download.
- This workspace is not a Git repository, so `git diff --check`, commit, and push validation were not applicable.
