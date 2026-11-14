"""Keep shared historical/study surfaces intact; point Street at isolated repairs."""
import shutil, subprocess
from unity_yaml import *

BASE='89336538c120b488cb93af6a6d46692a84ee5f69'
def repair():
 repo=ROOT.parent;out=ROOT/'Assets/Art/StreetRepair/Surfaces';folders(out)
 scene_path=ROOT/'Assets/Scenes/Street.unity';s=scene_path.read_text();manifest=[]
 for name in subprocess.check_output(['git','diff','--name-only',BASE],cwd=repo,text=True).splitlines():
  p=repo/name
  if not name.endswith('.asset') or '/PaintedDistrict/Leaves/' in name:continue
  if p in LEAF_PATHS:continue
  # Every changed pre-existing hardscape/grass mesh gets a distinct GUID. The
  # untouched Street_LeafStudy keeps its geometry, lighting and experiment.
  target=out/(p.stem+'.asset');shutil.copyfile(p,target);meta(target)
  s=s.replace(guid(p),guid(target))
  manifest.append(dict(source=str(p.relative_to(ROOT)),repaired=str(target.relative_to(ROOT))))
  p.write_bytes(subprocess.check_output(['git','show',BASE+':'+name],cwd=repo))
 scene_path.write_text(s)
 report=repo/'Reports/StreetRepair';report.mkdir(parents=True,exist_ok=True);(report/'surface-assets.json').write_text(json.dumps(manifest,indent=2))
 print('Isolated surface meshes:',len(manifest))

# Legacy/flying leaves are deliberately shared across the two scenes: all leaf
# shapes improve, whereas pavement/terrain must not punch holes in the study.
LEAF_PATHS=set()
for name in ['Street','Street_LeafStudy']:
 for x in scene(name)[2]:
  if any(isinstance(p,Path) and 'FallenLeaves' in p.name for p in x['materials']):LEAF_PATHS.add(x['path'])
if __name__=='__main__':repair()
