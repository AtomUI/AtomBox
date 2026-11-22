#!/usr/bin/env bash
set -euo pipefail

fail() {
  printf 'AppImage packaging: %s\n' "$*" >&2
  exit 1
}

[[ $# -eq 2 ]] || fail 'usage: create-appimage.sh <publish-directory> <output.AppImage>'
publish_dir=$(realpath -- "$1")
output=$(realpath -m -- "$2")
[[ $output == *.AppImage ]] || fail 'the output path must end in .AppImage'

asset_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
executable=AtomBox.Desktop
entrypoint="usr/bin/$executable"
[[ -f "$publish_dir/$executable" ]] || fail "missing published executable: $publish_dir/$executable"

work_dir=$(mktemp -d -t atombox-appimage.XXXXXXXX)
trap 'rm -rf -- "$work_dir"' EXIT
app_dir="$work_dir/AtomBox.AppDir"
mkdir -p -- "$app_dir/usr/bin" "$app_dir/usr/share/applications" "$app_dir/usr/share/icons"
cp -a -- "$publish_dir/." "$app_dir/usr/bin/"
chmod +x -- "$app_dir/$entrypoint"
ln -s -- "$entrypoint" "$app_dir/AppRun"
install -m 0644 -- "$asset_dir/atombox.desktop" "$app_dir/usr/share/applications/atombox.desktop"
cp -a -- "$asset_dir/icons/hicolor" "$app_dir/usr/share/icons/"

linuxdeploy --appdir "$app_dir" || fail 'linuxdeploy failed to prepare the AppDir'

image="$work_dir/$(basename -- "$output")"
appimagetool "$app_dir" "$image" || fail 'appimagetool failed to create the archive'
[[ -s "$image" && -x "$image" ]] || fail 'appimagetool did not produce an executable archive'

# Check what was actually packed, without needing a display or launching the GUI.
(
  cd -- "$work_dir"
  "$image" --appimage-extract > /dev/null
  [[ $(readlink -- squashfs-root/AppRun) == "$entrypoint" && -x squashfs-root/AppRun ]] ||
    fail "packed AppRun does not point to the executable $entrypoint"
  grep -Fxq "Exec=$executable" squashfs-root/atombox.desktop || fail 'incorrect packed desktop entry'
  [[ -f squashfs-root/atombox.png ]] || fail 'missing packed application icon'
)

mkdir -p -- "$(dirname -- "$output")"
mv -- "$image" "$output"
printf 'Verified AppImage: %s\n' "$output"
