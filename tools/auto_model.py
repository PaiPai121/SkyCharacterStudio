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

def _unit(vector, fallback):
    vector=np.asarray(vector,dtype=float)
    length=float(np.linalg.norm(vector))
    if not np.isfinite(length) or length<=1e-8:return np.asarray(fallback,dtype=float)
    return vector/length

def _chest_region(pair, nodes, center, height):
    """Build an oriented, lobe-shaped influence region from one detected side."""
    tip=np.asarray(pair['center'],dtype=float)
    base=np.asarray(pair.get('base_center',nodes.get(pair.get('base',''))),dtype=float)
    if base.shape!=(3,) or not np.isfinite(base).all():
        side=1.0 if tip[0]>=center else -1.0
        base=tip-np.asarray([side,0,1.5],dtype=float)*pair['radius']*.55
    axis_vector=tip-base
    axis_length=float(np.linalg.norm(axis_vector))
    side=float(np.sign(tip[0]-base[0]))
    if abs(side)<.5:side=1.0 if tip[0]>=center else -1.0
    axis=_unit(axis_vector,[side,0,1])
    outward=np.asarray([side,0,0],dtype=float)
    up=np.asarray([0,1,0],dtype=float)
    # All models fitted by bone_profile use +Z as the torso's front.  Deriving
    # front/up with a cross product of the outward axis mirrors UP on one side
    # after front is flipped to +Z, so the lower-pole sag becomes an upper-pole
    # lift there.  A reflected (rather than rotated) local frame keeps both
    # lobes at the same vertical angle while preserving their original mesh.
    front=np.asarray([0,0,1],dtype=float)
    origin=base+axis*axis_length*.55
    radius=float(pair['radius'])
    return {
        'origin':origin,
        'basis':np.column_stack((outward,up,front)),
        # A controlled lateral/depth envelope avoids pulling shoulders,
        # cleavage, and the back surface into the chest lobe while leaving
        # enough room for a visibly rounded, large adjustment.
        # Spread the influence a little farther through the rib cage,
        # especially in depth.  A wider, gentler field gives the slider more
        # visible range without concentrating the same displacement at a
        # single pointed tip.
        'radii':np.asarray([radius*1.132,radius*.994,radius*1.361],dtype=float),
        'side':side,
        'bone_names':[pair.get('base',''),pair.get('tip','')],
        'height':height,
    }

def profile(data, model_id, mode, is_base_game_character=False):
    nodes={n['name']:np.array(n['matrix'][3][:3]) for g in data['mesh_blocks'] for n in g.get('nodes',[])}
    allpos=np.concatenate([np.array(next(b['Buffer'] for b in p['vb'] if b['SemanticName']=='POSITION')) for g in data['mesh_buffers'] for p in g])
    height=float(np.ptp(allpos[:,1]));center=float((allpos[:,0].min()+allpos[:,0].max())/2)
    if height<=0 or not np.isfinite(allpos).all(): raise ValueError('Invalid model bounds')
    regions=[]
    if mode=='chest':
        if not adult_eligible(model_id,is_base_game_character): raise ValueError('Chest editing requires adult character metadata: '+age_info(model_id,is_base_game_character)['status'])
        detection=detect_chest(data)
        if detection['status']!='recognized':raise ValueError(detection['detail'])
        for pair in detection['pairs']:
            radius=pair['radius']
            if radius<height*.01 or radius>height*.15:raise ValueError('Unreliable chest bone geometry')
            regions.append(_chest_region(pair,nodes,center,height))
    gain=1.0
    if mode=='chest':
        # Bound the sampled deformation gradient across the entire slider range.
        # Allow the broad dome to use the larger end of the slider while
        # keeping both ends of the sampled deformation range at the unit
        # boundary.  The complete adult audit remains the per-mesh
        # determinant/face-direction guard for every catalog model.
        max_gradient=0.0
        for sample_strength in (-500,1000):
            _,jac=deform(allpos,sample_strength,mode,(center,regions,height,1.0))
            max_gradient=max(max_gradient,float(np.linalg.svd(jac-np.eye(3),compute_uv=False).max()))
        if max_gradient>0:gain=min(1.0,1.0/max_gradient)
    return center,regions,height,gain

