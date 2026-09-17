# Configurable reduced JPEG decoding

- Request: implement benchmarked reduced JPEG decode as a Management option and deploy to NAS.
- Changed ThumbnailService, ThumbnailQueueSettings, DatabaseInitializer, AdminController, GalleryController, AdminIndexViewModel and Gallery/Collections/picker/Admin views. Added full/jpeg-idct selection, strict validation, SQLite persistence, startup loading and immediate runtime update after save.
- Default remains full; Faster JPEG is selectable in Management > System > Thumbnail decoding. No production DB or settings replacement. Cache modes are separate and versioned URLs retain their requested processing mode even after the administrator switches settings. Non-JPEG signature falls back to normal decoding. Final orientation/Max resize/WebP quality remain the same.
- Extended FileSystemVisibilityHarness with reduced JPEG resolution, EXIF orientation 6 output dimensions, PNG fallback and cache mode checks. Harness and Release build/publish passed. No subjective visual quality or authenticated Management POST test performed.
- Backed up NAS application to web-data/backups/20260906-232601-thumbnail-option, deployed Windows-IIS output while briefly offline, preserving appsettings.json/web.config/database/keys. Removed app_offline.htm after copy.
- Live health returned status ok and login HTTP 200; published application hash comparison found zero mismatches. User must select Faster JPEG and Save in Management; reload existing gallery pages to request the new cache version. Existing cached thumbnails remain on disk.
