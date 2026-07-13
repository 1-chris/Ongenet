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

if ! command -v appimagetool >/dev/null 2>&1; then
  echo "appimagetool not found on PATH — install AppImageKit or set APPIMAGETOOL."
  exit 1
fi

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

RELEASE_DATE="$(date -u +%Y-%m-%d)"
sed -e "s/@VERSION@/$VERSION/g" -e "s/@RELEASE_DATE@/$RELEASE_DATE/g" \
  "$METAINFO_SRC" > "$APPDIR/usr/share/metainfo/net.onge.Ongenet.metainfo.xml"

mkdir -p "$ROOT/dist"
rm -f "$APPIMAGE"
ARCH="$(linux_arch_from_rid "$RID")"
if [ -n "${ARCH:-}" ] && [ "$ARCH" != "$RID" ]; then
  ARCH="$ARCH" appimagetool "$APPDIR" "$APPIMAGE"
else
  appimagetool "$APPDIR" "$APPIMAGE"
fi
echo "Created $APPIMAGE"
