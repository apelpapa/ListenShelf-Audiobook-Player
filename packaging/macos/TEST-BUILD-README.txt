ListenShelf macOS test build
=============================

This is an unsigned, unnotarized test build. It is not yet a supported release.

Requirements
------------
- macOS 14 or newer (the current .NET 10 support floor)
- The architecture named in the ZIP file
- VLC 3 installed as /Applications/VLC.app

If VLC is installed elsewhere, set LISTENSHELF_LIBVLC_PATH to the directory
containing its compatible LibVLC native libraries before launching ListenShelf.

First launch
------------
1. Extract the ZIP.
2. Drag ListenShelf.app to Applications if desired.
3. Control-click ListenShelf.app and choose Open.
4. If macOS still blocks it, use System Settings > Privacy & Security > Open Anyway.

Do not disable Gatekeeper globally.

Data
----
ListenShelf keeps its database, managed audiobook copies, covers, and logs in:
~/Library/Application Support/ListenShelf

Before reporting a problem, reproduce it once and include the log from the Logs
folder. Back up important libraries before testing this early build.
