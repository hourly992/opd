"""Consolidate resurfaced approach fragments into the existing asphalt batch."""
from surfaces import *

def repair():
 s,bs,items=scene();dest=next(x for x in items if x['path'].name=='LocalAsphalt.asset');m=load(dest['path']);count=0
 for x in items:
  mats=x['materials']
  if len(mats)<2 or not any(isinstance(p,Path) and p.name=='RedGreyRunningBond.mat' for p in mats) or '_Drive_' not in str(mats[-1]):continue
  old=load(x['path']);j=len(mats)-1
  if j==len(old['tri']):
   # An empty surplus slot can already be absent from a saved mesh.
   change_materials(bs,x,mats[:-1]);continue
  t=old['tri'][j]
  if len(t):
   ids=np.unique(t);remap=np.full(len(old['a'][0]),-1);remap[ids]=np.arange(len(ids));start=len(m['a'][0]);v=world(bs,x,old['a'][0][ids]);matrix3=matrix(bs,x['trans'])[:3,:3]
   ar={0:v,1:old['a'][1][ids]@np.linalg.inv(matrix3),3:old['a'][3][ids] if 3 in old['a'] else np.full((len(ids),4),255,dtype=np.uint8),4:old['a'][4][ids,:2] if 4 in old['a'] else v[:,[0,2]]}
   if ar[3].dtype!=np.uint8:ar[3]=np.clip(ar[3]*255,0,255).astype(np.uint8)
   m['a']={k:np.concatenate([m['a'][k],ar[k]]) for k in m['a']};m['tri'][0]=np.concatenate([m['tri'][0],remap[t]+start]);count+=1
  old['tri'].pop(j);save_mesh(old);change_materials(bs,x,mats[:-1])
 save_mesh(m);header=s[:s.index('--- !u!')];(ROOT/'Assets/Scenes/Street.unity').write_text(header+''.join(b for t,b in bs.values()));print('Consolidated asphalt material slots:',count)
if __name__=='__main__':repair()
