# Private-only gallery, scoped sharing, and gallery interactions

- Date: 2026-08-07 11:39:18 +07:00 (Asia/Bangkok)

## Request and intent

- Remove the confusing per-folder `Public` / `Private` / `Unlisted` modes.
- Make every user's complete gallery private and accessible only after that user logs in.
- Retain sharing only as separately created, revocable unlisted links scoped to the selected folder and descendants.
- Make a shared folder the visible navigation root, without exposing `Home` or ancestor paths.
- Correct List checkbox layout and make file names downloadable.
- Add Explorer-like Grid image selection gestures and improve original-image viewer sizing.

## Durable concepts established

- There is no anonymous public-folder route or visibility selector. Legacy non-private folder-rule rows are converted to `Private` during database initialization.
- A share token is the only anonymous gallery entry point. Its linked folder is both the authorization boundary and the breadcrumb/back-navigation root.
- Grid image interaction is: single click selects exclusively, Ctrl+click toggles additive selection, Ctrl+Shift+click adds a range from the last selection anchor, and double-click opens the viewer. Checkbox clicks continue to toggle only their own file.
- List selection controls occupy a dedicated column, and the file name is a direct download link.
- Small originals are not enlarged. Oversized originals initially fit the viewport and can toggle to native `1:1` pixels with scrolling.

## Files changed

- `Controllers/GalleryController.cs`
- `Data/DatabaseInitializer.cs`
- `ViewModels/GalleryViewModels.cs`
- `Views/Gallery/Index.cshtml`
- `wwwroot/js/site.js`
- `wwwroot/css/site.css`
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
- This worklog file

## Validation actually performed

- `dotnet build -c Release --no-restore`: passed with 0 warnings and 0 errors.
- `node --check wwwroot/js/site.js`: passed.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages reported from the configured sources.
- Local anonymous `/` returned a login redirect; the removed `/Gallery/Public` route returned 404.
- Local browser validation with generated Development data and three images confirmed Grid selection counts of 1 after single click, 2 after Ctrl+click, and 3 after Ctrl+Shift+click.
- Local browser double-click opened the full viewer. A 1920x1200 image initially rendered fitted at approximately 1235x772, and the `1:1` toggle rendered 1920x1200.
- A 280x175 test image was identified as smaller than the test viewport; the CSS/JavaScript path keeps native dimensions and hides the zoom toggle, but the browser automation measurement timed out while that image was loading, so this specific visual remains a manual regression check.
- Local List-view computed layout was `42px 66px minmax(...)`; the checkbox ended before the thumbnail began (`55.27px < 63.28px`), so they did not overlap. The name link pointed to the file download action.
- Local anonymous shared-root browser check showed breadcrumb text `Shared`, zero parent cards, and no `Home`. Inside `Shared/Subfolder`, the breadcrumb was `Shared / Subfolder` and the back card targeted `Shared`.
- A share token opened anonymously inside its scope, returned 404 for a sibling path, and returned 404 after revocation.
- Publish output was copied to `C:\Web\imagegallery` while `app_offline.htm` was present. SHA-256 hashes matched for `WebGallery.dll`, `wwwroot/js/site.js`, and `wwwroot/css/site.css`; the offline file was then removed.
- Live `https://gimgim.ddns.net:45570/Gallery/` returned 302 to the application login page, following the redirect returned 200, CSS and JavaScript returned 200, and the removed live Public route returned 404.
- Production SQLite query returned only `AccessMode=0` (`Private`) for the two retained legacy `FolderRules` rows.

## User-visible result

- Users now see only their private gallery and a Share button for managing scoped links.
- Anonymous share visitors begin at the shared folder name and cannot see or navigate to ancestors.
- List selection no longer overlays thumbnails, file names download directly, Grid selection follows the requested gestures, and the full viewer supports fit-to-screen and native-pixel viewing.

## Remaining manual validation

- After signing in on the public IIS endpoint, perform a final visual pass with the user's real image collection, especially a genuinely small image in the full viewer and Ctrl/Ctrl+Shift selection with the preferred physical keyboard/browser.
- The local Development fixture remains under source `App_Data` and is not part of the IIS publish output or production database.
