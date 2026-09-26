"""Create a file-only player ZIP and verify every entry against staging."""

import argparse
import hashlib
import zipfile
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--stage", type=Path, required=True)
parser.add_argument("--zip", dest="archive", type=Path, required=True)
args = parser.parse_args()

stage = args.stage.resolve()
archive_path = args.archive.resolve()
if not stage.is_dir():
    raise SystemExit(f"Missing release staging directory: {stage}")
if archive_path.exists():
    raise SystemExit(f"Release ZIP already exists; choose a new version: {archive_path}")

files = sorted((p for p in stage.rglob("*") if p.is_file()), key=lambda p: p.relative_to(stage).as_posix())
if not files:
    raise SystemExit("Release staging directory has no files")
names = [p.relative_to(stage).as_posix() for p in files]
if len(names) != len(set(names)):
    raise SystemExit("Duplicate release paths")
if any(p.is_symlink() or p.resolve().is_relative_to(stage) is False for p in files):
    raise SystemExit("Release contains a symlink or a path outside staging")

archive_path.parent.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9, allowZip64=True) as zip_out:
    for file, name in zip(files, names):
        zip_out.write(file, name)

with zipfile.ZipFile(archive_path) as zip_in:
    entries = zip_in.infolist()
    if [entry.filename for entry in entries] != names or any(entry.is_dir() for entry in entries):
        raise SystemExit("Release ZIP has unexpected or directory entries")
    for file, entry in zip(files, entries):
        source_hash = hashlib.sha256()
        archive_hash = hashlib.sha256()
        with file.open("rb") as source, zip_in.open(entry) as packed:
            while chunk := source.read(1024 * 1024):
                source_hash.update(chunk)
            while chunk := packed.read(1024 * 1024):
                archive_hash.update(chunk)
        if source_hash.digest() != archive_hash.digest():
            raise SystemExit(f"Release ZIP content mismatch: {entry.filename}")

print(f"PASS release ZIP: {len(files)} file entries, no directories or duplicates, all contents match staging")
