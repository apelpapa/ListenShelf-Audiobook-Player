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
and locally cached PNG, JPEG, or WebP covers, and repeat imports from the same
source location are detected instead of creating another copy. Removing a book
requires confirmation and permanently deletes its ListenShelf-managed audio,
cached cover, metadata, bookmarks, and listening progress together; the original
source file is never a deletion target.

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

Managed-book editing includes an optional [Open Library](https://openlibrary.org/) lookup. Searches are sent directly from the desktop app with no ListenShelf account or central server; only the text entered in the search box is transmitted. The user chooses a result, reviews the populated fields, and decides whether an available cover should be saved into ListenShelf's local cover cache. Manual metadata remains editable and audiobook-specific fields are not replaced by print-book search results.

The player supports local `.m4b`, `.m4a`, and `.mp3` audiobooks and provides play/pause, seeking, 15-second rewind, 30-second forward, playback-speed selection, volume, elapsed/remaining time, a sleep timer, and automatic per-file position persistence in a local SQLite database. Per-audiobook bookmarks can save the current timestamp with an optional name and note, retain the chapter context, and later be jumped to, edited, or deleted without modifying the audiobook file. Playback speed and volume are remembered globally between launches. On startup, ListenShelf restores those player settings, loads the most recently played available audiobook at its saved position, and opens the Player without starting playback. Space or K toggles playback, Left Arrow or J rewinds, and Right Arrow or L moves forward. On Windows, keyboard, headset, and other media buttons continue to control the loaded book while ListenShelf is minimized. When a file contains embedded chapters, ListenShelf discovers them during loading so the chapter selector and previous/next controls are ready before Play, tracks the current chapter, and provides direct chapter navigation.

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
bookmarks, and listening progress under `%LocalAppData%\ListenShelf` on Windows.
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
