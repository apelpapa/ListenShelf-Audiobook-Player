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

- [ ] Ensure library operations never modify the user's original source files.
- [ ] Ensure upgrades and uninstallers never delete a user's library or listening data.
- [ ] Clearly separate removing a book from the catalog from deleting a ListenShelf-managed copy.
- [ ] Require strong confirmation before deleting a ListenShelf-managed copy.
- [ ] Export managed audiobooks with their metadata, covers, bookmarks, and listening progress.
- [ ] Add database backup support.
- [ ] Add database restore support.
- [ ] Introduce explicit, versioned database migrations.
- [ ] Add a recovery path for a damaged or unreadable database.
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
- [ ] Improve user-facing LibVLC loading and native-library errors.
- [ ] Improve user-facing database and filesystem errors.

### Permanent automated tests

- [x] Create a permanent automated test project.
- [x] Test fresh database creation and current schema.
- [x] Test compatibility with the pre-managed-only database schema.
- [ ] Test explicit versioned migrations after migration infrastructure exists.
- [x] Test library importing.
- [x] Test duplicate detection.
- [x] Test managed-copy verification and integrity.
- [x] Test metadata and cover persistence.
- [x] Test listening-position persistence.
- [x] Test global playback-settings persistence.
- [x] Test bookmark creation, editing, ordering, and deletion.
- [ ] Test bookmark jumping through the player.
- [ ] Test missing-file and relinking behavior.
- [ ] Test M4B playback behavior.
- [ ] Test M4A playback behavior.
- [ ] Test MP3 playback behavior.
- [ ] Test embedded chapter discovery before Play.
- [ ] Add deterministic sample media for integration tests where licensing permits.

## P1 — Essential

### Library management

- [ ] Relink an audiobook that has moved or is missing from its saved location.
- [ ] Remove a book from the ListenShelf catalog without deleting the audiobook file.
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

- [ ] Add a local, privacy-respecting application log.
- [ ] Add an option to export a diagnostic report.
- [ ] Document where ListenShelf stores its database, managed books, covers, logs, and settings.
- [ ] Document backup, restore, export, relink, removal, and deletion behavior.
- [ ] Add a clear GitHub issue and feedback path.
- [ ] Add issue templates for bug reports and feature requests.

### Project and release policy

- [x] Choose and add a ListenShelf project license.
- [ ] Review third-party license obligations for Avalonia, LibVLCSharp, LibVLC, SQLite, and bundled components.
- [ ] Keep third-party notices current.
- [ ] Add a privacy statement covering local data and Open Library searches.
- [ ] Establish a versioning policy.
- [ ] Establish a database-compatibility and migration policy.
- [ ] Define supported operating-system versions and processor architectures.
- [ ] Publish known limitations.

### Continuous integration

- [ ] Add GitHub Actions builds and tests for Windows.
- [ ] Expand the GitHub Actions matrix to macOS and Linux when platform work begins.
- [ ] Generate checksums for every published artifact.
- [ ] Keep published source tags synchronized with downloadable builds.
- [ ] Add reproducible build instructions.

## P2 — Important

### Playback improvements

- [ ] Add configurable rewind intervals.
- [ ] Add configurable forward intervals.
- [ ] Remember the selected rewind and forward intervals globally.
- [ ] Route selected intervals through player buttons, keyboard shortcuts, headphones, and media controls.
- [ ] Add smart rewind after longer pauses.

### Cross-platform architecture

- [ ] Move Windows-specific behavior behind platform interfaces.
- [ ] Move Windows media-key registration behind a platform media-control interface.
- [ ] Add macOS media-control integration.
- [ ] Add Linux media-control integration.
- [ ] Make native LibVLC dependencies conditional by operating system and processor architecture.
- [ ] Replace Windows-only icon and manifest assumptions with platform-specific packaging assets.
- [ ] Keep storage locations behind cross-platform path services where necessary.
- [ ] Verify case-sensitive and case-insensitive path handling.
- [ ] Verify filesystem permissions and managed-copy behavior on each platform.
- [ ] Decide how native LibVLC will be supplied on macOS.
- [ ] Decide whether Linux packages will bundle LibVLC or declare it as a system dependency.
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
- [ ] Create an unsigned macOS Apple Silicon application bundle.
- [ ] Create an unsigned macOS Intel application bundle where supported.
- [ ] Package the unsigned macOS alpha in a DMG.
- [ ] Provide a macOS ZIP as an optional fallback.
- [ ] Document the one-time macOS Gatekeeper **Open Anyway** process.
- [ ] Never instruct users to disable Gatekeeper globally.
- [ ] Consider a universal macOS application bundle after separate builds are reliable.
- [ ] Create a Linux portable ZIP package.
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
