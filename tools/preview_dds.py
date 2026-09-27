"""Convert one DDS texture to a small PNG for the offline character studio.

Pillow is used because the game's image archive uses BC7/DX10 DDS textures,
which are not decoded by the stock WPF image codecs on every Windows install.
The helper is intentionally read-only with respect to the game archive: the
studio writes a temporary DDS cache and this script only creates the PNG next
to it.
"""
from __future__ import annotations

import io
import sys
from pathlib import Path

from PIL import Image
from asset_codec import decode_payload


def main() -> int:
    if len(sys.argv) != 3:
        print("usage: preview_dds.py INPUT.dds OUTPUT.png", file=sys.stderr)
        return 2
    source = Path(sys.argv[1])
    target = Path(sys.argv[2])
    target.parent.mkdir(parents=True, exist_ok=True)
    with Image.open(io.BytesIO(decode_payload(source.read_bytes()))) as image:
        image = image.convert("RGBA")
        image.thumbnail((640, 640), Image.Resampling.LANCZOS)
        image.save(target, "PNG", optimize=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
