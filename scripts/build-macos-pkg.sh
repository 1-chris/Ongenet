#!/usr/bin/env bash
# Build a macOS .pkg installer from a published Ongenet.Desktop app bundle.
#
# Usage:
#   ./scripts/build-macos-pkg.sh [rid]

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
STAGE="$ROOT/dist/pkg-stage"
PKG_ROOT="$STAGE/root/Applications"
PKG="$ROOT/dist/Ongenet-${VERSION}-${SLUG}.pkg"
ICNS="$ROOT/packaging/icons/Ongenet.icns"
PLIST_TEMPLATE="$ROOT/packaging/macos/Info.plist.template"
SCRIPTS="$ROOT/dist/pkg-scripts"

echo "=== Building pkg for $RID (v$VERSION) ==="
if [ ! -d "$PUBLISH" ] || [ -z "$(ls -A "$PUBLISH" 2>/dev/null || true)" ]; then
  dotnet publish "$ROOT/Ongenet.Desktop/Ongenet.Desktop.csproj" -c Release -r "$RID" --self-contained true \
    -p:DebugType=none -p:DebugSymbols=false
fi
ensure_license_in_publish "$PUBLISH" "$ROOT"
ensure_content_in_publish "$PUBLISH" "$ROOT"

rm -rf "$STAGE" "$SCRIPTS"
mkdir -p "$PKG_ROOT/$APP_NAME.app/Contents/MacOS" \
  "$PKG_ROOT/$APP_NAME.app/Contents/Resources" \
  "$SCRIPTS"

cp -a "$PUBLISH/." "$PKG_ROOT/$APP_NAME.app/Contents/MacOS/"
[ -f "$ICNS" ] && cp "$ICNS" "$PKG_ROOT/$APP_NAME.app/Contents/Resources/Ongenet.icns"
[ -f "$ROOT/LICENSE" ] && cp "$ROOT/LICENSE" "$PKG_ROOT/$APP_NAME.app/Contents/Resources/LICENSE"
sed "s/@VERSION@/$VERSION/g" "$PLIST_TEMPLATE" > "$PKG_ROOT/$APP_NAME.app/Contents/Info.plist"
chmod +x "$PKG_ROOT/$APP_NAME.app/Contents/MacOS/Ongenet" 2>/dev/null || true

sed "s/@VERSION@/$VERSION/g" "$ROOT/packaging/macos/preinstall" > "$SCRIPTS/preinstall"
sed "s/@VERSION@/$VERSION/g" "$ROOT/packaging/macos/postinstall" > "$SCRIPTS/postinstall"
chmod +x "$SCRIPTS/preinstall" "$SCRIPTS/postinstall"

COMPONENT="$STAGE/Ongenet-component.pkg"
rm -f "$PKG" "$COMPONENT"
pkgbuild --root "$STAGE/root" \
  --scripts "$SCRIPTS" \
  --identifier "dev.ongenet.desktop" \
  --version "$VERSION" \
  --install-location "/" \
  "$COMPONENT"

productbuild --package "$COMPONENT" "$PKG"
echo "Created $PKG"
