import json
import struct
from pathlib import Path
import numpy as np

root = Path(__file__).resolve().parents[1]
info = json.loads((root.parent / 'Sky1st-Scherazard-Mod/work/chr5002/mesh_info.json').read_text())
data = [(root / f'exports/chr5002_shape_{s:03}/asset/common/model/chr5002.mdl').read_bytes() for s in (0,97,100)]
offset = 12
while offset + 8 <= len(data[0]):
    kind, size = struct.unpack_from('<II', data[0], offset)
    if kind == 4:
        start = offset + 8
    offset += 8 + size
count = 0
error = 0
for group in info:
    for primitive in group['primitives']:
        e = next(e for e in primitive['Elements'] if e['Semantic'] == 'POSITION')
        for i in range(e['count']):
            off = start + e['offset'] + i * e['stride']
            v = [np.array(struct.unpack_from('<3f', d, off)) for d in data]
            error = max(error, float(np.max(np.abs(v[1] - (v[0] + .97*(v[2]-v[0]))))))
            count += 1
assert count > 40000 and error < 0.000001
print(f'Checked {count} vertex positions; 97% interpolation max error: {error} m')
