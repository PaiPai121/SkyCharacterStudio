"""Apply the validated Scherazard contour edit to an extracted MDL copy.

The first studio backend deliberately limits binary editing to chr5002.  The
mesh buffers and section offsets were validated against the working model in
Sky1st-Scherazard-Mod; every other scanned character is exported as a stock
copy plus a JSON preset until its topology has been checked separately.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
import sys
from pathlib import Path

import numpy as np


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--mesh-root", type=Path, required=True)
    parser.add_argument("--strength", type=float, required=True)
    return parser.parse_args()


def load_buffer_helpers(mesh_root: Path):
    vendor = mesh_root.resolve().parents[1] / "vendor"
    local_tools = Path(__file__).resolve().parent
    sys.path.insert(0, str(vendor))
    sys.path.insert(0, str(local_tools))
    from lib_fmtibvb import read_fmt, read_vb  # type: ignore

    return read_fmt, read_vb


def sections(data: bytes):
    offset = 12
    result = {}
    while offset + 8 <= len(data):
        kind, size = struct.unpack_from("<II", data, offset)
        if offset + 8 + size > len(data):
            raise ValueError(f"bad MDL section length for section {kind}")
        result[kind] = (offset + 8, size)
        offset += size + 8
    return result


def deformation(position, strength):
    factor = max(-5.0, min(10.0, strength / 100.0))
    p = np.asarray(position, dtype=np.float64)
    jacobian = np.eye(3)
    predicted = p.copy()
    for side in (-1.0, 1.0):
        delta = p - np.asarray([side * 0.087, 1.275, 0.15])
        r2 = np.sum((delta / 0.115) ** 2)
        if r2 < 1.0:
            weight = (1.0 - r2) ** 3
            direction = np.asarray([side * 0.004 * factor, 0.0, 0.014 * factor])
            predicted += direction * weight
            gradient = -6.0 * (1.0 - r2) ** 2 * delta / (0.115 ** 2)
            jacobian += np.outer(direction, gradient)
    return predicted, jacobian


def unit(vector):
    return vector / max(float(np.linalg.norm(vector)), 1e-12)


def patch(args: argparse.Namespace):
    strength = max(-500.0, min(1000.0, float(args.strength)))
    source = args.input.read_bytes()
    input_hash = hashlib.sha256(source).hexdigest()
    original_hash = '768d93a3327cce025f53f2ec64e3afc0e261bed092b0b532134644899b339e0a'
    installed_hash = 'e989dec9870ced4727fe22118dca427daa8b6c0c1efedbbc445efe36a3c744b0'
    if input_hash == installed_hash:
        baseline = args.mesh_root.resolve().parents[1] / 'source/asset/common/model/chr5002.mdl'
        source = baseline.read_bytes()
    if hashlib.sha256(source).hexdigest() != original_hash:
        raise ValueError('Unsupported source model or invalid original backup; export stopped')
    if source[:4] != b"MDL " or struct.unpack_from("<I", source, 4)[0] != 4:
        raise ValueError("only MDL v4 is supported")
    read_fmt, read_vb = load_buffer_helpers(args.mesh_root)
    info = json.loads((args.mesh_root / "mesh_info.json").read_text(encoding="utf-8"))
    primitive_start, _ = sections(source)[4]
    patched = bytearray(source)
    changed_vertices = 0
    max_displacement = 0.0
    old_hash = hashlib.sha256(source).hexdigest()

    for group_id, group in enumerate(info):
        for primitive_id, primitive in enumerate(group["primitives"]):
            if group_id not in (1, 2):
                continue
            name = f'{group_id}_{group["name"]}_{primitive_id:02d}'
            base = args.mesh_root / name
            fmt = read_fmt(str(base) + ".fmt")
            vb = read_vb(str(base) + ".vb", fmt)
            buffers = {entry["SemanticName"]: entry["Buffer"] for entry in vb}
            elements = {entry["Semantic"]: entry for entry in primitive["Elements"]}
            positions = buffers.get("POSITION", [])
            for vertex_id, old in enumerate(positions):
                new, jacobian = deformation(old, strength)
                delta = float(np.linalg.norm(new - np.asarray(old)))
                if delta < 1e-9:
                    continue
                position_entry = elements["POSITION"]
                position_offset = primitive_start + position_entry["offset"] + vertex_id * position_entry["stride"]
                packed_old = struct.unpack_from("<3f", source, position_offset)
                if not np.allclose(packed_old, old, atol=1e-4):
                    raise ValueError(f"mesh buffer mismatch at {name} vertex {vertex_id}")
                struct.pack_into("<3f", patched, position_offset, *new.tolist())

                transformed_normal = None
                if "NORMAL" in elements and "NORMAL" in buffers:
                    normal_entry = elements["NORMAL"]
                    normal = np.asarray(buffers["NORMAL"][vertex_id][:3], dtype=np.float64)
                    transformed_normal = unit(np.linalg.solve(jacobian.T, normal))
                    normal_offset = primitive_start + normal_entry["offset"] + vertex_id * normal_entry["stride"]
                    packed = np.clip(np.rint(transformed_normal * 127.0), -127, 127).astype(np.int8)
                    struct.pack_into("<3b", patched, normal_offset, int(packed[0]), int(packed[1]), int(packed[2]))

                if "TANGENT" in elements and "TANGENT" in buffers:
                    tangent_entry = elements["TANGENT"]
                    tangent = np.asarray(buffers["TANGENT"][vertex_id][:3], dtype=np.float64)
                    tangent = unit(jacobian @ tangent)
                    if transformed_normal is not None:
                        tangent = unit(tangent - transformed_normal * np.dot(transformed_normal, tangent))
                    tangent_offset = primitive_start + tangent_entry["offset"] + vertex_id * tangent_entry["stride"]
                    packed = np.clip(np.rint(tangent * 127.0), -127, 127).astype(np.int8)
                    struct.pack_into("<3b", patched, tangent_offset, int(packed[0]), int(packed[1]), int(packed[2]))

                changed_vertices += 1
                max_displacement = max(max_displacement, delta)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(patched)
    result = {
        "input_sha256": old_hash,
        "archive_model_sha256": input_hash,
        "restored_original_baseline": input_hash == installed_hash,
        "output_sha256": hashlib.sha256(patched).hexdigest(),
        "strength": strength,
        "changed_vertices": changed_vertices,
        "max_displacement_m": max_displacement,
        "same_size": len(source) == len(patched),
    }
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    try:
        patch(parse_args())
    except Exception as error:
        print(f"apply_shape.py: {error}", file=sys.stderr)
        raise SystemExit(1)