def _smoothstep(value):
    value=np.clip(value,0,1)
    return value*value*(3-2*value)

def _chest_strength(strength):
    """Keep the normal range linear and give the shrink tail a flatter target."""
    t=float(np.clip(strength,-500,1000))/100
    if t>1:return 1+(t-1)*.60
    if t< -1:return -1+(t+1)*1.10
    return t

def _chest_shrink_factor(t):
    """Map the negative slider to a blend with the neutral chest plane."""
    # _chest_strength(-500) is -5.4.  Keep a small residual thickness at the
    # slider minimum so the clothing shell does not collapse into the torso;
    # the result reads as flat in-game while retaining a safe surface normal
    # on every catalog model.
    return .85*float(np.clip(-t/5.4,0,1))

def _smooth_vertex_mask(mesh, mask):
    """Soften quantized skin weights across each primitive's triangle edges."""
    triangles=np.asarray(mesh.get('ib',{}).get('Buffer',[]),dtype=int).reshape(-1,3)
    if not len(triangles):return mask
    total=np.zeros(len(mask),dtype=float);count=np.zeros(len(mask),dtype=float)
    for column in range(3):
        source=triangles[:,column];target=triangles[:,(column+1)%3]
        np.add.at(total,source,mask[target]);np.add.at(count,source,1)
        np.add.at(total,target,mask[source]);np.add.at(count,target,1)
    average=np.divide(total,np.maximum(count,1),where=np.maximum(count,1)>0)
    return .45*mask+.55*np.where(count>0,average,mask)

def mesh_masks(data, group_index, mesh, regions):
    """Return per-region skinning masks for explicit chest bones.

    Clothing and skin usually carry the chest root weight, while nearby metal,
    hair, and accessory primitives do not.  The geometric envelope remains the
    fallback for estimated regions and meshes without blend data.
    """
    if not regions:return None
    buffers={(b['SemanticName'],int(b.get('SemanticIndex',0))):np.asarray(b['Buffer']) for b in mesh.get('vb',[])}
    weights=buffers.get(('BLENDWEIGHT',0));indices=buffers.get(('BLENDINDICES',0))
    if weights is None or indices is None:return None
    nodes=data['mesh_blocks'][group_index].get('nodes',[])
    lookup={node['name']:i for i,node in enumerate(nodes)}
    masks=[]
    for region in regions:
        bone_indices=[lookup[name] for name in region.get('bone_names',[]) if name in lookup]
        if not bone_indices:
            masks.append(None)
            continue
        chest_weight=np.where(np.isin(indices,bone_indices),weights,0).sum(axis=1)
        # Keep a small geometric fallback for clothing vertices whose weight was
        # quantized away, but strongly suppress unrelated accessories.
        masks.append(_smooth_vertex_mask(mesh,.08+.92*np.clip(chest_weight/.20,0,1)))
    return masks

