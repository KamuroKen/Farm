"""Compose the farm from existing atlas rectangles; no artwork is synthesized.

Run with a Python environment containing Pillow. Unity's FarmLevelBuilder reads
the resulting layout; the PNG is an authoring preview, not the game background.
"""
from pathlib import Path
import json
import math
import random
from PIL import Image
from sorting_rules import normalize

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
W, H, PPU = 64, 48, 16
PW, PH = W * PPU, H * PPU
rng = random.Random(3417)
GROUND = "Assets/Tileset/Autotile_Grass_and_Dirt_Path_Tileset.png"
sprites = {}
objects, colliders, groups, markers, layers = [], [], [], [], []


def register(name, atlas, x, y, w, h, pivot=(0, 0)):
    sprites[name] = dict(name=name, atlas=atlas, x=x, y=y, w=w, h=h,
                         pivotX=pivot[0], pivotY=pivot[1])
    return name


for item in json.loads((HERE / "prop-catalog.json").read_text()):
    register(**item)


def obj(sprite, px, py, group="Details", name=None, order=None, flip=False):
    s = sprites[sprite]
    objects.append(dict(name=name or sprite.replace('_', ' ').title(), sprite=sprite,
                        group=group, x=px/16-W/2, y=H/2-(py+s['h'])/16,
                        order=int((py+s['h'])*4) if order is None else order,
                        flipX=flip))
    return objects[-1]


def box(name, px, py, w, h, group="Collisions"):
    colliders.append(dict(name=name, group=group, x=(px+w/2)/16-W/2,
                          y=H/2-(py+h/2)/16, width=w/16, height=h/16))


def marker(name, px, py):
    markers.append(dict(name=name, x=px/16-W/2, y=H/2-py/16))


def layer(name, size, order):
    result = dict(name=name, cellSize=size, order=order, tiles=[])
    layers.append(result)
    return result['tiles']


grass = register('ground_grass', GROUND, 160, 48, 16, 16, (.5, .5))
water = register('ground_water', GROUND, 208, 16, 16, 16, (.5, .5))
base = layer('01 Grass and water', 1, -30000)
shore = layer('02 Shoreline', .5, -29990)
paths = layer('03 Earthen paths', .5, -29980)
soil = layer('04 Cultivated soil', .5, -29970)

# Irregular pond silhouette, larger at the south and narrowed near the jetty.
pond = set()
for y, (left, right) in enumerate([(44,49),(42,52),(41,53),(40,54),
                                  (40,55),(40,55),(41,55),(41,54),
                                  (42,54),(43,53),(45,51)],start=30):
    pond.update((x,y) for x in range(left,right+1))

road = set()


def road_rect(x0, y0, x1, y1):
    for y in range(y0, y1+1):
        for x in range(x0, x1+1):
            road.add((x, y))


road_rect(30, 22, 32, 47)
road_rect(30, 6, 32, 23)
road_rect(31, 0, 33, 7)
road_rect(17, 23, 45, 25)
road_rect(17, 18, 19, 24)
road_rect(43, 17, 45, 24)
road_rect(15, 17, 21, 19)
road_rect(41, 17, 48, 19)
road_rect(45, 23, 49, 25)
road_rect(25, 34, 31, 36)
road_rect(32, 29, 40, 31)
road_rect(39, 30, 41, 35)
road_rect(25, 22, 26, 24)  # Short approach to the seed shop.
for y in range(20, 29):
    for x in range(27, 36):
        if (x-31)**2 + (y-24)**2 < 20:
            road.add((x, y))
road -= pond

# Quarter-tile edge selection preserves the supplied convex/concave pixel art.
# Keys are quadrant, whether its horizontal and vertical neighbors exist,
# and whether the diagonal exists. Source coordinates have top-left origin.
def quad_source(kind, q, side_h, side_v, diagonal):
    right, bottom = q % 2, q // 2
    if kind == 'path':
        if side_h and side_v:
            if diagonal:
                return (256+right*8, 32+bottom*8)
            return ((304 if not right else 296), (32 if not bottom else 24))
        sx = (256+right*8) if side_h else (280 if right else 240)
        sy = (32+bottom*8) if side_v else (56 if bottom else 16)
        return sx, sy
    if side_h and side_v:
        if diagonal:
            return 160+right*8, 128+bottom*8
        return ((48 if not right else 40), (112 if not bottom else 104))
    sx = (176+right*8) if side_h else (200 if right else 144)
    sy = (120+bottom*8) if side_v else (152 if bottom else 96)
    return sx, sy


