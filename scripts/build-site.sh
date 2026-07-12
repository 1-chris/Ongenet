#!/usr/bin/env bash
# Build the full Ongenet documentation site (DocFX + WASM) and assemble _site/.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

CONFIG="${ROOT}/site/docfx.json"
BUILD_WASM="${BUILD_WASM:-1}"

# DocFX does not emit template public/ when a custom layout/_master.tmpl is used (last template wins).
ensure_docfx_public_assets() {
  local site_out="${ROOT}/site/_site"
  if [[ -f "${site_out}/public/docfx.min.css" ]]; then
    return 0
  fi

  echo "==> Copy DocFX template public assets (custom master layout skips them)"
  local version nuget_root src
  version="$(python3 -c "import json; print(json.load(open('${ROOT}/.config/dotnet-tools.json'))['tools']['docfx']['version'])")"
  nuget_root="${NUGET_PACKAGES:-${HOME}/.nuget/packages}"
  src="${nuget_root}/docfx/${version}/templates/modern/public"

  if [[ ! -f "${src}/docfx.min.css" ]]; then
    echo "error: DocFX public assets not found at ${src}" >&2
    exit 1
  fi

  mkdir -p "${site_out}/public"
  cp -a "${src}/." "${site_out}/public/"
}

API_PROJECTS=(
  Ongenet.Core/Ongenet.Core.csproj
  Ongenet.App/Ongenet.App.csproj
  Ongenet.Audio/Ongenet.Audio.csproj
  Ongenet.Scripting/Ongenet.Scripting.csproj
  Ongenet.Engine3D/Ongenet.Engine3D.csproj
  Ongenet.Engine3D.Abstractions/Ongenet.Engine3D.Abstractions.csproj
  Ongenet.PluginHost/Ongenet.PluginHost.csproj
  Ongenet.Clap/Ongenet.Clap.csproj
  Ongenet.Lv2/Ongenet.Lv2.csproj
  Ongenet.Vst/Ongenet.Vst.csproj
  Ongenet.Au/Ongenet.Au.csproj
  Ongenet.Ara/Ongenet.Ara.csproj
  Ongenet.Link/Ongenet.Link.csproj
)

echo "==> Restore DocFX tool"
dotnet tool restore

echo "==> Build API doc projects (Release, XML docs)"
for proj in "${API_PROJECTS[@]}"; do
  echo "    $proj"
  dotnet build "$proj" -c Release
done

echo "==> DocFX metadata"
dotnet docfx metadata "$CONFIG"

echo "==> Add API landing page to generated API table of contents"
python3 - "${ROOT}/site/api/toc.yml" <<'PY'
from pathlib import Path
import sys

toc = Path(sys.argv[1])
text = toc.read_text(encoding="utf-8")
entry = "- name: API reference\n  href: index.md\n"
if entry not in text:
    text = text.replace("items:\n", f"items:\n{entry}", 1)
    toc.write_text(text, encoding="utf-8")
PY

DEV_DIR="${ROOT}/site/dev"
echo "==> Link dev tutorials into site/dev for DocFX"
find "$DEV_DIR" -maxdepth 1 -type l -name '*.md' -delete
for f in "${ROOT}/docs"/*.md; do
  name="$(basename "$f")"
  ln -sf "../../docs/$name" "${DEV_DIR}/${name}"
done

echo "==> DocFX build"
dotnet docfx build "$CONFIG"

find "$DEV_DIR" -maxdepth 1 -type l -name '*.md' -delete


if [[ "$BUILD_WASM" == "1" ]]; then
  echo "==> Publish Ongenet.Web"
  dotnet workload install wasm-tools 2>/dev/null || true
  dotnet publish Ongenet.Web/Ongenet.Web.csproj -c Release
  echo "==> DocFX metadata (Web)"
  if dotnet docfx metadata "${ROOT}/site/docfx.web.json"; then
    dotnet docfx build "$CONFIG"
  else
    echo "warning: Ongenet.Web API metadata skipped" >&2
  fi
fi

# Optional: Android API when workload + SDK are available.
if dotnet workload list 2>/dev/null | grep -q android && [[ -n "${ANDROID_HOME:-}" || -d "${HOME}/Library/Android/sdk" ]]; then
  echo "==> DocFX metadata (Android)"
  dotnet docfx metadata "${ROOT}/site/docfx.android.json" && dotnet docfx build "$CONFIG" || true
fi

# Optional: Desktop API (may require non-self-contained publish settings).
if dotnet build Ongenet.Desktop/Ongenet.Desktop.csproj -c Release >/dev/null 2>&1; then
  echo "==> DocFX metadata (Desktop)"
  dotnet docfx metadata "${ROOT}/site/docfx.desktop.json" && dotnet docfx build "$CONFIG" || true
fi

ensure_docfx_public_assets

echo "==> Assemble _site"
bash "${ROOT}/scripts/assemble-site.sh"

echo "Done."
