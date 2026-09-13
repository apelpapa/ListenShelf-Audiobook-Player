# Windows build and testing

`ListenShelf.slnx` is the main Windows solution. The desktop project defaults to
`win-x64` and uses Avalonia's Win32 backend with Skia rendering and HarfBuzz text
shaping. Android stays in `ListenShelf.Android.slnx`; see `ANDROID_PREVIEW.md`.

## Current runtime strategy

- Core, application, storage, and playback logic remain reusable by Android.
- Every package carries its own architecture-matched .NET and LibVLC runtimes.
- Windows uses the official VideoLAN Windows native package, including plugins.
- Run the native-runtime probe against the completed package before testing.
- ListenShelf shows a diagnostic startup screen when the native runtime cannot
  be loaded. It does not replace or delete the library after this failure.

Users must not install VLC, LibVLC, or .NET before testing. A launch that works
only after installing VLC is a failed package, even if playback then succeeds.

## Data and logs

The Windows data root is `%LocalAppData%\ListenShelf`. It contains `listenshelf.db`,
managed audiobook copies in `Library`, cached artwork in `Covers`, and `Logs`.
This location is unchanged by the Windows-only build cleanup. Do not delete it
when cleaning build outputs or reinstalling the app.

The `Logs` child directory contains `listenshelf.log` and, after rotation,
`listenshelf.previous.log`. Logs are local and best-effort.

## Native packaging

Run from the repository root on Windows:

```powershell
dotnet restore ListenShelf.slnx
dotnet build ListenShelf.slnx --configuration Release --no-restore
dotnet test ListenShelf.slnx --configuration Release --no-build --no-restore
./build/Test-WindowsInstallerDataSafety.ps1

# Only when local release assets are wanted:
./build/Publish-WindowsRelease.ps1
```

The release script produces a self-contained portable ZIP, single-file EXE,
and MSI plus SHA-256 checksums beneath `artifacts/release/v<version>/assets`.
It creates local files only; it does not commit, push, or publish a GitHub release.
The single-file app extracts native libraries internally at runtime.

The GitHub Actions build-and-test workflow remains manual-only and runs on
Windows. It does not package or publish releases. Normal desktop builds require
the .NET 10 SDK but do not require Android/iOS workloads.

For a local Release build, the runtime probe is:

```powershell
& './src/ListenShelf.Desktop/bin/Release/net10.0/win-x64/ListenShelf.exe' --verify-native-runtime
```

Also run the probe against the EXE inside the actual extracted ZIP or single-file
package. Exit code 0 verifies native initialization, not audible playback or UI
behavior; complete the manual checks below too.

## Exact real-machine pass gate

Test the exact Windows x64 package, preferably in a clean VM with no VLC or .NET
installation. A package passes only when every **required** check below passes.
One required failure means that build is not ready to distribute.

Record this header before testing:

```text
Operating system and version:
Processor architecture: x64:
ListenShelf package filename:
Package SHA-256 matches: yes / no
VLC is not installed: yes / no
.NET is not installed: yes / no / unknown
```

### 1. Package and startup — required

- Verify the supplied SHA-256 before extraction.
- Confirm VLC is absent, then extract the package and launch ListenShelf.
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

### 7. Windows integration — required

- Test keyboard media keys and any available headset Play/Pause, Previous, and
  Next controls while ListenShelf is focused and minimized.
- **Pass:** the supported media commands control playback while focused and
  minimized, without registering keys while media controls are disabled.
- Test window restoration and About's troubleshooting-copy button, including
  pasting after closing ListenShelf. Compare the copy with its preview.

Use `build/Generate-SmokeTestMedia.ps1` to regenerate the deterministic media
set when needed. For any failure, include the failed step, whether sound was
heard, and the privacy-safe details from Settings → About. Review screenshots
and logs before sharing: they can contain book names and local file paths.

## Development smoke check — 2026-09-12

This was a Windows x64 Debug UI check, **not** final-package or audible-output
acceptance. A copy of the current source was built under the gitignored
`artifacts/playback-smoke-20260912` directory. Only its default data-root path
was overridden so imports, settings, and saved positions stayed in the test
folder rather than the user's real ListenShelf catalog.

- Imported the synthetic 12-second `short-with-chapters.m4b` through the file
  picker. The source and managed-copy SHA-256 values matched the fixture manifest.
- Observed playback progress, all three chapter labels, and the Finished state.
- Reproduced a bug: rewinding the finished book to 0:00 left chapter 3 selected
  and Next disabled. Seeking now updates chapter selection immediately without
  waiting for a native chapter-change event or starting playback.
- Repeated the case after rebuilding: 0:00 selected chapter 1, Previous was
  disabled, Next was enabled, and Next advanced to chapter 2 at 0:04 while ready.
  Play then restarted playback from that chapter without returning to Library.
- Reopening the test build restored the selected book at the beginning, ready
  without autoplay and with chapter controls available before Play.
- All 726 automated tests passed, including four new seek/selection regression
  cases. Existing countdown tests still cover delayed native chapter selection.

Stopped after this one fix. Nonzero-position restart/resume, sustained pause,
audible sound, other formats, long books, and final packaged-artifact checks
remain to be completed. The isolated fixture/build/data were retained for local
reproduction; no source audiobooks or real library entries were modified.
