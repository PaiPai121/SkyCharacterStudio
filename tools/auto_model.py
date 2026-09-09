"""Automatic MDL v4 preview/export. No character-specific mesh numbers or offsets."""
import argparse, hashlib, io, json, struct, sys
from pathlib import Path
import numpy as np
from PIL import Image
ROOT=Path(__file__).resolve().parents[1]
MOD=ROOT/'tools' if (ROOT/'tools/vendor').exists() else next((p/'Sky1st-Scherazard-Mod' for p in [ROOT.parent,ROOT.parent.parent] if (p/'Sky1st-Scherazard-Mod/vendor').exists()), None)
if MOD is None: raise RuntimeError('Missing MDL parser dependency')
sys.path.insert(0,str(MOD/'vendor'));sys.path.insert(0,str(MOD/'scripts'))
import kuro_mdl_export_meshes as mdl
from pac import entries
from apply_shape import sections
from bone_profile import detect_chest
# Eligibility metadata is separate from geometry detection. Unknown ages do not enable chest editing.
from character_age import age_info, adult_eligible
def read_entry(pac, name):
    e=next(e for e in entries(pac) if e['name']==name)
    with pac.open('rb') as f: f.seek(e['offset']);return f.read(e['size'])
def prepare(game, model_id):
    raw=read_entry(game/'pac/steam/asset_common_model.pac',f'asset/common/model/{model_id}.mdl')
    raw=mdl.decryptCLE(raw)
    if raw[:4]!=b'MDL ' or struct.unpack_from('<I',raw,4)[0]!=4: raise ValueError('Only MDL v4 editing is supported')
    mats=mdl.obtain_material_data(raw); data=mdl.obtain_mesh_data(raw,mats)
    if not data: raise ValueError('Model has no editable meshes')
    return raw,mats,data

def profile(data, model_id, mode):
    nodes={n['name']:np.array(n['matrix'][3][:3]) for g in data['mesh_blocks'] for n in g.get('nodes',[])}
    allpos=np.concatenate([np.array(next(b['Buffer'] for b in p['vb'] if b['SemanticName']=='POSITION')) for g in data['mesh_buffers'] for p in g])
    height=float(np.ptp(allpos[:,1]));center=float((allpos[:,0].min()+allpos[:,0].max())/2)
    if height<=0 or not np.isfinite(allpos).all(): raise ValueError('Invalid model bounds')
    centers=[]
    if mode=='chest':
        if not adult_eligible(model_id): raise ValueError('Chest editing requires confirmed adult character metadata: '+age_info(model_id)['status'])
        detection=detect_chest(data)
        if detection['status']!='recognized':raise ValueError(detection['detail'])
        for pair in detection['pairs']:
            radius=pair['radius']
            if radius<height*.01 or radius>height*.15:raise ValueError('Unreliable chest bone geometry')
            centers.append((np.array(pair['center']),radius))
    gain=1.0
    if mode=='chest':
        # Bound the sampled deformation gradient across the entire slider range.
        # A displacement gradient norm < 1 keeps local transformations invertible.
        _,jac=deform(allpos,100,mode,(center,centers,height,1.0))
        max_gradient=float(np.linalg.svd(jac-np.eye(3),compute_uv=False).max())
        if max_gradient>0:gain=min(1.0,.8/(10*max_gradient))
    return center,centers,height,gain

def deform(points, strength, mode, params):
    p=np.asarray(points,dtype=float);q=p.copy();j=np.broadcast_to(np.eye(3),(len(p),3,3)).copy()
    center,centers,height,gain=params
    t=np.clip(strength,-500,1000)/100
    if mode=='width':
        scale=1+t*.05;q[:,0]=center+(p[:,0]-center)*scale;j[:,0,0]=scale
    else:
        for tip,radius in centers:
            d=p-tip;r2=np.sum((d/radius)**2,axis=1);w=np.maximum(0,1-r2)
            direction=np.array([np.sign(tip[0]-center)*radius*.035,0,radius*.12])*t*gain
            q+=w[:,None]**3*direction
            gradient=-6*w[:,None]**2*d/radius**2
            j+=direction[None,:,None]*gradient[:,None,:]
    return q,j

