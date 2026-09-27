"""Read the installed 2nd Chapter models and round-trip each editable MDL.

Only the JSONL report and a summary remain. Temporary exports are removed
after each model; the game's PAC files are opened read-only.
"""
import argparse
import contextlib
import hashlib
import io
import json
import sys
import time
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import auto_model as editor


def inspect(data, materials):
    if not data["mesh_blocks"] or len(data["mesh_blocks"]) != len(data["mesh_buffers"]):
        raise ValueError("empty or unpaired mesh blocks")
    names = [material["material_name"] for material in materials]
    vertices = triangles = skinned = 0
    for group, buffers in zip(data["mesh_blocks"], data["mesh_buffers"]):
        if len(group["primitives"]) != len(buffers):
            raise ValueError("primitive/buffer count differs")
        for primitive, mesh in zip(group["primitives"], buffers):
            if primitive["material"] not in names:
                raise ValueError("material reference missing")
            attrs = {(item["SemanticName"], int(item.get("SemanticIndex", 0))): np.asarray(item["Buffer"])
                     for item in mesh["vb"]}
            position = attrs.get(("POSITION", 0))
            if position is None or position.ndim != 2 or position.shape[1] < 3 or not np.isfinite(position).all():
                raise ValueError("invalid vertex positions")
            count = len(position)
            vertices += count
            indices = np.asarray(mesh["ib"]["Buffer"], dtype=np.int64).reshape(-1)
            if len(indices) % 3 or (len(indices) and (indices.min() < 0 or indices.max() >= count)):
                raise ValueError("invalid triangle indices")
            triangles += len(indices) // 3
            weights = attrs.get(("BLENDWEIGHT", 0))
            bones = attrs.get(("BLENDINDICES", 0))
            if (weights is None) != (bones is None):
                raise ValueError("incomplete skinning buffers")
            if weights is not None:
                if weights.shape != bones.shape or len(weights) != count or not np.isfinite(weights).all():
                    raise ValueError("invalid skinning buffers")
                if (weights < -1e-5).any() or (weights.sum(axis=1) < .98).any() or (weights.sum(axis=1) > 1.02).any():
                    raise ValueError("skin weights do not sum to one")
                if len(group.get("nodes", [])) and (bones[weights > 0].max(initial=0) >= len(group["nodes"])):
                    raise ValueError("bone index outside skeleton")
                skinned += count
    if vertices == 0 or triangles == 0:
        raise ValueError("no drawable geometry")
    return {"vertices": vertices, "triangles": triangles, "skinned_vertices": skinned,
            "materials": len(names), "mesh_groups": len(data["mesh_blocks"])}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--game", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--timeout-seconds", type=int, default=900)
    parser.add_argument("--model", action="append", help="Limit the audit to a model ID; may be repeated")
    args = parser.parse_args()
    game = args.game.resolve(strict=True)
    archive = game / "pac/steam/asset_common_model.pac"
    if not (game / "sora_2nd.exe").is_file() or not archive.is_file():
        raise SystemExit("Expected the installed 2nd Chapter game directory")
    rows = [entry for entry in editor.entries(archive)
            if entry["name"].startswith("asset/common/model/chr")
            and entry["name"].endswith(".mdl")
            and "_" not in Path(entry["name"]).stem]
    if args.model:
        wanted = set(args.model)
        rows = [entry for entry in rows if Path(entry["name"]).stem in wanted]
        if len(rows) != len(wanted):
            raise SystemExit(f"Missing model IDs: {sorted(wanted - {Path(entry['name']).stem for entry in rows})}")
    report = args.report.resolve()
    report.parent.mkdir(parents=True, exist_ok=True)
    started = time.monotonic()
    failures = []
    versions = {}
    with report.open("w", encoding="utf-8") as log:
        for index, entry in enumerate(rows, 1):
            if time.monotonic() - started > args.timeout_seconds:
                raise TimeoutError(f"Model audit exceeded {args.timeout_seconds}s after {index - 1}/{len(rows)}; partial report: {report}")
            model_id = Path(entry["name"]).stem
            row = {"model": model_id, "archive_bytes": entry["size"]}
            output = report.parent / f"_audit_{model_id}"
            output.mkdir(exist_ok=False)
            try:
                raw, mats, data = editor.prepare(game, model_id)
                version = int.from_bytes(raw[4:8], "little")
                row["version"] = version
                row.update(inspect(data, mats))
                task = argparse.Namespace(game=game, model=model_id, mode="width", strength=100,
                                          out=output, export=True, base_game_character=True, edition="second")
                with contextlib.redirect_stdout(io.StringIO()):
                    editor.run(task)
                patched = (output / f"{model_id}.mdl").read_bytes()
                if len(raw) != len(patched) or patched[:8] != raw[:8]:
                    raise ValueError("export changed model size or header")
                remats = editor.mdl.obtain_material_data(patched)
                redata = editor.mdl.obtain_mesh_data(patched, remats)
                if [x["material_name"] for x in mats] != [x["material_name"] for x in remats]:
                    raise ValueError("material name order changed")
                if inspect(redata, remats) != {key: row[key] for key in
                    ("vertices", "triangles", "skinned_vertices", "materials", "mesh_groups")}:
                    raise ValueError("round-trip mesh contract changed")
                row["source_sha256"] = hashlib.sha256(raw).hexdigest()
                row["output_sha256"] = hashlib.sha256(patched).hexdigest()
                row["ok"] = True
                versions[version] = versions.get(version, 0) + 1
            except Exception as error:
                row["ok"] = False
                row["error"] = f"{type(error).__name__}: {error}"
                failures.append(row)
            finally:
                for item in output.iterdir():
                    if not item.is_file():
                        raise RuntimeError(f"Unexpected audit output directory: {item}")
                    item.unlink()
                output.rmdir()
            log.write(json.dumps(row, ensure_ascii=False) + "\n")
            log.flush()
            if index % 10 == 0 or index == len(rows):
                print(f"AUDIT {index}/{len(rows)} elapsed={time.monotonic()-started:.0f}s failures={len(failures)} report={report}", flush=True)
    summary = {"models": len(rows), "passed": len(rows) - len(failures), "failed": len(failures),
               "versions_passed": versions, "failures": failures, "elapsed_seconds": round(time.monotonic()-started, 1)}
    report.with_suffix(".summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(summary, ensure_ascii=False))
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
