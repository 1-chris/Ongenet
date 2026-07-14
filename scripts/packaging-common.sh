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

# Copy Content/Core into the publish tree (samples / SFZ). No-op if pack is absent.
ensure_content_in_publish() {
  local publish="$1" root="$2"
  local src="$root/Content/Core"
  local dest="$publish/Content/Core"
  if [ ! -d "$src" ]; then
    return 0
  fi
  mkdir -p "$dest"
  # Prefer rsync when available; fall back to cp -R
  if command -v rsync >/dev/null 2>&1; then
    rsync -a --delete "$src/" "$dest/"
  else
    rm -rf "$dest"
    mkdir -p "$(dirname "$dest")"
    cp -R "$src" "$dest"
  fi
  if [ -f "$src/ATTRIBUTION.md" ]; then
    cp "$src/ATTRIBUTION.md" "$publish/CONTENT_ATTRIBUTION.md" 2>/dev/null || true
  fi
}

# Fail if Content/Core exceeds size ceiling (uncompressed MB). Usage: assert_content_size_mb ROOT [MAX_MB]
assert_content_size_mb() {
  local root="$1"
  local max_mb="${2:-1600}"
  local src="$root/Content/Core"
  if [ ! -d "$src" ]; then
    return 0
  fi
  local bytes
  bytes=$(du -sk "$src" | awk '{print $1}')
  local mb=$((bytes / 1024))
  if [ "$mb" -gt "$max_mb" ]; then
    echo "ERROR: Content/Core is ${mb} MB (limit ${max_mb} MB)" >&2
    return 1
  fi
  echo "Content/Core size OK: ${mb} MB (limit ${max_mb} MB)"
}

