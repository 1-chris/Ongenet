#!/usr/bin/env bash
# Shared helpers for Ongenet packaging scripts.
# shellcheck disable=SC2034

packaging_root() {
  cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd
}

read_version() {
  local root="${1:-$(packaging_root)}"
  grep -o '<Version>[^<]*</Version>' "$root/Directory.Build.props" \
    | sed 's/<Version>\(.*\)<\/Version>/\1/' \
    | head -n1
}

# linux-x64 -> linux-x64, osx-arm64 -> macos-arm64
rid_to_release_slug() {
  case "$1" in
    linux-x64|linux-arm64|win-x64|win-arm64) echo "$1" ;;
    osx-arm64) echo "macos-arm64" ;;
    osx-x64) echo "macos-x64" ;;
    *) echo "$1" ;;
  esac
}

linux_arch_from_rid() {
  case "$1" in
    linux-x64) echo "x86_64" ;;
    linux-arm64) echo "aarch64" ;;
    *) echo "$1" ;;
  esac
}

portable_zip_name() {
  local version="$1" rid="$2"
  echo "Ongenet-${version}-$(rid_to_release_slug "$rid")-portable.zip"
}

publish_dir() {
  local root="$1" rid="$2"
  echo "$root/Ongenet.Desktop/bin/Release/net10.0/$rid/publish"
}

ensure_license_in_publish() {
  local publish="$1" root="$2"
  if [ ! -f "$publish/LICENSE" ] && [ -f "$root/LICENSE" ]; then
    cp "$root/LICENSE" "$publish/LICENSE"
  fi
}
