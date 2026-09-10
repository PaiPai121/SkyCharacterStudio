"""Read-only chest bone discovery, independent of character eligibility."""
import re
import numpy as np

def _detect_explicit(data):
    nodes={}
    for group in data['mesh_blocks']:
        for node in group.get('nodes',[]):
            pos=np.asarray(node['matrix'][3][:3],dtype=float)
            chest_name=any(t in node['name'].lower() for t in ('breast','bust','mune'))
            if chest_name and node['name'] in nodes and not np.allclose(nodes[node['name']],pos,atol=1e-4):
                return {'status':'ambiguous','detail':'同名骨骼的坐标不一致','pairs':[]}
            nodes[node['name']]=pos
    buckets={side:{'base':[],'tip':[]} for side in ('left','right')}
    candidates=[]
    for name,pos in nodes.items():
        key=re.sub(r'[^a-z0-9]','',name.lower())
        token=next((t for t in ('breast','bust','mune') if t in key),None)
        if not token:continue
        candidates.append(name)
        rest=key.replace(token,'')
        side='left' if 'left' in rest else 'right' if 'right' in rest else None
        if side is None:
            if rest.startswith('l') or rest.endswith('l'):side='left'
            elif rest.startswith('r') or rest.endswith('r'):side='right'
        if side is None:continue
        kind='tip' if any(t in rest for t in ('top','tip','end','nub')) else 'base'
        buckets[side][kind].append((name,pos))
    inferred=[]
    # Some models omit one unweighted terminal bone from the mesh palette.
    # Reconstruct only a single missing tip, using a symmetric pair of real roots.
    if all(len(buckets[s]['base'])==1 for s in buckets) and sorted(len(buckets[s]['tip']) for s in buckets)==[0,1]:
        missing=next(s for s in buckets if not buckets[s]['tip'])
        other='right' if missing=='left' else 'left'
        base=buckets[missing]['base'][0][1];opposite=buckets[other]['base'][0][1]
        delta=buckets[other]['tip'][0][1]-opposite
        spacing=abs(base[0]-opposite[0]);length=float(np.linalg.norm(delta))
        symmetric=(spacing>1e-4 and np.linalg.norm((base-opposite)[1:])<spacing*.05
                   and np.isfinite(delta).all() and spacing*.15<length<spacing*2)
        if symmetric:
            mirrored=base+delta*np.array([-1,1,1])
            centers=[mirrored,buckets[other]['tip'][0][1]]
            positions=[]
            for gi,g in enumerate(data['mesh_blocks']):
                if 'shadow' in g.get('name','').lower():continue
                if gi>=len(data.get('mesh_buffers',[])):continue
                for mesh in data.get('mesh_buffers',[])[gi]:
                    positions.extend(next((b['Buffer'] for b in mesh['vb'] if b['SemanticName']=='POSITION'),[]))
            p=np.asarray(positions,dtype=float)
            # Require actual nearby surface on both sides, not just plausible bone names.
            supported=(p.ndim==2 and p.shape[1]==3 and np.isfinite(p).all()
                       and all(np.count_nonzero(np.linalg.norm(p-c,axis=1)<length*.9)>=10 for c in centers))
            if supported:
                name=buckets[missing]['base'][0][0]+'_Top（对侧推定）'
                buckets[missing]['tip']=[(name,mirrored)]
                inferred.append(name)
    roots=[buckets[s]['base'][0][0] for s in ('left','right') if len(buckets[s]['base'])==1]
    if len(roots)==2 and any(len(buckets[s]['tip'])==0 for s in ('left','right')):
        return {'status':'roots_only','detail':'已识别左右胸部骨骼；缺少完整末端，当前变形算法不支持此结构',
                'bones':candidates,'can_deform':False,'pairs':[]}
    pairs=[]
    for side,bucket in buckets.items():
        if len(bucket['base'])!=1 or len(bucket['tip'])!=1:
            return {'status':'unsupported' if candidates else 'absent','detail':'检测到候选骨骼，但无法唯一匹配左右根部和末端' if candidates else '未发现胸部骨骼标记','candidates':candidates,'pairs':[]}
        base,tip=bucket['base'][0],bucket['tip'][0]
        radius=float(np.linalg.norm(tip[1]-base[1])*1.2)
        if not np.isfinite(radius) or radius<=1e-5:
            return {'status':'invalid','detail':'骨骼坐标无效或根部与末端重合','pairs':[]}
        pairs.append({'base':base[0],'tip':tip[0],'base_center':base[1].tolist(),
                      'center':tip[1].tolist(),'radius':radius})
    return {'status':'recognized','detail':'已识别左右根部；缺失末端由对侧对称推定，已检查附近网格' if inferred else '已识别左右根部和末端骨骼',
            'bones':candidates+inferred,'inferred_tips':inferred,'can_deform':True,'pairs':pairs}

