"""Surgical offline mesh edits for the saved street, never a map regeneration.

Uses the saved route/height geometry, retains every existing GameObject and
transform, and writes collision/render meshes together. Run once on the base
specified in README.md, followed by validate.py and Unity QA when available.
"""
from functools import lru_cache
import shapely as sh
from shapely.geometry import Polygon, LineString, Point
from shapely.ops import unary_union, substring
from unity_yaml import *

ART=ROOT/'Assets/Art/StreetRepair'
@lru_cache(None)
def load(p):
 m=mesh(p);m['tri']=[triangles(m,j).copy() for j in range(len(m['subs']))];return m
def polygons(g):
 if g.is_empty:return []
 return [g] if g.geom_type=='Polygon' else [p for x in getattr(g,'geoms',[]) for p in polygons(x)]
def planar(v):return Polygon(v[:,[0,2]])
def union(g):return unary_union(g) if len(g) else Polygon()
def projected(m,matindices,bs,x):
 v=world(bs,x,m['a'][0]);parts=[]
 for j in matindices:
  t=v[m['tri'][j]];norm=np.cross(t[:,1]-t[:,0],t[:,2]-t[:,0]);mask=norm[:,1]>.00001
  parts.extend(sh.polygons(t[mask][:,:,[0,2]]))
 return union(parts)
def props(b):return dict(re.findall(r'propertyPath: (m_Local(?:Position|Rotation|Scale)\.\w)\n\s+value: ([^\n]+)',b))
def routes(bs):
 b=next(b for t,b in bs.values() if t==114 and '  entranceCount:' in b)
 rr=[]
 for a,pts in re.findall(r'  - address: ([^\n]+)\n    points:\n(.*?)(?=    usesExistingSurface:)',b,re.S):
  v=np.array([[float(x) for x in row] for row in re.findall(r'\{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}',pts)])
  if len(v):rr.append((a,v))
 return rr
def short_aprons(access):
 result=[]
 for address,v in access:
  if len(v)<2:continue
  # The civil-stage connector is stored network-to-door; entrance routes are
  # stored door-to-network. A sparse two-point line still needs metric clipping.
  if 'stage05' in address:v=v[::-1]
  line=LineString(v[:,[0,2]]);short=substring(line,0,min(2.8,line.length))
  if short.geom_type=='LineString':result.append(short.buffer(.95,cap_style=2))
 return union(result)
def material_variant(source,name,finish=None):
 s=source.read_text();s=re.sub(r'  m_Name: .*','  m_Name: '+name,s)
 if finish=='paver':
  # A single red running-bond finish; keep grain/joints without random grey blocks.
  for key,value in [('_PaverGreyFraction','0'),('_PaverPattern','1'),('_BumpStrength','.22'),('_Weathering','.13')]:
   s=re.sub(r'(- '+key+r':) .*',r'\g<1> '+value,s)
  s=re.sub(r'(- _Color:) .*',r'\1 {r: 0.64, g: 0.46, b: 0.39, a: 1}',s)
  for key,color in [('_Color',(.92,.90,.88)),('_PaverRedTone',(.35,.20,.155)),('_PaverGreyTone',(.30,.295,.27)),('_PaverFarTone',(.35,.20,.155))]:
   s=re.sub(r'(- '+key+r':) .*',r'\g<1> '+f'{{r: {color[0]}, g: {color[1]}, b: {color[2]}, a: 1}}',s)
 p=ART/(name+'.mat');p.write_text(s);meta(p,fileid=2100000);return p
def change_materials(bs,x,materials):
 ri=x['render'];t,b=bs[ri]
 new='  m_Materials:\n'+''.join(f'  - {{fileID: 2100000, guid: {guid(p)}, type: 2}}\n' for p in materials)
 b=re.sub(r'  m_Materials:.*?(?=  m_StaticBatchInfo:)',new,b,flags=re.S);bs[ri]=(t,b)
 x['materials']=materials
