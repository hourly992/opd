"""Enforce metric apron length on saved data, including sparse legacy routes."""
from surfaces import *

def keep_mask(bs):
 data=json.loads((ROOT/'Assets/StreetSource/street.json').read_text());ids={114788180}
 for typ,b in bs.values():
  if typ!=1001:continue
  mo=re.search(r'm_SourcePrefab: .*guid: (\w+)',b);p=GUID.get(mo[1]) if mo else None
  if p and p.suffix=='.prefab':
   mo=re.search(r'  osmId: (\d+)',p.read_text())
   if mo:ids.add(int(mo[1]))
 building=union([Polygon([(p['x'],p['z']) for p in b['points']]) for b in data['buildings'] if b['id'] in ids]).buffer(.12,join_style=2)
 return short_aprons(routes(bs)).union(building.buffer(1.05,join_style=2).difference(building))

def repair():
 _,bs,items=scene();keep=keep_mask(bs);outside=sh.box(-2000,-2000,2000,2000).difference(keep)
 dest=next(x for x in items if x['path'].name=='LocalAsphalt.asset');target=load(dest['path']);count=0;pending=[]
 for x in items:
  if x['path'].name=='StreetWidePaving.asset':continue
  pslots=[j for j,p in enumerate(x['materials']) if isinstance(p,Path) and p.name=='RedGreyRunningBond.mat']
  if not pslots:continue
  m=load(x['path']);original={k:a.copy() for k,a in m['a'].items()};oldtri=[t.copy() for t in m['tri']]
  for j in pslots:
   if not len(oldtri[j]):continue
   if projected(m,[j],bs,x).difference(keep).area<.00001:continue
   lost=dict(m,a={k:a.copy() for k,a in original.items()},tri=[oldtri[j].copy()])
   cut(lost,bs,x,keep)
   if len(lost['tri'][0]):
    ar=lost['a'];t=lost['tri'][0];ids=np.unique(t);remap=np.full(len(ar[0]),-1);remap[ids]=np.arange(len(ids));start=len(target['a'][0]);v=world(bs,x,ar[0][ids]);mat=matrix(bs,x['trans'])[:3,:3]
    arrays={0:v,1:ar[1][ids]@np.linalg.inv(mat),3:ar[3][ids] if 3 in ar else np.full((len(ids),4),255,dtype=np.uint8),4:ar[4][ids,:2] if 4 in ar else v[:,[0,2]]}
    if arrays[3].dtype!=np.uint8:arrays[3]=np.clip(arrays[3]*255,0,255).astype(np.uint8)
    target['a']={k:np.concatenate([target['a'][k],arrays[k]]) for k in target['a']};target['tri'][0]=np.concatenate([target['tri'][0],remap[t]+start])
   retained=dict(m,a={k:a.copy() for k,a in original.items()},tri=[oldtri[j].copy()]);cut(retained,bs,x,outside)
   start=len(m['a'][0]);m['a']={k:np.concatenate([m['a'][k],v]) for k,v in retained['a'].items()};m['tri'][j]=retained['tri'][0]+start;count+=1
  pending.append(m)
 for m in pending:
  used=np.unique(np.concatenate(m['tri']));lookup=np.full(len(m['a'][0]),-1);lookup[used]=np.arange(len(used));m['a']={k:a[used] for k,a in m['a'].items()};m['tri']=[lookup[t] for t in m['tri']];save_mesh(m)
 save_mesh(target);print('Apron submeshes trimmed to metric length:',count)
if __name__=='__main__':repair()
