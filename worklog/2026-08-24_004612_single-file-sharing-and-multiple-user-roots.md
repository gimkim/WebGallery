# Single-file sharing and multiple user roots

## Date and time

- 2026-08-24 00:46:12 UTC+07:00 (Asia/Bangkok)

## Request / intent

- Allow an owner to create an unlisted link for one file that opens directly in Full View and cannot browse anything except that file.
- Let Management assign multiple filesystem folders to each user and present those configured roots as the user's Gallery home folders.
- Show a designed empty Gallery home when an account has no configured roots.
- Preserve existing production users, folder shares, collections, audit data, and deployment state during the schema upgrade.

## Concepts and rules established

- `UserRoots` is the owner-scoped source of Gallery roots. The private home is virtual, each root has an editable display name/path, and internal `@root-{id}` paths never appear as user-facing names or breadcrumbs.
- Existing non-empty `ApplicationUser.RootFolder` values seed one root per user. Existing folder-share, collection-folder, and legacy folder-rule paths migrate idempotently to the first root marker. The legacy column remains only for compatibility.
- Single-file shares are limited to images and videos because those types have Full View behavior. `ShareLink.TargetType = file` requires an exact normalized path match for the landing page and every thumbnail, original, download, metadata, stream, segment, and subtitle endpoint.
- A single-file recipient sees only the selected item. The image viewer or video player opens automatically; folder navigation, folder ZIP, Gallery toolbar, and sibling access are unavailable.
- Removing a root assignment does not delete any filesystem content.

## Files changed

- `Models/UserRoot.cs`, `Models/ApplicationUser.cs`, `Models/ShareLink.cs`
- `Data/GalleryDbContext.cs`, `Data/DatabaseInitializer.cs`
- `Services/FileSystemService.cs`
- `Controllers/AdminController.cs`, `Controllers/GalleryController.cs`, `Controllers/CollectionsController.cs`
- `ViewModels/GalleryViewModels.cs`
- `Views/Admin/Index.cshtml`, `Views/Admin/Logs.cshtml`, `Views/Gallery/Index.cshtml`
- `wwwroot/js/site.js`, `wwwroot/js/video-player.js`
- `wwwroot/css/site.css`, `wwwroot/css/site-modern.css`
- `tests/FileSystemVisibilityHarness/Program.cs`
- `README.md`, `AGENTS.md`, `.agents/PROJECT_NOTES.md`, and this worklog.

## Validation actually performed

- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- `node --check` passed for `site.js` and `video-player.js`.
- FileSystem visibility/multi-root harness passed, including two configured roots, a zero-root empty account, and hidden/system filtering.
- LoginAttemptLimiter and ShareAudit harnesses passed.
- A development SQLite upgrade completed with `PRAGMA integrity_check = ok`.
- A browserless authenticated Development HTTP flow logged in, rendered one root card, created one image file share, and verified: anonymous landing HTTP 200; one auto-open viewer launcher; exact-file `ViewFile` HTTP 200; sibling `ViewFile` HTTP 404; and a file-share landing URL with a browse `path` HTTP 404.
- `dotnet publish WebGallery.csproj -c Release -o publish\2026-08-24_004427`: compiled and published successfully. The generated staging tree contained development settings, so deployment explicitly excluded `appsettings.Development.json` as required.
- Before deployment, production SQLite returned `integrity_check = ok`, 3 users, and 13 share links. While `app_offline.htm` was active, 150 preceding deployment files plus the production SQLite file were backed up under `backup\2026-08-24_004427_pre-multiroot-file-share`; the backup database also returned `integrity_check = ok`.
- Deployed the Release output to `C:\Web\imagegallery`, excluding development settings, then removed the maintenance marker.
- The production migration returned `integrity_check = ok`, 3 users, 3 migrated roots, and all 13 original share links (12 folder and 1 collection). Zero non-collection legacy paths remained without an `@root-` marker.
- Live Login and `js/site.js` returned HTTP 200. Live private Gallery and Management returned their expected anonymous HTTP 302 redirects.
- Publish/deploy SHA-256 matched for `WebGallery.dll`, `site.js`, `video-player.js`, Retro CSS, and Modern CSS. The deployed DLL SHA-256 is `0E3EA8907C36D80FAADC467951F2AFF57632DA0BA836B480A0C82270DF8958FC`.
- Confirmed the IIS directory contains neither `app_offline.htm` nor `appsettings.Development.json` after deployment.
- `git diff --check` passed apart from expected checkout line-ending notices.

## User-visible result

- Management can add, rename, change, or remove multiple Gallery folders per user.
- A user's first Gallery page consists of those configured folders; no configuration produces an intentional empty state.
- Selecting exactly one image or video exposes `Share file`; the generated unlisted URL opens directly in Full View and authorizes only that exact file.
- Existing production roots, share links, collections, and audit history remain available through the migrated path model.

## Remaining manual test / uncertainty

- Interactive Management layout, root add/edit/remove, automatic image/video dialog opening, Copy/Revoke, and mobile behavior were not visually tested because the project-specific Codex in-app-browser crash workaround remains active.
- Production authenticated behavior was not exercised with a user's credentials. Live checks covered startup/schema/static files and anonymous authentication boundaries; the complete authenticated/file-scope flow was exercised against the Development server.
