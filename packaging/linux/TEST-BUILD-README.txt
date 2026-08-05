ListenShelf Linux test build
=============================

This is an early portable test build, not yet a supported release.

Requirements
------------
- 64-bit x86 Linux
- VLC 3 and the matching LibVLC runtime supplied by the distribution

Examples:
  Debian/Ubuntu: sudo apt install vlc libvlc-dev
  Fedora:        sudo dnf install vlc-devel

Package names vary by distribution. If LibVLC is installed in a non-standard
location, set LISTENSHELF_LIBVLC_PATH to the directory containing its native
libraries before launching ListenShelf.

Launch
------
1. Extract the ZIP while preserving its directory structure.
2. In a terminal, enter the extracted directory.
3. If needed, run: chmod +x ListenShelf
4. Run: ./ListenShelf

The included listenshelf.desktop file is an integration template for users who
place ListenShelf on PATH. The portable build itself does not modify your system.

Data
----
ListenShelf uses $XDG_DATA_HOME/ListenShelf when XDG_DATA_HOME is an absolute
path. Otherwise it uses ~/.local/share/ListenShelf.

Before reporting a problem, reproduce it once and include listenshelf.log from
the Logs directory. Back up important libraries before testing this early build.
