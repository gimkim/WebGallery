# Fix OneDrive Gallery Root Access

- Date: 2026-08-07 11:02:28 +07:00
- Session intent: Diagnose and repair the error when setting the admin gallery root to `C:\Users\tatsa\OneDrive\camera`.

## Request

The user reported an error after entering `C:\Users\tatsa\OneDrive\camera` as the user root folder.

## Concepts and rules established

- IIS accesses filesystem roots with its worker identity rather than the interactive Windows user.
- A user-profile or OneDrive root needs explicit read permission for IIS.
- Gallery roots should be validated as existing and enumerable during Admin save.
- OneDrive content intended for unattended serving should be kept locally available.

## Files changed

- Updated `Controllers/AdminController.cs` to validate root existence and enumeration before creating or updating a user.
- Added friendly Thai errors for inaccessible, missing, invalid, and unsupported root paths.
- Updated `AGENTS.md` and `.agents/PROJECT_NOTES.md` with filesystem identity and OneDrive rules.
- Published and deployed the updated application to `C:\Web\imagegallery` using `app_offline.htm` during DLL replacement.
- Added a Read & Execute ACL for `IIS_IUSRS` on `C:\Users\tatsa\OneDrive\camera` and descendants.
- Updated the production admin root in SQLite after an online SQLite backup.

## Validation performed

- Confirmed the requested OneDrive directory exists.
- Confirmed its original ACL did not grant IIS access.
- Confirmed IIS logged HTTP 500 for `POST /Gallery/Admin/UpdateUser`, while the database retained the previous root.
- Confirmed the new `IIS_IUSRS` ACL is explicit, inheritable, and limited to Read & Execute.
- `dotnet build -c Release --no-restore` passed with 0 warnings and 0 errors.
- Publish and deployment completed, and the temporary `app_offline.htm` was removed.
- Created `C:\Web\imagegallery-data\gallery-before-camera-20260807-110219.db` with SQLite's online backup command.
- Confirmed production SQLite now stores `C:\Users\tatsa\OneDrive\camera` for the `admin` user.
- Confirmed an anonymous request to `https://gimgim.ddns.net:45570/Gallery/` returns HTTP 200 at the application login page after the IIS authentication change.

## User-visible result

The admin account now starts at the requested OneDrive camera folder, and future invalid or inaccessible root paths will show a validation message instead of returning HTTP 500.

## Remaining manual tests or uncertainty

- The bootstrap credential file had already been removed, so the deployed Gallery could not be re-opened in an authenticated automated session after changing the root.
- The user should refresh the signed-in Gallery and confirm actual images appear.
- Mark the OneDrive camera folder **Always keep on this device** and test at least one cloud-originated image, thumbnail, full view, and download.
- This workspace is not a Git repository, so `git diff --check` was not applicable.
