#!/usr/bin/env bash
# Install or upgrade Ongenet from an AppImage (user scope).
#
# Usage:
#   ./scripts/install-appimage.sh [path/to/Ongenet-*.AppImage]
#
# Installs to ~/.local/share/Ongenet/Ongenet.AppImage and writes install.json.

set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
# shellcheck source=scripts/packaging-common.sh
source "$ROOT/scripts/packaging-common.sh"

APPIMAGE_SRC="${1:-}"
INSTALL_DIR="${HOME}/.local/share/Ongenet"
TARGET="${INSTALL_DIR}/Ongenet.AppImage"
BIN_LINK="${HOME}/.local/bin/ongenet"
DESKTOP_DIR="${HOME}/.local/share/applications"
DESKTOP_FILE="${DESKTOP_DIR}/net.onge.Ongenet.desktop"
CONFIG_DIR="${XDG_CONFIG_HOME:-${HOME}/.config}/Ongenet"
MANIFEST="${CONFIG_DIR}/install.json"
VERSION="$(read_version "$ROOT")"

if [ -z "$APPIMAGE_SRC" ]; then
  APPIMAGE_SRC="$(ls -t "$ROOT"/dist/Ongenet-*-linux-*.AppImage 2>/dev/null | head -n1 || true)"
fi
if [ -z "$APPIMAGE_SRC" ] || [ ! -f "$APPIMAGE_SRC" ]; then
  echo "Usage: $0 path/to/Ongenet-VERSION-linux-ARCH.AppImage"
  exit 1
fi

# Detect version from filename if possible
if [[ "$(basename "$APPIMAGE_SRC")" =~ Ongenet-([0-9]+\.[0-9]+\.[0-9]+)- ]]; then
  VERSION="${BASH_REMATCH[1]}"
fi

mkdir -p "$INSTALL_DIR" "$DESKTOP_DIR" "$CONFIG_DIR" "$(dirname "$BIN_LINK")"

# Upgrade: replace existing AppImage at fixed path
if [ -f "$TARGET" ]; then
  echo "Upgrading existing AppImage at $TARGET"
  rm -f "$TARGET"
fi
cp -f "$APPIMAGE_SRC" "$TARGET"
chmod +x "$TARGET"

ln -sf "$TARGET" "$BIN_LINK"

cat > "$DESKTOP_FILE" <<EOF
[Desktop Entry]
Type=Application
Name=Ongenet
Comment=Free and open-source digital audio workstation
Exec=$TARGET %F
Icon=net.onge.Ongenet
Terminal=false
Categories=AudioVideo;Audio;Midi;
StartupWMClass=Ongenet
EOF

ICON_DEST="${HOME}/.local/share/icons/hicolor/256x256/apps/net.onge.Ongenet.png"
mkdir -p "$(dirname "$ICON_DEST")"
cp -f "$ROOT/packaging/icons/ongenet-256.png" "$ICON_DEST" 2>/dev/null || true

DATE="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
cat > "$MANIFEST" <<EOF
{
  "method": "appimage",
  "installPath": "$TARGET",
  "version": "$VERSION",
  "installedAt": "$DATE"
}
EOF

echo "Installed Ongenet $VERSION"
echo "  AppImage: $TARGET"
echo "  Command:  ongenet  (or $BIN_LINK)"
echo "  Desktop:  $DESKTOP_FILE"
echo "Settings and presets are preserved in $CONFIG_DIR"
