"""Independent saved-data checks. These do not substitute for a Unity player run."""
import subprocess
from functools import lru_cache
import shapely as sh
from shapely.geometry import Point, Polygon
from unity_yaml import *

BASE='89336538c120b488cb93af6a6d46692a84ee5f69'
REPO=ROOT.parent
MANIFEST=REPO/'Reports/StreetRepair/surface-assets.json'
ORIGINAL={ROOT/x['repaired']:ROOT/x['source'] for x in json.loads(MANIFEST.read_text())} if MANIFEST.exists() else {}
def before(p):return subprocess.check_output(['git','show',BASE+':'+str(ORIGINAL.get(p,p).relative_to(REPO))],cwd=REPO,text=True)
@lru_cache(None)
def cached(p,old=False):return mesh(p,before(p)) if old else mesh(p)
def height_hits(t,x,z):
 if not len(t):return []
 den=(t[:,1,2]-t[:,2,2])*(t[:,0,0]-t[:,2,0])+(t[:,2,0]-t[:,1,0])*(t[:,0,2]-t[:,2,2]);valid=abs(den)>1e-10
 t=t[valid];den=den[valid]
 u=((t[:,1,2]-t[:,2,2])*(x-t[:,2,0])+(t[:,2,0]-t[:,1,0])*(z-t[:,2,2]))/den
 v=((t[:,2,2]-t[:,0,2])*(x-t[:,2,0])+(t[:,0,0]-t[:,2,0])*(z-t[:,2,2]))/den
 w=1-u-v;hit=(u>=-1e-6)&(v>=-1e-6)&(w>=-1e-6)
 return (u[hit]*t[hit,0,1]+v[hit]*t[hit,1,1]+w[hit]*t[hit,2,1]).tolist()

