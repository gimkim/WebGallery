# Grid bottom captions

2026-09-09 Asia/Bangkok.

- Request: align filenames at the bottom and prevent accidental filename downloads in Grid.
- Changed Views/Gallery/Index.cshtml to render an inert Grid caption separately from the existing List download link. Updated wwwroot/css/site.css to align caption/download on one bottom row and allow pointer hit testing through the media caption. Video badges stay above the name. Recorded the durable rule in AGENTS.md.
- Validation: Windows-IIS Release publish succeeded. Headless Edge test with representative image-card markup and real stylesheet measured caption bottom 8px from card bottom, hidden Grid filename link, and pointer hit on the image button through the caption. This was an isolated browser layout test, not an authenticated end-to-end gallery test.
- Deployed and hash-verified NAS application, preserving config/state; backup web-data/backups/20260909-164155-index-deploy. Remaining manual test: real gallery image/video cards and touch behavior. Linux output not updated in this session.