def run(a):
    raw,mats,data=prepare(a.game,a.model);params=profile(data,a.model,a.mode)
    a.out.mkdir(parents=True,exist_ok=True);result=[];missing=set();patched=bytearray(raw);start,size=sections(raw)[4]
    textures={}; imagepac=a.game/'pac/steam/image.pac'
    if not a.export:
        textures={Path(e['name']).name:e for e in entries(imagepac)}
    for gi,group in enumerate(data['mesh_blocks']):
        for pi,primitive in enumerate(group['primitives']):
            mb=data['mesh_buffers'][gi][pi]
            buffers={(b['SemanticName'],int(b['SemanticIndex'])):b['Buffer'] for b in mb['vb']}
            p=np.array(buffers['POSITION',0]);q,j=deform(p,a.strength if a.export else 100,a.mode,params)
            elements={e['Semantic']:e for e in primitive['Elements'] if e['Semantic'] in ('POSITION','NORMAL','TANGENT')}
            normals=np.array(buffers.get(('NORMAL',0),np.tile([0,1,0],(len(p),1))))[:,:3]
            if a.export:
                for semantic,values in [('POSITION',q),('NORMAL',normals),('TANGENT',np.array(buffers.get(('TANGENT',0),normals))[:,:3])]:
                    if semantic not in elements:continue
                    e=elements[semantic]
                    if semantic!='POSITION':
                        if e['stride']!=4: raise ValueError('Unsupported normal/tangent layout')
                        values=np.linalg.solve(np.transpose(j,(0,2,1)),values[...,None])[...,0] if semantic=='NORMAL' else np.einsum('nij,nj->ni',j,values)
                        values/=np.maximum(np.linalg.norm(values,axis=1,keepdims=True),1e-12)
                    elif e['stride']!=12:raise ValueError('Unsupported position layout')
                    for i,v in enumerate(values):
                        off=start+e['offset']+i*e['stride']
                        if off<start or off+e['stride']>start+size:raise ValueError('Vertex offset outside section')
                        if semantic=='POSITION':
                            if not np.allclose(struct.unpack_from('<3f',raw,off),p[i],atol=1e-6):raise ValueError('Source vertex mismatch')
                            struct.pack_into('<3f',patched,off,*v)
                        elif a.strength!=0:struct.pack_into('<3b',patched,off,*np.clip(np.rint(v*127),-127,127).astype(int))
                continue
            mat=next(m for m in mats if m['material_name']==primitive['material'])
            tex=next((t for t in mat['textures'] if t['texture_slot']==0),{})
            texname=tex.get('texture_image_name','');png=texname+'.png';e=textures.get(texname+'.dds')
            if e:
                dest=a.out/png
                if not dest.exists():
                    with imagepac.open('rb') as f:f.seek(e['offset']);img=Image.open(io.BytesIO(mdl.decryptCLE(f.read(e['size']))));img.save(dest)
            else:
                png=''
                if texname:missing.add(texname)
            uv=buffers.get(('TEXCOORD',0),[[0,0]]*len(p))
            uv=[[1-abs((u[0]%2)-1) if tex.get('wrapS')==1 else u[0]%1,(1-u[1])%1] for u in uv]
            result.append(dict(name=f'{gi}_{group["name"]}_{pi}',hidden=primitive['material'] in ('shadow','chr_shadow','eyes_add','face_02'),opaque=True,positions=p.tolist(),adjusted=q.tolist(),normals=normals.tolist(),normalDelta=(j-np.eye(3)).reshape(-1,9).tolist(),normalOffset=elements.get('NORMAL',{}).get('offset',-1),uv=uv,indices=[i for t in mb['ib']['Buffer'] for i in t],texture=png,positionOffset=elements['POSITION']['offset']))
    if a.export:
        path=a.out/(a.model+'.mdl');path.write_bytes(patched)
        (a.out/'report.json').write_text(json.dumps(dict(model=a.model,mode=a.mode,strength=a.strength,source_sha256=hashlib.sha256(raw).hexdigest(),output_sha256=hashlib.sha256(patched).hexdigest(),same_size=len(raw)==len(patched))))
    else:
        (a.out/'model.json').write_text(json.dumps(result,separators=(',',':')))
        (a.out/'model-meta.json').write_text(json.dumps({'missing_textures':sorted(missing),'chest_detection':detect_chest(data),'adult_eligible':adult_eligible(a.model),'age_info':age_info(a.model),'deformation_gain':params[3]}))
    print(json.dumps({'ok':True,'model':a.model,'mode':a.mode,'height':params[2]}))
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--game',type=Path,required=True);p.add_argument('--model',required=True);p.add_argument('--mode',choices=['width','chest'],default='width');p.add_argument('--strength',type=int,default=0);p.add_argument('--out',type=Path,required=True);p.add_argument('--export',action='store_true')
    run(p.parse_args())
