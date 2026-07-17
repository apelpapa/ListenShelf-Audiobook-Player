# ListenShelf — Audiobook Player

ListenShelf is a private, offline-first audiobook library and player. The first milestone is a focused Windows M4B playback slice built with Avalonia and .NET 10; the architecture keeps macOS, Linux, and possible future mobile clients open.

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

The preliminary Windows app has Library, Player, and Settings sections. First-run setup saves a choice between a linked library (keep files where they are) and a managed library (let ListenShelf manage copies). The Library can import multiple `.m4b` files, remembers linked paths, creates verified managed copies without changing the originals, prevents repeat imports from the same location, and plays cataloged books directly.

The player can open one local `.m4b` file and provides play/pause, seeking, 15-second rewind, 30-second forward, playback-speed selection, volume, elapsed/remaining time, and automatic per-file position persistence in a local SQLite database.

Run it from the repository root:

```powershell
dotnet run --project src/ListenShelf.Desktop/ListenShelf.Desktop.csproj
```
