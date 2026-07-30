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

The preliminary Windows app has Library, Player, and Settings sections.
ListenShelf maintains one application-managed library: adding an audiobook
creates a SHA-256-verified copy in ListenShelf's library while leaving the
original source file untouched. Library entries support editable book details
and locally cached PNG, JPEG, or WebP covers, and repeat imports from the same
source location are detected instead of creating another copy.

Managed-book editing includes an optional [Open Library](https://openlibrary.org/) lookup. Searches are sent directly from the desktop app with no ListenShelf account or central server; only the text entered in the search box is transmitted. The user chooses a result, reviews the populated fields, and decides whether an available cover should be saved into ListenShelf's local cover cache. Manual metadata remains editable and audiobook-specific fields are not replaced by print-book search results.

The player supports local `.m4b`, `.m4a`, and `.mp3` audiobooks and provides play/pause, seeking, 15-second rewind, 30-second forward, playback-speed selection, volume, elapsed/remaining time, a sleep timer, and automatic per-file position persistence in a local SQLite database. Per-audiobook bookmarks can save the current timestamp with an optional name and note, retain the chapter context, and later be jumped to, edited, or deleted without modifying the audiobook file. Playback speed and volume are remembered globally between launches. On startup, ListenShelf restores those player settings, loads the most recently played available audiobook at its saved position, and opens the Player without starting playback. Space or K toggles playback, Left Arrow or J rewinds, and Right Arrow or L moves forward. On Windows, keyboard, headset, and other media buttons continue to control the loaded book while ListenShelf is minimized. When a file contains embedded chapters, ListenShelf discovers them during loading so the chapter selector and previous/next controls are ready before Play, tracks the current chapter, and provides direct chapter navigation.

Run it from the repository root:

```powershell
dotnet run --project src/ListenShelf.Desktop/ListenShelf.Desktop.csproj
```

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
