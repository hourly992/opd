"""Bake original leaf silhouettes into existing batches, keeping placement and LODs."""
import hashlib
from PIL import Image, ImageDraw
from unity_yaml import *

ART=ROOT/'Assets/Art/StreetRepair'
def atlas():
 folders(ART)
 # Original outlines, drawn here; no reference image pixels or third-party assets.
 birch=[(0,-.49),(-.025,-.35),(-.15,-.29),(-.22,-.19),(-.27,-.08),(-.28,.04),(-.24,.17),(-.16,.30),(0,.49),(.15,.30),(.23,.18),(.28,.04),(.26,-.10),(.20,-.22),(.10,-.31),(.025,-.35),(.025,-.49)]
 maple=[(0,-.49),(-.025,-.30),(-.16,-.27),(-.38,-.22),(-.28,-.09),(-.47,.06),(-.30,.08),(-.35,.29),(-.17,.20),(-.10,.30),(0,.49),(.11,.28),(.18,.19),(.35,.29),(.31,.08),(.47,.06),(.28,-.09),(.38,-.22),(.15,-.27),(.025,-.30),(.025,-.49)]
 aspen=[(0,-.49),(-.025,-.31),(-.16,-.28),(-.29,-.18),(-.36,-.02),(-.34,.13),(-.25,.27),(-.12,.35),(0,.47),(.13,.35),(.28,.25),(.36,.10),(.35,-.05),(.26,-.21),(.12,-.30),(.025,-.31),(.025,-.49)]
 scale=4;size=256;im=Image.new('RGBA',(size*3*scale,size*scale),(255,255,255,0));d=ImageDraw.Draw(im)
 for k,outline in enumerate([birch,maple,aspen]):
  # Fine, restrained teeth on broadleaf edges; stem and tip remain clean.
  pts=[]
  for i,p in enumerate(outline):
   q=outline[(i+1)%len(outline)];pts.append(p)
   if k!=1 and abs(p[0])>.10 and abs(q[0])>.10:
    pts.extend([((p[0]*2+q[0])/3+np.sign(p[0])*.012,(p[1]*2+q[1])/3),((p[0]+q[0]*2)/3,(p[1]+q[1]*2)/3)])
  xy=[((k+.5+x)*size*scale,(.5-y)*size*scale) for x,y in pts]
  d.polygon(xy,fill=(255,255,255,255))
 im=im.resize((768,256),Image.Resampling.LANCZOS);p=ART/'AutumnLeafSilhouettes.png';im.save(p)
 # A small uncompressed, mipmapped, linear alpha atlas; no per-leaf textures.
 mp=Path(str(p)+'.meta');g=hashlib.md5(str(p.relative_to(ROOT)).encode()).hexdigest()
 mp.write_text(f'''fileFormatVersion: 2
guid: {g}
TextureImporter:
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: 0
    alphaTestReferenceValue: 0.45
    mipMapsPreserveCoverage: 1
  isReadable: 0
  textureType: 0
  textureShape: 1
  maxTextureSize: 1024
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 2
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  alphaSource: 1
  alphaIsTransparency: 1
  textureCompression: 0
  userData: Original birch maple aspen silhouettes authored for this project
  assetBundleName:
  assetBundleVariant:
''')
 return p

def repair():
 for mat in (ROOT/'Assets/PhotoReconstruction').rglob('FallenLeaves.mat'):
  if '_LeafAtlas:' in mat.read_text():raise RuntimeError('Leaf migration already applied; use a fresh base checkout.')
 p=atlas();materials=set();paths=set()
 for name in ['Street','Street_LeafStudy']:
  _,_,items=scene(name)
  for x in items:
   if any(isinstance(p,Path) and 'FallenLeaves' in p.name for p in x['materials']):
    paths.add(x['path']);materials.update(p for p in x['materials'] if isinstance(p,Path) and 'FallenLeaves' in p.name)
 report=[]
 for p in sorted(paths):
  m=mesh(p);a=m['a'];idx=np.concatenate([triangles(m,j) for j in range(len(m['subs']))]);n=len(a[0]);parent=np.arange(n)
  def root(x):
   while parent[x]!=x:parent[x]=parent[parent[x]];x=parent[x]
   return x
  for x,y,z in idx:parent[root(y)]=root(x);parent[root(z)]=root(x)
  groups={}
  for i in np.unique(idx):groups.setdefault(root(i),[]).append(i)
  v=[];uv=[];cols=[];tris=[];far=p.stem.endswith('_Far');seed=int(hashlib.md5(p.name.encode()).hexdigest()[:8],16)
  for j,ids in enumerate(groups.values()):
   olduv=a[4][ids,:2];oldv=a[0][ids];design=np.column_stack([np.ones(len(ids)),olduv]);basis=np.linalg.lstsq(design,oldv,rcond=None)[0];center,width,axis=basis
   normal=np.cross(axis,width);normal/=max(np.linalg.norm(normal),1e-12)
   if normal[1]<0:normal=-normal
   color=a[3][ids].mean(0)
   if a[3].dtype!=np.uint8:color=np.clip(color*255,0,255)
   variant=(j+seed)%3;first=len(v)
   outline=[(-.5,-.51),(-.5,.51),(.5,.51),(.5,-.51)] if far else [(-.5,-.51),(-.5,.51),(0,-.51),(0,.51),(.5,-.51),(.5,.51)]
   # 2 longitudinal panels form a gentle midrib; 4 triangles, matching old near LOD.
   for x,y in outline:
    curl=0 if far else (.003 if abs(x)>.1 else .010)+max(0,y-.25)*.012
    v.append(center+width*x+axis*y+normal*curl);uv.append(((variant+.5+x)/3,.5+y));cols.append(color)
   local=[(0,1,2),(0,2,3)] if far else [(0,1,3),(0,3,2),(2,3,5),(2,5,4)]
   tris.extend(tuple(first+i for i in t) for t in local)
  oldtri=len(idx);fresh_mesh(p,v,[tris],uv,cols)
  report.append(dict(path=str(p.relative_to(ROOT)),leaves=len(groups),beforeTriangles=oldtri,afterTriangles=len(tris)))
 for mat in materials:
  s=mat.read_text();texture=guid(ART/'AutumnLeafSilhouettes.png')
  s=s.replace('    m_TexEnvs: []',f'    m_TexEnvs:\n    - _LeafAtlas:\n        m_Texture: {{fileID: 2800000, guid: {texture}, type: 3}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}')
  if '_LeafAtlas:' not in s:
   s=s.replace('    m_TexEnvs:\n',f'    m_TexEnvs:\n    - _LeafAtlas:\n        m_Texture: {{fileID: 2800000, guid: {texture}, type: 3}}\n        m_Scale: {{x: 1, y: 1}}\n        m_Offset: {{x: 0, y: 0}}\n')
  mat.write_text(s)
 return report
if __name__=='__main__':
 r=repair();out=ROOT.parent/'Reports/StreetRepair';out.mkdir(parents=True,exist_ok=True);(out/'leaf-assets.json').write_text(json.dumps(r,indent=2));print('leaf assets',len(r),'leaves',sum(x['leaves'] for x in r),'triangles',sum(x['beforeTriangles'] for x in r),'->',sum(x['afterTriangles'] for x in r))
