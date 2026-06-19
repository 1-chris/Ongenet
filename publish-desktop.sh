#!/bin/bash
#
# Builds self-contained Ongenet packages for Linux, Windows and macOS, bundling the native PortAudio
# library (where available) and zipping each platform into dist/Ongenet-<rid>.zip.
#
# Usage:
#   ./publish.sh                 # all platforms, no debug symbols (smallest), zipped
#   ./publish.sh --symbols       # keep .pdb debug symbols
#   ./publish.sh --no-zip        # leave the publish folders, skip zipping
#   ./publish.sh --no-native     # don't (re)build the native PortAudio libs first
#   ./publish.sh linux-x64 win-x64   # only the listed RIDs
#
# Self-contained = the .NET runtime is bundled, so target machines need no .NET install.
# Native PortAudio: built locally for linux-x64 / win-x64 by ./build-portaudio.sh; macOS dylibs
# must be placed in native/osx-arm64|osx-x64/ separately (see that script). Missing native libs are
# simply not bundled (the app still runs, falling back to a system-installed PortAudio if present).

set -u
ROOT="$(cd "$(dirname "$0")" && pwd)"   # solution root (this script lives here)
cd "$ROOT"

PROJ="$ROOT/Ongenet.Desktop/Ongenet.Desktop.csproj"
OUTBASE="$ROOT/Ongenet.Desktop/bin/Release/net10.0"

ALL_RIDS="linux-x64 linux-arm64 win-x64 osx-arm64 osx-x64"
SYMBOLS=0
DO_ZIP=1
DO_NATIVE=1
RIDS=""

for arg in "$@"; do
    case "$arg" in
        --symbols)   SYMBOLS=1 ;;
        --no-zip)    DO_ZIP=0 ;;
        --no-native) DO_NATIVE=0 ;;
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

# Native PortAudio name expected in each publish folder, per platform.
native_file() {
    case "$1" in
        linux-*) echo "libportaudio.so.2" ;;
        win-x64) echo "portaudio.dll" ;;
        osx-*)   echo "libportaudio.2.dylib" ;;
    esac
}

# 1. Build the native PortAudio libraries (linux/windows) unless suppressed.
if [ "$DO_NATIVE" = "1" ]; then
    bash "$ROOT/build-portaudio.sh" || echo "warning: native PortAudio build had issues; bundling whatever exists."
fi

rm -rf "$DIST"
mkdir -p "$DIST"
SUMMARY=""

for rid in $RIDS; do
    echo ""
    echo "=== Publishing $rid ==="
    out="$OUTBASE/$rid/publish"
    rm -rf "$out"
    dotnet publish "$PROJ" $COMMON -r "$rid" || { echo "publish failed for $rid"; exit 1; }

    # Bundle the matching native PortAudio lib if we have it.
    nf="$(native_file "$rid")"
    if [ -f "$ROOT/native/$rid/$nf" ]; then
        cp -f "$ROOT/native/$rid/$nf" "$out/"
        echo "  bundled native/$rid/$nf"
        SUMMARY="$SUMMARY\n  $rid : PortAudio bundled ($nf)"
    else
        echo "  *** WARNING: native/$rid/$nf NOT found — package will have NO PortAudio (no audio on a"
        echo "      machine without it installed). Build it via native/build-portaudio.sh first."
        if [ "$rid" = "win-x64" ]; then
            echo "      Windows needs the MinGW cross-compiler: sudo dnf install mingw64-gcc mingw64-winpthreads-static"
        elif [ "$rid" = "linux-arm64" ]; then
            echo "      linux-arm64's .so is built natively on an aarch64 host — build it there/on CI and drop it in"
            echo "      native/$rid/ (build-portaudio.sh skips it on an x86 host)."
        elif [ "$rid" = "osx-arm64" ] || [ "$rid" = "osx-x64" ]; then
            echo "      macOS dylibs must be built on a Mac/CI and dropped in native/$rid/ (see build-portaudio.sh)."
        fi
        SUMMARY="$SUMMARY\n  $rid : *** NO PortAudio ***"
    fi

    # Linux: the apphost is named 'Ongenet.Desktop', which many desktop environments mistake for a
    # .desktop launcher. Rename it to a clear executable name (the managed dll name is embedded in the
    # apphost, so this rename is safe).
    case "$rid" in linux-*)
        if [ -f "$out/Ongenet.Desktop" ]; then
            mv -f "$out/Ongenet.Desktop" "$out/Ongenet.bin"
            echo "  renamed Ongenet.Desktop -> Ongenet.bin"
        fi ;;
    esac

    # 3. Package: stage into Ongenet-<rid>/ so it extracts to a tidy folder, then zip.
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
echo "PortAudio bundling summary:"
printf "%b\n" "$SUMMARY"
echo "Run targets inside each package:"
echo "  Linux:   ./Ongenet.bin"
echo "  Windows: Ongenet.Desktop.exe"
echo "  macOS:   ./Ongenet.Desktop"
