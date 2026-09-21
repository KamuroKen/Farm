"""Native-scale building assemblies using the pack's directional roof modules.

Every part is an unscaled, unmodified source rectangle. Coordinates are top-left.
The roof's rear ridge, slopes, front gable and facade remain distinct pieces.
"""
from pathlib import Path
import json
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
HOUSE = 'House_Tileset.png'
BARN = 'Barn_Tileset.png'
EXTERIOR = 'Exterior_Tileset.png'
PALETTES = {'red':(0,0),'orange':(272,0),'purple':(544,0),
            'green':(0,144),'blue':(272,144)}


class Building:
    def __init__(self,w,h):
        self.width,self.height=w,h
        self.parts=[]
        self.footprints=[]

    def part(self,label,atlas,rect,pos):
        x,y,w,h=rect
        dx,dy=pos
        self.parts.append(dict(name=label,atlas=atlas,x=x,y=y,w=w,h=h,dx=dx,dy=dy))

    def fill(self,label,atlas,rect,bounds):
        x,y,w,h=bounds
        sx,sy,sw,sh=rect
        for yy in range(y,y+h,sh):
            for xx in range(x,x+w,sw):
                self.part(label,atlas,(sx,sy,min(sw,x+w-xx),min(sh,y+h-yy)),(xx,yy))

    def export(self):
        return dict(width=self.width,height=self.height,parts=self.parts,footprints=self.footprints)


def wall(b,x,y,w,h,kind='wood'):
    if kind=='red':
        atlas=BARN
        field=(704,32,16,16)
        left,right=(696,80,4,16),(724,80,4,16)
    elif kind=='cream':
        atlas=BARN
        field=(536,32,16,16)
        left,right=(528,80,4,16),(556,80,4,16)
    else:
        atlas=HOUSE
        field=(1008,32,16,16)
        left,right=(1000,48,4,16),(1028,48,4,16)
    b.fill('Facade siding',atlas,field,(x+4,y,w-8,h))
    b.fill('Left facade post',atlas,left,(x+2,y,4,h))
    b.fill('Right facade post',atlas,right,(x+w-6,y,4,h))


