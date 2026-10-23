"""Regression check for player ZIPs accidentally repacked from a used folder."""

import hashlib
import json
import sys
import tempfile
import zipfile
from pathlib import Path


sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
from audit_release_zip import audit


def fixture(root: Path, name: str, extras: dict[str, bytes] | None = None,
            *, dirty: bool = False, directory: bool = False) -> Path:
    files = {
        "SkyCharacterStudio.exe": b"launcher fixture",
        "README-PLAYERS.md": b"portable app",
        "tools/auto_model.py": b"tool fixture",
        "runtime/python/python.exe": b"runtime fixture",
        "runtime/mod-template/xinput1_4.dll": b"first loader fixture",
        "runtime/second-loader/xinput1_4.dll": b"second loader fixture",
    }
    files.update(extras or {})
    manifest = {
        "version": "0.3.22-beta",
        "source_commit": "0" * 40,
        "source_dirty": dirty,
        "files": {path: hashlib.sha256(data).hexdigest() for path, data in files.items()},
    }
    archive = root / name
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as output:
        if directory:
            output.writestr("cache/", b"")
        for path, data in files.items():
            output.writestr(path, data)
        output.writestr("release-manifest.json", json.dumps(manifest).encode())
    return archive


def rejected(path: Path, reason: str, *, upload: bool = False) -> None:
    try:
        audit(path, upload=upload)
    except ValueError as error:
        if reason not in str(error):
            raise AssertionError(f"Expected {reason!r}; got {error!r}") from error
    else:
        raise AssertionError(f"Expected ZIP to be rejected: {path}")


def main() -> None:
    with tempfile.TemporaryDirectory(prefix="sky-release-audit-") as temporary:
        root = Path(temporary)
        good = fixture(root, "SkyCharacterStudio-0.3.22-beta-win-x64.zip")
        assert audit(good, upload=True) == 7
        rejected(fixture(root, "directory.zip", directory=True), "directory entries")
        rejected(fixture(root, "cache.zip", {"cache/models/chr5000.mdl": b"model"}), "local user state")
        rejected(fixture(root, "nested.zip", {"extra.zip": b"nested"}), "another archive")
        rejected(fixture(root, "pyc.zip", {"runtime/Lib/__pycache__/x.pyc": b"cache"}), "generated state")
        rejected(fixture(root, "game.zip", {"runtime/model.mdl": b"model"}), "game model or asset")
        rejected(fixture(root, "dirty.zip", dirty=True), "uncommitted source", upload=True)
        rejected(fixture(root, "wrong-name.zip"), "filename", upload=True)
    print("PASS release ZIP audit rejects repacked, dirty, nested and game-asset archives")


if __name__ == "__main__":
    main()
