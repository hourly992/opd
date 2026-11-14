"""Original low-poly garage entrances: real descending asphalt and cut ground.

Placement is explicitly provisional until panorama coordinates are supplied.
Two pairs occupy the clear central No.5/7 corridor, preserving all house poses.
"""
from surfaces import *

SITES=[dict(id='north-west',x=23.8,z=121.0,direction=-1),
       dict(id='north-east',x=30.3,z=121.0,direction=-1)]

def build_assets(drive):
 folders(ART)
 concrete=ART/'GarageWhiteConcrete.mat'
 template=ROOT/'Assets/PhotoReconstruction/Stage04Courtyard/Materials/CourtGreyRectangularPavers.mat'
 s=template.read_text();s=re.sub(r'  m_Name: .*','  m_Name: GarageWhiteConcrete',s)
 # Use the existing painted surface shader rather than importing a photo material.
 surfaceguid=guid(ROOT/'Assets/Shaders/StreetSurface.shader')
 s=re.sub(r'  m_Shader: .*',f'  m_Shader: {{fileID: 4800000, guid: {surfaceguid}, type: 3}}',s)
 for key,val in [('_Kind','6'),('_VertexTint','1'),('_Smoothness','.10')]:
  if f'- {key}:' in s:s=re.sub(r'(- '+key+':) .*',r'\g<1> '+val,s)
  else:s=s.replace('    m_Floats:\n',f'    m_Floats:\n    - {key}: {val}\n')
 s=re.sub(r'(- _Color:) .*',r'\1 {r: 0.84, g: 0.83, b: 0.77, a: 1}',s);concrete.write_text(s);meta(concrete,fileid=2100000)
 canopy=ART/'GarageCanopy.mat';s=s.replace('GarageWhiteConcrete','GarageCanopy');s=re.sub(r'  m_Shader: .*',f'  m_Shader: {{fileID: 4800000, guid: {guid(ROOT/"Assets/Shaders/StreetGarageCanopy.shader")}, type: 3}}',s);s=re.sub(r'(- _Color:) .*',r'\1 {r: 0.62, g: 0.69, b: 0.64, a: 0.24}',s);canopy.write_text(s);meta(canopy,fileid=2100000)
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
 def floor(z):
  # 1.5m vertical easements at the mouth and shutter; maximum grade 16%.
  if z<1.5:return -.16*z*z/3
  if z>16.5:return -2.64+.16*(18-z)**2/3
  return -.12-.16*(z-1.5)
 for z0,z1 in zip(np.arange(0,18,.75),np.arange(.75,18.01,.75)):
  a,b=floor(z0),floor(z1)
  face([(-2.2,a,z0),(-2.2,b,z1),(2.2,b,z1),(2.2,a,z0)],0)
  for sign in [-1,1]:
   x0,x1=sorted([sign*2.2,sign*2.46]);bottom=min(a,b)-.16
   box((x0,bottom,z0),(x1,.85,z1))
 # A closed, recessed garage shutter terminates the modeled entrance.
 box((-2.2,-2.70,18),(2.2,.85,18.16),.52)
 for y in np.arange(-2.5,.6,.27):box((-2.18,y,17.98),(2.18,y+.014,18.005),.40)
 for sign in [-1,1]:box((sign*2.2-.07,.85,0),(sign*2.2+.07,.90,18),.70)
 # Shallow barrel vault, 12 angular divisions, five ribs. Geometry is shared.
 gv=[];gt=[]
 def arch(theta,z):return np.array([2.47*np.cos(theta),.94+1.13*np.sin(theta),z])
 for j in range(12):
  a=j*np.pi/12;b=(j+1)*np.pi/12;first=len(gv)
  gv.extend([arch(a,6),arch(a,18),arch(b,18),arch(b,6)]);gt.extend([(first,first+1,first+2),(first,first+2,first+3)])
 for z in np.linspace(6,18,5):
  for j in range(12):
   a=j*np.pi/12;b=(j+1)*np.pi/12
   p,q=arch(a,z),arch(b,z);out=np.array([np.cos((a+b)*.5),np.sin((a+b)*.5),0])*.022;dz=np.array([0,0,.022])
   # Four-sided metal rib, folded into the same concrete/vertex-tint batch.
   pts=[p-out-dz,p+out-dz,p+out+dz,p-out+dz];qs=[q-out-dz,q+out-dz,q+out+dz,q-out+dz]
   for k in range(4):face([pts[k],qs[k],qs[(k+1)%4],pts[(k+1)%4]],color=.48)
 for sign in [-1,1]:box((sign*2.47-.025,.91,6),(sign*2.47+.025,.96,18),.48)
 op=ART/'GarageEntrance.asset';gp=ART/'GarageBarrelCanopy.asset';fresh_mesh(op,v,t,colors=colors);fresh_mesh(gp,gv,[gt])
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
 excavate=union(masks);edited=[]
 for x in ground:
  m=load(x['path']);v=world(bs,x,m['a'][0])
  if not len(v) or not excavate.intersects(sh.box(v[:,0].min(),v[:,2].min(),v[:,0].max(),v[:,2].max())):continue
  if cut(m,bs,x,excavate):save_mesh(m);edited.append(str(x['path'].relative_to(ROOT)))
 # Flat asphalt necks connect mouths to the existing north/south service drive.
 neck=union([Polygon([(p['x']-2.2,p['z']),(p['x']+2.2,p['z']),(p['x']+2.2,130 if p['direction']<0 else -55),(p['x']-2.2,130 if p['direction']<0 else -55)]) for p in SITES])
 for x in ground:
  m=load(x['path']);v=world(bs,x,m['a'][0])
  if len(v) and neck.intersects(sh.box(v[:,0].min(),v[:,2].min(),v[:,0].max(),v[:,2].max())):
   if cut(m,bs,x,neck):save_mesh(m)
 neckpath=plane_mesh(neck,sampler,ART/'GarageMouthAsphalt.asset');nx,nc=append_object(bs,items,'Асфальтовые сопряжения гаражных спусков',neckpath,[drive])
 header=s[:s.index('--- !u!')];(ROOT/'Assets/Scenes/Street.unity').write_text(header+''.join(b if b.endswith('\n') else b+'\n' for t,b in bs.values()))
 out=dict(entrances=report,excavatedMeshes=edited,excavatedArea=round(excavate.area,3));folder=ROOT.parent/'Reports/StreetRepair';folder.mkdir(parents=True,exist_ok=True);(folder/'garage-geometry.json').write_text(json.dumps(out,indent=2));print(json.dumps(out))
if __name__=='__main__':repair()
