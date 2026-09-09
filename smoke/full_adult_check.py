"""Read-only game input; full model round-trip audit for every catalog adult."""
import argparse, contextlib, io, json, sys, traceback
from pathlib import Path
from concurrent.futures import ProcessPoolExecutor, as_completed
import numpy as np
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'tools'))
import auto_model as m
from character_age import CATALOG
GAME=Path(r'D:\SteamLibrary\steamapps\common\Sora No Kiseki the 1st')

def check(id):
    with contextlib.redirect_stdout(io.StringIO()):raw,mats,data=m.prepare(GAME,id)
    row={'id':id,'name':CATALOG[id]['name'],'detection':m.detect_chest(data),'checks':[]}
    for mode in ['chest','width']:
        params=m.profile(data,id,mode)
        for strength in [-500,0,100,1000]:
            out=ROOT/'cache/full-adult-check'/id/mode/str(strength)
            with contextlib.redirect_stdout(io.StringIO()):m.run(argparse.Namespace(game=GAME,model=id,mode=mode,strength=strength,out=out,export=True))
            edited=(out/(id+'.mdl')).read_bytes()
            assert len(raw)==len(edited),'size changed'
            assert (raw==edited)==(strength==0),'zero identity or no effect'
            with contextlib.redirect_stdout(io.StringIO()):parsed=m.mdl.obtain_mesh_data(edited,mats)
            max_error=0.;min_det=1.;min_face_cos=1.;changed=0
            for ga,gb in zip(data['mesh_buffers'],parsed['mesh_buffers']):
                for a,b in zip(ga,gb):
                    pos=lambda mesh:np.asarray(next(v['Buffer'] for v in mesh['vb'] if v['SemanticName']=='POSITION'))
                    p=pos(a);q=pos(b);expected,j=m.deform(p,strength,mode,params)
                    assert np.isfinite(q).all()
                    max_error=max(max_error,float(np.abs(q-expected).max()))
                    min_det=min(min_det,float(np.linalg.det(j).min()))
                    changed+=int(np.count_nonzero(np.linalg.norm(q-p,axis=1)>1e-7))
                    assert a['ib']['Buffer']==b['ib']['Buffer'],'topology changed'
                    triangles=np.asarray(a['ib']['Buffer'],int).reshape(-1,3)
                    if len(triangles):
                        old=np.cross(p[triangles[:,1]]-p[triangles[:,0]],p[triangles[:,2]]-p[triangles[:,0]])
                        new=np.cross(q[triangles[:,1]]-q[triangles[:,0]],q[triangles[:,2]]-q[triangles[:,0]])
                        la=np.linalg.norm(old,axis=1);lb=np.linalg.norm(new,axis=1);valid=la>params[2]**2*1e-10
                        assert np.all(lb[valid]>la[valid]*.01),'collapsed face'
                        if valid.any():min_face_cos=min(min_face_cos,float(np.min(np.sum(old[valid]*new[valid],axis=1)/(la[valid]*lb[valid]))))
            assert max_error<1e-6,('position mismatch',max_error)
            assert min_det>.05,('local inversion',min_det)
            assert min_face_cos>0,('face reversal',min_face_cos)
            row['checks'].append(dict(mode=mode,strength=strength,changed_vertices=changed,max_error=max_error,min_jacobian=min_det,min_face_cos=min_face_cos,gain=params[3]))
    return row

if __name__=='__main__':
    rows=[];errors=[]
    ids=[id for id in CATALOG if m.adult_eligible(id)]
    with ProcessPoolExecutor(max_workers=4) as pool:
        jobs={pool.submit(check,id):id for id in ids}
        for job in as_completed(jobs):
            id=jobs[job]
            try:rows.append(job.result());print('PASS',id,len(rows),'/',len(ids),flush=True)
            except Exception as e:errors.append(dict(id=id,error=traceback.format_exc()));print('FAIL',id,str(e),flush=True)
    report={'models':len(ids),'passed':len(rows),'results':sorted(rows,key=lambda r:r['id']),'errors':errors}
    (ROOT/'cache/full-adult-check-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
    print('COMPLETE',len(rows),'/',len(ids),flush=True)
    if errors:sys.exit(1)