def _fit_torso(data):
    """Fit front torso surfaces from spine landmarks and skinning weights.

    No model IDs, mesh indices, character dimensions, or vertex offsets are used.
    Shadow meshes may have a different rest skeleton; do not use them as landmarks.
    """
    samples=[];landmarks=[]
    for gi,group in enumerate(data['mesh_blocks']):
        if 'shadow' in group.get('name','').lower():continue
        nodes=group.get('nodes',[])
        lookup={re.sub('[^a-z0-9]','',n['name'].lower()):n for n in nodes}
        spine=lookup.get('spine2');neck=lookup.get('neck')
        if spine is None or neck is None:continue
        s=np.asarray(spine['matrix'][3][:3],float);n=np.asarray(neck['matrix'][3][:3],float)
        if not np.isfinite([s,n]).all() or n[1]-s[1]<=.02:continue
        torso={i for i,node in enumerate(nodes) if re.sub('[^a-z0-9]','',node['name'].lower()) in ('spine1','spine2','neck') or any(t in node['name'].lower() for t in ('breast','bust','mune'))}
        group_samples=[]
        for mesh in data['mesh_buffers'][gi]:
            buffers={(b['SemanticName'],int(b['SemanticIndex'])):np.asarray(b['Buffer']) for b in mesh['vb']}
            p=buffers.get(('POSITION',0));w=buffers.get(('BLENDWEIGHT',0));indices=buffers.get(('BLENDINDICES',0))
            if p is None or w is None or indices is None:continue
            weight=np.sum(w*np.isin(indices,list(torso)),axis=1)
            span=n[1]-s[1];y=(s[1]+n[1])*.5
            mask=(weight>=.35)&(np.abs(p[:,1]-y)<span*.32)&(np.abs(p[:,0]-s[0])<span*.85)&(p[:,2]>s[2])
            if np.count_nonzero(mask)>=10:group_samples.extend(p[mask].tolist())
        if group_samples:
            samples.extend(group_samples);landmarks.append((s,n))
    if not landmarks:return None
    spine=np.median([s for s,n in landmarks],axis=0);neck=np.median([n for s,n in landmarks],axis=0)
    span=neck[1]-spine[1]
    # Multiple visible skeletons with materially different poses cannot share one fit.
    if any(np.linalg.norm(s-spine)>span*.1 or np.linalg.norm(n-neck)>span*.1 for s,n in landmarks):return None
    p=np.asarray(samples);pairs=[]
    for sign,side in [(1,'left'),(-1,'right')]:
        x=spine[0]+sign*span*.36;y=(spine[1]+neck[1])*.5
        region=p[(sign*(p[:,0]-spine[0])>span*.12)&(sign*(p[:,0]-spine[0])<span*.65)&(np.abs(p[:,1]-y)<span*.28)]
        if len(region)<10:return None
        z=float(np.quantile(region[:,2],.8))
        radius=float(span*.52);center=np.array([x,y,z])
        if np.count_nonzero(np.linalg.norm(region-center,axis=1)<radius*.8)<10:return None
        pairs.append({'base':'Spine2 / Neck','tip':side+'（蒙皮表面估计）',
                      'base_center':[float(spine[0]),float(y),float(spine[2])],
                      'center':center.tolist(),'radius':radius})
    return {'status':'recognized','detail':'已根据脊柱、颈部和蒙皮权重自动定位胸廓表面（估计）',
            'method':'torso_surface','bones':['Spine2','Neck'],'can_deform':True,'pairs':pairs}

def detect_chest(data):
    visible=dict(data,mesh_blocks=[],mesh_buffers=[])
    for gi,group in enumerate(data['mesh_blocks']):
        if 'shadow' not in group.get('name','').lower():
            visible['mesh_blocks'].append(group)
            visible['mesh_buffers'].append(data.get('mesh_buffers',[])[gi] if gi<len(data.get('mesh_buffers',[])) else [])
    direct=_detect_explicit(visible)
    if direct.get('can_deform'):return direct
    fitted=_fit_torso(visible)
    if fitted:return fitted
    direct['can_deform']=False
    direct['detail']+='；蒙皮表面自动定位也未通过，不能可靠调整'
    return direct
