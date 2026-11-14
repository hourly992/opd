import re, json, struct
from pathlib import Path
import numpy as np
ROOT=Path(__file__).resolve().parents[2]/'Unity_Walk'
def field(s,key,default=None):
 m=re.search(r'^\s*'+re.escape(key)+r': (.*)$',s,re.M)
 return m[1] if m else default
def vec(s,key):
 return np.array([float(x) for x in re.findall(r'[xyzw]: ([^,}]+)',field(s,key))])
def decode_name(s):
 s=field(s,'m_Name','').strip('"')
 return re.sub(r'\\(?:u([0-9a-fA-F]{4})|x([0-9a-fA-F]{2}))',lambda m:chr(int(m[1] or m[2],16)),s)
def blocks(s):
 return {int(m[2]):(int(m[1]),m[0]) for m in re.finditer(r'--- !u!(\d+) &(-?\d+)[^\n]*\n.*?(?=--- !u!|\Z)',s,re.S)}
GUID={}
for p in (ROOT/'Assets').rglob('*.meta'):
 m=re.search(r'^guid: (\w+)',p.read_text(),re.M)
 if m:GUID[m[1]]=Path(str(p)[:-5])
def refs(s,key):
 m=re.search(r'^  '+key+r':\s*\n((?:  - .*\n)+)',s,re.M)
 return re.findall(r'guid: (\w+)',m[1]) if m else []
def mesh(p,text=None):
 s=p.read_text() if text is None else text; n=int(field(s,'m_VertexCount'))
 ch=[tuple(map(int,m)) for m in re.findall(r'- stream: (\d+)\n\s+offset: (\d+)\n\s+format: (\d+)\n\s+dimension: (\d+)',s)]
 data=bytes.fromhex(field(s,'_typelessdata',''))
 sizes={0:4,1:2,2:1,3:1,4:2,5:2,6:1,7:1,8:2,9:2,10:4,11:4}
 strides={a:max(b+sizes[c]*d for a2,b,c,d in ch if a2==a) for a,b,c,d in ch if d}
 offsets={};off=0
 for st,stride in sorted(strides.items()): offsets[st]=off;off=(off+stride*n+15)//16*16
 arrays={}
 for i,(st,ofs,fmt,dim) in enumerate(ch):
  if dim:
   dtype={0:'<f4',1:'<f2',2:'u1',3:'i1',4:'<u2',5:'<i2',6:'u1',7:'i1',8:'<u2',9:'<i2',10:'<u4',11:'<i4'}[fmt]
   arrays[i]=np.ndarray((n,dim),dtype=dtype,buffer=data,offset=offsets[st]+ofs,strides=(strides[st],sizes[fmt])).copy() if n else np.empty((0,dim),dtype=dtype)
 idx=np.frombuffer(bytes.fromhex(field(s,'m_IndexBuffer','').rstrip('y')),dtype='<u4' if field(s,'m_IndexFormat')=='1' else '<u2').copy()
 subs=[tuple(map(int,m)) for m in re.findall(r'firstByte: (\d+)\n\s+indexCount: (\d+)\n\s+topology: (\d+)\n\s+baseVertex: (\d+)',s)]
 return dict(path=p,text=s,ch=ch,a=arrays,idx=idx,subs=subs)
def matrix(bs,tid):
 if not tid:return np.eye(4)
 tb=bs[tid][1]
 if not field(tb,'m_LocalPosition'):return np.eye(4)
 x,y,z,w=vec(tb,'m_LocalRotation');sc=vec(tb,'m_LocalScale');pos=vec(tb,'m_LocalPosition')
 r=np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])
 m=np.eye(4);m[:3,:3]=r*sc;m[:3,3]=pos
 par=int(re.search(r'm_Father: \{fileID: (\d+)',tb)[1]);return matrix(bs,par)@m if par else m
def world(bs,x,a):
 m=matrix(bs,x['trans']);return a@m[:3,:3].T+m[:3,3]
