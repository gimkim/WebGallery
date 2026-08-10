# Gallery UX overhaul

## Date and time

2026-08-11 00:23:22 +07:00 (Asia/Bangkok)

## Request / intent

Push the current source, then implement the complete proposed Gallery UX set: current-folder search/filtering, contextual selection, responsive toolbar and sorting, improved Grid/List cards and list header, breadcrumb/content summaries, and richer full-viewer navigation.

## Concepts and rules established

- Search and All/Folders/Images/Other-files filtering operate only on the already rendered current folder and must not add filesystem scans or MVC requests.
- Selection actions replace the normal toolbar only while files are selected. Select visible excludes filtered-out files, while the total still includes any earlier hidden selections.
- Touch long-press toggles file selection after 520ms and cancels when movement indicates scrolling.
- List columns stay below the live measured sticky-toolbar height and degrade from Modified to Size on narrow screens.
- Breadcrumb collapsing is applied only after constructing the already-scoped private/share/collection path list.
- Viewer neighbor prefetch is sequential, low priority, abortable, skipped for Save-Data/2G, rejected above a declared 96 MB per neighbor, and stored in the existing bounded full-Blob LRU.

## Files changed

- `Views/Gallery/Index.cshtml`
  - Added scoped breadcrumb overflow, current-folder counts, Search/type controls, responsive sort select, contextual selection toolbar, sortable List header, item-kind metadata, icon-only card downloads, viewer metadata, counter, and filmstrip host.
- `wwwroot/js/site.js`
  - Added immediate filtering/result state, visible-only selection, Clear, toolbar/header measurement, mobile long-press, responsive sort navigation, viewer chrome/filmstrip, bounded neighbor prefetch, and fitted-view swipe navigation.
- `wwwroot/css/site.css`
- `wwwroot/css/site-modern.css`
  - Added equivalent functional layout and responsive behavior for both themes, with theme-appropriate surfaces and selection/viewer presentation.
- `AGENTS.md`
- `.agents/PROJECT_NOTES.md`
  - Recorded the new durable interaction, performance, and scope rules.

## Validation actually performed

- The pre-edit `main` state (`0b0462b`) was already synchronized with `origin/main`.
- `node --check wwwroot/js/site.js` passed.
- `dotnet build WebGallery.csproj -c Release --no-restore` passed with 0 warnings and 0 errors.
- Filesystem visibility, login/security limiter, and share-audit self-test harnesses passed.
- NuGet vulnerable-package audit reported no vulnerable direct or transitive packages from the configured sources.
- Required markup/JavaScript marker assertions and Retro/Modern selector-count parity assertions passed.
- `git diff --check` passed with only existing CRLF conversion notices.
- The BrowserCacheProbeHarness was not counted as a completed test: it is a persistent local web server intended for a real browser probe. The first combined validation launch left its process listening on port 54137; that exact workspace-owned process was identified and stopped before continuing.
- Release publish completed at `publish/20260811_002322`.
- The prior IIS deployment was backed up to `backup/20260811_002322_pre-gallery-ux` (146 files).
- Release output was copied to `C:\Web\imagegallery` with `appsettings.Development.json` excluded. Publish/deploy SHA-256 matched for `WebGallery.dll`, `site.js`, `site.css`, and `site-modern.css`.
- Anonymous live requests to `/Gallery/` and `/Gallery/Collections` returned the expected HTTP 302 redirects to application Login.
- Public `site.js`, `site.css`, and `site-modern.css` returned HTTP 200 and their SHA-256 hashes matched the deployed Release output.
- Confirmed the deployment contains neither `app_offline.htm` nor `appsettings.Development.json` after completion.

## User-visible result

- Gallery now supports immediate filename search and type filtering with counts and a no-results state.
- Selection tools appear contextually, with Select visible, Clear, Download selected, desktop range semantics over visible cards, and touch long-press.
- Toolbar controls reorganize responsively; List has an aligned sticky sortable header; Grid/List downloads use icons.
- Breadcrumbs compact long paths safely and the heading shows current-folder folder/image/other-file counts.
- Full viewer shows current/total, file information, a nearby-image filmstrip, abortable neighbor prefetch, and fitted-view swipe navigation while preserving cache-first loading, zoom, pinch, pan, arrows, and keyboard navigation.

## Remaining manual validation / uncertainty

- Authenticated visual and gesture validation remains manual for Retro/Modern desktop and mobile widths because the project workaround prohibits the affected Codex in-app browser.
- In particular, manually verify long breadcrumb menus, contextual toolbar height transition, List column alignment, touch long-press versus scroll, pinch/pan versus swipe, filmstrip loading, and low-priority neighbor behavior with very large originals.
