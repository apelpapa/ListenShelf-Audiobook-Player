# ListenShelf Roadmap and To-Do List

This is the general backlog for ListenShelf. Add new work here, place it under the most appropriate priority, and move it when priorities change.

## Priority guide

- **P0 — Critical:** Data safety, core correctness, and problems that can make ListenShelf unusable.
- **P1 — Essential:** High-value work expected before a broad beta or cross-platform release.
- **P2 — Important:** Meaningful improvements that should follow the essential foundation.
- **P3 — Enhancement:** Valuable features and polish that are not release blockers.
- **P4 — Future:** Long-term expansion and ideas that are intentionally deferred.

## P0 — Critical

### Data safety and ownership

- [x] On import, create and verify a ListenShelf-managed copy while leaving the user's original source file untouched.
- [x] Keep user data outside installer ownership and fail Windows packaging if an upgrade or uninstaller could claim or delete it.
- [x] Treat book removal as one operation that deletes the catalog entry and its ListenShelf-managed copy together.
- [x] Require strong confirmation before removing a book and permanently deleting its ListenShelf-managed copy.
- [x] Verify that every audiobook file and folder in managed storage is referenced by the catalog, including detection of orphaned and stale import files.
- [x] Provide explicit recovery or confirmed cleanup for orphaned managed files; never silently delete a potentially recoverable audiobook.
- [x] Export managed audiobooks with their metadata, covers, bookmarks, and listening progress.
- [x] Add database backup support.
- [x] Add database restore support.
- [x] Introduce explicit, versioned database migrations.
- [x] Add a recovery path for a damaged or unreadable database.
- [ ] Require no known data-loss defects before beta.

### Core reliability

- [ ] Test repeated shutdown, restart, resume, and progress-saving behavior.
- [ ] Test long-duration audiobook playback and seeking.
- [ ] Test books with chapters and books without chapters.
- [ ] Test replaying an audiobook after it has completed.
- [x] Test Unicode audiobook paths.
- [ ] Test long and unusual file paths.
- [ ] Test operation without an internet connection.
- [ ] Improve user-facing playback errors.
- [x] Improve user-facing LibVLC loading and native-library errors.
- [x] Improve user-facing database errors with distinct recovery guidance for damage, access failures, failed migrations, and newer database versions.
- [ ] Improve user-facing filesystem errors.

### Permanent automated tests

- [x] Create a permanent automated test project.
- [x] Test fresh database creation and current schema.
- [x] Test compatibility with the pre-managed-only database schema.
- [x] Test fresh, legacy, repeat, failed, damaged, structurally incomplete, and newer-version database startup paths.
- [x] Test damaged-database preservation, managed-catalog rebuilding, and recovery-mode backup restore.
- [x] Test library importing.
- [x] Test duplicate detection.
- [x] Test managed-copy verification and integrity.
- [x] Test metadata and cover persistence.
- [x] Test listening-position persistence.
- [x] Test global playback-settings persistence.
- [x] Test bookmark creation, editing, ordering, and deletion.
- [x] Test managed-book removal, related-data cleanup, path safety, and interrupted-removal recovery.
- [x] Test managed-storage checks for missing files, orphaned paths, stale imports, unsafe catalog paths, and journaled removals.
- [x] Test orphaned audiobook recovery, stale-import cleanup, confirmed folder cleanup, and refusal to delete cataloged audiobooks.
- [x] Test versioned local backup creation, full integrity validation, path-rebased restore, pre-restore safety backups, and tamper rejection.
- [ ] Test bookmark jumping through the player.
- [ ] Test missing-file and relinking behavior.
- [ ] Test M4B playback behavior.
- [ ] Test M4A playback behavior.
- [ ] Test MP3 playback behavior.
- [ ] Test embedded chapter discovery before Play.
- [x] Add a deterministic, synthetic sample-media generator with M4B, M4A, MP3, chapter, and Unicode-name cases.

## P1 — Essential

### Library management

- [ ] Relink an audiobook that has moved or is missing from its saved location.
- [x] Remove a book by deleting its catalog entry, managed audiobook copy, cached cover, metadata, bookmarks, and listening progress as one confirmed operation.
- [x] Add a read-only managed-storage integrity check for missing, unreferenced, unsafe, and stale import paths.
- [x] Add a non-blocking Storage Care area with an attention indicator, orphan recovery, and inline-confirmed cleanup.
- [ ] Persist managed-file checksums and add audiobook corruption detection.
- [ ] Improve duplicate detection and duplicate-import messages.
- [ ] Add clear progress and error reporting for large imports.

### Library search, sorting, and filters

- [ ] Search by title.
- [ ] Search by author.
- [ ] Search by series.
- [ ] Search by narrator.
- [ ] Search by filename.
- [ ] Sort by title.
- [ ] Sort by author.
- [ ] Sort by series order.
- [ ] Sort by recently played.
- [ ] Sort by date added.
- [ ] Sort by listening progress.
- [ ] Filter books that have not been started.
- [ ] Filter books that are in progress.
- [ ] Filter completed books.

### Diagnostics and support

- [x] Add a local, privacy-respecting startup and native-runtime log with bounded rotation.
- [ ] Expand local logging to capture playback, import, backup, and recovery failures.
- [ ] Add an option to export a diagnostic report.
- [x] Document where ListenShelf stores its database, managed books, covers, logs, and settings on each desktop platform.
- [ ] Document backup, restore, export, relink, confirmed removal, and orphan-recovery behavior.
- [ ] Add a clear GitHub issue and feedback path.
- [ ] Add issue templates for bug reports and feature requests.

