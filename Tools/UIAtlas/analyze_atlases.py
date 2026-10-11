from pathlib import Path
from PIL import Image, ImageDraw
import numpy as np
import json, hashlib

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Tools/UIAtlas/Verification'
OUT.mkdir(parents=True, exist_ok=True)
for name in ['UiAtlas', 'UiAtlas1']:
    p = ROOT / f'Assets/Sprites/{name}.png'
    im = Image.open(p).convert('RGBA')
    a = np.array(im)[:,:,3]
    # Connected opaque cores for analysis only, never used as an automatic slicer.
    seen = np.zeros(a.shape, bool)
    mask = a >= 128
    components=[]
    for y,x in zip(*np.where(mask)):
        if seen[y,x]: continue
        stack=[(int(y),int(x))]; seen[y,x]=True
        x0=x1=int(x); y0=y1=int(y); n=0
        while stack:
            yy,xx=stack.pop(); n+=1
            x0=min(x0,xx); x1=max(x1,xx); y0=min(y0,yy); y1=max(y1,yy)
            for dy,dx in [(0,1),(0,-1),(1,0),(-1,0)]:
                ny,nx=yy+dy,xx+dx
                if 0<=ny<a.shape[0] and 0<=nx<a.shape[1] and mask[ny,nx] and not seen[ny,nx]:
                    seen[ny,nx]=True; stack.append((ny,nx))
        if n>300: components.append([x0,y0,x1+1,y1+1,n])
    print(name, sorted(components,key=lambda c:(c[1],c[0])))
    bg=Image.new('RGBA',im.size,(32,38,48,255)); bg.alpha_composite(im)
    d=ImageDraw.Draw(bg)
    for x in range(0,im.width,100):
        d.line((x,0,x,im.height),fill=(255,255,255,110)); d.text((x+2,2),str(x),fill='white')
    for y in range(0,im.height,100):
        d.line((0,y,im.width,y),fill=(255,255,255,110)); d.text((2,y+2),str(y),fill='white')
    bg.convert('RGB').save(OUT/f'{name}_grid.jpg',quality=95)
    (OUT/f'{name}_components.json').write_text(json.dumps(components))

baseline={}
for p in [ROOT/'Assets/Sprites/UiAtlas.png',ROOT/'Assets/Sprites/UiAtlas1.png',*ROOT.glob('Assets/**/*.unity'),*ROOT.glob('Assets/**/*.prefab')]:
    baseline[str(p.relative_to(ROOT)).replace('\\','/')]=hashlib.sha256(p.read_bytes()).hexdigest()
if not (OUT/'baseline.json').exists():
    (OUT/'baseline.json').write_text(json.dumps(baseline,indent=2))
for name in ['UiAtlas','UiAtlas1']:
    p=ROOT/f'Assets/Sprites/{name}.png.meta'
    backup=OUT/f'{name}.original.meta.txt'
    if not backup.exists(): backup.write_bytes(p.read_bytes())
