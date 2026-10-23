"""Reject player archives containing local state or files outside the release manifest."""

import argparse
import hashlib
import json
import re
import sys
import zipfile
from pathlib import Path


def audit(archive_path: Path, upload: bool = False) -> int:
    state_roots = {"cache", "exports", "install-backups", "logs", "_smoke", "_second_install_smoke"}
    game_extensions = {".mdl", ".dat", ".dds", ".pac", ".blend", ".fbx"}
    nested_archives = {".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".cab", ".iso"}
    with zipfile.ZipFile(archive_path) as archive:
        entries = archive.infolist()
        names = [entry.filename for entry in entries]
        if len(names) != len(set(names)) or any(entry.is_dir() for entry in entries):
            raise ValueError("Archive has duplicate paths or directory entries")
        for entry in entries:
            name = entry.filename
            path = Path(name)
            if (not name or "\\" in name or name.startswith("/") or
                    any(part in {"", ".", ".."} for part in name.split("/"))):
                raise ValueError(f"Unsafe archive path: {name}")
            if entry.flag_bits & 1:
                raise ValueError(f"Archive contains an encrypted file: {name}")
            if entry.compress_type not in {zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED}:
                raise ValueError(f"Archive uses unsupported compression: {name}")
            if (entry.external_attr >> 16) & 0o170000 == 0o120000:
                raise ValueError(f"Archive contains a symbolic link: {name}")
            if name.split("/", 1)[0].lower() in state_roots or name.lower() == "game-directory.txt":
                raise ValueError(f"Archive contains local user state: {name}")
            if path.suffix.lower() in game_extensions:
                raise ValueError(f"Archive contains a game model or asset: {name}")
            if path.suffix.lower() in nested_archives:
                raise ValueError(f"Archive contains another archive: {name}")
            if path.suffix.lower() in {".pyc", ".pyo", ".log"} or "__pycache__" in {part.lower() for part in name.split("/")}:
                raise ValueError(f"Archive contains generated state: {name}")

        if "release-manifest.json" not in names:
            raise ValueError("Archive has no root release manifest; do not repack a used application folder")
        with archive.open("release-manifest.json") as stream:
            manifest = json.load(stream)
        if upload:
            version = manifest.get("version")
            if manifest.get("source_dirty") is not False:
                raise ValueError("Upload ZIP was built from uncommitted source")
            if not isinstance(version, str) or archive_path.name != f"SkyCharacterStudio-{version}-win-x64.zip":
                raise ValueError("Upload ZIP filename does not match the release manifest version")
            if not re.fullmatch(r"[0-9a-fA-F]{40}", str(manifest.get("source_commit", ""))):
                raise ValueError("Upload ZIP has no source commit")
        declared = manifest.get("files")
        if not isinstance(declared, dict):
            raise ValueError("Release manifest has no file list")
        expected = set(declared) | {"release-manifest.json"}
        actual = set(names)
        if upload:
            required = {
                "SkyCharacterStudio.exe", "README-PLAYERS.md", "tools/auto_model.py",
                "runtime/python/python.exe", "runtime/mod-template/xinput1_4.dll",
                "runtime/second-loader/xinput1_4.dll",
            }
            if not required.issubset(actual):
                raise ValueError(f"Upload ZIP is missing runtime files: {sorted(required - actual)}")
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
    parser.add_argument("--upload", action="store_true", help="Require a clean-source, correctly named player ZIP")
    args = parser.parse_args()
    count = audit(args.archive, upload=args.upload)
    print(f"PASS clean player ZIP: {count} files match the release manifest")
    if args.upload:
        sha = hashlib.sha256()
        with args.archive.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                sha.update(chunk)
        print(f"UPLOAD THIS EXACT FILE: {args.archive.resolve()}")
        print(f"BYTES: {args.archive.stat().st_size}")
        print(f"SHA256: {sha.hexdigest().upper()}")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, zipfile.BadZipFile, json.JSONDecodeError) as error:
        sys.exit(f"Release ZIP rejected: {error}")
