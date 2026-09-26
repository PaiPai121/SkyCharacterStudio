import argparse, contextlib, io, json, sys
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
import auto_model as model
from character_age import CATALOG, age_info, adult_eligible

game = Path(r'D:\SteamLibrary\steamapps\common\Sora No Kiseki the 1st')
names = json.loads((ROOT/'cache/game-model-names.json').read_text(encoding='utf8'))
for id, info in CATALOG.items():
    assert info['name'] in names[id], (id, 'identity mismatch')
    assert info['basis'] and info['source']
    assert adult_eligible(id) == (info['status'] == 'adult')
assert age_info('chr5101')['age'] == 18 and adult_eligible('chr5101')
assert not adult_eligible('chr5004') and age_info('chr5004')['status'] == 'minor'
assert not adult_eligible('chr9999') and age_info('chr9999')['status'] == 'unknown'
assert not adult_eligible('chr5004', True)
assert not adult_eligible('chr9999', True) and age_info('chr9999', True)['status'] == 'unknown'
assert not adult_eligible('chr5000', True) and age_info('chr5000', True)['age'] == 16
for id in ['chr5102', 'chr5111', 'chr5107', 'chr5101']:
    with contextlib.redirect_stdout(io.StringIO()): raw, mats, data = model.prepare(game, id)
    for strength in [-500, 0, 100, 1000]:
        dest = ROOT/'cache/age-check'/id/str(strength)
        with contextlib.redirect_stdout(io.StringIO()):
            model.run(argparse.Namespace(game=game,model=id,mode='chest',out=dest,strength=strength,export=True))
        edited = (dest/(id+'.mdl')).read_bytes()
        assert len(edited)==len(raw)
        assert (edited==raw)==(strength==0)
        with contextlib.redirect_stdout(io.StringIO()): parsed=model.mdl.obtain_mesh_data(edited,mats)
        params=model.profile(data,id,'chest')
        for gi,(source_group,result_group) in enumerate(zip(data['mesh_buffers'],parsed['mesh_buffers'])):
            for source_mesh,result_mesh in zip(source_group,result_group):
                get=lambda mesh:np.array(next(b['Buffer'] for b in mesh['vb'] if b['SemanticName']=='POSITION'))
                expected,_=model.deform(get(source_mesh),strength,'chest',params,model.mesh_masks(data,gi,source_mesh,params[1]))
                np.testing.assert_allclose(get(result_mesh),expected,atol=1e-6,rtol=0)
    print('PASS actual chest exports, zero byte identity, parsed geometry at -500/0/100/1000:',id)
# Direct backend calls cannot use an unverified/minor model ID to enable chest mode.
for id in ['chr5000', 'chr5004', 'chr9999']:
    try: model.profile(data,id,'chest')
    except ValueError as e: assert 'adult character metadata' in str(e)
    else: raise AssertionError('Unexpected eligibility: '+id)
try: model.profile(data,'chr9999','chest',True)
except ValueError as e: assert 'adult character metadata' in str(e)
else: raise AssertionError('Unlisted base-game model unexpectedly eligible')
print('PASS catalog identity/source checks, adult/minor/unknown statuses and backend restrictions')
