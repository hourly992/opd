"""Apply the garage/photo 14–17 follow-up to PR #3's saved scene, once.

The original bake is archived in garages.py. This guarded follow-up preserves
every scene identity, collider reference, building, light and leaf asset.
"""
import subprocess
from garages import *

BASE='00f63cb0102b1dd800d7a11a0a519ba42034c58f'

def revise():
 s,bs,items=scene();scene_path=ROOT/'Assets/Scenes/Street.unity'
 old=subprocess.check_output(['git','show',BASE+':'+str(scene_path.relative_to(ROOT.parent))],cwd=ROOT.parent,text=True)
 if s!=old:
  raise SystemExit('Requires the unchanged PR #3 scene at '+BASE+'; refusing to overwrite edits.')
 entries=[x for x in items if x['path'].name=='GarageEntrance.asset']
 assert len(entries)==len(SITES)==2
 drive=entries[0]['materials'][0]
 ground=[x for x in items if x['path'].suffix=='.asset' and x['path'].name not in ['GarageEntrance.asset','GarageBarrelCanopy.asset','GarageMouthAsphalt.asset'] and
         any(isinstance(p,Path) and any(k in p.name for k in ['_Drive_','_Lawn_','_Paver_','RedGreyRunning','LocalAsphalt']) for p in x['materials'])]
 sampler=Sampler(ground,bs)
 base_y=float(vec(entries[0]['tb'],'m_LocalPosition')[1])
 op,gp,concrete,glass=build_assets(drive)
 yard_mesh(sampler,base_y)
 edited=[];mask=sh.box(*YARD)
 for x in ground:
  m=load(x['path']);v=world(bs,x,m['a'][0])
  if len(v) and mask.intersects(sh.box(v[:,0].min(),v[:,2].min(),v[:,0].max(),v[:,2].max())):
   if cut(m,bs,x,mask):save_mesh(m);edited.append(str(x['path'].relative_to(ROOT)))
 for site in SITES:
  x=next(x for x in entries if site['id'] in x['name'])
  typ,tb=bs[x['trans']]
  tb=re.sub(r'm_LocalPosition: .*',f'm_LocalPosition: {{x: {site["x"]}, y: {base_y}, z: {site["z"]}}}',tb)
  bs[x['trans']]=(typ,tb)
 yard=next(x for x in items if x['path'].name=='GarageMouthAsphalt.asset')
 typ,gb=bs[yard['go']]
 gb=re.sub(r'  m_Name: .*',lambda m:'  m_Name: '+json.dumps('Асфальтовый подъём между въездами и ровный двор над парковкой',ensure_ascii=True),gb)
 bs[yard['go']]=(typ,gb)
 header=s[:s.index('--- !u!')]
 scene_path.write_text(header+''.join(b if b.endswith('\n') else b+'\n' for typ,b in bs.values()))
 report=dict(revisionBaseline=BASE,entrances=[dict(**p,groundY=base_y,depth=2.64,clearWidth=4.4,length=18,maximumGrade=.16,provisional=True) for p in SITES],
             entranceCount=2,clearSeparation=3.21,yardBounds=list(YARD),yardRise=.85,frontRiseLength=6,rearReturnLength=5,
             excavatedMeshes=edited,excavatedArea=mask.area,
             note='Existing pair only. Photo-based geometry and spacing remain estimates; no surveyed positions inferred.')
 (ROOT.parent/'Reports/StreetRepair/garage-geometry.json').write_text(json.dumps(report,indent=2)+'\n')
 print(json.dumps(report,indent=2))

if __name__=='__main__':revise()
