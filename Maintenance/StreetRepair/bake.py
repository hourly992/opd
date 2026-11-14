"""One-time guarded migration, only from the documented painted-district base."""
import subprocess,sys
from pathlib import Path

repo=Path(__file__).resolve().parents[2]
base='89336538c120b488cb93af6a6d46692a84ee5f69'
if subprocess.check_output(['git','rev-parse','HEAD'],cwd=repo,text=True).strip()!=base:
    raise SystemExit('Reproduce in a separate checkout at '+base+'; never rebake the repaired scene.')
scene=repo/'Unity_Walk/Assets/Scenes/Street.unity'
original=subprocess.check_output(['git','show',base+':Unity_Walk/Assets/Scenes/Street.unity'],cwd=repo)
if scene.read_bytes()!=original:
    raise SystemExit('The saved scene differs from the base; existing work will not be overwritten.')
for name in ['leaves','surfaces','garages','batch_asphalt','trim_aprons','follow_surfaces','isolate','validate']:
    subprocess.run([sys.executable,str(Path(__file__).with_name(name+'.py'))],cwd=repo,check=True)
