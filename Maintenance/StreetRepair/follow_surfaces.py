"""Fit existing ground leaves and grass batches to changed pavement footprints."""
from surfaces import *

def repair():
 _,bs,items=scene();surfaces=[x for x in items if x['path'].parent==ART and x['path'].stem in ['StreetWidePaving','LocalAsphalt','GarageMouthAsphalt','GarageEntrance']]
 support=Sampler(surfaces,bs);mask=union([projected(load(x['path']),range(len(load(x['path'])['subs'])),bs,x) for x in surfaces])
 touched=0;removed=0
 for x in items:
  if x['path'].suffix!='.asset' or 'Grass' not in x['path'].name or x['name']=='Grass':continue
  m=load(x['path']);v=world(bs,x,m['a'][0])
  if not len(v) or not mask.intersects(sh.box(v[:,0].min(),v[:,2].min(),v[:,0].max(),v[:,2].max())):continue
  tri=m['tri'];count=0;out=[]
  for t in tri:
   center=v[t].mean(1);hit=sh.contains_xy(mask,center[:,0],center[:,2]);count+=int(hit.sum());out.append(t[~hit])
  if count:
   m['tri']=out;used=np.unique(np.concatenate(out));lookup=np.full(len(v),-1);lookup[used]=np.arange(len(used));m['a']={k:a[used] for k,a in m['a'].items()};m['tri']=[lookup[t] for t in out];save_mesh(m);removed+=count;touched+=1
 adjusted=0
 for x in items:
  if '/PaintedDistrict/Leaves/' not in str(x['path']):continue
  m=load(x['path']);v=world(bs,x,m['a'][0]);step=4 if x['path'].stem.endswith('_Far') else 6;groups=v.reshape(-1,step,3);center=groups.mean(1)
  hits=sh.contains_xy(mask,center[:,0],center[:,2]);positions=m['a'][0].reshape(-1,step,3)
  for i in np.flatnonzero(hits):
   y=support.height(center[i,0],center[i,2],default=groups[i,:,1].min()-.005)
   delta=y+.005-groups[i,:,1].min()
   if abs(delta)>.002:
    positions[i,:,1]+=delta;adjusted+=1
  if np.any(hits):save_mesh(m)
 report=dict(grassBatchesTrimmed=touched,grassTrianglesInsidePavementRemoved=removed,groundLeavesFitted=adjusted)
 folder=ROOT.parent/'Reports/StreetRepair';folder.mkdir(parents=True,exist_ok=True);(folder/'surface-following.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
if __name__=='__main__':repair()