def append_object(bs,items,name,p,mats,collider=True):
 """Clone stable Unity component schemas, with a new identity/world transform."""
 ids=[k for k in bs if 4000000000<=k<5000000000];go=max(ids+[4000000000])+10
 base=next(x for x in items if x['go']==736707028)
 tid,fid,rid,cid=go+1,go+2,go+3,go+4
 fields=''.join(f'  - component: {{fileID: {i}}}\n' for i in [tid,fid,rid]+([cid] if collider else []))
 g=f'''--- !u!1 &{go}
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
{fields}  m_Layer: 0
  m_Name: {json.dumps(name,ensure_ascii=True)}
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
'''
 tb=base['tb'];tb=re.sub(r'&\d+',f'&{tid}',tb,count=1);tb=re.sub(r'm_GameObject: .*',f'm_GameObject: {{fileID: {go}}}',tb);tb=re.sub(r'm_Father: .*','m_Father: {fileID: 0}',tb)
 fb=bs[base['filter']][1];fb=re.sub(r'&\d+',f'&{fid}',fb,count=1);fb=re.sub(r'm_GameObject: .*',f'm_GameObject: {{fileID: {go}}}',fb);fb=re.sub(r'm_Mesh: .*',f'm_Mesh: {{fileID: 4300000, guid: {guid(p)}, type: 2}}',fb)
 rb=base['rb'];rb=re.sub(r'&\d+',f'&{rid}',rb,count=1);rb=re.sub(r'm_GameObject: .*',f'm_GameObject: {{fileID: {go}}}',rb)
 x=dict(go=go,name=name,trans=tid,filter=fid,render=rid,path=p,materials=mats,tb=tb,rb=rb)
 bs.update({go:(1,g),tid:(4,tb),fid:(33,fb),rid:(23,rb)});change_materials(bs,x,mats)
 if collider:
  template=next(b for t,b in bs.values() if t==64 and 'm_Convex: 0' in b)
  cb=re.sub(r'&\d+',f'&{cid}',template,count=1);cb=re.sub(r'm_GameObject: .*',f'm_GameObject: {{fileID: {go}}}',cb);cb=re.sub(r'm_Mesh: .*',f'm_Mesh: {{fileID: 4300000, guid: {guid(p)}, type: 2}}',cb);bs[cid]=(64,cb)
 rootid=9223372036854775807;t,b=bs[rootid];bs[rootid]=(t,b+f'  - {{fileID: {tid}}}\n')
 return x,cid if collider else None

class Sampler:
 def __init__(self,records,bs):
  ts=[]
  for x in records:
   try:m=load(x['path'])
   except Exception:continue
   v=world(bs,x,m['a'][0]);t=v[np.concatenate(m['tri'])];cross=np.cross(t[:,1]-t[:,0],t[:,2]-t[:,0]);t=t[(cross[:,1]>.000001)&(t[:,:,1].max(1)<.4)]
   if len(t):ts.extend(t)
  self.t=np.array(ts);self.poly=sh.polygons(self.t[:,:,[0,2]]);self.tree=sh.STRtree(self.poly)
 def height(self,x,z,default=.035):
  ids=self.tree.query(Point(x,z),predicate='intersects')
  if not len(ids):return default
  ys=[]
  for i in ids:
   t=self.t[i];a=np.column_stack([t[:,0],t[:,2],np.ones(3)])
   try:ys.append(float(np.array([x,z,1])@np.linalg.solve(a,t[:,1])))
   except np.linalg.LinAlgError:pass
  return max(ys) if ys else default