def deform(points, strength, mode, params, masks=None):
    p=np.asarray(points,dtype=float);q=p.copy();j=np.broadcast_to(np.eye(3),(len(p),3,3)).copy()
    center,regions,height,gain=params
    t=_chest_strength(strength) if mode=='chest' else np.clip(strength,-500,1000)/100
    if mode=='width':
        scale=1+t*.05;q[:,0]=center+(p[:,0]-center)*scale;j[:,0,0]=scale
    else:
        amplitude=t*gain
        for region_index,region in enumerate(regions):
            basis=region['basis'];origin=region['origin'];rx,ry,rz=region['radii']
            local=(p-origin)@basis
            x,y,z=local[:,0],local[:,1],local[:,2]
            r2=(x/rx)**2+(y/ry)**2+(z/rz)**2
            s=np.clip(1-r2,0,1)
            w=s*s*(3-2*s)
            dw=6*s*(1-s)
            grad_w=-2*dw[:,None]*local/np.asarray([rx*rx,ry*ry,rz*rz])

            # Fade the inner edge of each lobe to prevent the two sides from
            # accumulating into one round ball at the centre line.
            gate_arg=(x/rx+.75)/.90
            gate=_smoothstep(gate_arg)
            gate_grad=np.zeros_like(local)
            active=(gate_arg>0)&(gate_arg<1)
            gate_grad[active,0]=6*gate_arg[active]*(1-gate_arg[active])/(.90*rx)
            weight=w*gate
            grad_weight=grad_w*gate[:,None]+w[:,None]*gate_grad
            # Positive growth follows the skinning weights so accessories stay
            # attached to their original bones.  For shrink, those quantized
            # per-vertex weights can disagree on duplicated seam vertices and
            # open visible cracks.  The geometric field is continuous by
            # itself, so omit the discontinuous mask on the negative side.
            if amplitude>=0 and masks is not None and region_index<len(masks):
                mask=masks[region_index]
                if mask is not None:
                    weight*=mask
                    grad_weight*=mask[:,None]

            if amplitude<0:
                # Shrink is a target operation: move only the part of the
                # shell that is in front of the rib-cage plane (local z>0)
                # back toward that plane.  Keeping x/y fixed preserves the
                # shoulder and under-bust silhouette; the smooth support field
                # fades the motion into the torso and keeps every seam closed.
                factor=_chest_shrink_factor(t)
                front=np.maximum(z,0)
                local_delta=np.zeros_like(local)
                local_delta[:,2]=-factor*front*weight
                local_jac=np.zeros((len(p),3,3),dtype=float)
                dz=(z>0).astype(float)
                local_jac[:,2,:]=-factor*(front[:,None]*grad_weight+weight[:,None]*np.column_stack((np.zeros(len(p)),np.zeros(len(p)),dz)))
            else:
                # The front profile starts gently at the rib side and becomes
                # strongest at the front surface.  A second, planar dome profile
                # makes the displacement largest around the middle of the lobe and
                # fade toward its upper/lower and outer edges.  Without that dome,
                # a large slider value turns the existing clothing shell into a
                # pointed wedge even though the support field is smooth.
                front_arg=np.clip(.5+.5*z/rz,0,1)
                front_curve=front_arg*front_arg*(3-2*front_arg)
                front_grad=np.zeros_like(local)
                front_active=(front_arg>0)&(front_arg<1)
                front_grad[front_active,2]=.84*3*front_arg[front_active]*(1-front_arg[front_active])/rz

                dome_arg=np.clip(1-(x/rx)**2-(y/ry)**2,0,1)
                dome_curve=dome_arg*dome_arg*(3-2*dome_arg)
                dome_grad=np.zeros_like(local)
                dome_active=(dome_arg>0)&(dome_arg<1)
                dome_derivative=6*dome_arg[dome_active]*(1-dome_arg[dome_active])
                dome_grad[dome_active,0]=dome_derivative*(-2*x[dome_active]/(rx*rx))
                dome_grad[dome_active,1]=dome_derivative*(-2*y[dome_active]/(ry*ry))
                front_profile=front_curve*dome_curve
                profile_grad=dome_curve[:,None]*front_grad+front_curve[:,None]*dome_grad

                # Added volume is allowed to grow; the gravity cue is a small
                # downward settlement of the lower/front portion while the
                # upper attachment remains comparatively supported.  This is
                # a static visual approximation, not a volume-preserving
                # simulation.
                sag_arg=np.clip(.5-.5*y/ry,0,1)
                sag_curve=sag_arg*sag_arg*(3-2*sag_arg)
                sag_grad=np.zeros_like(local)
                sag_active=(sag_arg>0)&(sag_arg<1)
                sag_grad[sag_active,1]=6*sag_arg[sag_active]*(1-sag_arg[sag_active])*(-.5/ry)
                sag_factor=.35+.65*sag_curve
                sag_profile=front_profile*sag_factor
                sag_profile_grad=sag_factor[:,None]*profile_grad+.65*front_profile[:,None]*sag_grad

                # The upper pole is held by the chest wall while the lower
                # pole carries more of the added load.  Apply that asymmetry
                # to the forward dome and vertical spread instead of making
                # the entire lobe inflate uniformly.
                growth_factor=.78+.22*sag_curve
                growth_profile=front_profile*growth_factor
                growth_profile_grad=growth_factor[:,None]*profile_grad+.22*front_profile[:,None]*sag_grad
                vertical_scale=.12+.10*sag_curve
                vertical_scale_grad=.10*sag_grad

                terms=np.column_stack((
                    .28*x+.05*rx*growth_profile,
                    vertical_scale*y-.09*rz*sag_profile,
                    .10*np.maximum(z,0)+.18*rz*growth_profile,
                ))
                term_grad=np.zeros((len(p),3,3),dtype=float)
                term_grad[:,0,0]=.28
                term_grad[:,0,:]+=.05*rx*growth_profile_grad
                term_grad[:,1,1]=vertical_scale
                term_grad[:,1,:]+=y[:,None]*vertical_scale_grad
                term_grad[:,1,:]-=.09*rz*sag_profile_grad
                term_grad[:,2,2]=.10*(z>0)
                term_grad[:,2,:]+=.18*rz*growth_profile_grad
                local_delta=amplitude*terms*weight[:,None]
                local_jac=amplitude*(term_grad*weight[:,None,None]+terms[:,:,None]*grad_weight[:,None,:])
            q+=local_delta@basis.T
            j+=np.einsum('ab,nbc,cd->nad',basis,local_jac,basis.T)
    return q,j

