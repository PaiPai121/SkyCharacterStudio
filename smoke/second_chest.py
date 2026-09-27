"""Offline 2nd Chapter adult chest export and mesh-safety audit.

The installed game is read-only. Per-model exports are removed after checking;
the JSONL report is retained for diagnosis.
"""
import argparse
import contextlib
import io
import json
import sys
import time
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import auto_model as editor
from character_age import CATALOGS
from second_assets import inspect


def check_geometry(params, strength, data, parsed):
    changed = 0
    minimum_jacobian = 1.0
    minimum_face_cosine = 1.0
    for group_index, (before_group, after_group) in enumerate(zip(data["mesh_buffers"], parsed["mesh_buffers"])):
        for before, after in zip(before_group, after_group):
            positions = lambda mesh: np.asarray(next(item["Buffer"] for item in mesh["vb"]
                                                     if item["SemanticName"] == "POSITION"))
            p, q = positions(before), positions(after)
            masks = editor.mesh_masks(data, group_index, before, params[1])
            expected, jacobian = editor.deform(p, strength, "chest", params, masks)
            if not np.isfinite(q).all() or not np.isfinite(jacobian).all():
                raise ValueError("non-finite deformation")
            if not np.allclose(q, expected, atol=1e-6):
                raise ValueError("round-trip position differs from live deformation")
            if before["ib"]["Buffer"] != after["ib"]["Buffer"]:
                raise ValueError("triangle topology changed")
            changed += int(np.count_nonzero(np.linalg.norm(q - p, axis=1) > 1e-7))
            minimum_jacobian = min(minimum_jacobian, float(np.linalg.det(jacobian).min()))
            triangles = np.asarray(before["ib"]["Buffer"], dtype=np.int64).reshape(-1, 3)
            if len(triangles):
                old = np.cross(p[triangles[:, 1]] - p[triangles[:, 0]], p[triangles[:, 2]] - p[triangles[:, 0]])
                new = np.cross(q[triangles[:, 1]] - q[triangles[:, 0]], q[triangles[:, 2]] - q[triangles[:, 0]])
                old_size = np.linalg.norm(old, axis=1)
                new_size = np.linalg.norm(new, axis=1)
                valid = old_size > params[2] ** 2 * 1e-10
                if np.any(new_size[valid] <= old_size[valid] * .01):
                    raise ValueError("collapsed triangle")
                if valid.any():
                    minimum_face_cosine = min(minimum_face_cosine,
                        float(np.min(np.sum(old[valid] * new[valid], axis=1) / (old_size[valid] * new_size[valid]))))
    if changed == 0 or minimum_jacobian <= .05 or minimum_face_cosine <= 0:
        raise ValueError(f"unsafe deformation: changed={changed}, jacobian={minimum_jacobian}, face_cos={minimum_face_cosine}")
    return {"changed_vertices": changed, "min_jacobian": minimum_jacobian,
            "min_face_cosine": minimum_face_cosine}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--game", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--timeout-seconds", type=int, default=1200)
    parser.add_argument("--model", action="append")
    args = parser.parse_args()
    game = args.game.resolve(strict=True)
    if not (game / "sora_2nd.exe").is_file():
        raise SystemExit("Expected the 2nd Chapter game directory")
    catalog = CATALOGS["second"]
    ids = sorted(model for model in catalog if editor.adult_eligible(model, True, "second"))
    if args.model:
        ids = [model for model in ids if model in set(args.model)]
        if len(ids) != len(set(args.model)):
            raise SystemExit("One or more requested IDs are absent from the adult catalog")
    report = args.report.resolve()
    report.parent.mkdir(parents=True, exist_ok=True)
    started = time.monotonic()
    failures = []
    with report.open("w", encoding="utf-8") as log:
        for index, model_id in enumerate(ids, 1):
            if time.monotonic() - started > args.timeout_seconds:
                raise TimeoutError(f"Chest audit exceeded {args.timeout_seconds}s after {index - 1}/{len(ids)}; report: {report}")
            row = {"model": model_id, "checks": []}
            output = report.parent / f"_chest_{model_id}"
            output.mkdir(exist_ok=False)
            try:
                raw, mats, data = editor.prepare(game, model_id)
                params = editor.profile(data, model_id, "chest", True, "second")
                row["detection"] = editor.detect_chest(data)["status"]
                source_contract = inspect(data, mats)
                for strength in (-500, 1000):
                    task = argparse.Namespace(game=game, model=model_id, mode="chest", strength=strength,
                                              out=output, export=True, base_game_character=True, edition="second")
                    with contextlib.redirect_stdout(io.StringIO()):
                        editor.run(task)
                    patched = (output / f"{model_id}.mdl").read_bytes()
                    if len(raw) != len(patched):
                        raise ValueError("export size changed")
                    parsed_mats = editor.mdl.obtain_material_data(patched)
                    parsed = editor.mdl.obtain_mesh_data(patched, parsed_mats)
                    if ([m["material_name"] for m in mats] != [m["material_name"] for m in parsed_mats]
                        or inspect(parsed, parsed_mats) != source_contract
                        or [[node["name"] for node in group.get("nodes", [])] for group in data["mesh_blocks"]]
                        != [[node["name"] for node in group.get("nodes", [])] for group in parsed["mesh_blocks"]]):
                        raise ValueError("material, skeleton or mesh contract changed")
                    row["checks"].append({"strength": strength,
                                          **check_geometry(params, strength, data, parsed)})
                row["ok"] = True
            except Exception as error:
                row["ok"] = False
                row["error"] = f"{type(error).__name__}: {error}"
                failures.append(row)
            finally:
                for item in output.iterdir():
                    if not item.is_file():
                        raise RuntimeError(f"Unexpected output directory: {item}")
                    item.unlink()
                output.rmdir()
            log.write(json.dumps(row, ensure_ascii=False) + "\n")
            log.flush()
            print(f"CHEST {index}/{len(ids)} {model_id} {'PASS' if row['ok'] else row['error']} elapsed={time.monotonic()-started:.0f}s", flush=True)
    summary = {"models": len(ids), "passed": len(ids) - len(failures), "failed": failures,
               "elapsed_seconds": round(time.monotonic() - started, 1)}
    report.with_suffix(".summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
