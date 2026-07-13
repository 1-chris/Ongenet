#!/bin/bash
#
# Builds self-contained Ongenet packages for Linux, Windows and macOS.
#
# Portable ZIPs (default):
#   dist/Ongenet-<version>-<platform>-portable.zip
#
# Usage:
#   ./publish-desktop.sh                 # all platforms, zipped
#   ./publish-desktop.sh --symbols       # keep .pdb debug symbols
#   ./publish-desktop.sh --no-zip        # leave publish folders only
#   ./publish-desktop.sh linux-x64 win-x64 win-arm64

set -u
ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT"
# shellcheck source=scripts/packaging-common.sh
source "$ROOT/scripts/packaging-common.sh"

PROJ="$ROOT/Ongenet.Desktop/Ongenet.Desktop.csproj"
OUTBASE="$ROOT/Ongenet.Desktop/bin/Release/net10.0"
VERSION="$(read_version "$ROOT")"

ALL_RIDS="linux-x64 linux-arm64 win-x64 win-arm64 osx-arm64 osx-x64"
SYMBOLS=0
DO_ZIP=1
RIDS=""

for arg in "$@"; do
    case "$arg" in
        --symbols)   SYMBOLS=1 ;;
        --no-zip)    DO_ZIP=0 ;;
        linux-x64|linux-arm64|win-x64|win-arm64|osx-arm64|osx-x64) RIDS="$RIDS $arg" ;;
        *) echo "Unknown option: $arg"; exit 1 ;;
    esac
done
[ -n "$RIDS" ] || RIDS="$ALL_RIDS"

SYMBOL_ARGS="-p:DebugType=none -p:DebugSymbols=false"
[ "$SYMBOLS" = "1" ] && SYMBOL_ARGS=""

COMMON="-c Release --self-contained true $SYMBOL_ARGS"
DIST="$ROOT/dist"

rm -rf "$DIST"
mkdir -p "$DIST"

for rid in $RIDS; do
    echo ""
    echo "=== Publishing $rid (v$VERSION) ==="
    out="$(publish_dir "$ROOT" "$rid")"
    rm -rf "$out"
    dotnet publish "$PROJ" $COMMON -r "$rid" || { echo "publish failed for $rid"; exit 1; }

    ensure_license_in_publish "$out" "$ROOT"

    case "$rid" in linux-*)
        if [ -f "$out/Ongenet" ]; then
            mv -f "$out/Ongenet" "$out/Ongenet.bin"
            echo "  renamed Ongenet -> Ongenet.bin"
        fi ;;
    esac

    if [ "$DO_ZIP" = "1" ]; then
        zip_name="$(portable_zip_name "$VERSION" "$rid")"
        inner="$(basename "${zip_name%.zip}")"
        stage="$DIST/$inner"
        rm -rf "$stage"; mkdir -p "$stage"
        cp -a "$out/." "$stage/"
        if command -v zip >/dev/null 2>&1; then
            (cd "$DIST" && zip -qr "$zip_name" "$(basename "$stage")")
            echo "  -> dist/$zip_name"
        else
            tar_name="${zip_name%.zip}.tar.gz"
            (cd "$DIST" && tar -czf "$tar_name" "$(basename "$stage")")
            echo "  zip not found — wrote dist/$tar_name instead"
        fi
        rm -rf "$stage"
    fi
done

echo ""
echo "Publishing complete! (version $VERSION)"
[ "$DO_ZIP" = "1" ] && echo "Portable packages in: $DIST"
echo "Run targets inside each package:"
echo "  Linux:   ./Ongenet.bin"
echo "  Windows: Ongenet.exe"
echo "  macOS:   ./Ongenet"
