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
- [x] Test content-based duplicates across names and folders, legacy fingerprints, same-path replacement, listening-data preservation, cancellation, concurrent imports, migrations, and backup restore.
- [x] Test managed-copy verification and integrity.
- [x] Test manual file verification for single books and full libraries, read-only behavior, missing/changed/unreadable files, absent baselines, progress, cancellation, and retained findings.
- [x] Test byte-progress reporting, cancellation during copying and verification, completed-import preservation, cleanup failures, batch summaries, and delayed UI progress updates.
- [x] Test metadata and cover persistence.
- [x] Test listening-position persistence.
- [x] Test global playback-settings persistence.
- [x] Test bookmark creation, editing, ordering, and deletion.
- [x] Test quick bookmarks while playing/paused, at pending resume positions and chapter boundaries, with editor compatibility, per-book persistence, availability guards, and save-failure feedback.
- [x] Test bookmark search matching, empty/no-result states, clear/count notifications, filtered add/edit/delete/jump commands, and book-switch/unload resets without search-driven data or playback changes.
- [x] Test bookmark deletion undo for full-data restoration, filtered/empty lists, latest-deletion-only behavior, dismissal, failed delete/restore retry, stale callbacks, existing-version protection, and book/disposal cleanup without playback changes.
- [x] Test chapter title/number filtering, counts/clear, original-index navigation, unaffected Previous/Next, metadata refresh, stale-result rejection, navigation failures, and book/unload resets.
- [x] Test current-book indicator state labels, live play/pause updates, duplicate titles, refresh/filter/group behavior, loading failures, badge transfer/removal, stable card sizing, and disposal without indicator-driven playback or progress changes.
- [x] Test sidebar playback across every page, live title/status/labels, navigation without autoplay, busy/disposal guards, book switching/removal/load failure, and inline playback failure/retry without reloads or browsing changes.
- [x] Test managed-book removal, related-data cleanup, path safety, and interrupted-removal recovery.
- [x] Test managed-storage checks for missing files, orphaned paths, stale imports, unsafe catalog paths, and journaled removals.
- [x] Test orphaned audiobook recovery, stale-import cleanup, confirmed folder cleanup, and refusal to delete cataloged audiobooks.
- [x] Test versioned local backup creation, full integrity validation, path-rebased restore, pre-restore safety backups, and tamper rejection.
- [ ] Test bookmark jumping through the player.
- [x] Test fingerprint-matched repair of missing or changed managed files, listening-data preservation, mismatched sources, cancellation, retained copies, failed replacement, and repair UI confirmation/close-wait behavior.
- [ ] Test M4B playback behavior.
- [ ] Test M4A playback behavior.
- [ ] Test MP3 playback behavior.
- [ ] Test embedded chapter discovery before Play.
- [x] Add a deterministic, synthetic sample-media generator with M4B, M4A, MP3, chapter, and Unicode-name cases.

## P1 — Essential

### Library management

- [x] Repair a missing or changed managed audiobook from a fingerprint-matching source, preserving its catalog identity and listening data instead of relinking to an external file.
- [ ] Manually smoke-test repair in the Windows app, including reloading the current book paused and confirming retained-copy cleanup.
- [x] Remove a book by deleting its catalog entry, managed audiobook copy, cached cover, metadata, bookmarks, and listening progress as one confirmed operation.
- [x] Add a read-only managed-storage integrity check for missing, unreferenced, unsafe, and stale import paths.
- [x] Add a non-blocking Storage Care area with an attention indicator, orphan recovery, and inline-confirmed cleanup.
- [x] Persist SHA-256 fingerprints for verified imports and fingerprint older same-sized candidate books on demand.
- [x] Add an explicit managed-audiobook verification scan in Storage Care using saved fingerprints, with per-book or whole-library scope, progress, cancellation, and no automatic repair or baseline changes.
- [x] Detect byte-identical duplicate imports regardless of filename or location and identify the existing book without changing its listening data.
- [x] Add clear progress and error reporting for large imports, with copying/verification bytes, book counts, overall progress, and per-file results.
- [x] Allow safe import cancellation: keep completed books, remove unfinished copies, leave source files untouched, and wait for cleanup on a normal window close.

