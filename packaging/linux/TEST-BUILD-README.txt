ListenShelf Linux test build
=============================

This is an early portable test build, not yet a supported release.

Requirements
------------
- 64-bit x86 Linux
- No .NET, VLC, or LibVLC installation is required. This package contains its
  own architecture-matched .NET and LibVLC runtimes.

Launch
------
1. Extract the ZIP while preserving its directory structure.
2. In a terminal, enter the extracted directory.
3. Run: ./ListenShelf

If the ZIP program discarded Linux executable permissions, repair the package
once with: chmod +x ListenShelf ListenShelf.bin

The included listenshelf.desktop file is an integration template for users who
place ListenShelf on PATH. The portable build itself does not modify your system.

Data
----
ListenShelf uses $XDG_DATA_HOME/ListenShelf when XDG_DATA_HOME is an absolute
path. Otherwise it uses ~/.local/share/ListenShelf.

Before reporting a problem, reproduce it once and include listenshelf.log from
the Logs directory. Back up important libraries before testing this early build.
