"""Saved-scene follow-up: connect the parking courts and add the west pair.

Run once on PR #3 head 2fb3b79. It edits ground/render collision together,
preserves the main carriageway and red paving, and never moves existing props.
The west placement is a free corridor on the opposite side of the main road;
it mirrors the entrance model, not unverified geographic coordinates.
"""
import subprocess
from garages import garage_mesh, floor, shoulder
from surfaces import *

BASE='2fb3b792ddba4966e3aceb8c74866f81827a36bc'
WEST=[dict(id='opposite-west',x=-108.,z=67.,yaw=90.),
      dict(id='opposite-east',x=-108.,z=75.2,yaw=90.)]
WEST_BOUNDS=(-117.,63.65,-84.8,78.55)
CONNECTIONS=[dict(id='north-west-mouth',points=[(22.1,121),(22.1,135.14)],width=4.4),
             dict(id='north-east-mouth',points=[(30.3,121),(30.3,135.46)],width=4.4),
             dict(id='west-mouths-detour',points=[(-108,57.7),(-113,61),(-113,71.1),(-108,71.1)],width=4.4)]

def asphalt_slot(p):
 if not isinstance(p,Path):return False
 name=field(p.read_text(),'m_Name','').lower()
 return any(w in name for w in ['asphalt','referenceasphalt','drive']) and not any(w in name for w in ['paver','kerb','roadpaint']) or '_Road_0.' in p.name or '_Drive_' in p.name

def hard_surface(x):
 p=x['path']
 if p.suffix!='.asset' or any(w in str(p) for w in ['/Leaves/','Leaf','Windblown']):return False
 return any(asphalt_slot(p) or 'Lawn' in str(p) or 'Paver' in str(p) or 'RedGreyRunning' in str(p) for p in x['materials']) or any(w in p.name for w in ['Grass_','CourtRetained_','Stage03Surface_','VergeOutside','ReferenceKerbsPaintedEdges','WideWalkOuterKerb'])

def save_scene(s,bs):
 (ROOT/'Assets/Scenes/Street.unity').write_text(s[:s.index('--- !u!')]+''.join(b if b.endswith('\n') else b+'\n' for _,b in bs.values()))

def append_world_mesh(dest,source):
 """Append to the existing world-space asphalt renderer and collider."""
 start=len(dest['a'][0]);n=len(source['a'][0])
 for key in dest['a']:
  assert key in source['a'],('Missing vertex channel in append',key)
  dest['a'][key]=np.concatenate([dest['a'][key],source['a'][key].astype(dest['a'][key].dtype)])
 dest['tri'][0]=np.concatenate([dest['tri'][0],source['tri'][0]+start])

def ensure_asphalt_basis(m):
 # Preserve the saved normals/geometry; provide the tangent basis required by
 # the currently installed normal-mapped asphalt as well as the requested one.
 n=m['a'][1].astype(np.float64)
 bad=np.linalg.norm(n,axis=1)<1e-9
 if np.any(bad):
  # A few retained near-zero-area seam vertices had no source normal. Repair
  # their shading basis only; positions/indices and collision stay identical.
  accum=np.zeros_like(n);v=m['a'][0]
  for tri in m['tri']:
   cross=np.cross(v[tri[:,1]]-v[tri[:,0]],v[tri[:,2]]-v[tri[:,0]])
   for j in range(3):np.add.at(accum,tri[:,j],cross)
  accum[np.linalg.norm(accum,axis=1)<1e-9]=[0,1,0]
  accum/=np.maximum(np.linalg.norm(accum,axis=1)[:,None],1e-9)
  n[bad]=accum[bad];m['a'][1][bad]=n[bad]
 n/=np.maximum(np.linalg.norm(n,axis=1)[:,None],1e-9)
 t=np.cross(np.tile([0,1,0],(len(n),1)),n)
 t[np.abs(n[:,1])>.8]=[1,0,0]
 t-=n*np.sum(t*n,axis=1)[:,None]
 t/=np.maximum(np.linalg.norm(t,axis=1)[:,None],1e-9)
 m['a'][2]=np.column_stack([t,np.ones(len(n))]).astype(np.float32)

