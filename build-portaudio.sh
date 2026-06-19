#!/bin/bash
#
# Builds the native PortAudio shared libraries this app loads at runtime, into native/<rid>/:
#   native/linux-x64/libportaudio.so.2     (native gcc + cmake, on an x86_64 host)
#   native/linux-arm64/libportaudio.so.2   (native gcc + cmake, on an aarch64 host)
#   native/win-x64/portaudio.dll           (MinGW-w64 cross-compile, statically linked runtime)
#
# Linux .so files are built NATIVELY — the host arch must match the target arch (x86_64 host builds
# linux-x64, aarch64 host builds linux-arm64). Cross-compiling PortAudio with ALSA + JACK across archs
# needs a full sysroot of the other arch's audio dev libs, so — like macOS — linux-arm64 is built on an
# arm64 machine / CI runner rather than cross-built from x86 (a mismatched host is skipped, not fatal).
#
# macOS dylibs are NOT built here — cross-compiling them from Linux needs Apple's SDK. Build those on
# a macOS machine / CI runner (e.g. `brew install portaudio`) and drop them in native/osx-arm64/ and
# native/osx-x64/ as libportaudio.2.dylib. publish.sh bundles whatever exists in native/<rid>/.
#
# Requirements:
#   linux-x64   : gcc, cmake, make            (+ ALSA dev headers: libasound2-dev / alsa-lib-devel) on x86_64
#   linux-arm64 : gcc, cmake, make            (+ ALSA dev headers) on an aarch64 host
#   win-x64     : cmake + MinGW-w64 cross gcc  (Fedora/Nobara: mingw64-gcc mingw64-winpthreads-static)
#   network     : to clone PortAudio, OR set PORTAUDIO_SRC to an existing source tree.
#
# Each target is skipped (not fatal) if its toolchain (or host arch) is missing, so this is safe to run
# anywhere. Env overrides: PORTAUDIO_TAG (default v19.7.0), PORTAUDIO_SRC, RIDS="linux-x64 win-x64".

# This script lives at the solution root; it writes built libraries into ./native/<rid>/ and keeps
# the PortAudio source/build tree under ./native/.build/ (both are git-ignored).
ROOT="$(cd "$(dirname "$0")" && pwd)"
NATIVE="$ROOT/native"
WORK="$NATIVE/.build"
SRC="${PORTAUDIO_SRC:-$WORK/portaudio}"
PA_TAG="${PORTAUDIO_TAG:-v19.7.0}"
RIDS="${RIDS:-linux-x64 linux-arm64 win-x64}"

want() { case " $RIDS " in *" $1 "*) return 0 ;; *) return 1 ;; esac; }

ensure_src() {
    if [ -d "$SRC" ]; then return 0; fi
    if ! command -v git >/dev/null 2>&1; then
        echo "  ! git not found and no PORTAUDIO_SRC — cannot fetch PortAudio."; return 1
    fi
    echo "  Cloning PortAudio $PA_TAG into $SRC ..."
    mkdir -p "$WORK"
    git clone --depth 1 --branch "$PA_TAG" https://github.com/PortAudio/portaudio "$SRC" || {
        echo "  ! clone failed (offline?). Set PORTAUDIO_SRC to a local source tree."; return 1; }
}

