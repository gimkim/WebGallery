# Date Taken undated-file ordering

- Date: 2026-09-17 19:25 Asia/Bangkok. User requests dated files first sorted by Date Taken, undated files afterwards sorted by Date Modified.
- Changed FileSystemService.SortTaken to preserve the known/unknown grouping and use modified UTC ticks only as the undated group's ordering key. Both groups honor Asc/Desc, filename breaks ties; existing folders-first convention remains. No fabricated capture dates or index/cache resets. Sorting still precedes pagination and selected shares reuse the helper.
- Updated Gallery tooltip, AGENTS rule and DateTakenHarness regression assertions.
- Validation: DateTakenHarness passed mixed groups in both directions, newer modified timestamps cannot move undated files ahead of dated files,1001-row ordering and filename/modified disagreement, plus existing index/progress/re-index tests. Both publish profiles and git diff --check passed. No browser interaction test this small follow-up.
- NAS state-preserving deploy completed with application hash verification and preserved config; pinned HTTPS health checked. Linux output published only; no container deploy or Git push.