def cut(m,bs,x,mask):
 """Subtract a footprint from all triangle surfaces, interpolating every channel.

Clipping preserves both collision and visible edges (no top-only hiding plane).
Only triangles intersecting the mask are rebuilt; all others retain attributes.
"""
 if mask.is_empty:return 0
 a=m['a'];v=world(bs,x,a[0]);extra={k:[] for k in a};newtri=[];nextindex=len(v);changes=0
 for indices in m['tri']:
  indices=np.array(indices);tv=v[indices];p=sh.polygons(tv[:,:,[0,2]]);valid=sh.area(p)>1e-9
  # Vertical skirts/curb faces have zero projected area, but must not survive
  # above a cut ramp or inside a widened walk as invisible collision fences.
  vertical=(~valid)&sh.contains_xy(mask,tv[:,:,0].mean(1),tv[:,:,2].mean(1))
  hits=np.flatnonzero(valid & sh.intersects(p,mask))
  keep=np.ones(len(indices),bool);keep[hits]=False;dest=indices[keep].tolist()
  if np.any(vertical):
   keep[vertical]=False;dest=indices[keep].tolist();changes+=int(vertical.sum())
  for j in hits:
   remaining=p[j].difference(mask)
   if remaining.area>p[j].area-1e-9:
    dest.append(indices[j].tolist());continue
   changes+=1
   if remaining.is_empty:continue
   orig=indices[j];t=tv[j];A=np.vstack([t[:,0],t[:,2],np.ones(3)])
   for poly in polygons(remaining):
    for tri in sh.constrained_delaunay_triangles(poly).geoms:
     if tri.area<1e-9:continue
     points=np.array(tri.exterior.coords)[:3]
     if (np.cross(t[1]-t[0],t[2]-t[0])[1]>0)==(sh.is_ccw(tri.exterior)):points=points[::-1]
     dest.append([nextindex,nextindex+1,nextindex+2]);nextindex+=3
     w=np.linalg.solve(A,np.column_stack([points,np.ones(3)]).T).T
     for k in a:extra[k].extend(w@a[k][orig].astype(float))
  newtri.append(np.array(dest,dtype=np.int64).reshape(-1,3))
 if changes:
  for k in a:
   ex=np.array(extra[k]).reshape(-1,a[k].shape[1]);m['a'][k]=np.concatenate([a[k],np.clip(np.round(ex),0,255).astype(np.uint8) if a[k].dtype==np.uint8 else ex.astype(np.float32)])
  m['tri']=newtri
 return changes

def plane_mesh(polys,sampler,p):
 vs=[];ts=[]
 for poly in polygons(polys):
  for tri in sh.constrained_delaunay_triangles(poly).geoms:
   if tri.area<1e-7:continue
   pts=list(tri.exterior.coords)[:3];start=len(vs)
   # World +Y uses clockwise XZ winding.
   if sh.is_ccw(tri.exterior):pts=pts[::-1]
   vs.extend((x,sampler.height(x,z),z) for x,z in pts);ts.append((start,start+1,start+2))
 fresh_mesh(p,vs,[ts]);return p