def quarters(kind, mask, x, y, target):
    for q in range(4):
        dx, dy = (1 if q%2 else -1), (1 if q//2 else -1)
        sx, sy = quad_source(kind, q, (x+dx,y) in mask,
                             (x,y+dy) in mask, (x+dx,y+dy) in mask)
        name = f'{kind}_{sx}_{sy}'
        if name not in sprites:
            register(name, GROUND, sx, sy, 8, 8, (.5, .5))
        target.append(dict(sprite=name, x=x*2+q%2-W,
                           y=H-1-y*2-q//2))


land = {(x, y) for y in range(-1,H+1) for x in range(-1,W+1)} - pond
for y in range(H):
    for x in range(W):
        base.append(dict(sprite=water if (x,y) in pond else grass,
                         x=x-W//2, y=H//2-y-1))
        if (x,y) not in pond and any((x+a,y+b) in pond
                                      for a in (-1,0,1) for b in (-1,0,1)):
            quarters('shore', land, x, y, shore)
        if (x,y) in road:
            quarters('path', road, x, y, paths)

# Buildings are genuinely editable modular assemblies, with one sorting group
# each, rather than a flattened screenshot of the farm.
buildings = json.loads((HERE / 'building-parts.json').read_text())
for key, px, py, title in [('cottage',224,200,'Buildings/Cottage'),
                            ('barn',688,188,'Buildings/Barn'),
                            ('shop',384,284,'Buildings/Seed Shop'),
                            ('shed',864,364,'Buildings/Storage Shed')]:
    building = buildings[key]
    groups.append(dict(name=title, sortingOrder=(py+building['height']-4)*4))
    for i, part in enumerate(building['parts']):
        name = f"{key}_{part['x']}_{part['y']}_{part['w']}_{part['h']}"
        if name not in sprites:
            register(name, 'Assets/Tileset/'+part['atlas'], part['x'],part['y'],part['w'],part['h'])
        obj(name, px+part['dx'], py+part['dy'], title, part['name'], i)
    for rect in building['footprints']:
        box(title.split('/')[-1]+' footprint',px+rect['x'],py+rect['y'],
            rect['w'],rect['h'],'Collisions/Buildings')

marker('Player Spawn — cottage porch',272,298)
marker('North Exit — woodland trail',520,24)
marker('South Entrance',504,736)
marker('Garden Interaction Area',420,568)
marker('Barn Entrance',720,286)
marker('Pond Jetty',656,551)
marker('Seed Shop Entrance',416,364)
marker('Storage Shed Entrance',888,448)

# A low fence around the garden and a larger rail enclosure beside the barn.
def fence_rect(group, x0,y0,x1,y1, gap_side=None, gap_at=0, gap_width=32, style='brown'):
    horizontal = 'fence_'+style+'_horizontal'
    vertical = 'fence_'+style+'_vertical'
    for y, side in [(y0,'north'),(y1,'south')]:
        for x in range(x0,x1,24):
            if side==gap_side and x < gap_at+gap_width and x+24 > gap_at:
                continue
            obj(horizontal,x,y,group)
            box('Fence rail',x+3,y+8,24,4,'Collisions/Fences')
    for x, side in [(x0,'west'),(x1,'east')]:
        for y in range(y0+8,y1,16):
            if side==gap_side and y < gap_at+gap_width and y+16 > gap_at:
                continue
            obj(vertical,x,y,group)
            box('Fence post',x+2,y,4,16,'Collisions/Fences')


fence_rect('Garden/Fencing',152,472,424,664,'east',544,48,'brown')
fence_rect('Barnyard/Enclosure',784,288,928,448,'west',368,48,'rail')

# Six crop beds with differently staged plantings; purely visual, no growth logic.
crop_types = ['carrot','cabbage','pumpkin','strawberry','corn','wheat']
for index, (bx,by) in enumerate([(11,31),(16,31),(21,31),(11,37),(16,37),(21,37)]):
    for qy in range(8):
        for qx in range(8):
            sx = 16 if qx==0 else (24 if qx==7 else 20)
            sy = 416 if qy==0 else (424 if qy==7 else 420)
            name = f'bed_soil_{sx}_{sy}'
            if name not in sprites:
                register(name,'Assets/Tileset/Crops_Tileset.png',sx,sy,8,8,(.5,.5))
            soil.append(dict(sprite=name,x=bx*2+qx-64,y=47-by*2-qy))
    for yy in range(4):
        for xx in range(4):
            stage = 'mature' if index in (0,1,4,5) else ('growing' if index==3 else 'sprout')
            if index==2 and xx==3 and yy>1:
                continue
            obj(f'crop_{crop_types[index]}_{stage}',(bx+xx)*16,(by+yy)*16-16,
                f'Garden/Bed {index+1} - {crop_types[index]}')
    obj('sign_blank',bx*16+24,(by+4)*16,'Garden/Bed labels')

for name,x,y in [('well_orange_roof',400,472),('barrel_water',432,478),
                  ('bucket',452,496),('crate_open',408,630),('crate_closed',428,624),
                  ('barrel_plain',144,628)]:
    obj(name,x,y,'Garden/Supplies')
box('Well',408,488,17,15,'Collisions/Props')

# A quiet yard at the cottage; facade planters belong to its building assembly.
for name,x,y in [('mailbox_red',338,274),('bench_wood',164,291),('crate_closed',189,265),
                  ('barrel_plain',178,267),('sign_arrow_right',342,326),
                  ('flower_box_pink',161,328),('flower_box_pink',180,328)]:
    obj(name,x,y,'Cottage/Yard')

# A tiny seed shop beside the crossroads. Stock remains set dressing.
register('seed_bag_carrot','Assets/Tileset/Crops_Tileset.png',16,16,16,16)
register('seed_bag_cabbage','Assets/Tileset/Crops_Tileset.png',32,16,16,16)
for name,x,y in [('crate_open',440,326),('crate_open',456,326),
                  ('seed_bag_carrot',440,335),('seed_bag_cabbage',456,335),
                  ('mailbox_blue',366,323),('sign_blank',448,362),
                  ('barrel_plain',370,309)]:
    obj(name,x,y,'Seed Shop/Display')
box('Seed display crates',442,343,28,13,'Collisions/Props')
obj('crate_closed',837,420,'Storage Shed/Supplies')

# Storage and feeding props make the enclosure legible without placing animals.
register('trough', 'Assets/Tileset/Barn_Tileset.png',368,32,32,16)
register('hay_bale', 'Assets/Tileset/Barn_Tileset.png',432,32,16,16)
obj('hay_bale',842,412,'Storage Shed/Supplies')
for name,x,y in [('trough',816,304),('trough',856,304),('hay_bale',889,306),
                  ('hay_bale',904,314),('crate_closed',628,253),('crate_open',643,262),
                  ('barrel_plain',779,246),('barrel_water',797,251),
                  ('notice_board',625,284),('bucket',872,335)]:
    obj(name,x,y,'Barnyard/Supplies')
for i in range(25):
    obj(rng.choice(['grass_tuft','grass_sparse','clover_patch']),
        rng.randrange(800,911),rng.randrange(337,428),'Barnyard/Grass')

# Dock crosses the bank, with rocks/reeds/lilies following the water silhouette.
for py in (512,528,544):
    obj('dock_vertical_panel',640,py,'Pond/Jetty',order=300)
obj('dock_horizontal_panel',656,544,'Pond/Jetty',order=301)
obj('bench_wood',603,480,'Pond/Rest area')
obj('sign_arrow_left',622,510,'Pond/Rest area')
for name,x,y in [('lily_pink',711,535),('lily_plain',734,549),('lily_white',816,582),
                  ('lily_plain',831,568),('lily_pink',762,620),('lily_plain',785,621)]:
    obj(name,x,y,'Pond/Lilies',order=180)
for name,x,y in [('reeds_brown',674,488),('reeds_green',690,485),('reeds_brown',854,510),
                  ('reeds_green',875,541),('reeds_brown',856,632),('reeds_green',839,651),
                  ('reeds_young',693,633),('reeds_brown',677,617),
                  ('rocks_blue_large',872,582),('rock_blue_medium',870,616),
                  ('rock_blue_small',889,622),('rocks_blue_large',688,452)]:
    obj(name,x,y,'Pond/Bank')

# Water collision is merged per row; the jetty remains available for later walking.
for y in range(H):
    row = [x for x in range(W) if (x,y) in pond and not (40<=x<=42 and 32<=y<=35)]
    runs=[]
    for x in row:
        if runs and x==runs[-1][-1]+1: runs[-1].append(x)
        else: runs.append([x])
    for run in runs:
        box('Water',run[0]*16,y*16,len(run)*16,16,'Collisions/Pond')

# Small resting place at the crossroads, off the walking route.
obj('bench_wood',556,407,'Common/Yard')
obj('notice_board',549,357,'Common/Yard')
obj('flower_box_blue',548,434,'Common/Yard')
obj('flower_box_blue',578,434,'Common/Yard')
obj('sign_arrow_left',456,335,'Common/Wayfinding')
obj('sign_arrow_right',535,323,'Common/Wayfinding')

tree_footprints=[]


def tree(name, px, py, group='Nature/Woodland'):
    obj(name,px,py,group)
    s=sprites[name]
    fx,fy=px+s['w']/2,py+s['h']-5
    tree_footprints.append((fx,fy))
    box('Tree trunk',fx-4,fy-4,8,7,'Collisions/Trees')


# Overlapping canopy masses on the perimeter, with openings at both entrances.
for row in range(3):
    for px in range(-12, PW+10, 33):
        if 456<px<554: continue
        tree(rng.choice(['tree_round','tree_canopy_cluster','tree_pine']),
             px+rng.randrange(-7,8),row*29+rng.randrange(-14,7))
for side in [0,1]:
    for py in range(89, PH-44, 30):
        for col in range(2):
            px=(col*33-12) if not side else (PW-55+col*29)
            tree(rng.choice(['tree_round','tree_canopy_cluster','tree_pine_cluster']),
                 px+rng.randrange(-9,9),py+rng.randrange(-7,8))
for px in range(45,PW-60,34):
    if 441<px<560: continue
    tree(rng.choice(['tree_round','tree_pine','tree_canopy_cluster']),
         px+rng.randrange(-8,8),PH-rng.randrange(40,67))

# Deliberate clusters soften the central clearing without blocking its paths.
for name,x,y in [('tree_round',111,164),('tree_canopy_cluster',136,118),
                  ('tree_round',360,144),('tree_pine',380,181),
                  ('tree_round',566,165),('tree_pine_cluster',576,119),
                  ('tree_round',821,125),('tree_pine',858,174),
                  ('tree_canopy_cluster',76,450),('tree_round',96,491),
                  ('tree_pine',130,443),('tree_round',864,449),
                  ('tree_canopy_cluster',901,478),('tree_round',598,638),
                  ('tree_pine',571,683),('tree_round',425,650),
                  ('tree_pine',398,692)]:
    tree(name,x,y)
for px,py in [(116,343),(170,359),(116,405),(179,414)]:
    tree('tree_round',px,py,'Nature/Small orchard')
for name,x,y in [('fallen_log',112,225),('stump',138,206),('mushrooms',125,255),
                  ('rocks_sand_large',568,205),('rock_blue_medium',597,225),
                  ('fallen_log_moss',876,679),('stump',901,654),
                  ('rocks_blue_large',341,681),('rock_blue_medium',378,697)]:
    obj(name,x,y,'Nature/Landmarks')

# Short hedges and sparse ground details are deterministic and avoid circulation.
for x in range(220,330,16):
    obj(rng.choice(['bush_round','bush_dense']),x,145,'Cottage/Hedge')
for x in range(638,775,17):
    obj('bush_dense',x,137,'Barnyard/Hedge')
for px,py in [(93,313),(362,291),(385,285),(579,314),(588,340),(599,581),
              (910,597),(613,661),(316,702),(891,218),(871,243),(432,191)]:
    obj(rng.choice(['bush_round','bush_dense','bush_leafy']),px,py,'Nature/Shrubs')

reserved=[(198,159,353,314),(618,153,806,314),(144,462,464,675),
          (778,282,937,457),(535,349,597,449),(376,276,478,372)]
for i in range(650):
    px,py=rng.randrange(70,PW-70),rng.randrange(104,PH-55)
    cx,cy=(px+8)//16,(py+8)//16
    if (cx,cy) in road or any((cx+dx,cy+dy) in pond for dx,dy in [(0,0),(1,0),(-1,0),(0,1),(0,-1)]): continue
    if any(a<=px<=c and b<=py<=d for a,b,c,d in reserved): continue
    name=rng.choices(['grass_tuft','grass_sparse','clover_patch','flowers_white','flowers_yellow','flowers_purple','pebble_pair'],[25,30,12,9,8,4,4])[0]
    obj(name,px,py,'Nature/Ground cover',order=50)

for px,py in [(194,316),(212,328),(331,318),(350,309),(154,693),(174,685),
              (603,455),(584,457),(859,466),(845,452)]:
    obj('flowers_pink',px,py,'Nature/Flower clusters',order=51)

# South gate posts and a small welcome sign: an obvious future arrival point.
for x0,x1 in [(64,464),(544,952)]:
    for px in range(x0,x1,24):
        obj('fence_rail_horizontal',px,709,'Entrance/Fence')
        box('Entrance fence',px+3,718,24,4,'Collisions/Fences')
obj('notice_board',546,678,'Entrance/Sign')
obj('flower_box_yellow',454,710,'Entrance/Flowers')
obj('flower_box_yellow',541,710,'Entrance/Flowers')
for name,x,y,w,h in [('West map edge',-16,0,16,PH),('East map edge',PW,0,16,PH),
                      ('North map edge',0,-16,PW,16),('South map edge',0,PH,PW,16)]:
    box(name,x,y,w,h,'Collisions/World bounds')

plan=dict(width=W,height=H,sprites=list(sprites.values()),tileLayers=layers,
          groups=groups,objects=objects,colliders=colliders,markers=markers,
          camera=dict(x=0,y=0,size=24))
normalize(plan)
design=ROOT/'Assets/Farm/Design/farm-layout.json'
design.parent.mkdir(parents=True,exist_ok=True)
design.write_text(json.dumps(plan,ensure_ascii=False,indent=2),encoding='utf-8')

# Pixel-exact authoring preview from the same atlas rectangles/positions.
images={s['atlas']:Image.open(ROOT/s['atlas']).convert('RGBA') for s in sprites.values()}
canvas=Image.new('RGBA',(PW,PH))
def paste(name,px,py,flip=False):
    s=sprites[name]
    tile=images[s['atlas']].crop((s['x'],s['y'],s['x']+s['w'],s['y']+s['h']))
    if flip: tile=tile.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    canvas.alpha_composite(tile,(round(px),round(py)))
for lay in sorted(layers,key=lambda l:l['order']):
    for t in lay['tiles']:
        unit=lay['cellSize']
        px=(t['x']*unit+W/2)*16
        py=(H/2-(t['y']+1)*unit)*16
        paste(t['sprite'],px,py)
sort_groups={g['name']:g['sortingOrder'] for g in groups}
for ob in sorted(objects,key=lambda o:(sort_groups.get(o['group'],o['order']),o['order'])):
    s=sprites[ob['sprite']]
    paste(ob['sprite'],(ob['x']+W/2)*16,(H/2-ob['y'])*16-s['h'],ob['flipX'])
art=ROOT/'BuildArtifacts'
art.mkdir(exist_ok=True)
canvas.save(art/'FarmLevel-Layout.png')
canvas.resize((PW*2,PH*2),Image.Resampling.NEAREST).save(art/'FarmLevel-Layout-2x.png')
print(json.dumps(dict(sprites=len(sprites),objects=len(objects),colliders=len(colliders),
                      tiles=sum(len(l['tiles']) for l in layers))))
