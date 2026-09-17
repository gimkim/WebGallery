# Use retry gaps for medium background work

2026-09-09 Asia/Bangkok.

- Request: when all remaining small thumbnails are deferred, use available background slots for medium instead of waiting idle.
- Changes: Services/GalleryIndexService.cs adds HasRunnableSmall and changes the medium query gate to exclude future retry deadlines; Services/BackgroundThumbnailService.cs and ThumbnailService.cs use it at phase/feed/job boundaries. Medium candidates still require their own small signature ready. Due small retries stop new medium feeding; active medium may complete. On-demand preemption unchanged.
- Tests: tests/GalleryIndexHarness/Program.cs now verifies eligible small blocks medium, only deferred small permits medium for another ready-small image, and advancing retry eligibility blocks medium again. Full harness passed, including background generation, index refresh and ownership tests. Both Release publish profiles passed.
- Deployment: Windows NAS backup/copy/hash verified (backup timestamp in session tool output), preserved configs/persistent state, LAN-pinned HTTPS health ok. No Linux runtime deploy, cache purge, Git push or authenticated NAS UI test.
- Documentation: AGENTS.md and .agents/PROJECT_NOTES.md explicitly supersede the earlier deferred-small gate. Counter semantics remain total unfinished, not merely eligible work.