# Build the Linux PortAudio .so for one RID (linux-x64 or linux-arm64). Built natively, so the host
# arch must match the target arch — a mismatch is skipped (not fatal), see the header note.
build_linux() {
    local rid="$1"
    want "$rid" || return 0

    # Map RID -> required host arch, then bail non-fatally if this host can't build it natively.
    local need_arch
    case "$rid" in
        linux-x64)   need_arch="x86_64" ;;
        linux-arm64) need_arch="aarch64" ;;
        *) echo "$rid: not a Linux RID"; return 0 ;;
    esac
    local host_arch; host_arch="$(uname -m)"
    if [ "$host_arch" != "$need_arch" ]; then
        echo "$rid: skipped — needs a $need_arch host (this is $host_arch). Cross-building PortAudio with"
        echo "        ALSA+JACK across archs isn't supported here; build it on a matching machine / CI runner."
        return 0
    fi

    if ! command -v cmake >/dev/null 2>&1 || ! command -v gcc >/dev/null 2>&1; then
        echo "$rid: skipped (need gcc + cmake)."; return 0
    fi
    ensure_src || return 0
    echo "$rid: building..."
    local b="$WORK/build-$rid"
    rm -rf "$b"

    # PipeWire support: PortAudio has no native PipeWire backend — it reaches PipeWire via ALSA (the
    # pipewire-alsa plugin) and, for tighter integration / lower latency, via JACK (PipeWire's JACK
    # server). JACK is REQUIRED for our Linux builds, so bail loudly if its dev files are missing.
    if ! pkg-config --exists jack 2>/dev/null; then
        echo "  ! JACK dev files not found — pkg-config can't see 'jack'. JACK is required for Linux builds."
        echo "    Install it:  Fedora/Nobara: sudo dnf install pipewire-jack-audio-connection-kit-devel"
        echo "                 Debian/Ubuntu: sudo apt-get install libjack-jackd2-dev"
        return 1
    fi
    # On a PipeWire-JACK system libjack.so lives in a non-standard dir (e.g. /usr/lib64/pipewire-0.3/jack)
    # that the linker doesn't search by default — PortAudio emits a bare `-ljack`, so without pkg-config's
    # -L path the link fails ("cannot find -ljack"). Feed those dirs to the shared-lib linker flags.
    local jack_flag="-DPA_USE_JACK=ON"
    local jack_ldflags; jack_ldflags="$(pkg-config --libs-only-L jack 2>/dev/null)"
    echo "  PipeWire/JACK backend: ENABLED ($(pkg-config --libs jack 2>/dev/null))."

    # CMAKE_POLICY_VERSION_MINIMUM=3.5: PortAudio v19.7.0's cmake_minimum_required is < 3.5, which
    # CMake 4.x refuses outright. This flag lets it configure anyway (harmless on CMake 3.x).
    cmake -S "$SRC" -B "$b" -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=ON \
        -DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
        -DCMAKE_SHARED_LINKER_FLAGS="$jack_ldflags" \
        -DPA_BUILD_TESTS=OFF -DPA_BUILD_EXAMPLES=OFF -DPA_USE_ALSA=ON $jack_flag >/dev/null \
        || { echo "  ! cmake configure failed"; return 0; }
    cmake --build "$b" -j"$(nproc)" >/dev/null || { echo "  ! build failed"; return 0; }

    local so
    so="$(readlink -f "$b/libportaudio.so" 2>/dev/null)"
    [ -f "$so" ] || so="$(ls "$b"/libportaudio.so.2* 2>/dev/null | head -n1)"
    if [ -z "$so" ] || [ ! -f "$so" ]; then echo "  ! could not locate built libportaudio.so"; return 0; fi
    mkdir -p "$NATIVE/$rid"
    cp -f "$so" "$NATIVE/$rid/libportaudio.so.2"
    echo "  -> native/$rid/libportaudio.so.2"
}

build_windows() {
    want win-x64 || return 0
    local cc=x86_64-w64-mingw32-gcc
    local cxx=x86_64-w64-mingw32-g++
    local rc=x86_64-w64-mingw32-windres
    local hint="sudo dnf install mingw64-gcc mingw64-gcc-c++ mingw64-winpthreads-static"
    if ! command -v cmake >/dev/null 2>&1 || ! command -v "$cc" >/dev/null 2>&1; then
        echo "win-x64: skipped (need cmake + MinGW C compiler — $hint)."
        return 0
    fi
    # PortAudio's CMake project() enables C++, so CMake probes a C++ compiler at configure time. Without
    # the MinGW C++ compiler it falls back to the host g++ and the host linker fails on Windows-only
    # flags (e.g. --major-image-version). Require the MinGW C++ compiler too.
    if ! command -v "$cxx" >/dev/null 2>&1; then
        echo "win-x64: skipped — MinGW C++ compiler ($cxx) missing. Install it: $hint"
        return 0
    fi
    ensure_src || return 0
    echo "win-x64: building (MinGW cross)..."
    local b="$WORK/build-win"
    rm -rf "$b"
    # Point CMake at the full MinGW toolchain (C, C++, RC) and statically fold the MinGW gcc/winpthread
    # runtime into the DLL so users need no extra MinGW DLLs.
    cmake -S "$SRC" -B "$b" -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=ON \
        -DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
        -DPA_BUILD_TESTS=OFF -DPA_BUILD_EXAMPLES=OFF \
        -DCMAKE_SYSTEM_NAME=Windows \
        -DCMAKE_C_COMPILER="$cc" \
        -DCMAKE_CXX_COMPILER="$cxx" \
        -DCMAKE_RC_COMPILER="$rc" \
        -DCMAKE_SHARED_LINKER_FLAGS="-static -static-libgcc -static-libstdc++ -Wl,-Bstatic,--whole-archive -lwinpthread -Wl,--no-whole-archive" \
        >/dev/null || { echo "  ! cmake configure failed"; return 0; }
    cmake --build "$b" -j"$(nproc)" >/dev/null || { echo "  ! build failed"; return 0; }

    local dll
    dll="$(ls "$b"/libportaudio*.dll "$b"/portaudio*.dll 2>/dev/null | head -n1)"
    if [ -z "$dll" ] || [ ! -f "$dll" ]; then echo "  ! could not locate built portaudio.dll"; return 0; fi
    mkdir -p "$NATIVE/win-x64"
    cp -f "$dll" "$NATIVE/win-x64/portaudio.dll"
    echo "  -> native/win-x64/portaudio.dll"
}

echo "Building native PortAudio ($RIDS)..."
build_linux linux-x64
build_linux linux-arm64
build_windows
echo "Native PortAudio step done."
