#!/usr/bin/env python3
"""Generate Strings.ja.axaml from Strings.en.axaml using exact Japanese translations."""

from __future__ import annotations

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EN = ROOT / "Ongenet.App" / "Resources" / "Strings.en.axaml"
JA = ROOT / "Ongenet.App" / "Resources" / "Strings.ja.axaml"
EXACT = Path(__file__).resolve().parent / "i18n_ja_exact.json"


def load_exact() -> dict[str, str]:
    return json.loads(EXACT.read_text(encoding="utf-8"))


def translate_value(en: str, exact: dict[str, str]) -> str:
    if en in exact:
        return exact[en]
    raise KeyError(f"Missing Japanese translation for: {en!r}")


def escape_xml(s: str) -> str:
    return (
        s.replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace('"', "&quot;")
    )


def parse_en(path: Path) -> list[tuple[str, str]]:
    text = path.read_text(encoding="utf-8")
    entries: list[tuple[str, str]] = []
    for m in re.finditer(r'x:Key="([^"]+)"[^>]*>([^<]*)</system:String>', text):
        entries.append((m.group(1), m.group(2)))
    return entries


def main() -> None:
    exact = load_exact()
    entries = parse_en(EN)
    missing: list[str] = []

    lines = [
        '<ResourceDictionary xmlns="https://github.com/avaloniaui"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"',
        '                    xmlns:system="using:System">',
        "    <!-- UI strings (Japanese). Keys must match Strings.en.axaml. -->",
    ]
    for key, value in entries:
        try:
            ja = translate_value(value, exact)
        except KeyError:
            missing.append(value)
            ja = value
        lines.append(f'    <system:String x:Key="{key}">{escape_xml(ja)}</system:String>')
    lines.append("</ResourceDictionary>")

    if missing:
        unique = sorted(set(missing))
        raise SystemExit(
            f"Missing {len(unique)} translation(s). Add to {EXACT.name}, e.g.:\n"
            + "\n".join(f"  {v!r}" for v in unique[:20])
        )

    JA.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"Wrote {len(entries)} keys to {JA}")


if __name__ == "__main__":
    main()
