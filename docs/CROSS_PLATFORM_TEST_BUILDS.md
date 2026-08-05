# Cross-platform test builds

macOS and Linux support is provisional. These packages exist to expose technical
risks and gather real-machine results; they are not yet supported releases.

## Current runtime strategy

- The Avalonia desktop application and LibVLCSharp managed binding are shared.
- Windows packages continue to carry the Windows LibVLC native runtime.
- Linux test builds use the distribution's VLC 3 / LibVLC packages.
- macOS test builds look for VLC 3 at `/Applications/VLC.app`, then honor a
  custom `LISTENSHELF_LIBVLC_PATH` directory.
- ListenShelf shows a diagnostic startup screen when the native runtime cannot
  be loaded. It does not replace or delete the library after this failure.

This is a test-build strategy, not the final distribution decision. In
particular, requiring users to install VLC separately may not meet the standard
for a supported release.

## Data and logs

| Platform | ListenShelf data root |
| --- | --- |
| Windows | `%LocalAppData%\ListenShelf` |
| macOS | `~/Library/Application Support/ListenShelf` |
| Linux | `$XDG_DATA_HOME/ListenShelf`, or `~/.local/share/ListenShelf` |

The `Logs` child directory contains `listenshelf.log` and, after rotation,
`listenshelf.previous.log`. Logs are local and best-effort.

## Native packaging

Run the packaging script on the target operating system:

```powershell
./build/Publish-CrossPlatformTestBuild.ps1 -RuntimeIdentifier linux-x64
./build/Publish-CrossPlatformTestBuild.ps1 -RuntimeIdentifier osx-arm64
./build/Publish-CrossPlatformTestBuild.ps1 -RuntimeIdentifier osx-x64
```

It produces a self-contained .NET portable ZIP and SHA-256 checksum beneath
`artifacts/test-builds`. Linux packaging uses `zip` so executable permissions
survive extraction. macOS packaging creates a normal `.app` bundle and uses
`ditto` on macOS so its metadata and executable bit survive the ZIP.

The GitHub Actions workflow is manual-only. It compiles, tests, and packages on
native macOS and Linux runners, then stores workflow artifacts. It does not
publish a GitHub Release.

## Real-machine pass criteria

A target remains experimental until all of these pass on a clean machine:

- The package launches without a .NET SDK installed.
- A missing or incompatible LibVLC runtime produces actionable instructions.
- M4B, M4A, MP3, chaptered, chapterless, and Unicode-path fixtures import and play.
- Play, pause, seek, completion replay, chapters, speed, volume, bookmarks, and
  the sleep timer behave correctly.
- The previous book and position restore without autoplay after restart.
- Managed imports leave source files untouched and verified copies survive restart.
- Backup, restore, removal, and Storage Care work on the platform filesystem.
- Database, library, cover, and log paths follow the table above.
- Light/dark layouts remain usable at common scaling levels.
- Closing and reopening repeatedly does not lose progress or leave the database locked.

Use `build/Generate-SmokeTestMedia.ps1` to create the deterministic media set.
Record the OS version, processor architecture, VLC version, package checksum,
and each failed criterion when reporting results.

## macOS first launch

The current test bundle is unsigned and unnotarized. Control-click the app and
choose **Open**. If it remains blocked, use **System Settings > Privacy &
Security > Open Anyway**. Never disable Gatekeeper globally.

## Technical references

- [Avalonia macOS deployment](https://docs.avaloniaui.net/docs/deployment/macos/)
- [Avalonia Linux deployment](https://docs.avaloniaui.net/docs/deployment/linux)
- [LibVLCSharp getting started](https://docs.videolan.me/libvlcsharp/docs/getting_started.html)
- [LibVLCSharp Linux setup](https://docs.videolan.me/libvlcsharp/docs/linux-setup.html)
- [.NET 10 supported operating systems](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
