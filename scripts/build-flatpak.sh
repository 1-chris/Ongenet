#!/usr/bin/env bash
# Build a Flatpak bundle from a published Ongenet.Desktop folder.
#
# Usage:
#   ./scripts/build-flatpak.sh [rid]
#
# Requires: flatpak, flatpak-builder, org.freedesktop.Platform//24.08

set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
# shellcheck source=scripts/packaging-common.sh
source "$ROOT/scripts/packaging-common.sh"

RID="${1:-linux-x64}"
VERSION="$(read_version "$ROOT")"
SLUG="$(rid_to_release_slug "$RID")"
PUBLISH="$(publish_dir "$ROOT" "$RID")"
BUILD_DIR="$ROOT/packaging/flatpak/build"
MANIFEST="$BUILD_DIR/net.onge.Ongenet.yml"
REPO_DIR="$ROOT/packaging/flatpak/repo"
BUNDLE="$ROOT/dist/Ongenet-${VERSION}-${SLUG}.flatpak"
RELEASE_DATE="$(date -u +%Y-%m-%d)"

if ! command -v flatpak-builder >/dev/null 2>&1; then
  echo "flatpak-builder not found — install flatpak and flatpak-builder."
  exit 1
fi

echo "=== Flatpak build $RID (v$VERSION) ==="
if [ ! -f "$PUBLISH/Ongenet.bin" ]; then
  dotnet publish "$ROOT/Ongenet.Desktop/Ongenet.Desktop.csproj" -c Release -r "$RID" --self-contained true \
    -p:DebugType=none -p:DebugSymbols=false
  if [ -f "$PUBLISH/Ongenet" ]; then
    mv -f "$PUBLISH/Ongenet" "$PUBLISH/Ongenet.bin"
  fi
fi
ensure_license_in_publish "$PUBLISH" "$ROOT"

mkdir -p "$BUILD_DIR/sources/publish" "$ROOT/dist" "$REPO_DIR"
rm -rf "$BUILD_DIR/sources/publish"/*
cp -a "$PUBLISH/." "$BUILD_DIR/sources/publish/"

cat > "$BUILD_DIR/launch.sh" <<'EOF'
#!/bin/sh
cd /app/lib/ongenet
exec ./Ongenet.bin "$@"
EOF
chmod +x "$BUILD_DIR/launch.sh"

sed -e "s/@VERSION@/$VERSION/g" -e "s/@RELEASE_DATE@/$RELEASE_DATE/g" \
  "$ROOT/packaging/flatpak/net.onge.Ongenet.metainfo.xml" > "$BUILD_DIR/net.onge.Ongenet.metainfo.xml"
cp "$ROOT/packaging/linux/net.onge.Ongenet.desktop" "$BUILD_DIR/net.onge.Ongenet.desktop"
cp "$ROOT/packaging/icons/ongenet-256.png" "$BUILD_DIR/ongenet-256.png"
cp "$ROOT/LICENSE" "$BUILD_DIR/LICENSE"

cat > "$MANIFEST" <<'EOF'
app-id: net.onge.Ongenet
runtime: org.freedesktop.Platform
runtime-version: '24.08'
sdk: org.freedesktop.Sdk
command: launch.sh

finish-args:
  - --share=ipc
  - --socket=x11
  - --socket=wayland
  - --socket=pulseaudio
  - --device=dri
  - --filesystem=home
  - --talk-name=org.freedesktop.portal.*
  - --env=DOTNET_EnableDiagnostics=0

modules:
  - name: ongenet
    buildsystem: simple
    # Publish output is already Release-stripped; skip flatpak-builder's elfutils
    # post-process (eu-strip / eu-elfcompress) so CI doesn't need those packages.
    build-options:
      strip: false
      no-debuginfo: true
    build-commands:
      - mkdir -p /app/lib/ongenet /app/bin /app/share/metainfo /app/share/applications
      - mkdir -p /app/share/icons/hicolor/256x256/apps /app/share/licenses/Ongenet
      - cp -a publish/. /app/lib/ongenet/
      - install -Dm755 launch.sh /app/bin/launch.sh
      - install -Dm644 net.onge.Ongenet.metainfo.xml /app/share/metainfo/net.onge.Ongenet.metainfo.xml
      - install -Dm644 net.onge.Ongenet.desktop /app/share/applications/net.onge.Ongenet.desktop
      - install -Dm644 ongenet-256.png /app/share/icons/hicolor/256x256/apps/net.onge.Ongenet.png
      - install -Dm644 LICENSE /app/share/licenses/Ongenet/LICENSE
    sources:
      - type: dir
        path: sources/publish
        dest: publish
      - type: file
        path: launch.sh
      - type: file
        path: net.onge.Ongenet.metainfo.xml
      - type: file
        path: net.onge.Ongenet.desktop
      - type: file
        path: ongenet-256.png
      - type: file
        path: LICENSE
EOF

flatpak-builder --force-clean --repo="$REPO_DIR" "$BUILD_DIR/flatpak-build" "$MANIFEST"
flatpak build-export "$REPO_DIR" "$BUILD_DIR/flatpak-build" "$VERSION"
flatpak build-bundle "$REPO_DIR" "$BUNDLE" net.onge.Ongenet "$VERSION"
echo "Created $BUNDLE"
