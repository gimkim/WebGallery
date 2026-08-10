# Windows 11 IIS deployment ZIP

- Date: 2026-08-10 09:57:24 +07:00 (Asia/Bangkok)

## Request and intent

- Prepare a ZIP that can be extracted directly into an IIS application folder on another Windows 11 machine.
- Keep the package clean and safe to distribute without carrying production state or credentials from this machine.

## Packaging rules established

- The archive is a framework-dependent .NET 10 Release publish. The target machine requires IIS plus the current supported .NET 10 Hosting Bundle.
- `WebGallery.dll` and `web.config` are at the ZIP root so IIS can point directly at the extracted folder.
- The package includes a Windows 11/IIS installation and ACL guide.
- Production SQLite/WAL/SHM files, bootstrap credentials, thumbnail cache, gallery content, backups, tests, source files, and `appsettings.Development.json` are excluded.
- `appsettings.json` retains the recommended `C:\Web\imagegallery-data` and `C:\Web\gallery-content` paths and contains no bootstrap password.

## Publish contamination found and corrected

- The first packaging attempt was rejected by the package inventory check before a ZIP was created.
- Existing `publish` and `publish-current` directories were being included recursively by the SDK default content rules, bringing old nested publish/backup trees and development settings into staging.
- `WebGallery.csproj` now explicitly removes `publish`, `publish-current`, and `release` trees from Compile/Content/None items, in addition to the existing backup and test exclusions.
- A clean publish after this correction dropped from 141 files with nested stale trees to 51 intended deployment files.

## Files changed or created

- `WebGallery.csproj`
- `deployment/INSTALL-WINDOWS11-IIS.txt`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- `release/WebGallery_NET10_Windows11_IIS_20260810_095607.zip`
- `release/WebGallery_NET10_Windows11_IIS_20260810_095607.zip.sha256`
- This worklog file

## Validation actually performed

- `dotnet build WebGallery.csproj -c Release`: passed with 0 warnings and 0 errors.
- `dotnet list WebGallery.csproj package --vulnerable --include-transitive`: no vulnerable packages were reported by the configured sources.
- `dotnet publish WebGallery.csproj -c Release` to a fresh external staging directory: passed.
- Package inventory confirmed 51 files and rejected database files, C# source, credentials, development settings, cache/content, backups, tests, and nested publish directories.
- Required-file assertions confirmed binaries, runtime metadata, `web.config`, production settings, installation guide, both theme stylesheets, JavaScript, and Windows x64/ARM64 SQLite native runtimes.
- Extracted the finished ZIP into a new temporary directory and compared every extracted file SHA-256 with staging; all 51 files matched.
- Confirmed `WebGallery.dll` and `web.config` are directly at the extracted root.
- Started the application from the extracted package with isolated environment overrides and a new temporary SQLite database. HTTP Login returned 200, SQLite integrity returned `ok`, both collection tables were created, and a random bootstrap-admin file was generated.
- Stopped the isolated smoke-test server and confirmed its port was no longer listening.
- This session did not modify or redeploy `C:\Web\imagegallery` and did not touch the production database/content/cache.
- The source workspace is not a Git repository, so Git status and `git diff --check` were unavailable.
- No interactive browser/UI validation was performed; packaging validation used build, archive, extracted-file, isolated HTTP, and SQLite checks.

## Artifact

- ZIP: `release/WebGallery_NET10_Windows11_IIS_20260810_095607.zip`
- Size: 19,903,156 bytes
- SHA-256: `76B1602650B7FF932358DF685C3A749727A611FA1740346C910114782E72C504`

## User-visible result

- The ZIP can be extracted directly into an IIS application physical path. The included guide covers prerequisites, application-pool/authentication settings, persistent folders, ACLs, first login, and HTTPS considerations.

## Remaining target-machine checks

- Install IIS before the .NET 10 Hosting Bundle and restart IIS/Windows afterward.
- Apply ACLs using the actual application-pool name.
- Configure the IIS site/application, Anonymous Authentication, HTTPS binding, certificate, and network exposure.
- Perform the first real login and change/remove the generated bootstrap credential on a fresh installation.
