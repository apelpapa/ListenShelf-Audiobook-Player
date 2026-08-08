#!/bin/sh

set -eu

app_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)

export LD_LIBRARY_PATH="$app_dir/libvlc:$app_dir/libvlc/deps${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export VLC_PLUGIN_PATH="$app_dir/libvlc/plugins"

exec "$app_dir/ListenShelf.bin" "$@"
