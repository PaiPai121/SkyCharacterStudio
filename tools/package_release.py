"""Build a whitelisted, portable distribution without extracted game assets."""
import argparse,hashlib,json,os,shutil,subprocess,sys
from pathlib import Path

p=argparse.ArgumentParser();p.add_argument('--build',type=Path,required=True);p.add_argument('--out',type=Path,required=True);p.add_argument('--native-dir',type=Path);p.add_argument('--version',required=True);a=p.parse_args()
root=Path(__file__).resolve().parents[1];mod=Path(os.environ.get('SKY_STUDIO_TOOLCHAIN',root.parent/'Sky1st-Scherazard-Mod')).resolve()
native_dir=(a.native_dir or (root/'release-stage'/'native')).resolve()
if hashlib.sha256((root/'assets/character-ages.json').read_bytes()).hexdigest()!=(root/'release/verified-catalog.sha256').read_text().strip():
 raise SystemExit('Character catalog differs from reviewed release data; verify identities/ages before packaging')
if hashlib.sha256((root/'assets/character-ages-2nd.json').read_bytes()).hexdigest()!=(root/'release/verified-catalog-2nd.sha256').read_text().strip():
 raise SystemExit('Second Chapter character catalog differs from reviewed release data')
if a.out.exists():raise SystemExit('Output already exists; choose a fresh staging directory')
a.out.mkdir(parents=True)
def copy(src,dest):
 dest.parent.mkdir(parents=True,exist_ok=True)
 if src.is_dir():shutil.copytree(src,dest,dirs_exist_ok=True,ignore=shutil.ignore_patterns('__pycache__','*.pyc','*.bat','*.cmd','tests','test'))
 else:shutil.copy2(src,dest)
for file in a.build.iterdir():
 if file.is_file() and file.suffix in ('.exe','.dll','.json'):copy(file,a.out/file.name)
for name in ['character-ages.json','character-ages-2nd.json','supported-game.json','SkyCharacterStudio.ico']:copy(root/'assets'/name,a.out/'assets'/name)
for name in ['auto_model.py','asset_codec.py','bone_profile.py','character_age.py','apply_shape.py','preview_dds.py','lib_fmtibvb.py']:copy(root/'tools'/name,a.out/'tools'/name)
dat_builder=next((candidate for candidate in [native_dir/'build_summon_dat.exe',root/'tools'/'build_summon_dat.exe',mod/'tools'/'build_summon_dat.exe'] if candidate.exists()),None)
if dat_builder is None:raise SystemExit('Missing build_summon_dat.exe; build native components before packaging')
copy(dat_builder,a.out/'tools'/'build_summon_dat.exe')
for name in ['kuro_mdl_export_meshes.py','lib_fmtibvb.py','LICENSE','README.md']:copy(mod/'vendor'/name,a.out/'tools/vendor'/name)
copy(mod/'scripts/pac.py',a.out/'tools/scripts/pac.py')
python=Path(sys.base_prefix);runtime=a.out/'runtime/python'
for name in ['python.exe','python3.dll','python311.dll','vcruntime140.dll','vcruntime140_1.dll','LICENSE.txt']:copy(python/name,runtime/name)
copy(python/'DLLs',runtime/'DLLs')
for file in (python/'Lib').iterdir():
 if file.name not in ['site-packages','test','tests','idlelib','tkinter','turtledemo','ensurepip','venv','__pycache__']:copy(file,runtime/'Lib'/file.name)
sites=[Path(sys.prefix)/'Lib/site-packages',python/'Lib/site-packages']
for module in ['numpy','PIL','zstandard','xxhash','lz4','blowfish.py']:
 source=next(site/module for site in sites if (site/module).exists());copy(source,runtime/'Lib/site-packages'/module)