def repair():
 folders(ART);s,bs,items=scene();report={}
 if any(x['path'].name=='StreetWidePaving.asset' for x in items):raise RuntimeError('Surface migration already applied; use a fresh base checkout.')
 data=json.loads((ROOT/'Assets/StreetSource/street.json').read_text());profile=json.loads((ROOT/'Assets/StreetSource/road_profiles.json').read_text())
 line=LineString([(p['x'],p['z']) for p in next(r for r in data['roads'] if r['main'])['points']])
 pavers=[x for x in items if x['path'].suffix=='.asset' and any(isinstance(p,Path) and '_Paver_' in p.name for p in x['materials'])]
 ground=[x for x in items if x['path'].suffix=='.asset' and (any(isinstance(p,Path) and any(k in p.name for k in ['_Drive_','_Road_','_Paver_','_Footpath_','_Lawn_']) for p in x['materials'])) and not ('Grass' in x['path'].name and x['name']!='Grass')]
 drive=next(p for x in ground for p in x['materials'] if isinstance(p,Path) and '_Drive_' in p.name)
 lawn=next(p for x in ground for p in x['materials'] if isinstance(p,Path) and '_Lawn_' in p.name)
 kerb=next(p for x in items for p in x['materials'] if isinstance(p,Path) and '_RoadKerb_' in p.name)
 paver=material_variant(next(p for x in pavers for p in x['materials'] if '_Paver_' in p.name),'RedGreyRunningBond','paver')
 walkold=next(x for x in items if x['go']==736707028);walkm=load(walkold['path']);walksampler=Sampler([walkold],bs);sampler=Sampler(ground,bs)
 # Preserve the road centreline, carriageway widths, verges and longitudinal profile.
 def section(d):
  sec=next((p for p in profile['sections'] if p['from']<=d<p['to']),profile['sections'][-1]).copy()
  for i,b in enumerate(profile['sections'][1:],1):
   if abs(d-b['from'])<8:
    a=profile['sections'][i-1];t=np.clip((d-b['from']+8)/16,0,1);t=t*t*(3-2*t)
    for k in ['halfWidth','leftSetback','rightSetback','leftWidth','rightWidth']:sec[k]=a[k]*(1-t)+b[k]*t
  return sec
 def frame(d):
  p=np.array(line.interpolate(d).coords[0]);delta=np.array(line.interpolate(min(line.length,d+.05)).coords[0])-np.array(line.interpolate(max(0,d-.05)).coords[0]);delta/=np.linalg.norm(delta);return p,np.array([delta[1],-delta[0]])
 stations=np.unique(np.r_[np.arange(0,line.length,1.8),line.length,[p['from'] for p in profile['sections']]])
 vertices=[];tri=[[],[]];walkpolys=[];outerpaths=[]
 for side,label in [(-1,'left'),(1,'right')]:
  outer=[]
  for d in stations:
   sec=section(d);p,right=frame(d);inner=sec['halfWidth']+.16+sec[label+'Setback'];old=inner+sec[label+'Width']*.5
   oldp=p+right*(side*old);height=walksampler.height(*oldp,default=.14)
   height=float(np.clip(height,.035,.22));outside=inner+3.2
   innerp=p+right*(side*inner);outerp=p+right*(side*outside)
   vertices.extend([(innerp[0],height,innerp[1]),(outerp[0],height+.012,outerp[1])]);outer.append((outerp[0],height+.012,outerp[1]))
  offset=len(vertices)-2*len(stations)
  for j in range(len(stations)-1):
   ids=[offset+j*2,offset+j*2+1,offset+j*2+3,offset+j*2+2];vs=np.array([vertices[i] for i in ids]);poly=planar(vs)
   if not poly.is_valid:continue
   walkpolys.append(poly)
   # Existing lowered driveway crossings retain their asphalt surface.
   d=(stations[j]+stations[j+1])*.5;sec=section(d);p,right=frame(d);q=p+right*side*(sec['halfWidth']+sec[label+'Setback']+sec[label+'Width']*.5)
   # Infer the saved driveway material at the old walk, rather than inventing crossings.
   dest=1 if walks_asphalt_at(walkm,q) else 0
   for ids3 in [(ids[0],ids[1],ids[2]),(ids[0],ids[2],ids[3])]:
    a,b,c=np.array([vertices[i] for i in ids3]);tri[dest].append(ids3 if np.cross(b-a,c-a)[1]>0 else ids3[::-1])
  outerpaths.append(outer)
 walkmask=union(walkpolys)
 # Existing authored approaches start at their entrance; retain a short 2.8m apron.
 access=routes(bs);short=short_aprons(access)
 presentids=set()
 for t,b in bs.values():
  if t!=1001:continue
  mo=re.search(r'm_SourcePrefab: .*guid: (\w+)',b);pf=GUID.get(mo[1]) if mo else None
  if pf and pf.suffix=='.prefab':
   match=re.search(r'  osmId: (\d+)',pf.read_text())
   if match:presentids.add(int(match[1]))
 presentids.add(114788180)
 buildings=union([Polygon([(p['x'],p['z']) for p in b['points']]) for b in data['buildings'] if b['id'] in presentids]).buffer(.12,join_style=2)
 # Preserve facade strips, planting beds and existing court furniture.
 facade=buildings.buffer(1.05,join_style=2).difference(buildings)
 islands=[]
 for x in items:
  if 'IslandBody' in x['path'].stem:
   m=load(x['path']);v=world(bs,x,m['a'][0]);islands.append(sh.MultiPoint(v[:,[0,2]]).convex_hull.buffer(.02))
 islands=union(islands)
 # Keep trees, shrubs and street furniture in their existing positions, with
 # small unpaved collars where a widened service surface passes beside them.
 guards=[]
 for typ,b in bs.values():
  if typ!=1001:continue
  mo=re.search(r'm_SourcePrefab: .*guid: (\w+)',b);pf=GUID.get(mo[1]) if mo else None
  pr=props(b)
  if not pf or 'm_LocalPosition.x' not in pr:continue
  radius=.65 if any(w in pf.name for w in ['Broadleaf','Birch','Spruce','Shrub','Hedge']) else .26 if 'Lamp' in pf.name else .8 if any(w in pf.name for w in ['Bench','Container','Screen']) else 0
  if radius:guards.append(Point(float(pr['m_LocalPosition.x']),float(pr['m_LocalPosition.z'])).buffer(radius,quad_segs=4))
 islands=islands.union(union(guards))
 paved=union([projected(load(x['path']),[j for j,p in enumerate(x['materials']) if isinstance(p,Path) and '_Paver_' in p.name],bs,x) for x in pavers if x['go']!=736707028])
 keep=short.union(facade)
 # Former long pedestrian-only ribbons become an asphalt local access network.
 # Existing open drive/parking areas remain untouched outside the changed footprint.
 local=paved.difference(keep).buffer(1.45,join_style=2).difference(buildings.union(islands).union(keep).union(walkmask))
 local=sh.make_valid(local)
 report.update(streetLength=round(line.length,3),sidewalkWidth=3.2,sidewalkArea=round(walkmask.area,2),localAsphaltArea=round(local.area,2),entranceRoutes=len(access))
 # Resurface existing paver triangles away from the retained door/facade aprons.
 # Splitting by a mask keeps real material boundaries, not just centre-point tests.
 changes=[]
 for x in pavers:
  m=load(x['path']);mats=list(x['materials'])
  if x['go']==736707028:continue
  original=[t.copy() for t in m['tri']];a=m['a'];worldv=world(bs,x,a[0]);pidx=[j for j,p in enumerate(mats) if '_Paver_' in str(p)]
  # Duplicate triangles into asphalt submesh, then clip complementary footprints.
  newmat=len(mats);mats.append(drive);asphalt=[]
  for j in pidx:
   t=original[j];tv=worldv[t];pp=sh.polygons(tv[:,:,[0,2]]);cent=sh.centroid(pp)
   # Exact splitting is performed below by the common cut routine using a copy.
   copy=dict(m);copy['a']={k:v.copy() for k,v in a.items()};copy['tri']=[t.copy()]
   cut(copy,bs,x,keep.union(walkmask))
   start=len(m['a'][0]);m['a']={k:np.concatenate([m['a'][k],v]) for k,v in copy['a'].items()};asphalt.extend((copy['tri'][0]+start).tolist())
  # Original surfaces retain only short door/facade paving; all other original pavers are cut.
  # Non-paver material slots are kept unchanged.
  for j in pidx:
   cm=dict(m);cm['a']={k:v.copy() for k,v in a.items()};cm['tri']=[original[j]]
   extent=Polygon([(-2000,-2000),(2000,-2000),(2000,2000),(-2000,2000)])
   cut(cm,bs,x,extent.difference(keep).union(walkmask))
   start=len(m['a'][0]);m['a']={k:np.concatenate([m['a'][k],v]) for k,v in cm['a'].items()};m['tri'][j]=cm['tri'][0]+start
   mats[j]=paver
  m['tri'].append(np.array(asphalt,dtype=np.int64).reshape(-1,3));change_materials(bs,x,mats)
  changes.append(x['path'])
 # New widened asphalt is cut from existing ground and hardscape before it is added.
 # The street road material itself is excluded: no carriageway/profile edits.
 for x in ground:
  m=load(x['path'])
  if x['go']==736707028:
   # Retain the saved grass verge, asphalt driveway throats and crossing paint.
   original=m['tri'];result=[]
   for j,indices in enumerate(original):
    m['tri']=[indices]
    if j in [2,3,5]:cut(m,bs,x,line.buffer(9,cap_style=2))
    elif j in [0,1]:cut(m,bs,x,walkmask)
    result.append(m['tri'][0])
   m['tri']=result;changes.append(x['path']);continue
  if x in pavers:
   # Converted old asphalt overlaps the widened plane; cut it too.
   pass
  if x['path'].name=='ReferenceRoad.asset':
   # Only the service apron slot may be trimmed under the wider pedestrian band.
   orig=m['tri'];m['tri']=[orig[1]];cut(m,bs,x,walkmask);new=m['tri'][0];m['tri']=[orig[0],new];changes.append(x['path']);continue
  if cut(m,bs,x,local.union(walkmask)):changes.append(x['path'])
 # Remove superseded outer kerbs from inside the wider pavement. The inner
 # carriageway curb lies outside walkmask and is retained at its saved height.
 for x in items:
  if 'ReferenceKerbs' in x['path'].stem or (x['path'].suffix=='.asset' and any('_WalkEdge_' in str(p) for p in x['materials']) and not len(load(x['path'])['a'][0])==0 and world(bs,x,load(x['path'])['a'][0])[:,1].max()<.45):
   m=load(x['path'])
   if cut(m,bs,x,walkmask.union(local)):changes.append(x['path'])
   for _,(typ,b) in bs.items():
    if typ!=64 or f'm_GameObject: {{fileID: {x["go"]}}}' not in b:continue
    match=re.search(r'm_Mesh: .*guid: (\w+)',b)
    if match and GUID.get(match[1])!=x['path']:
     p=GUID.get(match[1]);cm=load(p)
     if cut(cm,bs,x,walkmask.union(local)):changes.append(p)
 for p in set(changes):
  m=load(p);used=np.unique(np.concatenate(m['tri']));remap=np.full(len(m['a'][0]),-1);remap[used]=np.arange(len(used));m['a']={k:v[used] for k,v in m['a'].items()};m['tri']=[remap[t] for t in m['tri']];save_mesh(m)
 walkpath=ART/'StreetWidePaving.asset';fresh_mesh(walkpath,vertices,tri)
 wx,wc=append_object(bs,items,'Уличный тротуар · непрерывная плитка 3,2 м',walkpath,[paver,drive])
 ap=plane_mesh(local,sampler,ART/'LocalAsphalt.asset');lx,lc=append_object(bs,items,'Местные асфальтовые проезды · короткие подходы к входам',ap,[drive])
 # New OUTER curb follows the widened walk; preserve the saved carriageway curb.
 cv=[];ct=[]
 for pts in outerpaths:
  for a,b in zip(pts,pts[1:]):
   a=np.array(a);b=np.array(b);d=b-a;d[1]=0;side=np.array([d[2],0,-d[0]]);side/=np.linalg.norm(side);side*=.055
   q=[a-side,b-side,b+side,a+side];first=len(cv);cv.extend(q);ct.extend([(first,first+1,first+2),(first,first+2,first+3)])
   for q0,q1 in zip(q,q[1:]+q[:1]):
    n=len(cv);cv.extend([q0,q1,q1-np.array([0,.10,0]),q0-np.array([0,.10,0])]);ct.extend([(n,n+1,n+2),(n,n+2,n+3)])
 cp=ART/'WideWalkOuterKerb.asset';fresh_mesh(cp,cv,[ct]);cx,cc=append_object(bs,items,'Бордюр широкой уличной полосы',cp,[kerb])
 for i,(t,b) in list(bs.items()):
  if t==114 and '::ParkingSite' in b:
   b=re.sub(r'(  surfaces:\n(?:  - \{fileID: \d+\}\n)+)',lambda m:m[1]+f'  - {{fileID: {lc}}}\n',b);bs[i]=(t,b)
  if t==114 and ('  entranceCount:' in b or '  streetLength:' in b):
   at=b.index('  surfaces:');end=b.find('\n  ',at+12)
   # Serialized array entries use two spaces too; match the entire reference array.
   b=re.sub(r'(  surfaces:\n(?:  - \{fileID: \d+\}\n)+)',lambda m:m[1]+''.join(f'  - {{fileID: {c}}}\n' for c in [wc,lc,cc]),b)
   bs[i]=(t,b)
 header=s[:s.index('--- !u!')];(ROOT/'Assets/Scenes/Street.unity').write_text(header+''.join(b if b.endswith('\n') else b+'\n' for t,b in bs.values()))
 report['modifiedSurfaceMeshes']=len(set(changes));report['newMeshes']=[str(p.relative_to(ROOT)) for p in [walkpath,ap,cp]]
 out=ROOT.parent/'Reports/StreetRepair';out.mkdir(parents=True,exist_ok=True);(out/'surface-geometry.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))

def walks_asphalt_at(m,q):
 # Only the saved drive crossing submesh, not the old asphalt pedestrian finish.
 t=m['a'][0][m['tri'][0]];p=sh.polygons(t[:,:,[0,2]])
 return bool(np.any(sh.contains_xy(p,q[0],q[1])))

if __name__=='__main__':repair()
