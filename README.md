# ListenShelf — Audiobook Player

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)

ListenShelf is a free and open-source, privacy-first audiobook library and player. It began as a focused Windows M4B playback slice built with Avalonia and .NET 10; the architecture keeps macOS, Linux, and possible future mobile clients open.

> **Alpha:** ListenShelf is early software. Windows preview downloads are available from [GitHub Releases](https://github.com/apelpapa/ListenShelf-Audiobook-Player/releases).

## Repository layout

```text
src/
  ListenShelf.Core/            Audiobook domain rules and models
  ListenShelf.Application/     Use cases and application-owned interfaces
  ListenShelf.Playback/        Audio-engine implementations
  ListenShelf.Infrastructure/  Persistence, metadata, and filesystem services
  ListenShelf.Desktop/         Shared Avalonia desktop application
tests/                         Test projects, added alongside behavior
```

`ListenShelf.slnx` is the solution entry point. Package versions and common .NET settings are managed at the repository root.

## Current preview

The preliminary Windows app has Library, Player, Storage Care, and Settings sections.
ListenShelf maintains one application-managed library: adding an audiobook
creates a SHA-256-verified copy in ListenShelf's library while leaving the
original source file untouched. Library entries support editable book details
and locally cached PNG, JPEG, or WebP covers. Byte-identical audiobooks are
recognized by SHA-256 even after renaming or moving the source file; duplicate
imports identify the existing book and keep its metadata and listening data
without creating another copy. Different bytes, including different recordings,
remain separate even if the title or source filename is the same. Removing a book
requires confirmation and permanently deletes its ListenShelf-managed audio,
cached cover, metadata, bookmarks, and listening progress together; the original
source file is never a deletion target.

Imports show the current filename, book count, fingerprint checks, copying and SHA-256 verification
byte progress, and overall batch progress directly in the Library. **Cancel
import** stops unfinished work and removes that attempt's partial copy; books
already added stay in the library and original files remain untouched. A book
already in the final catalog-save step finishes safely before cancellation takes
effect. A normal window close waits for that cleanup or final save. A dismissible
summary lists added, duplicate, failed, canceled, and unprocessed files, with
per-file details and Storage Care guidance if an unfinished copy could not be
removed. Playback can continue while importing.

Fingerprints are saved with newly verified imports. Older books are fingerprinted
on demand only when their recorded size matches an incoming file, not during
startup. An existing fingerprint match is checked against the managed file
before skipping the import. Missing, unreadable, or changed candidate files
produce a clear error instead of claiming a healthy duplicate. Existing duplicate
catalog entries are not automatically merged or deleted. This is exact-file
matching, not audio similarity: retagging or re-encoding a file changes its bytes.

The library includes instant local search across titles, subtitles, authors,
series, narrators, genres, publishers, identifiers, publication details, and
filenames. Multiple words can match different fields, and the filtered results
flow through list, tile, and grouped views without sending library data anywhere.
Library controls can sort by title, author, numeric series/book order, recently
played, date added, or listening progress, and filter to All, Not started,
In progress, or Finished. Sort and status choices are remembered between launches
and included in local backups. Filters combine with search; group counts and
previews use only matching books, and stacks follow the sort order of their first
matching book. Finished means the saved position has reached the known duration;
rewinding back into a book makes it In progress again.

On startup, ListenShelf performs a read-only managed-storage integrity check.
The same check can be run again from Storage Care to find catalog entries with
missing files, unreferenced files or folders, unsafe catalog paths, and
unfinished `.importing` files that have been inactive for at least 24 hours.
The report never moves, recovers, or deletes files automatically. It quietly
shows an exclamation mark beside Storage Care when something needs attention;
there is no startup warning dialog. Supported orphaned audiobooks can be
recovered into the catalog, while unneeded orphan files, folders, and stale
imports can be permanently cleaned up only after an inline confirmation.
Recovery reuses an unreferenced ListenShelf book folder when possible;
otherwise it verifies a new managed copy before removing the oddly placed
orphan. ListenShelf refuses cleanup requests outside managed storage, against
a cataloged audiobook, or through a filesystem link or junction.

Storage Care also includes **Verify library files**, a separate manual check for
one selected book or the entire managed library. It compares file contents with
saved SHA-256 fingerprints and reports **Verified unchanged**, **Changed**,
**Missing**, **Unreadable**, or **No verification baseline**. A changed file may
reflect corruption or an external edit; an unchanged fingerprint is not a test
of audio playability. Older or recovered books with no fingerprint are never
claimed to be verified, and scanning does not create or replace their baselines.
Scans show byte and batch progress, can be canceled, and retain completed results.
Findings remain visible in Storage Care for the current session; checking another
book or canceling does not clear an earlier finding. Results are dated snapshots,
not continuous monitoring. Playback remains available, while conflicting imports,
removals, backups, and recovery operations are unavailable until the scan ends. A normal
window close cancels the scan and waits for its file handles to close. No scan
runs automatically at startup, and verification never deletes or repairs files.

For missing or changed books, **Storage Care → Repair managed copy** lets you
select an original file or a known-good copy and then explicitly confirm repair.
The source must match the book's saved SHA-256 fingerprint exactly; a different
edition, re-encoded audio, or even changed embedded tags will be refused. Repair
copies the source and verifies the staged copy before replacing the managed file
at its existing path. Book identity, metadata, cover, bookmarks, and listening
position stay intact, and the source is never modified. Books without a saved
fingerprint require a known-good library backup instead; repair never invents a baseline.

If the book is loaded, repair unloads it (stopping its sleep timer) and reopens
it paused at its saved position. Other library-changing operations are blocked
during repair. Copying and verification show byte progress and can be canceled;
once final replacement starts, a normal window close waits for it to finish.
Any displaced managed file remains as a **Retained repair copy** in the storage
check below, requiring inline-confirmed cleanup. Interrupted repair files also
remain visible there if cleanup or replacement could not finish. Neither is
deleted automatically. Full library backups include these retained files.
See [managed-file repair and testing](docs/MANAGED_FILE_REPAIR.md) for details.

Managed-book editing includes an optional [Open Library](https://openlibrary.org/) lookup. Searches are sent directly from the desktop app with no ListenShelf account or central server; only the text entered in the search box is transmitted. The user chooses a result, reviews the populated fields, and decides whether an available cover should be saved into ListenShelf's local cover cache. Manual metadata remains editable and audiobook-specific fields are not replaced by print-book search results.

The player supports local `.m4b`, `.m4a`, and `.mp3` audiobooks and provides play/pause, seeking, configurable rewind and forward intervals, playback-speed selection, volume, elapsed/remaining time, a sleep timer, and automatic per-file position persistence in a local SQLite database. Per-audiobook bookmarks can save the current timestamp with an optional name and note, retain the chapter context, and later be jumped to, edited, or deleted without modifying the audiobook file. Playback speed, volume, and skip intervals are remembered globally between launches. Under **Settings → Playback**, rewind and forward can each be set to 1–600 whole seconds; the defaults remain 15 seconds backward and 30 seconds forward. These intervals apply immediately to player buttons, keyboard shortcuts, and Windows headphone/media controls. On startup, ListenShelf restores those player settings, loads the most recently played available audiobook at its saved position, and opens the Player without starting playback. Space or K toggles playback, Left Arrow or J rewinds, and Right Arrow or L moves forward. On Windows, keyboard, headset, and other media buttons continue to control the loaded book while ListenShelf is minimized. When a file contains embedded chapters, ListenShelf discovers them during loading so the chapter selector and previous/next controls are ready before Play, tracks the current chapter, and provides direct chapter navigation.

Below the player timeline, **Listening time left** estimates the remaining
listening time at the selected playback speed, excluding pauses. For example,
6 hours of audiobook time becomes about 4 hours at 1.5×. The estimate updates
when you seek or change speed, while the timeline and saved positions remain
in original audiobook time. It also works while paused, including after restoring
a book with a known duration; an unknown duration shows a dash instead of a
misleading zero.

The chapter controls also show **Chapter time left**, adjusted for the selected
speed and updated as you listen or seek between chapters. Missing chapter durations
use the next chapter's start, or the known book ending for the final chapter.
The estimate stays hidden for books without chapters or usable chapter boundaries.
Chapter navigation and original audiobook timestamps remain unchanged.

Settings can export the entire local library as one versioned
`.listenshelf-backup` file. It contains a consistent database snapshot,
managed audiobooks, covers, settings, bookmarks, progress, and recoverable
orphaned storage. Every entry is size-checked and SHA-256 verified before a
restore. Restoring is an explicit full replacement rather than an ambiguous
merge; ListenShelf first creates a separate backup of the current library,
stages and validates the selected backup, rebases its managed paths, and rolls
back the live directory if replacement fails. Backups remain local and are not
uploaded. The format is documented in
[`docs/BACKUP_FORMAT.md`](docs/BACKUP_FORMAT.md).

The SQLite catalog now has explicit numbered migrations. ListenShelf checks
database integrity and compatibility before opening the normal interface,
creates a database safety copy before an upgrade, and applies each migration
as a transaction. A damaged, inaccessible, failed-to-migrate, or newer-version
database opens a dedicated recovery screen instead of being silently replaced.
From there, a user can retry, inspect the data folder, restore a validated
backup, or—when actual damage is detected—preserve the damaged database and
rebuild a basic catalog from recognizable managed audiobook folders. The
database policy and recovery behavior are documented in
[`docs/DATABASE_SAFETY.md`](docs/DATABASE_SAFETY.md).

### Data preservation

ListenShelf stores its database, managed audiobook copies, covers, settings,
bookmarks, listening progress, and local diagnostic logs outside the installed
application. The data root is `%LocalAppData%\ListenShelf` on Windows,
`~/Library/Application Support/ListenShelf` on macOS, and
`$XDG_DATA_HOME/ListenShelf` (or `~/.local/share/ListenShelf`) on Linux.
The installer owns only the application files under `Program Files`, so upgrading
or uninstalling ListenShelf leaves the library and listening data in place. The
release build performs a packaging safety check and stops if the Windows
installer is changed to claim or delete those user-data directories.

Run it from the repository root:

```powershell
dotnet run --project src/ListenShelf.Desktop/ListenShelf.Desktop.csproj
```

Run the automated tests:

```powershell
dotnet test ListenShelf.slnx
```

The persistence and library tests use isolated temporary databases and files;
they do not read from or write to your actual ListenShelf library.

Create all Windows x64 release assets from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build\Publish-WindowsRelease.ps1
```

Early macOS and Linux work is kept separate from releases. The repository now
has native-runner test-build packaging, a manual GitHub Actions workflow, and a
synthetic M4B/M4A/MP3 smoke-media generator. The macOS and Linux test packages
carry private architecture-matched LibVLC runtimes; testers do not install VLC
or .NET separately. The current limitations, commands, data locations, and
real-machine pass criteria are in
[`docs/CROSS_PLATFORM_TEST_BUILDS.md`](docs/CROSS_PLATFORM_TEST_BUILDS.md).

## Free software and optional skins

Copyright © 2026 Abel Papazian.

ListenShelf's application code and bundled free themes are licensed under the
[GNU General Public License version 3 only](LICENSE). You may use, study,
modify, and redistribute them—including commercially—under the GPL's terms.
Distributed third-party components retain their own licenses as listed in
[`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt).

The complete player is intended to remain free, with no advertisements or paid
functional features that do not create an ongoing cost to provide. If
monetization is introduced for a full release, the current plan is to offer
optional official cosmetic skins. The standard light and dark appearances will
remain included for free. Official paid skin packages will be distributed
separately under their own asset licenses and are not part of this repository
unless explicitly stated otherwise.

The GPL covers the software, not permission to present a fork as the official
ListenShelf product. The ListenShelf name, logo, icon, and other source-identifying
branding remain governed by the [`TRADEMARKS.md`](TRADEMARKS.md) brand policy.