for site in sites:
 for item in site.iterdir():
  if item.name.lower().startswith(('numpy','pillow','zstandard','xxhash','lz4','blowfish')) and (item.name.endswith('.libs') or item.name.endswith('.dist-info')):
   copy(item,runtime/'Lib/site-packages'/item.name)
   direct=runtime/'Lib/site-packages'/item.name/'direct_url.json'
   if direct.exists():direct.unlink()
(runtime/'python311._pth').write_text('DLLs\nLib\nLib/site-packages\n../../tools\n',encoding='ascii')

template=a.out/'runtime/mod-template';base=mod/'dist/Scherazard_Runtime'
if not base.is_dir():raise SystemExit('Missing verified runtime template: '+str(base))
for name in ['xinput1_4.dll','ED9Loader/ED9ModManager.exe','ED9Loader/plugins/ScriptInject.dll']:
 copy(base/name,template/name)
native_plugins={
 'EventStarter.dll':[native_dir/'EventStarter.dll',root/'release-stage'/'native'/'EventStarter.dll',base/'ED9Loader/plugins/EventStarter.dll'],
 'SceneRedirect.dll':[native_dir/'SceneRedirect.dll',root/'release-stage'/'native'/'SceneRedirect.dll',base/'ED9Loader/plugins/SceneRedirect.dll']
}
for name,candidates in native_plugins.items():
 source=next((candidate for candidate in candidates if candidate.exists()),None)
 if source is None:raise SystemExit('Missing native plugin '+name+'; build native components before packaging')
 copy(source,template/'ED9Loader/plugins'/name)
copy(base/'ED9Loader/config/EventStarter.ini',template/'ED9Loader/config/EventStarter.ini')
ini=template/'ED9Loader/config/EventStarter.ini';ini.write_text(ini.read_text()+'\nenabled=0\n',encoding='ascii')
(template/'Mod/ScherazardSummon/asset/common/model').mkdir(parents=True)
(template/'Mod/ScherazardSummon/add_dat_ini.json').write_text('{"inject":[]}',encoding='ascii')
copy(mod/'vendor/ed9modmanager/extracted/ED9ModManager/LICENSE',a.out/'licenses/ED9ModManager.txt')
copy(mod/'vendor/LICENSE',a.out/'licenses/KuroMDLTool-GPL-3.0.txt')
frameworks=json.loads((a.build/'SkyCharacterStudio.runtimeconfig.json').read_text())['runtimeOptions']['includedFrameworks']
for framework in frameworks:
 package=Path.home()/'.nuget/packages'/(framework['name'].lower()+'.runtime.win-x64')/framework['version']
 for file in package.iterdir():
  if file.is_file() and ('license' in file.name.lower() or 'notice' in file.name.lower()):copy(file,a.out/'licenses'/framework['name']/file.name)
for name in ['README-PLAYERS.md','THIRD-PARTY-NOTICES.md','CHANGELOG.md']:copy(root/'release'/name,a.out/name)
# The Nexus/player ZIP is runtime-only.  Build inputs and project source stay
# in the repository (and can be published separately when a source release is
# needed); shipping them here makes the portable package harder to audit and
# is unnecessary for normal installation.
for f in a.out.rglob('*'):
 if f.suffix.lower() in ['.mdl','.dat','.dds','.pac','.blend']:raise RuntimeError('Game asset in package: '+str(f))
git_root=Path(os.environ.get('SKY_STUDIO_GIT_ROOT',root)).resolve()
git=lambda *args:subprocess.check_output(['git','-c','safe.directory='+git_root.as_posix(),'-C',str(git_root),*args]).decode().strip()
status=git('status','--porcelain','--untracked-files=all')
manifest={'version':a.version,'source_commit':git('rev-parse','HEAD'),'source_dirty':bool(status),'files':{str(f.relative_to(a.out)).replace('\\','/'):hashlib.sha256(f.read_bytes()).hexdigest() for f in a.out.rglob('*') if f.is_file()}}
(a.out/'release-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf8')
print('PORTABLE_STAGE_READY',a.out,len(manifest['files']))
