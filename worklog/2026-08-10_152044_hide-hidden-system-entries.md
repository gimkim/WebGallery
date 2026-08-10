# Hide Windows hidden/system gallery entries

- Date/time: 2026-08-10 15:20:44 Asia/Bangkok (UTC+07:00)
- User request: Hide hidden/system files and folders such as `System Volume Information` and `thumb.db` from the gallery.
- Concepts/rules established: Entries with the Windows `Hidden` or `System` attribute are treated as private filesystem metadata and are hidden everywhere. `Thumbs.db` and the common singular `Thumb.db` spelling are ignored case-insensitively. Failed/disappearing attribute or directory enumeration is skipped safely. The policy applies to listings, folder covers, collection/share roots, direct file/thumbnail/selected-download endpoints, and streamed recursive ZIPs.
- Files changed: `Services/FileSystemService.cs`, `Controllers/GalleryController.cs`, `Controllers/CollectionsController.cs`, `tests/FileSystemVisibilityHarness/FileSystemVisibilityHarness.csproj`, `tests/FileSystemVisibilityHarness/Program.cs`, `AGENTS.md`, `.agents/PROJECT_NOTES.md`.
- Validation performed:
  - `dotnet build WebGallery.csproj -c Release` - passed with 0 warnings and 0 errors.
  - `dotnet run --project tests\\FileSystemVisibilityHarness\\FileSystemVisibilityHarness.csproj -c Release` - passed; hidden/system files, both thumbnail database spellings, and the system directory were omitted/rejected.
  - `dotnet run --project tests\\LoginAttemptLimiterHarness\\LoginAttemptLimiterHarness.csproj -c Release` - passed.
  - `node --check wwwroot\\js\\site.js` - passed.
  - `dotnet list WebGallery.csproj package --vulnerable --include-transitive` - no vulnerable packages reported.
  - `git diff --check` - passed; only normal LF-to-CRLF warnings were reported by Git for existing Markdown/source files.
- Deployment: Created backup `backup\\2026-08-10_1518_pre-hidden-system-filter\\` containing the prior IIS deployment and a SQLite `.backup`; `PRAGMA integrity_check` returned `ok`. Published to a temporary staging directory, copied to `C:\\Web\\imagegallery` while preserving external data/keys, verified the deployed `WebGallery.dll` SHA-256 (`A318E1C2829CCF03E17A5A305F9488035EB2C7772D9CEA1B65644D0B0B48E258`), and removed the maintenance marker. `https://gimgim.ddns.net:45570/Gallery` followed to the live Sign in page with HTTP 200, the maintenance marker is absent, and the production database integrity check remains `ok`.
- User-visible result: Hidden/System entries and Windows thumbnail metadata no longer appear or download from the deployed Gallery, including folders such as `System Volume Information`.
- Remaining uncertainty: No authenticated visual browser session was used; live validation covered the deployed application response and static/source self-test. A signed-in UI check can confirm the filtered list/ZIP behavior if needed.
