#!/usr/bin/env bash
# Assemble the GitHub Pages site from DocFX output, marketing homepage, caps, and WASM app.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOCFX_OUT="${ROOT}/site/_site"
OUT="${ROOT}/_site"
BUNDLE="${ROOT}/Ongenet.Web/bin/Release/net10.0-browser/browser-wasm/AppBundle"

rm -rf "$OUT"
mkdir -p "$OUT/app"

if [[ ! -d "$DOCFX_OUT" ]]; then
  echo "error: DocFX output not found at $DOCFX_OUT — run docfx build first" >&2
  exit 1
fi

cp -r "$DOCFX_OUT/." "$OUT/"

# Remove stale nested folders from older docfx dest paths.
rm -rf "$OUT/dev/dev" "$OUT/api/api"

# DocFX template assets (Bootstrap + DocFX JS). Publish as /docfx/ — avoids any
# hosting quirks with a top-level public/ folder and gives us a stable URL.
if [[ ! -d "$OUT/public" ]]; then
  echo "error: DocFX public assets missing at $OUT/public — docfx build incomplete" >&2
  exit 1
fi
rm -rf "$OUT/docfx"
cp -a "$OUT/public" "$OUT/docfx"

if [[ ! -f "$OUT/api/index.html" ]] || ! head -1 "$OUT/api/index.html" | grep -q '<!DOCTYPE html>'; then
  echo "error: styled api/index.html missing — check site/api/index.md is in docfx.json content" >&2
  exit 1
fi

# Marketing homepage replaces DocFX index.md output.
cp "${ROOT}/site/homepage/index.html" "$OUT/index.html"

# Screenshots (theme-cap images on homepage).
cp -r "${ROOT}/docs/caps" "$OUT/caps"

# Static assets at site root (/assets/).
mkdir -p "$OUT/assets"
cp "${ROOT}/site/assets/home.css" "$OUT/assets/"
cp "${ROOT}/site/assets/home.js" "$OUT/assets/"
cp "${ROOT}/site/assets/doc-content.css" "$OUT/assets/"
cp "${ROOT}/site/assets/docfx-theme.css" "$OUT/assets/"

# Custom domain + disable Jekyll (required for /app/_framework/).
if [[ -f "${ROOT}/site/CNAME" ]]; then
  cp "${ROOT}/site/CNAME" "$OUT/CNAME"
fi
touch "$OUT/.nojekyll"

# WASM web demo under /app/.
if [[ -d "$BUNDLE" ]]; then
  cp -r "$BUNDLE/." "$OUT/app/"
else
  echo "warning: WASM AppBundle not found at $BUNDLE — skipping /app/" >&2
fi

echo "Assembled site at $OUT"
find "$OUT" -maxdepth 2 -mindepth 1 | sort | awk 'NR <= 40'
