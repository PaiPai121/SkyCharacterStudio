"""Reject player archives containing local state or files outside the release manifest."""

import argparse
import hashlib
import json
import sys
import zipfile
from pathlib import Path


def audit(archive_path: Path) -> int:
    state_roots = {"cache", "exports", "install-backups", "logs", "_smoke", "_second_install_smoke"}
    game_extensions = {".mdl", ".dat", ".dds", ".pac", ".blend", ".fbx"}
    with zipfile.ZipFile(archive_path) as archive:
        entries = archive.infolist()
        names = [entry.filename for entry in entries]
        if len(names) != len(set(names)) or any(entry.is_dir() for entry in entries):
            raise ValueError("Archive has duplicate paths or directory entries")
        for name in names:
            path = Path(name)
            if (not name or "\\" in name or name.startswith("/") or
                    any(part in {"", ".", ".."} for part in name.split("/"))):
                raise ValueError(f"Unsafe archive path: {name}")
            if name.split("/", 1)[0].lower() in state_roots or name.lower() == "game-directory.txt":
                raise ValueError(f"Archive contains local user state: {name}")
            if path.suffix.lower() in game_extensions:
                raise ValueError(f"Archive contains a game model or asset: {name}")

        if "release-manifest.json" not in names:
            raise ValueError("Archive has no root release manifest; do not repack a used application folder")
        with archive.open("release-manifest.json") as stream:
            manifest = json.load(stream)
        declared = manifest.get("files")
        if not isinstance(declared, dict):
            raise ValueError("Release manifest has no file list")
        expected = set(declared) | {"release-manifest.json"}
        actual = set(names)
        if actual != expected:
            extra = sorted(actual - expected)
            missing = sorted(expected - actual)
            raise ValueError(f"Archive differs from manifest; extra={extra[:5]}, missing={missing[:5]}")

        for name, expected_hash in declared.items():
            digest = hashlib.sha256()
            with archive.open(name) as stream:
                for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                    digest.update(chunk)
            if digest.hexdigest().lower() != str(expected_hash).lower():
                raise ValueError(f"Archive file differs from manifest: {name}")
    return len(names)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--zip", dest="archive", required=True, type=Path)
    args = parser.parse_args()
    count = audit(args.archive)
    print(f"PASS clean player ZIP: {count} files match the release manifest")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, zipfile.BadZipFile, json.JSONDecodeError) as error:
        sys.exit(f"Release ZIP rejected: {error}")
