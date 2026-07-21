# ListenShelf — Audiobook Player

ListenShelf is a private, offline-first audiobook library and player. It began as a focused Windows M4B playback slice built with Avalonia and .NET 10; the architecture keeps macOS, Linux, and possible future mobile clients open.

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

The preliminary Windows app has Library, Player, and Settings sections. First-run setup saves a choice between Player Only Mode and a managed library. Player Only Mode remembers original file locations and listening positions without copying files or offering metadata editing. Managed Library mode creates verified copies without changing the originals and supports editable book details and locally cached PNG, JPEG, or WebP covers. Both modes prevent repeat imports from the same location and play cataloged books directly.

Managed libraries remain isolated while managed data exists. Selecting Player Only Mode from a managed library explains that a permanent switch will eventually require export followed by deliberate deletion, then offers a separate temporary player window. Files and playback activity in that temporary session are not saved.

Managed-book editing includes an optional [Open Library](https://openlibrary.org/) lookup. Searches are sent directly from the desktop app with no ListenShelf account or central server; only the text entered in the search box is transmitted. The user chooses a result, reviews the populated fields, and decides whether an available cover should be saved into ListenShelf's local cover cache. Manual metadata remains editable and audiobook-specific fields are not replaced by print-book search results.

The player supports local `.m4b`, `.m4a`, and `.mp3` audiobooks and provides play/pause, seeking, 15-second rewind, 30-second forward, playback-speed selection, volume, elapsed/remaining time, and automatic per-file position persistence in a local SQLite database. When a file contains embedded chapters, ListenShelf shows the chapter list, tracks the current chapter, and provides direct previous/next chapter navigation.

Run it from the repository root:

```powershell
dotnet run --project src/ListenShelf.Desktop/ListenShelf.Desktop.csproj
```

Create all Windows x64 release assets from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build\Publish-WindowsRelease.ps1
```

## License status

ListenShelf does not yet have a project license. The source is visible for this alpha, but no open-source reuse license has been selected. Distributed third-party components retain their own licenses as listed in `THIRD-PARTY-NOTICES.txt`.