def west_yard(sampler,base_y):
 # Local +Z descends east, local X points south; the mouths face west.
 def world_point(u,l):return -108+l,71.1-u
 holes=union([sh.box(c-2.47,0,c+2.47,18.17) for c in [-4.1,4.1]])
 xs=[-7.45,-6.6,-6.57,-1.63,1.63,6.57,6.6,7.45]
 zs=[-9,0,6,18,18.17,18.2,23.2]
 vs=[];ts=[]
 for a,b in zip(xs,xs[1:]):
  for c,d in zip(zs,zs[1:]):
   for poly in polygons(sh.box(a,c,b,d).difference(holes)):
    for tri in sh.constrained_delaunay_triangles(poly).geoms:
     pts=list(tri.exterior.coords)[:3]
     # The local-to-world rotation preserves orientation in XZ.
     if sh.is_ccw(tri.exterior):pts=pts[::-1]
     first=len(vs)
     for u,l in pts:
      x,z=world_point(u,l)
      w=min(1,max(0,l/6),max(0,(23.2-l)/5),max(0,(7.45-abs(u))/.85))
      y=sampler.height(x,z,default=base_y)*(1-w)+(base_y+.85)*w
      vs.append((x,y,z))
     ts.append((first,first+1,first+2))
 p=ART/'WestParkingCourt.asset'
 garage_mesh(p,vs,[ts])
 return load(p)

def add_entrance(bs,items,site,drive,concrete,source):
 obj,cid=append_object(bs,items,'Подземная парковка · '+site['id'],ART/'GarageEntrance.asset',[drive,concrete])
 # The new pair's visuals are batched into the existing north renderers.
 # These objects retain only identity, collision and inspection metadata.
 for key in ['filter','render']:
  bs.pop(obj[key])
  typ,b=bs[obj['go']];bs[obj['go']]=(typ,b.replace(f'  - component: {{fileID: {obj[key]}}}\n',''))
 typ,tb=bs[obj['trans']]
 angle=np.radians(site['yaw']*.5)
 tb=re.sub(r'm_LocalPosition: .*',lambda _:f'm_LocalPosition: {{x: {site["x"]}, y: {site["y"]}, z: {site["z"]}}}',tb)
 tb=re.sub(r'm_LocalRotation: .*',lambda _:f'm_LocalRotation: {{x: 0, y: {np.sin(angle)}, z: 0, w: {np.cos(angle)}}}',tb)
 bs[obj['trans']]=(typ,tb)
 mid=obj['go']+5
 typ,gb=bs[obj['go']];gb=gb.replace('  m_Layer:',f'  - component: {{fileID: {mid}}}\n  m_Layer:');bs[obj['go']]=(typ,gb)
 _,mb=next((t,b) for t,b in bs.values() if t==114 and '::UndergroundParkingEntrance' in b and f'm_GameObject: {{fileID: {source["go"]}}}' in b)
 mb=re.sub(r'&\d+',f'&{mid}',mb,count=1)
 mb=re.sub(r'm_GameObject: .*',f'm_GameObject: {{fileID: {obj["go"]}}}',mb)
 mb=re.sub(r'  address: .*','  address: west-side mirrored pair',mb)
 mb=re.sub(r'  floorAndWalls: .*',f'  floorAndWalls: {{fileID: {cid}}}',mb)
 mb=re.sub(r'  evidence: .*','  evidence: Identical entrance model across the main street; free corridor checked against saved buildings and props. Placement is an estimate from user references 20-25.',mb)
 bs[mid]=(114,mb)
 return obj,cid