### Library search, sorting, and filters

- [x] Search by title.
- [x] Search by author.
- [x] Search by series.
- [x] Search by narrator.
- [x] Search by filename.
- [x] Sort by title.
- [x] Sort by author.
- [x] Sort by series order.
- [x] Sort by recently played.
- [x] Sort by date added.
- [x] Sort by listening progress.
- [x] Filter books that have not been started.
- [x] Filter books that are in progress.
- [x] Filter completed books.
- [x] Remember sort/status choices and combine them with search, list/tile views, and group stacks.
- [x] Add Ctrl+F to open/focus library search and select its text, plus Escape to clear only the focused search query while preserving browsing choices and playback.

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

- [x] Add GitHub Actions builds and tests for Windows, macOS, and Linux (currently manual-only).
- [x] Add a manual GitHub Actions matrix that compiles, tests, and packages macOS and Linux test builds on native runners.
- [x] Generate checksums for every packaged artifact.
- [ ] Keep published source tags synchronized with downloadable builds.
- [x] Add reproducible native test-build and synthetic-media instructions.

## P2 — Important

### Playback improvements

- [x] Add configurable rewind intervals.
- [x] Add configurable forward intervals.
- [x] Remember the selected rewind and forward intervals globally.
- [x] Route selected intervals through player buttons, keyboard shortcuts, headphones, and media controls.
- [x] Show estimated listening time remaining at the selected playback speed, updating on playback/seek/speed changes while retaining original timeline timestamps.
- [x] Show speed-adjusted current-chapter time remaining, following the playhead across chapter boundaries and hiding the estimate when chapter timing is unavailable.
- [x] Add a one-shot stop-at-end-of-chapter sleep option, with a fixed media-position target, safe timer-mode switching, and no cross-book or restart carryover.
- [x] Add custom sleep-timer durations of 1–1440 whole minutes, with input validation, safe replacement/cancel, and the existing countdown/add-time behavior.
- [x] Remember the last preset/custom sleep duration, offer explicit one-click reuse and custom-dialog prefill, include it in local backups, and never rearm automatically on launch.
- [x] Add Jump to time from the elapsed timestamp, with mm:ss/hh:mm:ss validation, book-length bounds, keyboard confirmation/cancel, and no automatic play/pause change.
- [x] Add a mute/unmute speaker button and M shortcut that preserve the selected/saved volume, restore the last audible level from zero, and leave system volume and playback state unchanged.
- [x] Add one-click quick bookmarks without a dialog, preserving the current/restored position and available chapter context, with optional names and notes editable afterward and no playback interruption.
- [x] Add live, case-insensitive current-book bookmark search by name/note, with matching counts, clear/no-results states, timestamp ordering, and a reset when loading or unloading a book.
- [x] Add inline, one-level bookmark deletion undo with full bookmark restoration, retry on failure, explicit dismissal, no timer, and no carryover across book loads or app sessions.
- [x] Add collapsible current-book chapter search by title or displayed number, with ordered results, clear/no-match states, explicit chapter navigation, and no changes to selection or playback while typing.
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
- [ ] Validate the private bundled LibVLC runtime on clean macOS Apple Silicon and Intel machines with no VLC installation.
- [ ] Validate the private bundled LibVLC runtime on clean Ubuntu and Linux Mint machines with no VLC installation.
- [ ] Ensure normal users can install and run ListenShelf without troubleshooting native libraries in a terminal; the ZIP test packages are not the final installer experience.

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

### Build automation

- [ ] Re-enable automatic GitHub Actions build-and-test checks on pushes to `main` and pull requests for Windows, macOS, and Linux; the workflow is currently manual-only.

## P3 — Enhancements

### Playback and interface

- [x] Add a collapsible keyboard-shortcut reference in Settings, covering implemented playback/search controls, Jump to time dialog keys, focus exceptions, and Windows media buttons.
- [x] Mark the currently loaded book in library list/tile cards with live playing/paused/ready status, retain the indicator through library refreshes and opened groups, and clear/transfer it on unload or book changes without changing card sizes.
- [x] Add a compact current-book sidebar panel with live title/status, shared Play/Pause, title navigation to Player, inline error feedback, and no automatic playback or page changes when controlling audio.
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