def gable(b,x,y,w,depth,color,kind='wood'):
    """Pitch rises one pixel per two horizontally; roof depth is independent."""
    if kind=='red' or kind=='cream':
        atlas=BARN
        if kind=='red':
            left,right,roof_y,front_y=224,240,176,224
        else:
            left,right,roof_y,front_y=304,320,16,64
        pale=(708,32,4,4) if kind=='red' else (540,32,4,4)
    else:
        atlas=HOUSE
        ox,oy=PALETTES[color]
        left,right,roof_y,front_y=240+ox,256+ox,16+oy,48+oy
        pale=(24,24,4,4)
    edge=depth+w//4
    wall(b,x,y+edge+4,w,24,kind)
    b.fill('Gable field',atlas,pale,(x+4,y+depth-2,w-8,w//4+6))
    for side in (0,1):
        for i in range(w//16):
            # Eight-pixel strips retain both phases of the original shingles.
            sx=(left if side==0 else right)+(i%2)*8
            if side==0:
                dx=x+i*8
                shift=(w//16-i)*4-(8 if i%2==0 else 4)
            else:
                dx=x+w//2+i*8
                shift=i*4-(0 if i%2==0 else 4)
            dy=y+shift
            b.part('Rear roof edge',atlas,(sx,roof_y,8,16),(dx,dy))
            for length in range(16,depth,8):
                body_y=48 if kind=='cream' else roof_y+16
                b.part('Pitched roof slope',atlas,(sx,body_y,8,min(8,depth-length)),(dx,dy+length))
            front_height=min(16,edge+4-shift-depth)
            b.part('Front eave and gable',atlas,(sx,front_y,8,front_height),(dx,dy+depth))
    return edge


def flat_roof(b,x,y,w,color):
    ox,oy=PALETTES[color]
    b.fill('Roof front shingles',HOUSE,(48+ox,80+oy,16,32),(x+8,y,w-16,32))
    b.part('Roof left frame',HOUSE,(24+ox,80+oy,8,32),(x,y))
    b.part('Roof right frame',HOUSE,(32+ox,80+oy,8,32),(x+w-8,y))
    b.fill('Roof ridge highlight',HOUSE,(28+ox,80+oy,4,2),(x+8,y,w-16,2))
    b.fill('Roof front fascia',HOUSE,(48+ox,108+oy,16,4),(x+8,y+28,w-16,4))


def window(b,x,y,kind='glass'):
    rect={'glass':(832,48,16,16),'shutters':(880,80,16,16),
          'round':(848,16,16,16),'loft':(832,16,16,16)}[kind]
    b.part('Window '+kind,HOUSE,rect,(x,y))


def entrance(b,x,y,color='orange',stable=False):
    if stable:
        b.part('Stable door upper panel',HOUSE,(960,16,16,8),(x,y))
        b.part('Stable door lower panel',BARN,(368,128,16,16),(x,y+8))
        return
    doorx={'orange':928,'green':944,'blue':960,'red':912,'purple':912}[color]
    doory=96 if color=='purple' else 64
    b.part('Front door',HOUSE,(doorx,doory,16,16),(x,y))
    awningy={'orange':176,'green':192,'blue':208,'red':160,'purple':224}[color]
    b.part('Door awning',HOUSE,(864,awningy,16,8),(x,y-5))


def planter(b,x,y,color='yellow'):
    sx=160 if color=='yellow' else 112
    b.part('Flower box',EXTERIOR,(sx,160,16,16),(x,y))


cottage=Building(96,88)
wall(cottage,0,28,96,36)
flat_roof(cottage,0,0,96,'orange')
window(cottage,8,39)
window(cottage,72,39)
planter(cottage,8,52)
planter(cottage,72,52)
cottage.part('Roof dormer left',HOUSE,(528,80,16,16),(12,16))
cottage.part('Roof dormer right',HOUSE,(528,80,16,16),(68,16))
front=gable(cottage,24,24,48,16,'orange')
window(cottage,40,24+front-9,'loft')
entrance(cottage,40,24+front+8,'orange')
cottage.part('Porch threshold',HOUSE,(1008,76,16,4),(40,80))
cottage.footprints=[dict(x=4,y=38,w=88,h=26),dict(x=28,y=62,w=40,h=18)]

barn=Building(64,92)
edge=gable(barn,0,0,64,48,'red','red')
window(barn,24,edge-10,'loft')
entrance(barn,24,edge+1,stable=True)
barn.footprints=[dict(x=4,y=52,w=56,h=36)]

shop=Building(64,76)
edge=gable(shop,0,0,64,24,'green')
window(shop,24,edge-10,'round')
entrance(shop,24,edge+8,'green')
window(shop,6,edge+7,'shutters')
window(shop,42,edge+7,'shutters')
shop.part('Porch threshold',HOUSE,(1008,76,16,4),(24,68))
shop.footprints=[dict(x=4,y=34,w=56,h=30)]

shed=Building(48,80)
edge=gable(shed,0,0,48,40,'purple','cream')
window(shed,16,edge-8,'round')
entrance(shed,16,edge+8,'orange')
planter(shed,2,edge+10)
planter(shed,30,edge+10)
shed.footprints=[dict(x=4,y=44,w=40,h=32)]

buildings={n:b.export() for n,b in [('cottage',cottage),('barn',barn),('shop',shop),('shed',shed)]}
images={f:Image.open(ROOT/'Assets/Tileset'/f).convert('RGBA') for f in (HOUSE,BARN,EXTERIOR)}
previews=[]
for name,b in buildings.items():
    im=Image.new('RGBA',(b['width'],b['height']))
    for p in b['parts']:
        atlas=images[p['atlas']]
        assert 0<=p['x']<p['x']+p['w']<=atlas.width
        assert 0<=p['y']<p['y']+p['h']<=atlas.height
        im.alpha_composite(atlas.crop((p['x'],p['y'],p['x']+p['w'],p['y']+p['h'])),(p['dx'],p['dy']))
    im.save(ROOT/f'BuildArtifacts/{name}-v2.png')
    previews.append((name,im))
sheet=Image.new('RGBA',(352*4,116*4),(175,194,89,255))
draw=ImageDraw.Draw(sheet)
xx=8
for name,im in previews:
    sheet.alpha_composite(im.resize((im.width*4,im.height*4),Image.Resampling.NEAREST),(xx*4,12*4))
    draw.text((xx*4,4),name,fill=(40,50,35))
    xx+=im.width+16
sheet.save(ROOT/'BuildArtifacts/Buildings-v2-preview.png')
(ROOT/'Tools/Farm/building-parts.json').write_text(json.dumps(buildings,indent=2),encoding='utf-8')
print({n:len(b['parts']) for n,b in buildings.items()})
