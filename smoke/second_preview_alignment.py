"""Read-only bind-pose audit against the installed 2nd Chapter models."""
import argparse
import sys
import time
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from auto_model import prepare, preview_group_transforms, preview_vertex_matrices, preview_transform, preview_normal_transform
from pac import entries


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--timeout-seconds', type=int, default=600)
    args = parser.parse_args()
    start = time.monotonic()
    archive = args.game / 'pac/steam/asset_common_model.pac'
    models = [Path(item['name']).stem for item in entries(archive)
              if item['name'].startswith('asset/common/model/chr')
              and item['name'].endswith('.mdl') and '_' not in Path(item['name']).stem]
    aligned = set()
    checked_primitives = 0
    for index, model_id in enumerate(models, 1):
        if time.monotonic() - start > args.timeout_seconds:
            raise TimeoutError(f'Preview audit exceeded {args.timeout_seconds}s at {model_id}')
        _, _, data = prepare(args.game, model_id)
        palettes, report = preview_group_transforms(data)
        if report:
            aligned.add(model_id)
        for group_index, group in enumerate(data['mesh_blocks']):
            if 'shadow' in group['name'].lower():
                continue
            for mesh in data['mesh_buffers'][group_index]:
                buffers = {item['SemanticName']: np.asarray(item['Buffer']) for item in mesh['vb']}
                position = buffers.get('POSITION')
                if position is None or not len(position):
                    continue
                matrices = preview_vertex_matrices(mesh, palettes[group_index], len(position))
                adjusted = preview_transform(position, matrices)
                if adjusted.shape != position.shape or not np.isfinite(adjusted).all():
                    raise ValueError(f'Non-finite or incomplete preview geometry: {model_id} / {group["name"]}')
                if 'NORMAL' in buffers:
                    normals = preview_normal_transform(buffers['NORMAL'][:, :3], matrices)
                    if not np.isfinite(normals).all():
                        raise ValueError(f'Non-finite preview normals: {model_id} / {group["name"]}')
                checked_primitives += 1
        if index % 20 == 0 or index == len(models):
            print(f'STAGE {index}/{len(models)} elapsed={time.monotonic()-start:.1f}s', flush=True)
    print(f'PASS models={len(models)} visible_primitives={checked_primitives} '
          f'bind-aligned_models={len(aligned)} elapsed={time.monotonic()-start:.1f}s')


if __name__ == '__main__':
    main()
