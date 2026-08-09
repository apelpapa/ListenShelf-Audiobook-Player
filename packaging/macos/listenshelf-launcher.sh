#!/bin/sh

set -eu

macos_root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
vlc_root="$macos_root/../Frameworks/libvlc"

export VLC_PLUGIN_PATH="$vlc_root/plugins"
if [ -n "${DYLD_LIBRARY_PATH:-}" ]; then
    export DYLD_LIBRARY_PATH="$vlc_root/lib:$DYLD_LIBRARY_PATH"
else
    export DYLD_LIBRARY_PATH="$vlc_root/lib"
fi

exec "$macos_root/ListenShelf.bin" "$@"
