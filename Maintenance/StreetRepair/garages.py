"""Shared garage models and raised courtyard, baked into saved scene assets.

Only the two existing provisional entrances are modelled. Their spacing and
surrounding grade are estimates from references 14–17, not survey coordinates.
"""
from surfaces import *

SITES=[dict(id='north-west',x=22.1,z=121.0,direction=-1),
       dict(id='north-east',x=30.3,z=121.0,direction=-1)]

YARD=(18.75,97.8,33.65,130.0)

def floor(z):
 # Preserve the existing eased 16% descending driving profile.
 if z<1.5:return -.16*z*z/3
 if z>16.5:return -2.64+.16*(18-z)**2/3
 return -.12-.16*(z-1.5)

def shoulder(z):
 return .85*np.clip(z/6,0,1)

def garage_mesh(p,vertices,tri,colors=None,normals=None):
 """Surface shaders need a nonzero orthogonal tangent basis on saved meshes.

Keep this fix local: historical leaf/terrain assets must not be rewritten.
"""
 fresh_mesh(p,vertices,tri,colors=colors)
 m=mesh(p);m['tri']=tri
 n=m['a'][1] if normals is None else np.asarray(normals,dtype=np.float32)
 n/=np.maximum(np.linalg.norm(n,axis=1)[:,None],1e-9)
 tang=np.cross(np.tile([0,1,0],(len(n),1)),n)
 tang[np.abs(n[:,1])>.8]=[1,0,0]
 # Gram-Schmidt also handles the slightly sloping ramp/deck surfaces.
 tang-=n*np.sum(n*tang,axis=1)[:,None]
 tang/=np.maximum(np.linalg.norm(tang,axis=1)[:,None],1e-9)
 m['a'][1]=n;m['a'][2]=np.column_stack([tang,np.ones(len(n))]).astype(np.float32)
 save_mesh(m)
 p.write_text('\n'.join(line.rstrip() for line in p.read_text().splitlines())+'\n')
 return p

def yard_mesh(sampler,ground_y):
 """Rising asphalt between separate ramps, flat roof yard and rear return.

The original renderer/collider is reused, with holes only in the ramp lanes.
Boundary samples match saved ground. No runtime generation is introduced.
"""
 holes=union([sh.box(p['x']-2.47,102.83,p['x']+2.47,121) for p in SITES])
 xmin,zmin,xmax,zmax=YARD
 xs=[xmin,xmin+.85]+[p['x']+d for p in SITES for d in [-2.47,2.47]]+[xmax-.85,xmax]
 zs=[zmin,102.8,102.83,103,115,121,zmax]
 vs=[];ts=[]
 def height(x,z):
  side=min(1,max(0,(x-xmin)/.85),max(0,(xmax-x)/.85))
  rise=min(1,max(0,(121-z)/6),max(0,(z-zmin)/5))
  w=min(side,rise)
  return sampler.height(x,z,default=ground_y)*(1-w)+(ground_y+.85)*w
 for x0,x1 in zip(sorted(set(xs)),sorted(set(xs))[1:]):
  for z0,z1 in zip(zs,zs[1:]):
   for poly in polygons(sh.box(x0,z0,x1,z1).difference(holes)):
    for t in sh.constrained_delaunay_triangles(poly).geoms:
     pts=list(t.exterior.coords)[:3]
     if sh.is_ccw(t.exterior):pts=pts[::-1]
     start=len(vs);vs.extend((x,height(x,z),z) for x,z in pts);ts.append((start,start+1,start+2))
 return garage_mesh(ART/'GarageMouthAsphalt.asset',vs,[ts])

