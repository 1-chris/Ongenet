#!/usr/bin/env python3
"""Embed a PNG as a Vista-style ICO (single image). No third-party deps."""
from __future__ import annotations

import struct
import sys
from pathlib import Path


def png_to_ico(png_path: Path, ico_path: Path) -> None:
    png_data = png_path.read_bytes()
    if png_data[:8] != b"\x89PNG\r\n\x1a\n":
        raise SystemExit(f"Not a PNG: {png_path}")

    # 6-byte ICO header + 16-byte directory entry + PNG payload
    header = struct.pack("<HHH", 0, 1, 1)
    # width/height 0 means 256 in ICO directory entries
    entry = struct.pack("<BBBBHHII", 0, 0, 0, 0, 1, 32, len(png_data), 6 + 16)
    ico_path.write_bytes(header + entry + png_data)


def main() -> None:
    if len(sys.argv) != 3:
        raise SystemExit(f"Usage: {sys.argv[0]} <input.png> <output.ico>")
    png_to_ico(Path(sys.argv[1]), Path(sys.argv[2]))


if __name__ == "__main__":
    main()
