#!/usr/bin/env bash

set -euo pipefail

if [[ $# -ne 1 ]]; then
    echo "Usage: $0 <output-directory>" >&2
    exit 64
fi

output_root="$(realpath -m "$1")"
download_root="$output_root/debs"
sysroot="$output_root/root"

rm -rf "$output_root"
mkdir -p "$download_root" "$sysroot"

declare -a requested_packages=(libvlc5 libvlccore9 vlc-plugin-base)
mapfile -t dependency_candidates < <(
    apt-cache depends --recurse \
        --no-recommends \
        --no-suggests \
        --no-conflicts \
        --no-breaks \
        --no-replaces \
        --no-enhances \
        "${requested_packages[@]}" \
        | awk '$1 == "Depends:" { print $2 }' \
        | tr -d '<>' \
        | sort -u
)

declare -a downloadable_packages=("${requested_packages[@]}")
for package_name in "${dependency_candidates[@]}"; do
    if [[ "$package_name" == *:* ]]; then
        continue
    fi
    if apt-cache show --no-all-versions "$package_name" 2>/dev/null \
        | grep -q '^Package:'; then
        downloadable_packages+=("$package_name")
    fi
done

mapfile -t downloadable_packages < <(
    printf '%s\n' "${downloadable_packages[@]}" | sort -u
)

pushd "$download_root" >/dev/null
apt-get download "${downloadable_packages[@]}"
for package_file in ./*.deb; do
    dpkg-deb -x "$package_file" "$sysroot"
done
popd >/dev/null

package_manifest="$sysroot/LISTENSHELF-PACKAGES.txt"
for package_file in "$download_root"/*.deb; do
    dpkg-deb -f "$package_file" Package Version
done | paste - - | sort -u | paste -sd, - > "$package_manifest"

apt-cache policy libvlc5 \
    | awk '/Candidate:/ { print $2; exit }' \
    > "$sysroot/LISTENSHELF-LIBVLC-VERSION.txt"

package_count="$(find "$download_root" -maxdepth 1 -type f -name '*.deb' | wc -l)"
sysroot_size="$(du -sh "$sysroot" | cut -f1)"
echo "Prepared $package_count packages in a $sysroot_size temporary LibVLC sysroot."