def build_assets(drive):
 folders(ART)
 concrete=ART/'GarageWhiteConcrete.mat'
 # A dedicated light concrete material; do not inherit courtyard paver settings.
 surfaceguid=guid(ROOT/'Assets/Shaders/StreetSurface.shader')
 template=ROOT/'Assets/PhotoReconstruction/Stage04Courtyard/Materials/CourtGreyRectangularPavers.mat'
 s=(concrete if concrete.exists() else template).read_text()
 s=re.sub(r'  m_Name: .*','  m_Name: GarageWhiteConcrete',s)
 s=re.sub(r'  m_Shader: .*',f'  m_Shader: {{fileID: 4800000, guid: {surfaceguid}, type: 3}}',s)
 s=re.sub(r'    m_TexEnvs:.*?(?=    m_Ints:)', '    m_TexEnvs: []\n',s,flags=re.S)
 s=re.sub(r'    m_Floats:.*?(?=    m_Colors:)', '''    m_Floats:
    - _Kind: 6
    - _VertexTint: 1
    - _Smoothness: 0.10
    - _Metallic: 0
    - _Cull: 2
''',s,flags=re.S)
 s=re.sub(r'    m_Colors:.*?(?=  m_BuildTextureStacks:)', '''    m_Colors:
    - _Color: {r: 0.94, g: 0.94, b: 0.91, a: 1}
    - _EmissionColor: {r: 0, g: 0, b: 0, a: 1}
''',s,flags=re.S)
 concrete.write_text(s);meta(concrete,fileid=2100000)
 canopy=ART/'GarageCanopy.mat';s=s.replace('GarageWhiteConcrete','GarageCanopy')
 s=re.sub(r'  m_Shader: .*',f'  m_Shader: {{fileID: 4800000, guid: {guid(ROOT/"Assets/Shaders/StreetGarageCanopy.shader")}, type: 3}}',s)
 s=re.sub(r'(- _Color:) .*',r'\1 {r: 0.72, g: 0.85, b: 0.89, a: 0.32}',s)
 canopy.write_text(s);meta(canopy,fileid=2100000)
 v=[];colors=[];t=[[],[]]
 def face(points,slot=1,color=1):
  first=len(v);v.extend(points);colors.extend([[int(color*255)]*3+[255]]*len(points))
  for i in range(1,len(points)-1):t[slot].append((first,first+i,first+i+1))
 def box(lo,hi,color=1):
  x,y,z=lo;X,Y,Z=hi
  face([(x,Y,z),(x,Y,Z),(X,Y,Z),(X,Y,z)],color=color)
  face([(x,y,z),(X,y,z),(X,y,Z),(x,y,Z)],color=color)
  face([(x,y,z),(x,Y,z),(X,Y,z),(X,y,z)],color=color)
  face([(X,y,Z),(X,Y,Z),(x,Y,Z),(x,y,Z)],color=color)
  face([(x,y,Z),(x,Y,Z),(x,Y,z),(x,y,z)],color=color)
  face([(X,y,z),(X,Y,z),(X,Y,Z),(X,y,Z)],color=color)
 for z0,z1 in zip(np.arange(0,18,.75),np.arange(.75,18.01,.75)):
  a,b=floor(z0),floor(z1)
  face([(-2.2,a,z0),(-2.2,b,z1),(2.2,b,z1),(2.2,a,z0)],0)
  for sign in [-1,1]:
   x0,x1=sorted([sign*2.2,sign*2.46]);lo0,lo1=a-.16,b-.16
   hi0,hi1=shoulder(z0)+.16,shoulder(z1)+.16
   # Continuous white retaining walls, with no internal box end faces.
   face([(x0,lo0,z0),(x0,lo1,z1),(x0,hi1,z1),(x0,hi0,z0)])
   face([(x1,lo0,z0),(x1,hi0,z0),(x1,hi1,z1),(x1,lo1,z1)])
   face([(x0,hi0,z0),(x0,hi1,z1),(x1,hi1,z1),(x1,hi0,z0)])
   face([(x0,lo0,z0),(x1,lo0,z0),(x1,lo1,z1),(x0,lo1,z1)])
 for sign in [-1,1]:
  x0,x1=sorted([sign*2.2,sign*2.46])
  face([(x0,-.16,0),(x0,.16,0),(x1,.16,0),(x1,-.16,0)])
  face([(x0,-2.8,18),(x1,-2.8,18),(x1,1.01,18),(x0,1.01,18)])
 # A closed, recessed garage shutter terminates the modeled entrance.
 box((-2.2,-2.70,18),(2.2,1.01,18.16),.88)
 for y in np.arange(-2.5,.8,.27):box((-2.18,y,17.98),(2.18,y+.014,18.005),.78)
 # Clearly curved translucent barrel roof with light metal ribs.
 gv=[];gt=[];gn=[]
 def arch(theta,z):return np.array([2.47*np.cos(theta),1.04+1.55*np.sin(theta),z])
 def outward(theta):
  n=np.array([np.cos(theta)/2.47,np.sin(theta)/1.55,0]);return n/np.linalg.norm(n)
 for j in range(12):
  a=j*np.pi/12;b=(j+1)*np.pi/12;first=len(gv)
  gv.extend([arch(a,6),arch(b,6),arch(b,18),arch(a,18)])
  gn.extend([outward(a),outward(b),outward(b),outward(a)])
  gt.extend([(first,first+1,first+2),(first,first+2,first+3)])
 for z in np.linspace(6,18,5):
  for j in range(12):
   a=j*np.pi/12;b=(j+1)*np.pi/12
   p,q=arch(a,z),arch(b,z);out=np.array([np.cos((a+b)*.5),np.sin((a+b)*.5),0])*.022;dz=np.array([0,0,.022])
   # Four-sided metal rib, folded into the same concrete/vertex-tint batch.
   pts=[p-out-dz,p+out-dz,p+out+dz,p-out+dz];qs=[q-out-dz,q+out-dz,q+out+dz,q-out+dz]
   for k in range(4):face([pts[k],qs[k],qs[(k+1)%4],pts[(k+1)%4]],color=.82)
 for sign in [-1,1]:box((sign*2.47-.025,1.015,6),(sign*2.47+.025,1.065,18),.82)
 op=ART/'GarageEntrance.asset';gp=ART/'GarageBarrelCanopy.asset'
 garage_mesh(op,v,t,colors=colors);garage_mesh(gp,gv,[gt],normals=gn)
 return op,gp,concrete,canopy

