#!/usr/bin/env bash
# Build a macOS .dmg from a published Ongenet.Desktop app bundle.
#
# Usage:
#   ./scripts/build-dmg.sh [rid]
#
# Default rid: osx-arm64 on Apple Silicon, osx-x64 otherwise.

set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

RID="${1:-}"
if [ -z "$RID" ]; then
  case "$(uname -m)" in
    arm64) RID="osx-arm64" ;;
    *) RID="osx-x64" ;;
  esac
fi

PUBLISH="$ROOT/Ongenet.Desktop/bin/Release/net10.0/$RID/publish"
APP_NAME="Ongenet"
STAGE="$ROOT/dist/dmg-stage"
DMG="$ROOT/dist/Ongenet-$RID.dmg"

echo "=== Publishing $RID ==="
dotnet publish "$ROOT/Ongenet.Desktop/Ongenet.Desktop.csproj" -c Release -r "$RID" --self-contained true \
  -p:DebugType=none -p:DebugSymbols=false

mkdir -p "$STAGE" "$ROOT/dist"
rm -rf "$STAGE/$APP_NAME.app"
mkdir -p "$STAGE/$APP_NAME.app/Contents/MacOS" "$STAGE/$APP_NAME.app/Contents/Resources"

cp -a "$PUBLISH/." "$STAGE/$APP_NAME.app/Contents/MacOS/"

cat > "$STAGE/$APP_NAME.app/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Ongenet</string>
  <key>CFBundleDisplayName</key><string>Ongenet</string>
  <key>CFBundleIdentifier</key><string>dev.ongenet.desktop</string>
  <key>CFBundleExecutable</key><string>Ongenet</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
</dict>
</plist>
PLIST

chmod +x "$STAGE/$APP_NAME.app/Contents/MacOS/Ongenet" 2>/dev/null || true

rm -f "$DMG"
hdiutil create -volname "Ongenet" -srcfolder "$STAGE" -ov -format UDZO "$DMG"
echo "Created $DMG"