def triangles(m,j):
 b,n,top,base=m['subs'][j];size=m['idx'].dtype.itemsize
 return m['idx'][b//size:b//size+n].reshape(-1,3)+base
def guid(p):
 return re.search(r'^guid: (\w+)',Path(str(p)+'.meta').read_text(),re.M)[1]
def meta(p,importer='NativeFormatImporter',fileid=4300000):
 import hashlib
 g=hashlib.md5(str(p.relative_to(ROOT)).encode()).hexdigest()
 mp=Path(str(p)+'.meta')
 if not mp.exists():
  mp.write_text(f'fileFormatVersion: 2\nguid: {g}\n'+('folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n' if p.is_dir() else f'{importer}:\n  externalObjects: {{}}\n'+(f'  mainObjectFileID: {fileid}\n' if importer=='NativeFormatImporter' else '')+'  userData: \n  assetBundleName: \n  assetBundleVariant: \n'))
 return guid(p)
def folders(p):
 p.mkdir(parents=True,exist_ok=True)
 while p!=ROOT/'Assets':meta(p);p=p.parent
def save_mesh(m,p=None):
 """Write a readable Unity 6 mesh; preserves GUID, channels and submesh slots."""
 p=p or m['path'];a=m['a'];n=len(a[0]);data=bytearray();channels=[];offset=0
 for i in range(14):
  if i in a:
   ar=a[i];dim=ar.shape[1];fmt=2 if ar.dtype==np.uint8 else 0
   channels.append((0,offset,fmt,dim));offset+=dim*(1 if fmt==2 else 4)
  else:channels.append((0,0,0,0))
 data=bytearray(offset*n)
 for i,ar in a.items():
  st,ofs,fmt,dim=channels[i];dt='u1' if fmt==2 else '<f4'
  if n:np.ndarray((n,dim),dtype=dt,buffer=data,offset=ofs,strides=(offset,1 if fmt==2 else 4))[:]=ar
 def bounds(v):
  if not len(v):v=np.zeros((1,3))
  lo=v.min(0);hi=v.max(0)
  def v3(q):return '{'+', '.join(f'{k}: {float(v):.9g}' for k,v in zip('xyz',q))+'}'
  return f'      m_Center: {v3((lo+hi)/2)}\n      m_Extent: {v3((hi-lo)/2)}\n'
 subs=[];indices=[];offsetb=0;index32=n>65535;size=4 if index32 else 2
 for t in m['tri']:
  t=np.asarray(t,dtype=np.int64).reshape(-1,3);flat=t.flatten();used=np.unique(flat)
  assert not len(flat) or (flat.min()>=0 and flat.max()<n)
  subs.append(f'  - serializedVersion: 2\n    firstByte: {offsetb}\n    indexCount: {len(flat)}\n    topology: 0\n    baseVertex: 0\n    firstVertex: {int(used.min()) if len(used) else 0}\n    vertexCount: {int(used.max()-used.min()+1) if len(used) else 0}\n    localAABB:\n'+bounds(a[0][used]))
  offsetb+=len(flat)*size;indices.extend(flat)
 s=m['text'];s=re.sub(r'  m_SubMeshes:.*?(?=  m_Shapes:)', '  m_SubMeshes:\n'+''.join(subs),s,flags=re.S)
 s=re.sub(r'  m_IndexFormat: .*','  m_IndexFormat: '+str(int(index32)),s)
 s=re.sub(r'  m_IndexBuffer: .*','  m_IndexBuffer: '+np.asarray(indices,dtype='<u4' if index32 else '<u2').tobytes().hex()+'y',s)
 c=''.join(f'    - stream: {st}\n      offset: {ofs}\n      format: {fmt}\n      dimension: {dim}\n' for st,ofs,fmt,dim in channels)
 vd=f'  m_VertexData:\n    serializedVersion: 3\n    m_VertexCount: {n}\n    m_Channels:\n'+c+f'    m_DataSize: {len(data)}\n    _typelessdata:'+(' '+data.hex() if data else '')+'\n'
 s=re.sub(r'  m_VertexData:.*?(?=  m_CompressedMesh:)',vd,s,flags=re.S)
 s=re.sub(r'  m_LocalAABB:\n.*?(?=  m_CookingOptions:)', '  m_LocalAABB:\n'+bounds(a[0]).replace('      ','    '),s,flags=re.S)
 # A mesh rewritten here has no serialized Unity-generated internal mesh LOD chain.
 s=re.sub(r'  m_MeshLodInfo:.*', '',s,flags=re.S)
 s=s.replace('  m_BakedTriangleCollisionMesh: '+field(s,'m_BakedTriangleCollisionMesh',''),'  m_BakedTriangleCollisionMesh: ')
 s=re.sub(r'  m_Name: .*','  m_Name: '+p.stem,s)
 p.parent.mkdir(parents=True,exist_ok=True);p.write_text(s);return s
def fresh_mesh(p,vertices,tri,uv=None,colors=None):
 template=ROOT/'Assets/PhotoReconstruction/Stage09Courtyard/Leaves/WindblownLeaf.asset'
 m=mesh(template);v=np.asarray(vertices,dtype=np.float32);norm=np.zeros_like(v)
 for t in tri:
  t=np.asarray(t).reshape(-1,3)
  if not len(t):continue
  cross=np.cross(v[t[:,1]]-v[t[:,0]],v[t[:,2]]-v[t[:,0]])
  for j in range(3):np.add.at(norm,t[:,j],cross)
 norm/=np.maximum(np.linalg.norm(norm,axis=1)[:,None],1e-9)
 m['a']={0:v,1:norm,3:np.asarray(colors if colors is not None else np.full((len(v),4),255),dtype=np.uint8),4:np.asarray(uv if uv is not None else v[:,[0,2]],dtype=np.float32)}
 m['tri']=tri;save_mesh(m,p);meta(p);return p
def scene(name='Street'):
 s=(ROOT/f'Assets/Scenes/{name}.unity').read_text();bs=blocks(s)
 gos={i:decode_name(b) for i,(t,b) in bs.items() if t==1}
 trans={int(field(b,'m_GameObject').split(':')[1].strip(' }')):(i,b) for i,(t,b) in bs.items() if t==4 and field(b,'m_GameObject')}
 renders={int(re.search(r'm_GameObject: \{fileID: (\d+)',b)[1]):(i,b) for i,(t,b) in bs.items() if t==23}
 items=[]
 for i,(t,b) in bs.items():
  if t!=33:continue
  go=int(re.search(r'm_GameObject: \{fileID: (\d+)',b)[1]);m=re.search(r'm_Mesh: \{fileID: (\d+), guid: (\w+)',b)
  if not m or m[2] not in GUID:continue
  ti,tb=trans.get(go,(None,''));ri,rb=renders.get(go,(None,''))
  items.append(dict(go=go,name=gos.get(go),filter=i,trans=ti,render=ri,path=GUID[m[2]],materials=[GUID.get(g,g) for g in refs(rb,'m_Materials')],tb=tb,rb=rb))
 return s,bs,items
