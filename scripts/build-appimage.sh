#!/usr/bin/env bash
# Build a Linux AppImage from a published Ongenet.Desktop folder.
#
# Usage:
#   ./scripts/build-appimage.sh [rid]
#
# Requires: appimagetool (https://github.com/AppImage/AppImageKit)
# Default rid: linux-x64

set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
# shellcheck source=scripts/packaging-common.sh
source "$ROOT/scripts/packaging-common.sh"

RID="${1:-linux-x64}"
VERSION="$(read_version "$ROOT")"
SLUG="$(rid_to_release_slug "$RID")"
PUBLISH="$(publish_dir "$ROOT" "$RID")"
APPDIR="$ROOT/dist/Ongenet-${VERSION}-${SLUG}.AppDir"
APPIMAGE="$ROOT/dist/Ongenet-${VERSION}-${SLUG}.AppImage"
ICON="$ROOT/packaging/icons/ongenet-256.png"
DESKTOP_SRC="$ROOT/packaging/linux/net.onge.Ongenet.desktop"
METAINFO_SRC="$ROOT/packaging/flatpak/net.onge.Ongenet.metainfo.xml"

resolve_appimagetool() {
  if [ -n "${APPIMAGETOOL:-}" ] && [ -x "${APPIMAGETOOL}" ]; then
    echo "${APPIMAGETOOL}"
    return
  fi
  if command -v appimagetool >/dev/null 2>&1; then
    command -v appimagetool
    return
  fi
  echo "appimagetool not found on PATH — install AppImageKit or set APPIMAGETOOL." >&2
  return 1
}

run_appimagetool() {
  local tool="$1" arch="$2" appdir="$3" output="$4"
  local -a extra=()
  # AppImage-distributed appimagetool needs FUSE unless extracted first (common on CI).
  if [[ "$tool" == *.AppImage ]]; then
    extra=(--appimage-extract-and-run)
  fi
  ARCH="$arch" "$tool" "${extra[@]}" "$appdir" "$output"
}

echo "=== Publishing $RID (v$VERSION) ==="
if [ ! -d "$PUBLISH" ] || [ -z "$(ls -A "$PUBLISH" 2>/dev/null || true)" ]; then
  dotnet publish "$ROOT/Ongenet.Desktop/Ongenet.Desktop.csproj" -c Release -r "$RID" --self-contained true \
    -p:DebugType=none -p:DebugSymbols=false
fi
ensure_license_in_publish "$PUBLISH" "$ROOT"

rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" \
  "$APPDIR/usr/share/icons/hicolor/256x256/apps" \
  "$APPDIR/usr/share/metainfo" "$APPDIR/usr/share/doc/Ongenet"

cp -a "$PUBLISH/." "$APPDIR/usr/bin/"
if [ -f "$APPDIR/usr/bin/Ongenet" ]; then
  mv "$APPDIR/usr/bin/Ongenet" "$APPDIR/usr/bin/Ongenet.bin"
fi
[ -f "$ROOT/LICENSE" ] && cp "$ROOT/LICENSE" "$APPDIR/usr/share/doc/Ongenet/LICENSE"

cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0" 2>/dev/null || realpath "$0")")"
cd "$HERE/usr/bin"
exec "$HERE/usr/bin/Ongenet.bin" "$@"
EOF
chmod +x "$APPDIR/AppRun" "$APPDIR/usr/bin/Ongenet.bin"

cp "$DESKTOP_SRC" "$APPDIR/usr/share/applications/net.onge.Ongenet.desktop"
cp "$ICON" "$APPDIR/usr/share/icons/hicolor/256x256/apps/net.onge.Ongenet.png"
cp "$ICON" "$APPDIR/ongenet.png"

# appimagetool requires a .desktop file in the AppDir root (Exec=AppRun, Icon without path).
cat > "$APPDIR/net.onge.Ongenet.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=Ongenet
Comment=Free and open-source digital audio workstation
Exec=AppRun %F
Icon=ongenet
Terminal=false
Categories=AudioVideo;Audio;Midi;
StartupWMClass=Ongenet
EOF

RELEASE_DATE="$(date -u +%Y-%m-%d)"
sed -e "s/@VERSION@/$VERSION/g" -e "s/@RELEASE_DATE@/$RELEASE_DATE/g" \
  "$METAINFO_SRC" > "$APPDIR/usr/share/metainfo/net.onge.Ongenet.metainfo.xml"

mkdir -p "$ROOT/dist"
rm -f "$APPIMAGE"
APPIMAGETOOL_BIN="$(resolve_appimagetool)"
ARCH="$(linux_arch_from_rid "$RID")"
run_appimagetool "$APPIMAGETOOL_BIN" "$ARCH" "$APPDIR" "$APPIMAGE"
echo "Created $APPIMAGE"
