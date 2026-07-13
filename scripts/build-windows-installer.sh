#!/usr/bin/env bash
# Build the Windows Inno Setup installer.
#
# Usage:
#   ./scripts/build-windows-installer.sh
#
# Requires: iscc (Inno Setup 6) on PATH — typical location on CI:
#   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
# shellcheck source=scripts/packaging-common.sh
source "$ROOT/scripts/packaging-common.sh"

VERSION="$(read_version "$ROOT")"
RID="win-x64"
PUBLISH="$(publish_dir "$ROOT" "$RID")"
ISS="$ROOT/packaging/windows/ongenet.iss"

echo "=== Publishing $RID (v$VERSION) ==="
if [ ! -f "$PUBLISH/Ongenet.exe" ]; then
  dotnet publish "$ROOT/Ongenet.Desktop/Ongenet.Desktop.csproj" -c Release -r "$RID" --self-contained true \
    -p:DebugType=none -p:DebugSymbols=false
fi
ensure_license_in_publish "$PUBLISH" "$ROOT"

if ! command -v iscc >/dev/null 2>&1; then
  if [ -x "/c/Program Files (x86)/Inno Setup 6/ISCC.exe" ]; then
    ISCC="/c/Program Files (x86)/Inno Setup 6/ISCC.exe"
  elif [ -x "C:/Program Files (x86)/Inno Setup 6/ISCC.exe" ]; then
    ISCC="C:/Program Files (x86)/Inno Setup 6/ISCC.exe"
  else
    echo "iscc not found — install Inno Setup 6 and add ISCC.exe to PATH."
    exit 1
  fi
else
  ISCC="iscc"
fi

mkdir -p "$ROOT/dist"

# Git Bash / MSYS converts /D… to a Windows path, which makes ISCC see two script files.
if [[ "$(uname -s 2>/dev/null || echo)" == MINGW* ]] || [[ "$(uname -s 2>/dev/null || echo)" == MSYS* ]]; then
  export MSYS2_ARG_CONV_EXCL='*'
fi

"$ISCC" "//DMyAppVersion=$VERSION" "$ISS"
echo "Created dist/Ongenet-${VERSION}-win-x64-setup.exe"