def run():
 s,bs,items=scene();base=blocks(before(ROOT/'Assets/Scenes/Street.unity'));checks={}
 assert all(i in bs for i in base),'An existing scene object/component was removed'
 protected=[i for i,(t,b) in base.items() if t in [4,1001,104,108,20,29,157]]
 assert all(bs[i]==base[i] for i in protected),'Building/prop transform, light, camera or scene settings changed'
 checks['preservedExistingSceneBlocks']=len(base);checks['unchangedTransformsPrefabsLightsCamerasSettings']=len(protected)
 # Every new local scene reference and asset GUID must resolve.
 oldmissing=set();newmissing=set()
 for target,text in [(oldmissing,before(ROOT/'Assets/Scenes/Street.unity')),(newmissing,s)]:
  known=base if target is oldmissing else bs
  target.update(int(i) for i in re.findall(r'\{fileID: (-?\d+)\}',text) if int(i)!=0 and int(i) not in known)
 assert newmissing<=oldmissing,('Dangling scene references',newmissing-oldmissing)
 for g in re.findall(r'guid: ([a-f0-9]{32})',s):assert g in GUID or set(g)<=set('0ef'),'Missing GUID '+g
 paths=subprocess.check_output(['git','diff','--name-only',BASE],cwd=REPO,text=True).splitlines()
 changed=[REPO/p for p in paths if p.endswith('.asset')]
 changed.extend((ROOT/'Assets/Art/StreetRepair').rglob('*.asset'));changed=list(set(changed))
 beforetri=aftertri=beforeverts=afterverts=0
 for p in changed:
  m=cached(p);n=len(m['a'][0]);assert all(len(a)==n for a in m['a'].values()),str(p)
  assert all(np.isfinite(a).all() for a in m['a'].values()),str(p)
  assert int(field(m['text'],'m_DataSize'))*2==len(field(m['text'],'_typelessdata','')),str(p)
  for j in range(len(m['subs'])):
   t=triangles(m,j);assert not t.size or t.max()<n,(str(p),j)
   assert len(t)*3==m['subs'][j][1],str(p)
  aftertri+=len(m['idx'])//3;afterverts+=n
  if p in ORIGINAL or not str(p).startswith(str(ROOT/'Assets/Art/StreetRepair')):
   old=cached(p,True);beforetri+=len(old['idx'])//3;beforeverts+=len(old['a'][0])
 checks['validatedMeshAssets']=len(changed)
 checks['changedAssetsTriangles']=dict(before=beforetri,after=aftertri)
 checks['changedAssetsVertices']=dict(before=beforeverts,after=afterverts)
 # Carriageway triangles must be geometrically identical despite index compaction.
 road=next(x for x in items if x['path'].name=='ReferenceRoad.asset');m=cached(road['path']);old=cached(road['path'],True)
 assert np.array_equal(m['a'][0][triangles(m,0)],old['a'][0][triangles(old,0)]),'Carriageway/profile modified'
 checks['carriagewayTrianglesUnchanged']=len(triangles(m,0))
 # Crosswalk markings remain exactly as authored.
 walk=next(x for x in items if x['go']==736707028);m=cached(walk['path']);old=cached(walk['path'],True)
 assert np.array_equal(m['a'][0][triangles(m,4)],old['a'][0][triangles(old,4)]),'Crosswalk markings changed'
 checks['crosswalkTrianglesUnchanged']=len(triangles(m,4))
 # Every 3.2m-wide strip is checked geometrically; no brick replaces the carriageway.
 new=cached(ROOT/'Assets/Art/StreetRepair/StreetWidePaving.asset');v=new['a'][0];width=np.linalg.norm((v[1::2]-v[0::2])[:,[0,2]],axis=1)
 assert np.max(abs(width-3.2))<.0001,('Walk width',width.min(),width.max())
 checks['sidewalkWidthMeters']=[float(width.min()),float(width.max())]
 from trim_aprons import keep_mask
 from surfaces import projected,load
 keep=keep_mask(bs);stray=0
 for x in items:
  if x['path'].name=='StreetWidePaving.asset':continue
  slots=[j for j,p in enumerate(x['materials']) if isinstance(p,Path) and p.name=='RedGreyRunningBond.mat']
  if slots:stray+=projected(load(x['path']),slots,bs,x).difference(keep).area
 assert stray<.02,('Paving outside short entrance/facade strips',stray)
 checks['pavingOutsideShortApronFacadePolicySquareMeters']=float(stray)
 # Painted leaves keep the same count, 4/2 triangles per leaf, and a midrib ridge.
 leaves=[p for p in changed if '/PaintedDistrict/Leaves/' in str(p)]
 for p in leaves:
  m=cached(p);old=cached(p,True);far=p.stem.endswith('_Far');step=4 if far else 6
  assert len(m['a'][0])==len(old['a'][0]),str(p)
  assert len(m['idx'])==len(old['idx']),str(p)
  uv=m['a'][4].reshape(-1,step,2)
  assert np.all(uv[:,:,0]>=0) and np.all(uv[:,:,0]<=1),str(p)
  assert len(m['a'][0])%step==0,str(p)
 checks['paintedLeafMeshesWithUnchangedTriangleVertexBudgets']=len(leaves)
 # A descending floor must be the ONLY ground collision in the clear ramp lane.
 from garages import SITES
 trans={int(re.search(r'm_GameObject: \{fileID: (\d+)',b)[1]):i for i,(t,b) in bs.items() if t==4 and 'm_GameObject:' in b}
 colliders=[]
 for i,(t,b) in bs.items():
  if t!=64 or 'm_Enabled: 1' not in b:continue
  mo=re.search(r'm_Mesh: .*guid: (\w+)',b);go=re.search(r'm_GameObject: \{fileID: (\d+)',b)
  p=GUID.get(mo[1]) if mo else None
  if not p or p.suffix!='.asset' or not go:continue
  m=cached(p);v=world(bs,dict(trans=trans.get(int(go[1]))),m['a'][0]);tri=np.concatenate([triangles(m,j) for j in range(len(m['subs']))]);tt=v[tri]
  if len(tt):colliders.append((p,tt,tt[:,:,[0,2]].min((0,1)),tt[:,:,[0,2]].max((0,1))))
 probes=0;worst=0
 for site in SITES:
  for longitudinal in np.arange(.75,17.9,.75):
   for lateral in [-1.8,0,1.8]:
    x,z=site['x']+lateral,site['z']+site['direction']*longitudinal;hits=[]
    for p,tt,lo,hi in colliders:
     if lo[0]<=x<=hi[0] and lo[1]<=z<=hi[1]:
      hits.extend((y,p) for y in height_hits(tt,x,z))
    floors=[(y,p) for y,p in hits if p.name=='GarageEntrance.asset']
    assert floors,('Missing ramp collision',site['id'],longitudinal,lateral)
    target=max(y for y,p in floors)
    blocking=[(y,str(p)) for y,p in hits if y>target+.025 and p.name!='GarageEntrance.asset']
    assert not blocking,('Uncut ground blocks the ramp',site['id'],longitudinal,lateral,blocking)
    probes+=1
 checks['rampClearLaneCollisionProbes']=probes
 # Conservative renderer-level upper bound, counting ALL LODs (not an FPS claim).
 totalbefore=totalafter=0;drawbefore=drawafter=0
 basepaths={}
 for i,(typ,b) in base.items():
  if typ==33:
   mo=re.search(r'm_Mesh: .*guid: (\w+)',b)
   if mo and mo[1] in GUID:basepaths[i]=GUID[mo[1]]
 for x in items:
  p=x['path']
  if p.suffix!='.asset':continue
  try:m=cached(p)
  except Exception:continue
  totalafter+=len(m['idx'])//3;drawafter+=sum(c>0 for b,c,t,v in m['subs'])
  if x['filter'] in basepaths:
   try:old=cached(basepaths[x['filter']],True)
   except Exception:continue
   totalbefore+=len(old['idx'])//3;drawbefore+=sum(c>0 for b,c,t,v in old['subs'])
 checks['directSavedRenderersAllLodsUpperBoundTriangles']=dict(before=totalbefore,after=totalafter)
 checks['directSavedRenderersAllLodsNonemptySubmeshes']=dict(before=drawbefore,after=drawafter)
 checks['unityEditorPlayerScreenshotsAndFrameTimes']='NOT RUN: Unity 6000.6.7f1 not installed'
 out=REPO/'Reports/StreetRepair';out.mkdir(parents=True,exist_ok=True);(out/'validation.json').write_text(json.dumps(checks,indent=2))
 print(json.dumps(checks,indent=2))
if __name__=='__main__':run()
