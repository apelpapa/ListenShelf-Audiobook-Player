# Android preview

ListenShelf's preliminary Android client lives in this repository. It uses
Avalonia 12 for its phone interface, the existing application/library models,
SQLite storage and LibVLC audio engine, plus Android-specific file picking,
audio focus, media-session controls and a foreground playback service.

`ListenShelf.Android.slnx` is the Android solution. The original
`ListenShelf.slnx` remains the desktop solution, so desktop contributors and
build hosts do not need an Android workload.

## Build and install

Requirements: .NET 10 with its `android` workload, Android SDK platform 36 and
build tools 36.0.0, and JDK 21. Visual Studio's .NET mobile workload can install
these. Android Studio is useful for creating and running an emulator.

From the repository root in PowerShell:

```powershell
# Pixel / other x86-64 emulator
./build/Build-AndroidPreview.ps1 -Install

# Build an APK for a typical ARM64 Android phone
./build/Build-AndroidPreview.ps1 -RuntimeIdentifier android-arm64

# Install on a specific connected device (find its ID with adb devices)
./build/Build-AndroidPreview.ps1 -RuntimeIdentifier android-arm64 -Install -Device YOUR_DEVICE_ID
```

The script writes `artifacts/android/ListenShelf-preview-<architecture>.apk`.
These are debug-signed, self-contained preview APKs for local testing. They are
not signed or configured for a public store release. `-Install` replaces an
existing build with the same debug signature while preserving its local data.

The script stages a source snapshot in a uniquely named temporary directory.
This works around Android's Windows packaging limitation with non-ASCII project
paths, including the em dash in the original repository folder name. Source
changes remain in the repository; staged files are removed after building.
Use `-KeepBuildDirectory` to retain the snapshot while diagnosing build failures.
An ordinary ASCII path can also build `src/ListenShelf.Android/ListenShelf.Android.csproj`
directly with `-p:RuntimeIdentifier=android-x64 -p:EmbedAssembliesIntoApk=true`.

## Included in the first preview

- Phone-sized library, local search, player, and a mini-player on the library.
- Android document picker for one M4B, M4A, or MP3 at a time, with import status
  and cancellation. The selected source is copied to temporary app storage,
  then imported through the existing verified managed-library implementation.
  Exact duplicates use the existing content fingerprint checks.
- Playback, seeking, rewind/forward, remembered speed, embedded chapters and
  direct chapter navigation.
- Per-book saved position and restoring the last book paused on launch.
- Quick bookmarks and jumping back to their saved positions.
- 15/30/45/60-minute sleep timers and cancellation. Timers run independently of
  the player screen, pause playback on expiry, and are not restored on launch.
- Android media-session controls, a playback notification, foreground playback,
  an audio wake lock while playing, pause on lost audio focus, and pause when
  headphones disconnect. Resume after an interruption is explicit.

Use the phone's volume buttons. Library files and progress are private to this
Android installation. Android OS backup is disabled; uninstalling the app or
clearing its storage removes this preview's library and listening data. Original
files selected through the document picker remain untouched. Desktop and phone
libraries do not sync.

## Preview boundaries

This is an initial mobile client, not full desktop feature parity. It does not
yet expose metadata editing/lookup, book removal, backup/restore, Storage Care,
bookmark editing/deletion, Android Auto, widgets, or cross-device sync. Imported
books initially use their filenames as titles. Phone hardware, Bluetooth devices,
older Android versions and vendor battery-management behavior need separate
acceptance testing; emulator checks cannot establish those behaviors.

The native Android runtime is pinned to VideoLAN.LibVLC.Android **3.7.0-beta**
for its 16 KB page alignment. The older stable 3.6.5 package contains unaligned
native libraries. This prerelease dependency needs review before distributing a
production version. The desktop VLC runtime is unchanged.

## Verification

The build script checks that the signed APK contains VLC, its C++ runtime,
Skia and SQLite for the selected CPU architecture. Verify on the target device:

1. Import a local audiobook; cancel an import and try a duplicate.
2. Open it, seek, switch chapters and playback speed, and add/jump to a bookmark.
3. Play while on the home screen and with the display off; exercise Android's
   media controls and confirm position continues advancing.
4. Pause and restart the app; confirm the last book, position and speed return
   without autoplay. Reopen bookmarks and check their saved times.
5. Start/cancel a sleep timer. On a real phone, test headphone disconnection,
   audio-focus interruptions and longer listening sessions.

Shared desktop regression suite:

```powershell
dotnet test tests/ListenShelf.Tests/ListenShelf.Tests.csproj -c Release
```

Synthetic fixtures can be generated with `build/Generate-SmokeTestMedia.ps1`
when FFmpeg is available. No third-party audiobook recordings are shipped.

### Local preview validation (2026-09-08)

On the Pixel 10 Pro XL x86-64 emulator (Android 17, 16 KB pages), a synthetic
three-minute M4B with three chapters was imported through the document picker.
The following were exercised: library/player navigation, playback and Android
media pause, playback continuing with the display off, 1.5x speed, forward skip,
slider scrubbing, chapter jumps, adding/jumping to a bookmark, starting/canceling
a sleep timer, and canceling the document picker. A full process restart restored
the book paused at 1:04, retained 1.5x speed and its bookmark, and playback then
continued from the restored position. Re-importing the same file kept one book.

Both architecture builds and their native-library package checks passed without
warnings. The shared desktop regression suite passed all 716 tests. ARM64 has
been built but has not been run on a physical phone. Long timer expiry, cancellation
during a large file copy, Bluetooth/headphone interruption, and long listening
sessions remain device acceptance checks.
