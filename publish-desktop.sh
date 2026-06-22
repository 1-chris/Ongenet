#!/bin/bash
#
# Builds self-contained Ongenet packages for Linux, Windows and macOS, zipping each platform into
# dist/Ongenet-<rid>.zip.
#
# Usage:
#   ./publish-desktop.sh                 # all platforms, no debug symbols (smallest), zipped
#   ./publish-desktop.sh --symbols       # keep .pdb debug symbols
#   ./publish-desktop.sh --no-zip        # leave the publish folders, skip zipping
#   ./publish-desktop.sh linux-x64 win-x64   # only the listed RIDs
#
# Self-contained = the .NET runtime is bundled, so target machines need no .NET install. Audio uses the
# OS-native backend (ALSA/PipeWire/JACK/Pulse on Linux, CoreAudio on macOS, WASAPI on Windows) via
# P/Invoke to the platform's own libraries — nothing extra is bundled.

set -u
ROOT="$(cd "$(dirname "$0")" && pwd)"   # solution root (this script lives here)
cd "$ROOT"

PROJ="$ROOT/Ongenet.Desktop/Ongenet.Desktop.csproj"
OUTBASE="$ROOT/Ongenet.Desktop/bin/Release/net10.0"

ALL_RIDS="linux-x64 linux-arm64 win-x64 osx-arm64 osx-x64"
SYMBOLS=0
DO_ZIP=1
RIDS=""

for arg in "$@"; do
    case "$arg" in
        --symbols)   SYMBOLS=1 ;;
        --no-zip)    DO_ZIP=0 ;;
        linux-x64|linux-arm64|win-x64|osx-arm64|osx-x64) RIDS="$RIDS $arg" ;;
        *) echo "Unknown option: $arg"; exit 1 ;;
    esac
done
[ -n "$RIDS" ] || RIDS="$ALL_RIDS"

# Default build: strip debug symbols to keep the packages small. --symbols keeps them.
SYMBOL_ARGS="-p:DebugType=none -p:DebugSymbols=false"
[ "$SYMBOLS" = "1" ] && SYMBOL_ARGS=""

COMMON="-c Release --self-contained true $SYMBOL_ARGS"
DIST="$ROOT/dist"

rm -rf "$DIST"
mkdir -p "$DIST"

for rid in $RIDS; do
    echo ""
    echo "=== Publishing $rid ==="
    out="$OUTBASE/$rid/publish"
    rm -rf "$out"
    dotnet publish "$PROJ" $COMMON -r "$rid" || { echo "publish failed for $rid"; exit 1; }

    # Linux: the apphost is named 'Ongenet', which some desktop environments mistake for a .desktop
    # launcher. Rename it to a clear executable name (the managed dll name is embedded in the apphost,
    # so this rename is safe).
    case "$rid" in linux-*)
        if [ -f "$out/Ongenet" ]; then
            mv -f "$out/Ongenet" "$out/Ongenet.bin"
            echo "  renamed Ongenet -> Ongenet.bin"
        fi ;;
    esac

    # Package: stage into Ongenet-<rid>/ so it extracts to a tidy folder, then zip.
    if [ "$DO_ZIP" = "1" ]; then
        stage="$DIST/Ongenet-$rid"
        rm -rf "$stage"; mkdir -p "$stage"
        cp -a "$out/." "$stage/"
        if command -v zip >/dev/null 2>&1; then
            (cd "$DIST" && zip -qr "Ongenet-$rid.zip" "Ongenet-$rid")
            echo "  -> dist/Ongenet-$rid.zip"
        else
            (cd "$DIST" && tar -czf "Ongenet-$rid.tar.gz" "Ongenet-$rid")
            echo "  zip not found — wrote dist/Ongenet-$rid.tar.gz instead"
        fi
        rm -rf "$stage"
    fi
done

echo ""
echo "Publishing complete!"
[ "$DO_ZIP" = "1" ] && echo "Packages in: $DIST"
echo "Run targets inside each package:"
echo "  Linux:   ./Ongenet.bin"
echo "  Windows: Ongenet.exe"
echo "  macOS:   ./Ongenet"
