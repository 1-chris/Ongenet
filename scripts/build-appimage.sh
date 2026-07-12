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
cd "$ROOT"

RID="${1:-linux-x64}"
PUBLISH="$ROOT/Ongenet.Desktop/bin/Release/net10.0/$RID/publish"
APPDIR="$ROOT/dist/Ongenet-$RID.AppDir"
APPIMAGE="$ROOT/dist/Ongenet-$RID.AppImage"

if ! command -v appimagetool >/dev/null 2>&1; then
  echo "appimagetool not found on PATH — install AppImageKit or set APPIMAGETOOL."
  exit 1
fi

echo "=== Publishing $RID ==="
dotnet publish "$ROOT/Ongenet.Desktop/Ongenet.Desktop.csproj" -c Release -r "$RID" --self-contained true \
  -p:DebugType=none -p:DebugSymbols=false

rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/256x256/apps"

cp -a "$PUBLISH/." "$APPDIR/usr/bin/"
if [ -f "$APPDIR/usr/bin/Ongenet" ]; then
  mv "$APPDIR/usr/bin/Ongenet" "$APPDIR/usr/bin/Ongenet.bin"
fi

cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/Ongenet.bin" "$@"
EOF
chmod +x "$APPDIR/AppRun" "$APPDIR/usr/bin/Ongenet.bin"

cat > "$APPDIR/usr/share/applications/ongenet.desktop" <<'EOF'
[Desktop Entry]
Name=Ongenet
Exec=Ongenet.bin
Icon=ongenet
Type=Application
Categories=AudioVideo;Audio;
EOF

# Placeholder icon (1x1 PNG) if none bundled
printf '\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR\x00\x00\x00\x01\x00\x00\x00\x01\x08\x06\x00\x00\x00\x1f\x15\xc4\x89\x00\x00\x00\nIDATx\x9cc\x00\x01\x00\x00\x05\x00\x01\r\n-\xdb\x00\x00\x00\x00IEND\xaeB`\x82' \
  > "$APPDIR/usr/share/icons/hicolor/256x256/apps/ongenet.png"

mkdir -p "$ROOT/dist"
rm -f "$APPIMAGE"
appimagetool "$APPDIR" "$APPIMAGE"
echo "Created $APPIMAGE"
