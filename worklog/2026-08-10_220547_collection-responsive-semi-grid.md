# Responsive semi-grid Collection cards

## Request / intent

Change the main Collections list from full-width rows to semi-grid cards. When a collection's Folders section is expanded, let that card grow only as much as its actual folder count needs.

## Files changed

- `Views/Collections/Index.cshtml`
  - Added folder-count metadata to each collection card and folder grid.
  - Capped the server-rendered initial folder-grid column count to the number of member folders.
- `wwwroot/js/site.js`
  - Capped every collection folder grid to the smaller of the saved density and actual folder count.
  - Added responsive collection-card span calculation, recomputed after collection/folder disclosure changes, density changes, and collection-list resize.
  - Expanded Folders use one base track per two visible folder columns, capped by available viewport tracks; collapsed cards and collapsed Folders return to one track.
- `wwwroot/css/site.css`
- `wwwroot/css/site-modern.css`
  - Added matching responsive semi-grid layout, card track spans, narrow-screen fallback, and wrapping collection headers in both themes.
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
  - Recorded the durable Collection sizing and density rules.

## Validation actually performed

- `node --check wwwroot/js/site.js` passed.
- `dotnet build WebGallery.csproj -c Release` passed with 0 warnings and 0 errors.
- `dotnet run --project tests/ShareAuditHarness/ShareAuditHarness.csproj -c Release` passed.
- `git diff --check` passed (Git reported only existing line-ending conversion notices).
- Release publish completed at `publish/20260810_220547`.
- Backed up the existing IIS deployment to `backup/20260810_220547_pre-collection-semi-grid` (146 files).
- Copied the Release publish to `C:\Web\imagegallery` while excluding `appsettings.Development.json`.
- SHA-256 matched between publish output and deployment for `WebGallery.dll`, `wwwroot/js/site.js`, `wwwroot/css/site.css`, and `wwwroot/css/site-modern.css`.
- Anonymous live request to `https://gimgim.ddns.net:45570/Gallery/Collections` returned the expected HTTP 302 login redirect.
- Live `https://gimgim.ddns.net:45570/Gallery/js/site.js` returned HTTP 200 and SHA-256 `BEEB4BC2CACC44D4A0C87A881D8726ADAF6BBF979B0A06C6F5F4FF76BA07B9D3`, matching the deployed publish.

## User-visible result

Collapsed Collections now share rows as responsive cards. Expanding a collection does not automatically make it full width: opening Folders grows only that card in proportion to the number of folders and current Folders-per-row preference, while small collections stay compact. Closing Folders contracts the card again.

## Remaining manual validation / uncertainty

- Authenticated visual testing of several collections with different folder counts, both themes, and mobile widths remains manual because this project avoids the in-app browser on the affected Codex desktop version.
