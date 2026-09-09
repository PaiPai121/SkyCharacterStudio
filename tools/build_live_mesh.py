"""Build preview geometry from the same verified source buffers as the exporter."""
import json
import shutil
from pathlib import Path
from apply_shape import deformation, load_buffer_helpers

root = Path(__file__).resolve().parents[1]
mod = root.parent / 'Sky1st-Scherazard-Mod'
mesh = mod / 'work/chr5002'
read_fmt, read_vb = load_buffer_helpers(mesh)
from lib_fmtibvb import read_ib
materials = json.loads((mesh / 'material_info.json').read_text())
out = root / 'assets/live'
out.mkdir(parents=True, exist_ok=True)
result = []
for gi, group in enumerate(json.loads((mesh / 'mesh_info.json').read_text())):
    for pi, primitive in enumerate(group['primitives']):
        name = f'{gi}_{group["name"]}_{pi:02}'
        base = mesh / name
        fmt = read_fmt(str(base)+'.fmt')
        # Keep TEXCOORD0: TEXCOORD1 is auxiliary shader data, not the diffuse UV.
        buffers = {b['SemanticName']: b['Buffer'] for b in read_vb(str(base)+'.vb', fmt) if int(b.get('SemanticIndex', 0)) == 0}
        mat = next(m for m in materials if m['material_name']==primitive['material'])
        tex = next((t for t in mat['textures'] if t['texture_slot']==0), {})
        texture = tex.get('texture_image_name','')+'.png'
        src = mod / 'work/textures' / texture
        if src.exists():
            dst = out / texture
            if not dst.exists() or src.read_bytes() != dst.read_bytes(): shutil.copy2(src, dst)
        else: texture = ''
        positions = buffers['POSITION']
        changed = [deformation(p,100)[0].tolist() if gi in (1,2) else p for p in positions]
        uv = [[1-abs((u[0]%2)-1) if tex.get('wrapS')==1 else u[0]%1, (1-u[1])%1] for u in buffers['TEXCOORD']]
        result.append(dict(name=name, opaque=primitive['material'] not in ('eyes_add','face_02'), hidden=primitive['material'] in ('shadow','eyes_add','face_02'),
            positions=positions, adjusted=changed, normals=[n[:3] for n in buffers['NORMAL']],
            uv=uv, indices=[i for tri in read_ib(str(base)+'.ib',fmt) for i in tri], texture=texture,
            positionOffset=next(e['offset'] for e in primitive['Elements'] if e['Semantic']=='POSITION')))
(out/'chr5002.json').write_text(json.dumps(result,separators=(',',':')))
print('Built live meshes:',len(result))
