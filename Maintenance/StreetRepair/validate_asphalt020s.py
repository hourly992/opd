"""Read-only asset audit, independent from the installer; writes only its report."""
import argparse,hashlib,io,json,re,subprocess,zipfile
from functools import lru_cache
from pathlib import Path
import numpy as np
from PIL import Image
from unity_yaml import ROOT,GUID,field,guid,scene
BASE='57691931f92fac05831e3246239ae93489372fce'
REPO=ROOT.parent
ASSET=ROOT/'Assets/ThirdParty/ambientCG/Asphalt020S'
@lru_cache(None)
def before(p):return subprocess.check_output(['git','show',BASE+':'+str(p.relative_to(REPO))],cwd=REPO)
def digest(raw):return hashlib.sha256(raw).hexdigest()
def texture_guids(s):
 out={}
 for key in ('_MainTex','_BumpMap','_MaskMap'):
  m=re.search(r'    - '+key+r':\n\s+m_Texture: \{fileID: \d+, guid: (\w+)',s)
  if m:out[key]=m[1]
 return out

def audit(archive):
 # Discover dependency closure without the installer's classification routine.
 pending=[ROOT/'Assets/Scenes/Street.unity'];visited=set();allmats=set()
 while pending:
  p=pending.pop()
  if p in visited:continue
  visited.add(p)
  for g in re.findall(r'guid: ([a-f0-9]{32})',p.read_text(errors='ignore')):
   q=GUID.get(g)
   if q and q.suffix=='.mat':allmats.add(q)
   if q and q.suffix=='.prefab' and q not in visited:pending.append(q)
 expected={p for p in allmats if 'asphalt' in field(before(p).decode(),'m_Name','').lower() or field(before(p).decode(),'- _SurfaceClass','0')=='4'}
 assert len(expected)==7
 scene_path=ROOT/'Assets/Scenes/Street.unity'
 assert scene_path.read_bytes()==before(scene_path)
 changed=subprocess.check_output(['git','diff','--name-only',BASE,'--','Unity_Walk'],cwd=REPO,text=True).splitlines()
 assert not [p for p in changed if p.endswith(('.asset','.unity','.prefab','.fbx','.obj','.blend'))], changed
 assert not [p for p in changed if '/Scripts/' in p or any(k in p.lower() for k in ('/leaves/','leaf','lighting','road_profiles'))], changed
 changed_mats={REPO/p for p in changed if p.endswith('.mat')}
 assert changed_mats==expected,(changed_mats,expected)
 # Every other referenced material is byte-identical, including pavers/concrete/leaves.
 for p in allmats-expected:assert p.read_bytes()==before(p),p
 oldscene_refs=set(re.findall(rb'guid: ([a-f0-9]{32})',before(scene_path)))
 manifest=json.loads((ASSET/'SOURCE.json').read_text())
 keys={'color':'Color','normalGL':'NormalGL','roughness':'Roughness','ambientOcclusion':'AmbientOcclusion'}
 maps=[];images={};entrybytes={}
 with zipfile.ZipFile(archive) as z:
  assert z.testzip() is None
  assert set(z.namelist())=={'Asphalt020S_2K-JPG_'+suffix+'.jpg' for suffix in keys.values()}
  for key,suffix in keys.items():
   name='Asphalt020S_2K-JPG_'+suffix+'.jpg';source=z.read(name);p=ASSET/name
   assert p.read_bytes()==source,p
   assert manifest['files'][key]['sha256']==digest(source)
   if 'originalFileHashes' in manifest:assert manifest['originalFileHashes'][name]==digest(source)
   with Image.open(io.BytesIO(source)) as im:
    assert im.format=='JPEG' and im.size==(2048,2048)
    images[key]=np.array(im.convert('RGB'))
    maps.append(dict(file=name,sourceSize=list(im.size),format=im.format,mode=im.mode,sha256=digest(source),byteIdenticalToSuppliedZip=True))
   entrybytes[key]=p
 assert manifest['asset']=='Asphalt020S' and manifest['license']=='CC0-1.0'
 assert manifest['physicalTileMeters']==[1.85,1.85]
 # The checksum belongs to the actual user-repacked four-map ZIP, not the full official archive.
 delivery=manifest.get('sourceDelivery','').lower()
 assert 'repack' in delivery or 'four' in delivery or 'user' in delivery,delivery
 actual_archive_hash=digest(archive.read_bytes())
 assert manifest.get('archiveSHA256')==actual_archive_hash or manifest.get('suppliedArchiveSHA256')==actual_archive_hash,manifest
 assert (ASSET/'SOURCE-LICENSE.md').is_file()
 license_text=(ASSET/'SOURCE-LICENSE.md').read_text()
 assert 'CC0' in license_text and 'https://ambientcg.com/view?id=Asphalt020S' in license_text
 packed=ASSET/'AO_Roughness.png'
 with Image.open(packed) as im:arr=np.array(im.convert('RGB'));assert im.size==(2048,2048)
 assert np.array_equal(arr[:,:,0],images['ambientOcclusion'][:,:,0])
 assert np.array_equal(arr[:,:,1],images['roughness'][:,:,0])
 assert not arr[:,:,2].any()
 assert manifest['files']['packedMask']['sha256']==digest(packed.read_bytes())
 metadata=[]
 for key,p in dict(entrybytes,packedMask=packed).items():
  text=Path(str(p)+'.meta').read_text()
  normal=key=='normalGL';linear=key!='color'
  assert field(text,'sRGBTexture')==str(int(not linear)),p
  assert field(text,'textureType')==str(int(normal)),p
  if normal:assert field(text,'flipGreenChannel')=='0'
  assert field(text,'enableMipMap')=='1'
  assert field(text,'wrapU')==field(text,'wrapV')=='0'
  assert field(text,'textureCompression')=='2'
  expected_limit=1024 if key in ('normalGL','packedMask') else 2048
  assert int(field(text,'maxTextureSize'))==expected_limit,(p,field(text,'maxTextureSize'),expected_limit)
  assert int(field(text,'aniso'))==8
  metadata.append(dict(file=p.name,sRGB=not linear,textureType=int(normal),greenChannelFlipped=False if normal else None,mipmaps=True,repeat=True,compression='CompressedHQ',maxSize=int(field(text,'maxTextureSize')),aniso=int(field(text,'aniso'))))
 shader=ROOT/'Assets/Shaders/StreetAsphalt020S.shader';shader_s=shader.read_text()
 assert len(re.findall(r'\btex2D\s*\(',shader_s))==3
 assert '#pragma multi_compile_instancing' in shader_s
 assert 'vertex:vert' in shader_s and 'v.tangent = float4(t,1)' in shader_s
 materials=[];old_runtime_maps={};new_runtime_maps={}
 for p in sorted(expected):
  old=before(p).decode();now=p.read_text()
  assert field(old,'m_Name')==field(now,'m_Name'),p
  assert Path(str(p)+'.meta').read_bytes()==before(Path(str(p)+'.meta')),p
  assert guid(shader) in field(now,'m_Shader'),p
  assert field(now,'m_EnableInstancingVariants')=='1'
  assert field(now,'- _SurfaceClass')=='4'
  actual=texture_guids(now)
  expected_refs={'_MainTex':guid(entrybytes['color']),'_BumpMap':guid(entrybytes['normalGL']),'_MaskMap':guid(packed)}
  assert actual==expected_refs,(p,actual,expected_refs)
  for g in texture_guids(old).values():old_runtime_maps[g]=GUID[g]
  for g in actual.values():new_runtime_maps[g]=GUID[g]
  materials.append(dict(path=str(p.relative_to(ROOT)),name=field(now,'m_Name'),guid=guid(p),identityAndGuidPreserved=True,shader='Street/Asphalt020S',sharedSourceMaps=list(actual)))
 def source_size(p):
  try:
   with Image.open(p) as im:return im.size
  except Exception:
   if p.suffix.lower()=='.hdr':
    m=re.search(rb'-Y (\d+) \+X (\d+)',p.read_bytes()[:256])
    if m:return int(m[2]),int(m[1])
   raise
 def pixel_budget(paths):
  total=0
  for p in paths:
   meta_text=Path(str(p)+'.meta').read_text();limit=int(field(meta_text,'maxTextureSize'))
   w,h=source_size(p)
   scale=min(1,limit/max(w,h));total+=round(w*scale)*round(h*scale)
  return total
 # Include every material in the scene/prefab closure, so shared WalkEdge maps
 # retained by concrete/kerbs are counted instead of falsely treating them as freed.
 full_before={};full_after={}
 for p in allmats:
  for dest,text in [(full_before,before(p).decode()),(full_after,p.read_text())]:
   for g in re.findall(r'm_Texture: \{fileID: \d+, guid: (\w+)',text):
    if g in GUID:dest[g]=GUID[g]
 before_texels=pixel_budget(full_before.values());after_texels=pixel_budget(full_after.values())
 assert after_texels<=before_texels,(before_texels,after_texels)
 assert len(new_runtime_maps)==3
 assert pixel_budget(new_runtime_maps.values())==6291456
 # Identify all renderer roles using the current scene, without altering it.
 _,_,items=scene();roles={}
 for role,predicate in [('road',lambda x:x['path'].name=='ReferenceRoad.asset'),('ramps',lambda x:x['path'].name.startswith('GaragePairedBody_')),('driveways',lambda x:x['path'].name in ('LocalAsphalt.asset','GarageMouthAsphalt.asset')),('asphaltEdges',lambda x:any(field(p.read_text(),'m_Name')=='AsphaltArris' for p in x['materials'] if isinstance(p,Path)))]:
  selected=[x for x in items if predicate(x)]
  assert selected,role
  refs=[p for x in selected for p in x['materials'] if isinstance(p,Path) and p in expected]
  assert refs,role
  roles[role]=dict(rendererCount=len(selected),asphaltMaterials=sorted({field(p.read_text(),'m_Name') for p in refs}))
 out=dict(baselineCommit=BASE,sourceDelivery=manifest.get('sourceDelivery'),suppliedArchiveFileName=archive.name,suppliedArchiveBytes=archive.stat().st_size,suppliedArchiveSHA256=actual_archive_hash,suppliedArchiveIsFullOfficialZip=False,
  originalMaps=maps,dependencyClosure=dict(scene='Assets/Scenes/Street.unity',prefabs=len(visited)-1,materials=len(allmats),activeAsphaltMaterials=len(expected)),activeAsphaltMaterials=materials,asphaltRoleCoverage=roles,
  packedAOAndRoughnessExactPixelCount=2048*2048,packedChannels='R=original AO, G=original roughness, B=0',textureImportSettings=metadata,runtimeTextureReads=3,legacyRoadShaderTextureReads=6,
  sourceTextureResolution=[2048,2048],runtimeUniqueTextures=dict(before=len(old_runtime_maps),after=len(new_runtime_maps)),runtimeImportedTexelUpperBound=dict(before=pixel_budget(old_runtime_maps.values()),after=pixel_budget(new_runtime_maps.values())),
  fullMaterialClosureTextureUnion=dict(beforeUniqueTextures=len(full_before),afterUniqueTextures=len(full_after),beforeLevelZeroTexels=before_texels,afterLevelZeroTexels=after_texels,oldAsphaltMapsStillReferenced=sorted(str(p.relative_to(ROOT)) for g,p in old_runtime_maps.items() if g in full_after),comparison='Texel/import-limit upper bound only; not measured GPU bytes or FPS'),
  sceneByteIdentical=True,meshAndPrefabGeometryUnchanged=True,nonAsphaltMaterialsByteIdentical=True,paverAndConcreteMaterialsUnchanged=True,leavesAndLightingUnchanged=True,materialIdentitiesAndGuidsPreserved=True,noRuntimeGenerationOrUpdaterAdded=True,
  limitations=['Unity shader/asset import and C# compilation were not executed.','GPU frame time/FPS and actual imported GPU formats/memory were not measured.','Source map identity is verified byte-for-byte against the user-supplied four-map archive; no checksum of the full official ZIP is claimed.'])
 return out
if __name__=='__main__':
 parser=argparse.ArgumentParser(description=__doc__)
 parser.add_argument('--archive',type=Path,required=True,help='User-supplied ZIP containing the four unchanged original JPEG maps')
 parser.add_argument('--output',type=Path,help='Optional JSON report; no asset writes')
 args=parser.parse_args()
 result=audit(args.archive)
 content=json.dumps(result,indent=2,ensure_ascii=False)+'\n'
 if args.output:
  args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(content)
 print(content,end='')