def repair():
 s,bs,items=scene();drive=next(p for x in items for p in x['materials'] if isinstance(p,Path) and '_Drive_' in p.name)
 if '::UndergroundParkingEntrance' in s:raise RuntimeError('Garage migration already applied; use a fresh base checkout.')
 op,gp,concrete,glass=build_assets(drive)
 # Use the saved hard surfaces for the mouth level. Ground is excavated, not hidden.
 ground=[]
 for x in items:
  if x['path'].suffix!='.asset' or not x['materials']:continue
  if any(isinstance(p,Path) and any(k in p.name for k in ['_Drive_','_Lawn_','_Paver_','RedGreyRunning','LocalAsphalt']) for p in x['materials']) or x['path'].name=='LocalAsphalt.asset':ground.append(x)
 sampler=Sampler(ground,bs);masks=[];report=[]
 for site in SITES:
  x,z,sign=site['x'],site['z'],site['direction'];y=sampler.height(x,z,default=.035)
  # Rotation by 180 degrees flips both X and Z for the northern pair.
  corners=[(x-2.47,z),(x+2.47,z),(x+2.47,z+18.17*sign),(x-2.47,z+18.17*sign)];mask=Polygon(corners);masks.append(mask)
  body,coll=append_object(bs,items,'Подземная парковка 5/7 · '+site['id'],op,[drive,concrete])
  roof,_=append_object(bs,items,'Прозрачный полукруглый навес · '+site['id'],gp,[glass],False)
  for obj in [body,roof]:
   tid=obj['trans'];typ,tb=bs[tid];tb=re.sub(r'm_LocalPosition: .*',f'm_LocalPosition: {{x: {x}, y: {y}, z: {z}}}',tb)
   if sign<0:tb=re.sub(r'm_LocalRotation: .*','m_LocalRotation: {x: 0, y: 1, z: 0, w: 0}',tb)
   bs[tid]=(typ,tb)
  # Roof is a child of the entrance; its local transform is identity.
  typ,tb=bs[roof['trans']];tb=re.sub(r'm_Father: .*',f'm_Father: {{fileID: {body["trans"]}}}',tb);tb=re.sub(r'm_LocalPosition: .*','m_LocalPosition: {x: 0, y: 0, z: 0}',tb);tb=re.sub(r'm_LocalRotation: .*','m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}',tb);bs[roof['trans']]=(typ,tb)
  typ,tb=bs[body['trans']];tb=tb.replace('m_Children: []',f'm_Children:\n  - {{fileID: {roof["trans"]}}}');bs[body['trans']]=(typ,tb)
  typ,rb=bs[roof['render']];rb=rb.replace('m_CastShadows: 1','m_CastShadows: 0');bs[roof['render']]=(typ,rb)
  rid=9223372036854775807;typ,rb=bs[rid];bs[rid]=(typ,rb.replace(f'  - {{fileID: {roof["trans"]}}}\n',''))
  # Provenance and QA data live on the entrance itself, without runtime generation.
  mid=body['go']+5;typ,gb=bs[body['go']];gb=gb.replace('  m_Layer:',f'  - component: {{fileID: {mid}}}\n  m_Layer:');bs[body['go']]=(typ,gb)
  bs[mid]=(114,f'''--- !u!114 &{mid}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {body['go']}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {guid(ROOT/'Assets/Scripts/Environment/UndergroundParkingEntrance.cs')}, type: 3}}
  m_Name:
  m_EditorClassIdentifier: Assembly-CSharp::UndergroundParkingEntrance
  address: 5/7
  placementProvisional: 1
  length: 18
  clearWidth: 4.4
  depth: 2.64
  maximumGrade: 0.16
  floorAndWalls: {{fileID: {coll}}}
  evidence: User panorama references; documented underground garage 86:12:0000000:3567. Positions and dimensions are estimates pending panorama coordinates.
''')
  report.append(dict(**site,groundY=y,depth=2.64,clearWidth=4.4,length=18,maximumGrade=.16,provisional=True))
 # The asphalt yard replaces both old ground and the narrow grass divider.
 # Its only holes are the two ramp lanes. Match the original ground at edges.
 neckpath=yard_mesh(sampler,report[0]['groundY'])
 excavate=sh.box(*YARD);edited=[]
 for x in ground:
  m=load(x['path']);v=world(bs,x,m['a'][0])
  if not len(v) or not excavate.intersects(sh.box(v[:,0].min(),v[:,2].min(),v[:,0].max(),v[:,2].max())):continue
  if cut(m,bs,x,excavate):save_mesh(m);edited.append(str(x['path'].relative_to(ROOT)))
 nx,nc=append_object(bs,items,'Асфальтовый подъём между въездами и ровный двор над парковкой',neckpath,[drive])
 header=s[:s.index('--- !u!')];(ROOT/'Assets/Scenes/Street.unity').write_text(header+''.join(b if b.endswith('\n') else b+'\n' for t,b in bs.values()))
 out=dict(entrances=report,excavatedMeshes=edited,excavatedArea=round(excavate.area,3));folder=ROOT.parent/'Reports/StreetRepair';folder.mkdir(parents=True,exist_ok=True);(folder/'garage-geometry.json').write_text(json.dumps(out,indent=2));print(json.dumps(out))
if __name__=='__main__':repair()