def run(a):
    is_base_game_character=getattr(a,'base_game_character',False)
    raw,mats,data=prepare(a.game,a.model);params=profile(data,a.model,a.mode,is_base_game_character)
    a.out.mkdir(parents=True,exist_ok=True);result=[];missing=set();patched=bytearray(raw);start,size=sections(raw)[4]
    textures={}; imagepac=a.game/'pac/steam/image.pac'
    if not a.export:
        textures={Path(e['name']).name:e for e in entries(imagepac)}
    for gi,group in enumerate(data['mesh_blocks']):
        for pi,primitive in enumerate(group['primitives']):
            mb=data['mesh_buffers'][gi][pi]
            buffers={(b['SemanticName'],int(b['SemanticIndex'])):b['Buffer'] for b in mb['vb']}
            p=np.array(buffers['POSITION',0])
            masks=mesh_masks(data,gi,mb,params[1]) if a.mode=='chest' else None
            q,j=deform(p,a.strength if a.export else 100,a.mode,params,masks)
            shrink=None;shrink_j=None
            if not a.export and a.mode=='chest':
                # Keep a separate full-flatten target for the live view.  The
                # negative side intentionally does not use quantized bone
                # masks, so duplicated seam vertices remain coincident.
                shrink,shrink_j=deform(p,-500,a.mode,params,masks)
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
            result.append(dict(name=f'{gi}_{group["name"]}_{pi}',hidden=primitive['material'] in ('shadow','chr_shadow','eyes_add','face_02'),opaque=True,positions=p.tolist(),adjusted=q.tolist(),shrink=[] if shrink is None else shrink.tolist(),normals=normals.tolist(),normalDelta=(j-np.eye(3)).reshape(-1,9).tolist(),shrinkNormalDelta=[] if shrink_j is None else (shrink_j-np.eye(3)).reshape(-1,9).tolist(),normalOffset=elements.get('NORMAL',{}).get('offset',-1),uv=uv,indices=[i for t in mb['ib']['Buffer'] for i in t],texture=png,positionOffset=elements['POSITION']['offset']))
    if a.export:
        path=a.out/(a.model+'.mdl');path.write_bytes(patched)
        (a.out/'report.json').write_text(json.dumps(dict(model=a.model,mode=a.mode,strength=a.strength,source_sha256=hashlib.sha256(raw).hexdigest(),output_sha256=hashlib.sha256(patched).hexdigest(),same_size=len(raw)==len(patched))))
    else:
        (a.out/'model.json').write_text(json.dumps(result,separators=(',',':')))
        (a.out/'model-meta.json').write_text(json.dumps({'missing_textures':sorted(missing),'chest_detection':detect_chest(data),'adult_eligible':adult_eligible(a.model,is_base_game_character),'age_info':age_info(a.model,is_base_game_character),'deformation_gain':params[3]}))
    print(json.dumps({'ok':True,'model':a.model,'mode':a.mode,'height':params[2]}))
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--game',type=Path,required=True);p.add_argument('--model',required=True);p.add_argument('--mode',choices=['width','chest'],default='width');p.add_argument('--strength',type=int,default=0);p.add_argument('--out',type=Path,required=True);p.add_argument('--export',action='store_true');p.add_argument('--base-game-character',action='store_true')
    run(p.parse_args())
