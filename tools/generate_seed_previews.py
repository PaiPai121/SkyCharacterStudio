"""Generate bundled portrait seeds for the first-run offline preview.

This utility is for the developer workspace only. It reads selected DDS
entries from the game image.pac and writes small PNGs under assets/previews;
the game archive itself is never modified.
"""
from __future__ import annotations

import io
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
MOD_ROOT = ROOT.parent / "Sky1st-Scherazard-Mod"
sys.path.insert(0, str(MOD_ROOT / "scripts"))
from pac import entries  # noqa: E402


def choose(items, model_id):
    prefix = "fc_" + model_id
    candidates = [
        item for item in items
        if item["name"].lower().endswith(".dds")
        and item["name"].rsplit("/", 1)[-1].lower().startswith(prefix)
    ]
    def rank(item):
        name = item["name"].lower()
        return (0 if "e50" in name else 1 if "e00" in name else 2, len(name))
    return min(candidates, key=rank) if candidates else None


def main():
    game_root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(r"D:/SteamLibrary/steamapps/common/Sora No Kiseki the 1st")
    archive_path = game_root / "pac/steam/image.pac"
    archive_items = entries(archive_path)
    output = ROOT / "assets/previews"
    output.mkdir(parents=True, exist_ok=True)
    for number in range(5000, 5008):
        model_id = f"chr{number}"
        selected = choose(archive_items, model_id)
        if selected is None:
            continue
        with archive_path.open("rb") as stream:
            stream.seek(selected["offset"])
            data = stream.read(selected["size"])
        with Image.open(io.BytesIO(data)) as image:
            image = image.convert("RGBA")
            image.thumbnail((640, 640), Image.Resampling.LANCZOS)
            image.save(output / f"{model_id}.png", "PNG", optimize=True)
        print(model_id, selected["name"])


if __name__ == "__main__":
    main()
