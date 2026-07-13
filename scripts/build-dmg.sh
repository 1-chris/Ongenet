#!/usr/bin/env bash
# Build a macOS .dmg from a published Ongenet.Desktop app bundle.
#
# Usage:
#   ./scripts/build-dmg.sh [rid]
#
# Default rid: osx-arm64 on Apple Silicon, osx-x64 otherwise.

set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
# shellcheck source=scripts/packaging-common.sh
source "$ROOT/scripts/packaging-common.sh"

RID="${1:-}"
if [ -z "$RID" ]; then
  case "$(uname -m)" in
    arm64) RID="osx-arm64" ;;
    *) RID="osx-x64" ;;
  esac
fi

VERSION="$(read_version "$ROOT")"
SLUG="$(rid_to_release_slug "$RID")"
PUBLISH="$(publish_dir "$ROOT" "$RID")"
APP_NAME="Ongenet"
STAGE="$ROOT/dist/dmg-stage"
DMG="$ROOT/dist/Ongenet-${VERSION}-${SLUG}.dmg"
ICNS="$ROOT/packaging/icons/Ongenet.icns"
PLIST_TEMPLATE="$ROOT/packaging/macos/Info.plist.template"

echo "=== Publishing $RID (v$VERSION) ==="
if [ ! -d "$PUBLISH" ] || [ -z "$(ls -A "$PUBLISH" 2>/dev/null || true)" ]; then
  dotnet publish "$ROOT/Ongenet.Desktop/Ongenet.Desktop.csproj" -c Release -r "$RID" --self-contained true \
    -p:DebugType=none -p:DebugSymbols=false
fi
ensure_license_in_publish "$PUBLISH" "$ROOT"

mkdir -p "$STAGE" "$ROOT/dist"
rm -rf "$STAGE/$APP_NAME.app"
mkdir -p "$STAGE/$APP_NAME.app/Contents/MacOS" "$STAGE/$APP_NAME.app/Contents/Resources"

cp -a "$PUBLISH/." "$STAGE/$APP_NAME.app/Contents/MacOS/"
[ -f "$ICNS" ] && cp "$ICNS" "$STAGE/$APP_NAME.app/Contents/Resources/Ongenet.icns"
[ -f "$ROOT/LICENSE" ] && cp "$ROOT/LICENSE" "$STAGE/$APP_NAME.app/Contents/Resources/LICENSE"

sed "s/@VERSION@/$VERSION/g" "$PLIST_TEMPLATE" > "$STAGE/$APP_NAME.app/Contents/Info.plist"

chmod +x "$STAGE/$APP_NAME.app/Contents/MacOS/Ongenet" 2>/dev/null || true

rm -f "$DMG"
hdiutil create -volname "Ongenet" -srcfolder "$STAGE" -ov -format UDZO "$DMG"
echo "Created $DMG"