def run():
 s,bs,items=scene();scene_path=ROOT/'Assets/Scenes/Street.unity'
 old=subprocess.check_output(['git','show',BASE+':'+str(scene_path.relative_to(ROOT.parent))],cwd=ROOT.parent,text=True)
 if s!=old:raise SystemExit('Requires unchanged saved scene at '+BASE+'; refusing to overwrite edits.')
 entrances=[x for x in items if x['path'].name=='GarageEntrance.asset']
 assert len(entrances)==2
 source=next(x for x in entrances if 'north-west' in x['name'])
 drive,concrete=source['materials']
 ground=[x for x in items if hard_surface(x) and not x['path'].name.startswith('Garage')]
 sampler=Sampler([x for x in ground if 'Grass_' not in x['path'].name],bs)
 base_y=sampler.height(-108,71.1,default=-.01)
 sites=[dict(**p,y=base_y) for p in WEST]
 # Use only actual asphalt tops for connectivity; pavers stay byte-identical.
 asphalt=union([projected(load(x['path']),[j for j,p in enumerate(x['materials']) if asphalt_slot(p)],bs,x)
                for x in items if x['path'].suffix=='.asset' and any(asphalt_slot(p) for p in x['materials']) and x not in entrances])
 pavers=union([projected(load(x['path']),[j for j,p in enumerate(x['materials']) if isinstance(p,Path) and ('Paver' in p.name or 'RedGreyRunning' in p.name)],bs,x)
               for x in items if x['path'].suffix=='.asset' and any(isinstance(p,Path) and ('Paver' in p.name or 'RedGreyRunning' in p.name) for p in x['materials'])])
 corridors=union([LineString(c['points']).buffer(c['width']/2,cap_style=2,join_style=2) for c in CONNECTIONS])
 yard_mask=sh.box(*WEST_BOUNDS)
 additions=corridors.difference(asphalt.union(pavers).union(yard_mask))
 # Build surfaces before cutting, so their outside heights match saved ground.
 yard=west_yard(sampler,base_y)
 cp=plane_mesh(additions,sampler,ART/'ParkingNetworkLinks.asset')
 links=load(cp)
 mask=yard_mask.union(additions)
 changed=[]
 for x in ground:
  if x['path'].name=='ReferenceRoad.asset':continue # main profile remains exact
  m=load(x['path']);v=world(bs,x,m['a'][0])
  if not len(v) or not mask.intersects(sh.box(v[:,0].min(),v[:,2].min(),v[:,0].max(),v[:,2].max())):continue
  # Red pavers never change. The selected envelope/corridors avoid their area.
  if any('Paver' in str(p) or 'RedGreyRunning' in str(p) for p in x['materials']):
   assert projected(m,[j for j,p in enumerate(x['materials']) if 'Paver' in str(p) or 'RedGreyRunning' in str(p)],bs,x).intersection(mask).area<.001
  if cut(m,bs,x,mask):
   if not x['path'].is_relative_to(ART):
    # Keep historical study scenes on their original shared grass/ground.
    old_guid=guid(x['path']);target=ART/'Surfaces'/('GarageNetwork_'+x['path'].name)
    save_mesh(m,target);meta(target);new_guid=guid(target)
    for clean in [target,Path(str(target)+'.meta')]:
     clean.write_text('\n'.join(line.rstrip() for line in clean.read_text().splitlines())+'\n')
    for cid,(ct,cb) in list(bs.items()):
     if ct in [33,64] and f'm_GameObject: {{fileID: {x["go"]}}}' in cb:
      bs[cid]=(ct,cb.replace(old_guid,new_guid))
    x['path']=target
   else:save_mesh(m)
   changed.append(str(x['path'].relative_to(ROOT)))
 dest=next(x for x in items if x['path'].name=='LocalAsphalt.asset')
 m=load(dest['path']);append_world_mesh(m,yard);append_world_mesh(m,links);ensure_asphalt_basis(m);save_mesh(m)
 # These were assembly inputs, not additional renderers/resources to ship.
 for p in [ART/'WestParkingCourt.asset',cp]:
  p.unlink();Path(str(p)+'.meta').unlink()
 new_colliders=[]
 for site in sites:
  obj,cid=add_entrance(bs,items,site,drive,concrete,source);new_colliders.append(cid)
 from batch_garages import batch
 batching=batch(bs,items,sites)
 save_scene(s,bs)
 out=dict(baseCommit=BASE,mirrorInterpretation='Identical opposed models on west side of main street, not literal reflection into woodland',
          westEntrances=sites,westYardBounds=list(WEST_BOUNDS),westYardRise=.85,connections=CONNECTIONS,
          addedConnectorArea=additions.area,cutGroundAssets=changed,mirrorColliderIds=new_colliders,batching=batching,
          source='User panoramas 20-24 and overhead25; saved street/service axes. Placement and dimensions remain estimates.')
 (ROOT.parent/'Reports/StreetRepair/parking-network-geometry.json').write_text(json.dumps(out,indent=2)+'\n')
 print(json.dumps(out,indent=2))

if __name__=='__main__':run()
