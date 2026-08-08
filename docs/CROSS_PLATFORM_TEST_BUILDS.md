# Cross-platform test builds

macOS and Linux support is provisional. These packages exist to expose technical
risks and gather real-machine results; they are not yet supported releases.

## Current runtime strategy

- The Avalonia desktop application and LibVLCSharp managed binding are shared.
- Every package carries its own architecture-matched .NET and LibVLC runtimes.
- Windows continues to use the official VideoLAN Windows native package.
- Linux packaging copies LibVLC, its playback plugins, and non-system dynamic
  dependencies into a private directory reached through the included launcher.
- macOS packaging copies the official VideoLAN runtime into
  `ListenShelf.app/Contents/Frameworks/libvlc`.
- Packaging runs a native-runtime probe and fails instead of producing an
  artifact when LibVLC cannot initialize from the completed package.
- ListenShelf shows a diagnostic startup screen when the native runtime cannot
  be loaded. It does not replace or delete the library after this failure.

Users must not install VLC, LibVLC, or .NET before testing. A launch that works
only after installing VLC is a failed package, even if playback then succeeds.

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

It produces a self-contained .NET and LibVLC portable ZIP plus SHA-256 checksum
beneath `artifacts/test-builds`. Linux packaging uses `zip` so executable
permissions survive extraction. macOS packaging creates a normal `.app` bundle
and uses `ditto` so its metadata, symbolic links, and executable bit survive.

The GitHub Actions workflow is manual-only. It compiles, tests, and packages on
native macOS and Linux runners, then stores workflow artifacts. It does not
publish a GitHub Release.

## Exact real-machine pass gate

Test each operating-system and processor package separately. A platform passes
only when every **required** check below passes on a machine where VLC is not
installed. One required failure means that platform build remains experimental.

Record this header before testing:

```text
Operating system and version:
Processor: Intel x64 / Apple Silicon:
ListenShelf package filename:
Package SHA-256 matches: yes / no
VLC is not installed: yes / no
.NET is not installed: yes / no / unknown
```

### 1. Package and startup — required

- Verify the supplied SHA-256 before extraction.
- Confirm VLC/VLC.app is absent, then extract the package and launch ListenShelf.
- **Pass:** the normal Library or Player opens without the startup-diagnostics
  window, a terminal command, an environment variable, or any additional install.
- Close and reopen ListenShelf three times.
- **Pass:** all three launches succeed and no database-lock or native-runtime
  error appears.

### 2. Format and path coverage — required

Import every file from the supplied smoke-media package:

- `short-no-chapters.m4b`
- `short-with-chapters.m4b`
- `short-no-chapters.m4a`
- `short-no-chapters.mp3`
- `Café — 第1章.m4b`

For each file, start playback, listen for sound, seek forward, seek backward,
pause, resume, and let it reach the end.

- **Pass:** every file imports and produces audible output; controls respond;
  no filename becomes corrupted; and no playback error is shown.
- Rewind the completed file and press Play again.
- **Pass:** playback restarts without returning to the Library first.

### 3. Chapters and player state — required

- Open `short-with-chapters.m4b` before pressing Play.
- **Pass:** chapters are already visible, selecting a chapter seeks to it, and
  Previous/Next chapter work.
- Set a non-default speed and volume, play partway, close ListenShelf, and reopen it.
- **Pass:** the same book is loaded at approximately the saved position without
  autoplay, and the chosen speed and volume are restored.

### 4. Library safety — required

- Before import, record the source smoke-file hashes and locations.
- Import, edit metadata, add a cover, add a bookmark, and restart ListenShelf.
- **Pass:** the source files and hashes are unchanged, while the managed copies,
  metadata, cover, bookmark, and listening position survive restart.
- Remove one imported test book using the confirmation flow.
- **Pass:** its catalog entry and ListenShelf-managed copy are gone together,
  while the original source file remains unchanged.
- Run Storage Care.
- **Pass:** it does not report valid managed books as orphaned or missing.

### 5. Backup and recovery — required

- Export a backup containing the test library.
- Remove or alter at least one test entry, then restore the backup.
- **Pass:** books, managed audio, metadata, covers, bookmarks, settings, and
  listening positions return, and playback still works after another restart.

### 6. Desktop behavior — required

- Test light and dark modes, list and tile views, grouping, tile-size adjustment,
  and library search.
- **Pass:** controls remain visible and usable with no overlapping or clipped
  content at 100% display scaling.
- Test Space/K, J/Left, and L/Right while focus is outside a text box; then type
  those characters into Library search.
- **Pass:** shortcuts control playback normally and never intercept search text.
- Set a one-minute sleep timer and let it expire.
- **Pass:** playback pauses when the timer expires.

### 7. Platform integration — provisional, report separately

- Test keyboard media keys and any available headset Play/Pause, Previous, and
  Next controls while ListenShelf is focused and minimized.
- A failure here does not invalidate the bundled playback runtime, but it blocks
  claiming full media-control support on that platform.

Use `build/Generate-SmokeTestMedia.ps1` to regenerate the deterministic media
set when needed. For any failure, include the failed step, whether sound was
heard, a screenshot, and `listenshelf.log` from the platform data directory.

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
