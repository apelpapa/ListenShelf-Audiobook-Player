#!/usr/bin/env bash

set -euo pipefail

if [[ $# -ne 2 ]]; then
    echo "Usage: $0 <published-app-directory> <runtime-output-directory>" >&2
    exit 64
fi

package_root="$(realpath "$1")"
runtime_root="$(realpath -m "$2")"

if [[ ! -x "$package_root/ListenShelf.bin" ]]; then
    echo "ListenShelf.bin was not found or is not executable in $package_root" >&2
    exit 65
fi

case "$(uname -m)" in
    x86_64)
        expected_library_pattern='x86_64-linux-gnu'
        ;;
    *)
        echo "Unsupported Linux test-build architecture: $(uname -m)" >&2
        exit 66
        ;;
esac

find_package_file() {
    local package_name="$1"
    local file_pattern="$2"
    dpkg-query -L "$package_name" \
        | grep -E "/${expected_library_pattern}/${file_pattern}$" \
        | sed -n '1p'
}

libvlc_path="$(find_package_file libvlc5 'libvlc\.so\.5(\.[0-9]+)*')"
libvlccore_path="$(find_package_file libvlccore9 'libvlccore\.so\.9(\.[0-9]+)*')"
plugin_root="$(find "/usr/lib/${expected_library_pattern}/vlc" \
    -type d -path '*/plugins' -print -quit)"

if [[ -z "$libvlc_path" || -z "$libvlccore_path" || -z "$plugin_root" ]]; then
    echo "The required Debian/Ubuntu LibVLC runtime packages are incomplete." >&2
    exit 67
fi

rm -rf "$runtime_root"
mkdir -p "$runtime_root/deps"

declare -A packaged_debian_packages=(
    [libvlc5]=1
    [libvlccore9]=1
    [vlc-plugin-base]=1
)

record_package_owner() {
    local source_path="$1"
    local owner
    owner="$(dpkg-query -S "$source_path" 2>/dev/null | sed -n '1p' || true)"
    if [[ -n "$owner" ]]; then
        owner="${owner%%:*}"
        packaged_debian_packages[$owner]=1
    fi
}

# Use real files instead of symlinks for the entry-point names so extraction
# works even in ZIP tools that do not recreate Unix symbolic links.
cp -L "$libvlc_path" "$runtime_root/libvlc.so"
cp -L "$libvlc_path" "$runtime_root/libvlc.so.5"
cp -L "$libvlccore_path" "$runtime_root/libvlccore.so.9"
cp -a "$plugin_root" "$runtime_root/plugins"
find "$runtime_root/plugins" -type f -name 'plugins.dat' -delete

declare -a dependency_queue=()
while IFS= read -r binary; do
    dependency_queue+=("$binary")
done < <(find "$package_root" -type f \
    \( -name '*.so' -o -name '*.so.*' -o -perm -0100 \) -print)

declare -A inspected=()
queue_index=0
while (( queue_index < ${#dependency_queue[@]} )); do
    binary="${dependency_queue[$queue_index]}"
    ((queue_index += 1))

    if [[ -n "${inspected[$binary]:-}" ]]; then
        continue
    fi
    inspected[$binary]=1

    while IFS= read -r dependency; do
        dependency_name="$(basename "$dependency")"
        case "$dependency_name" in
            linux-vdso.so.*|ld-linux-*.so.*|libc.so.*|libdl.so.*|libm.so.*|\
            libpthread.so.*|libresolv.so.*|librt.so.*|libutil.so.*|libnss_*.so.*)
                continue
                ;;
        esac

        if [[ -e "$runtime_root/$dependency_name" ]]; then
            continue
        fi

        destination="$runtime_root/deps/$dependency_name"
        if [[ ! -e "$destination" ]]; then
            record_package_owner "$dependency"
            cp -L "$dependency" "$destination"
            dependency_queue+=("$destination")
        fi
    done < <(ldd "$binary" 2>/dev/null \
        | awk '$2 == "=>" && $3 ~ /^\// { print $3 }
               $1 ~ /^\// { print $1 }')
done

plugin_count="$(find "$runtime_root/plugins" -type f -name '*.so' | wc -l)"
if (( plugin_count < 20 )); then
    echo "Only $plugin_count LibVLC plugins were bundled; refusing an incomplete package." >&2
    exit 68
fi

for required_plugin in libmp4_plugin.so libavcodec_plugin.so; do
    if ! find "$runtime_root/plugins" -type f -name "$required_plugin" -print -quit \
        | grep -q .; then
        echo "Required playback plugin $required_plugin was not bundled." >&2
        exit 69
    fi
done

if ! find "$runtime_root/plugins" -type f \
    \( -name 'libpulse_plugin.so' -o -name 'libalsa_plugin.so' \) \
    -print -quit | grep -q .; then
    echo "No supported LibVLC Linux audio-output plugin was bundled." >&2
    exit 70
fi

license_root="$runtime_root/licenses"
mkdir -p "$license_root"
for package_name in "${!packaged_debian_packages[@]}"; do
    copyright_path="/usr/share/doc/$package_name/copyright"
    if [[ -f "$copyright_path" ]]; then
        cp "$copyright_path" "$license_root/$package_name.copyright"
    fi
done

package_list="$(printf '%s\n' "${!packaged_debian_packages[@]}" | sort | paste -sd, -)"

vlc_version="$(dpkg-query -W -f='${Version}' libvlc5)"
{
    printf 'ListenShelf private LibVLC runtime\n'
    printf 'Source packages: Ubuntu/Debian libvlc5, libvlccore9, vlc-plugin-base\n'
    printf 'LibVLC package version: %s\n' "$vlc_version"
    printf 'Architecture: %s\n' "$(uname -m)"
    printf 'Plugins: %s\n' "$plugin_count"
    printf 'Packaged Debian/Ubuntu libraries: %s\n' "$package_list"
} > "$runtime_root/BUNDLED-RUNTIME.txt"

runtime_size="$(du -sh "$runtime_root" | cut -f1)"
echo "Bundled LibVLC $vlc_version: $plugin_count plugins, $runtime_size"