### Project and release policy

- [x] Choose and add a ListenShelf project license.
- [ ] Review third-party license obligations for Avalonia, LibVLCSharp, LibVLC, SQLite, and bundled components.
- [ ] Keep third-party notices current.
- [ ] Add a privacy statement covering local data and Open Library searches.
- [ ] Establish a versioning policy.
- [x] Establish a database-compatibility, migration, and recovery policy.
- [ ] Define supported operating-system versions and processor architectures.
- [ ] Publish known limitations.

### Continuous integration

- [x] Add GitHub Actions builds and tests for Windows, macOS, and Linux.
- [x] Add a manual GitHub Actions matrix that compiles, tests, and packages macOS and Linux test builds on native runners.
- [x] Generate checksums for every packaged artifact.
- [ ] Keep published source tags synchronized with downloadable builds.
- [x] Add reproducible native test-build and synthetic-media instructions.

## P2 — Important

### Playback improvements

- [ ] Add configurable rewind intervals.
- [ ] Add configurable forward intervals.
- [ ] Remember the selected rewind and forward intervals globally.
- [ ] Route selected intervals through player buttons, keyboard shortcuts, headphones, and media controls.
- [ ] Add smart rewind after longer pauses.

### Cross-platform architecture

- [x] Move the current Windows-specific desktop behavior behind platform interfaces.
- [x] Move Windows media-key registration behind a platform media-control interface.
- [ ] Add macOS media-control integration.
- [ ] Add Linux media-control integration.
- [x] Make the Windows native LibVLC dependency conditional so it is excluded from macOS and Linux builds.
- [x] Replace Windows-only icon and manifest assumptions with platform-specific packaging assets.
- [x] Keep storage locations behind cross-platform path services.
- [ ] Verify case-sensitive and case-insensitive path handling.
- [ ] Verify filesystem permissions and managed-copy behavior on each platform.
- [ ] Decide how native LibVLC will be supplied in supported macOS releases; test builds temporarily use an installed VLC app or an explicit runtime path.
- [ ] Decide whether supported Linux packages will bundle LibVLC or declare it as a system dependency; test builds temporarily use system packages.
- [ ] Ensure normal users can install and run ListenShelf without troubleshooting native libraries in a terminal.

### Cross-platform acceptance

- [ ] Launch successfully on Windows x64.
- [ ] Launch successfully on macOS Apple Silicon.
- [ ] Launch successfully on macOS Intel where supported.
- [ ] Launch successfully on Linux x64.
- [ ] Import audiobooks into the managed library on every supported platform.
- [ ] Play M4B, M4A, and MP3 files on every supported platform.
- [ ] Discover embedded chapters before Play.
- [ ] Seek, pause, resume, and replay completed books.
- [ ] Restore the previous audiobook and position without autoplay.
- [ ] Preserve playback speed and volume.
- [ ] Preserve bookmarks, covers, and metadata.
- [ ] Verify sleep-timer behavior.
- [ ] Verify keyboard, headphone, and media-button behavior.
- [ ] Verify Open Library search and cover downloading.
- [ ] Verify offline behavior apart from optional metadata searches.
- [ ] Verify light and dark themes.
- [ ] Verify layouts at supported scaling levels and display sizes.
- [ ] Complete real-machine testing on every supported operating system.

### Packaging

- [ ] Maintain the Windows portable single-file executable.
- [ ] Maintain the Windows portable ZIP package.
- [ ] Maintain the Windows installer.
- [x] Add native-runner packaging for unsigned macOS Apple Silicon test bundles.
- [x] Add native-runner packaging for unsigned macOS Intel test bundles.
- [ ] Package the unsigned macOS alpha in a DMG.
- [x] Provide ZIP packaging for unsigned macOS test bundles.
- [x] Document the one-time macOS Gatekeeper **Open Anyway** process.
- [x] Never instruct users to disable Gatekeeper globally.
- [ ] Consider a universal macOS application bundle after separate builds are reliable.
- [x] Add native-runner packaging for a Linux x64 portable test ZIP.
- [ ] Create a Debian/Ubuntu `.deb` package.
- [ ] Consider an RPM package after Debian-family packaging is reliable.
- [ ] Revisit AppImage when the packaging path is sufficiently mature.
- [ ] Produce separate artifacts for each operating system and processor architecture.
- [ ] Test clean installation.
- [ ] Test upgrades from existing alpha data.
- [ ] Test uninstallation without deleting user data.

## P3 — Enhancements

### Playback and interface

- [ ] Add A-B repeat.
- [ ] Add a mini-player.
- [ ] Add playback and listening statistics.
- [ ] Add equalizer controls.
- [ ] Add additional audio-enhancement controls.
- [ ] Add additional sleep-recovery intelligence.
- [ ] Add more library display and personalization options.

### Distribution polish

- [ ] Add macOS Developer ID signing if paid Apple Developer membership becomes worthwhile.
- [ ] Add macOS notarization if paid Apple Developer membership becomes worthwhile.
- [ ] Create a signed and notarized macOS DMG.
- [ ] Evaluate Windows code signing.
- [ ] Improve installer presentation and platform-native branding.

## P4 — Future

- [ ] Add automatic application updates.
- [ ] Build mobile applications.
- [ ] Add advanced statistics and listening insights.
- [ ] Add advanced audio processing.
- [ ] Evaluate cloud synchronization as an optional feature without making it required.
- [ ] Continue working toward every useful Smart AudioBook Player feature and beyond.
