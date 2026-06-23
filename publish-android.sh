#!/bin/bash
#
# Builds a sideloadable Ongenet Android APK and drops it in dist/Ongenet-<version>.apk.
#
# Usage:
#   ./publish-android.sh                 # Release build, debug-signed APK (installable by sideload)
#   ./publish-android.sh --debug         # Debug build instead of Release
#   ./publish-android.sh --no-copy       # leave the APK in bin/, don't copy into dist/
#
# Unlike the desktop packages, this needs the Android build toolchain (see DEVELOPMENT.md):
#   - the .NET "android" workload   (dotnet workload install android)
#   - an Android SDK                (provisioned once via -t:InstallAndroidDependencies)
#   - JDK 21                        (the .NET Android tooling requires exactly 21)
#
# It does NOT need Android Studio or an emulator — the output is a plain APK you copy to the tablet.
#
# The SDK and JDK locations are auto-detected but can be overridden with environment variables:
#   ANDROID_SDK   (or ANDROID_HOME / ANDROID_SDK_ROOT)   default: $HOME/Android/Sdk
#   JAVA21_HOME                                          default: first JDK 21 found under /usr/lib/jvm

set -u
ROOT="$(cd "$(dirname "$0")" && pwd)"   # solution root (this script lives here)
cd "$ROOT"

PROJ="$ROOT/Ongenet.Android/Ongenet.Android.csproj"
CONFIG="Release"
DO_COPY=1

for arg in "$@"; do
    case "$arg" in
        --debug)   CONFIG="Debug" ;;
        --no-copy) DO_COPY=0 ;;
        *) echo "Unknown option: $arg"; exit 1 ;;
    esac
done

# --- Locate the Android SDK ------------------------------------------------------------------------
SDK="${ANDROID_SDK:-${ANDROID_HOME:-${ANDROID_SDK_ROOT:-$HOME/Android/Sdk}}}"
if [ ! -d "$SDK/platform-tools" ] && [ ! -d "$SDK/cmdline-tools" ]; then
    echo "Android SDK not found at: $SDK"
    echo "Set ANDROID_SDK (or ANDROID_HOME), or provision one once with:"
    echo "  dotnet build $PROJ -t:InstallAndroidDependencies \\"
    echo "    -p:AndroidSdkDirectory=\$HOME/Android/Sdk -p:JavaSdkDirectory=<jdk21> -p:AcceptAndroidSDKLicenses=True"
    exit 1
fi

# --- Locate a JDK 21 (the .NET Android tooling requires exactly 21, with javac/jar present) ---------
JDK="${JAVA21_HOME:-}"
if [ -z "$JDK" ]; then
    for d in /usr/lib/jvm/java-21-openjdk /usr/lib/jvm/*21* /usr/lib/jvm/*jdk-21*; do
        if [ -x "$d/bin/javac" ] && [ -x "$d/bin/jar" ]; then JDK="$d"; break; fi
    done
fi
if [ -z "$JDK" ] || [ ! -x "$JDK/bin/javac" ] || [ ! -x "$JDK/bin/jar" ]; then
    echo "A full JDK 21 (with javac and jar) was not found."
    echo "Install one (e.g. 'sudo dnf install java-21-openjdk-devel') and/or set JAVA21_HOME."
    exit 1
fi

echo "Android SDK: $SDK"
echo "JDK 21:      $JDK"
echo ""
echo "=== Publishing Ongenet.Android ($CONFIG) ==="

# AndroidKeyStore=false signs with the Android debug key, which is fine for sideloading. For a Play Store
# upload you'd configure a real keystore and emit an .aab instead (see DEVELOPMENT.md).
dotnet publish "$PROJ" -c "$CONFIG" \
    -p:AndroidSdkDirectory="$SDK" \
    -p:JavaSdkDirectory="$JDK" \
    -p:AndroidKeyStore=false \
    || { echo "publish failed"; exit 1; }

# --- Find the signed APK -------------------------------------------------------------------------
OUT="$ROOT/Ongenet.Android/bin/$CONFIG/net10.0-android"
APK="$(ls "$OUT/publish/"*-Signed.apk "$OUT/"*-Signed.apk 2>/dev/null | head -1)"
[ -n "$APK" ] || APK="$(ls "$OUT/publish/"*.apk "$OUT/"*.apk 2>/dev/null | grep -v 'Signed' | head -1)"

if [ -z "$APK" ] || [ ! -f "$APK" ]; then
    echo "Build succeeded but no APK was found under $OUT"
    exit 1
fi

echo ""
echo "Built APK: $APK"

if [ "$DO_COPY" = "1" ]; then
    VERSION="$(grep -oP '<Version>\K[^<]+(?=</Version>)' "$ROOT/Directory.Build.props" | head -1)"
    [ -n "$VERSION" ] || VERSION="dev"
    DIST="$ROOT/dist"
    mkdir -p "$DIST"
    DEST="$DIST/Ongenet-$VERSION.apk"
    cp -f "$APK" "$DEST"
    echo "  -> dist/Ongenet-$VERSION.apk"
fi

echo ""
echo "Publishing complete!"
echo "Sideload it onto the tablet with:  adb install -r \"${DEST:-$APK}\""
echo "(or copy the .apk to the device and open it with a file manager)."
