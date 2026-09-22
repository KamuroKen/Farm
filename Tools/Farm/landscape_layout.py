"""Author the revised terrain using supplied tile assets, never a flat background."""
import json
from pathlib import Path
from road_layout import road_mask, update_saved_plan
ROOT = Path(__file__).resolve().parents[2]
path = ROOT/'Assets/Farm/Design/farm-layout.json'
plan = json.loads(path.read_text())
# Explicit fixed outline makes reruns deterministic (pond moved three cells south).
pond = {(x,y) for y,(left,right) in enumerate([(44,49),(42,52),(41,53),(40,54),(40,55),(40,55),(41,55),(41,54),(42,54),(43,53),(45,51)],33) for x in range(left,right+1)}
river = {(x,y) for x in range(64) for y in range(6 if x<12 or 27<=x<43 or x>=56 else 5)}
water = pond | river
base = next(l for l in plan['tileLayers'] if l['name']=='01 Grass and water')
base['tiles']=[dict(sprite='ground_water' if (x,y) in water else 'ground_grass',x=x-32,y=23-y) for y in range(48) for x in range(64)]
land = {(x,y) for x in range(-1,65) for y in range(-1,49)}-water
shore=[]
for x,y in sorted(land):
 if not(0<=x<64 and 0<=y<48) or not any((x+dx,y+dy) in water for dx in (-1,0,1) for dy in (-1,0,1)):continue
 for q in range(4):
  r,b=q%2,q//2;dx,dy=(1 if r else -1),(1 if b else -1)
  h,v,d=(x+dx,y) in land,(x,y+dy) in land,(x+dx,y+dy) in land
  if h and v:
   sx,sy=(160+r*8,128+b*8) if d else ((40 if r else 48),(104 if b else 112))
  else:sx,sy=((176+r*8) if h else (200 if r else 144)),((120+b*8) if v else (152 if b else 96))
  name=f'shore_{sx}_{sy}'
  assert (ROOT/f'Assets/Farm/Tiles/{name}.asset').exists(),name
  shore.append(dict(sprite=name,x=x*2+r-64,y=47-y*2-b))
next(l for l in plan['tileLayers'] if l['name']=='02 Shoreline')['tiles']=shore
path.write_text(json.dumps(plan,ensure_ascii=False,indent=2)+'\n')
update_saved_plan(ROOT)
plan=json.loads(path.read_text())
manifest={'layers':[l for l in plan['tileLayers'] if l['name'] in ['01 Grass and water','02 Shoreline','03 Earthen paths']]}
(ROOT/'Assets/Farm/Design/landscape-layout.json').write_text(json.dumps(manifest,indent=2)+'\n')
print(f'Landscape: {len(pond)} lake cells, {len(river)} river cells, {len(shore)} bank quarters')
