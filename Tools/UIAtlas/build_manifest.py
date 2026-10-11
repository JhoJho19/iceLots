from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import json
import numpy as np

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Tools/UIAtlas/Verification'
DEST=ROOT/'Assets/Editor/UIAtlasSlicing'
DEST.mkdir(parents=True,exist_ok=True)
colors=['Blue','Green','Amber','Red','Gray']
atlases=[]

def add_group(entries,names,cores,area,borders=None):
    cores=sorted(cores,key=lambda c:c[0])
    assert len(names)==len(cores),(names,cores)
    left,top,right,bottom=area
    edges=[left]+[(a[2]+b[0])//2 for a,b in zip(cores,cores[1:])]+[right]
    for i,(name,c) in enumerate(zip(names,cores)):
        entries.append(dict(name=name,x=edges[i],top=top,width=edges[i+1]-edges[i],height=bottom-top,
            border=list(borders or [0,0,0,0]),core=c[:4]))

cs=json.loads((OUT/'UiAtlas_components.json').read_text())
e=[]
def row(y0,y1):return [c for c in cs if y0<=c[1]<y1]
add_group(e,[f'Button_Rect_{c}' for c in colors],row(0,140),(0,0,1448,141),[108,40,112,55])
add_group(e,[f'Button_Pill_{c}' for c in colors],row(140,255),(0,141,1448,255),[100,32,100,35])
add_group(e,[f'Button_Round_{c}' for c in colors]+[f'Button_Square_{c}' for c in colors]+[f'Button_Hex_{c}' for c in colors],row(255,375),(0,255,1448,377))
for s in e:
    if s['name'].startswith('Button_Square_'):s['border']=[35,28,35,30]
add_group(e,[f'Button_Play_Round_{c}' for c in colors]+['Button_Left_Round_Blue','Button_Right_Round_Blue','Button_Up_Round_Blue','Button_Down_Round_Blue','Button_Left_Diamond_Blue','Button_Right_Diamond_Blue'],row(375,515),(0,377,1357,514))
plus=row(515,615)
merged=next(c for c in plus if c[0]==172)
plus.remove(merged)
plus.extend([[172,522,248,604,0],[251,522,325,604,0]])
add_group(e,[f'Button_Plus_Round_{c}' for c in colors]+[f'Button_Minus_Round_{c}' for c in colors]+[f'Button_Check_Round_{c}' for c in colors]+['Button_Close_Round_Blue','Button_Close_Round_Red','Button_Close_Round_Gray'],plus,(0,514,1448,615))
add_group(e,[f'Toggle_Off_{c}' for c in colors]+['Indicator_Green','Indicator_Blue','Indicator_Amber','Indicator_Red','Indicator_Gray'],row(615,710),(0,615,1448,707))
add_group(e,[f'Tabs_Three_{c}' for c in colors],row(710,803),(0,707,1448,803))
bars=[c for c in row(803,891) if c[0]<990]
flags=[c for c in row(803,891) if c[0]>=990]
add_group(e,[f'Panel_Bar_{c}' for c in colors[:3]],bars,(0,803,992,891),[110,28,105,34])
add_group(e,[f'Ribbon_{c}' for c in colors],flags,(992,803,1448,977))
add_group(e,[f'Panel_Bar_{c}' for c in colors[3:]],row(891,977),(0,891,789,977),[120,28,120,34])
bottom=row(977,1086)
add_group(e,[f'Button_Arrow_{c}' for c in colors],[c for c in bottom if c[0]<740],(0,977,744,1086))
add_group(e,['Arrow_Left_Ice','Arrow_Right_Ice','Arrow_Up_Ice','Arrow_Down_Ice'],[c for c in bottom if 740<=c[0]<1010],(744,977,1016,1086))
add_group(e,['Progress_Star_Blue'],[c for c in bottom if c[0]>=1010],(1016,977,1448,1086))
atlases.append(dict(path='Assets/Sprites/UiAtlas.png',width=1448,height=1086,sprites=e))

cs=json.loads((OUT/'UiAtlas1_components.json').read_text())
e=[]
add_group(e,['Button_Play_Green','Button_Spin_Blue'],[c for c in cs if c[1]<170 and c[0]<680],(15,32,676,189))
add_group(e,['Button_Settings_Blue','Button_Sound_Blue','Button_Music_Blue','Button_Info_Blue','Button_Close_Blue'],[c for c in cs if c[1]<170 and c[0]>=680],(676,25,1420,169))
add_group(e,['Button_Buy_Amber','Button_Collect_Blue'],[c for c in cs if 170<=c[1]<316 and c[0]<680],(15,189,677,319))
add_group(e,['Button_Home_Wood','Button_Shop_Wood','Button_Gift_Wood','Button_Trophy_Wood','Button_Menu_Wood'],[c for c in cs if 170<=c[1]<316 and c[0]>=680],(682,169,1415,309))
add_group(e,['Button_Info_Ice','Button_Locked_Gray'],[c for c in cs if 316<=c[1]<440 and c[0]<680],(20,319,679,450))
add_group(e,['Toggle_On_Green','Toggle_Off_Blue_Compact'],[c for c in cs if 316<=c[1]<390 and 680<=c[0]<980],(682,309,979,394))
add_group(e,['Button_Play_Green_Compact','Button_Play_Amber_Compact','Button_Left_Blue','Button_Right_Blue'],[c for c in cs if 309<=c[1]<420 and c[0]>=980],(979,309,1423,420))
add_group(e,['Button_Plus_Green','Button_Minus_Blue'],[c for c in cs if 390<=c[1]<420],(687,394,923,495))
add_group(e,['Progress_Runner_Blue'],[c for c in cs if 420<=c[1]<465],(934,420,1415,497))
add_group(e,['Window_Congratulations'],[c for c in cs if 465<=c[1]<490],(16,460,641,810))
add_group(e,['Window_Ice_Close'],[c for c in cs if 490<=c[1]<550 and c[0]<1160],(647,497,1156,810))
e[-1]['border']=[135,90,145,80]
add_group(e,['Panel_Ice_Vertical'],[c for c in cs if 490<=c[1]<550 and c[0]>=1160],(1157,497,1440,860),[100,85,100,68])
add_group(e,['Panel_Landscape_Ice'],[c for c in cs if 810<=c[1]<840 and c[0]<700],(17,810,683,1057))
add_group(e,['Panel_Ice_Horizontal'],[c for c in cs if 810<=c[1]<840 and 700<=c[0]<1100],(695,810,1098,925),[100,34,130,47])
add_group(e,['Panel_Wood_Snow'],[c for c in cs if 925<=c[1]<950],(694,925,1101,1054))
add_group(e,['Counter_Coins'],[c for c in cs if 850<=c[1]<925],(1103,860,1435,950))
add_group(e,['Counter_Gems'],[c for c in cs if 950<=c[1]<1060],(1103,950,1435,1054))
atlases.append(dict(path='Assets/Sprites/UiAtlas1.png',width=1448,height=1086,sprites=e))

for atlas in atlases:
    entries=atlas['sprites']
    im=Image.open(ROOT/atlas['path']).convert('RGBA')
    alpha=np.array(im)[:,:,3]
    for i,s in enumerate(entries):
        x,y,w,h=s['x'],s['top'],s['width'],s['height']
        c=s.pop('core')
        assert x<=c[0] and y<=c[1] and x+w>=c[2] and y+h>=c[3],(s,c)
        assert s['border'][0]+s['border'][2]<w and s['border'][1]+s['border'][3]<h,s
        for t in entries[:i]:
            assert not (x<t['x']+t['width'] and x+w>t['x'] and y<t['top']+t['height'] and y+h>t['top']),(s,t)
    # Review images are contact sheets, not replacement sprite assets.
    overlay=Image.new('RGBA',im.size,(38,42,50,255)); overlay.alpha_composite(im)
    d=ImageDraw.Draw(overlay)
    for i,s in enumerate(entries):
        x,y,w,h=s['x'],s['top'],s['width'],s['height']
        d.rectangle((x,y,x+w-1,y+h-1),outline=(255,215,30,255),width=1)
        d.text((x+3,y+3),str(i+1),fill='white',stroke_width=1,stroke_fill='black')
    name=Path(atlas['path']).stem
    overlay.convert('RGB').save(OUT/f'{name}_rects.jpg',quality=96)
    for page in range((len(entries)+11)//12):
        sheet=Image.new('RGB',(1400,1000),(43,48,58)); d=ImageDraw.Draw(sheet)
        for j,s in enumerate(entries[page*12:page*12+12]):
            crop=im.crop((s['x'],s['top'],s['x']+s['width'],s['top']+s['height']))
            crop.thumbnail((330,215))
            x=(j%4)*350+10;y=(j//4)*330+45
            for yy in range(y,y+230,16):
                for xx in range(x,x+330,16):
                    color=(65,72,84) if ((xx-x)//16+(yy-y)//16)%2 else (105,112,124)
                    d.rectangle((xx,yy,xx+15,yy+15),fill=color)
            sheet.paste(crop,(x+(330-crop.width)//2,y+(230-crop.height)//2),crop)
            d.text((x,y+240),s['name'],fill='white')
        sheet.save(OUT/f'{name}_sprites_{page+1:02}.jpg',quality=96)
    print(name,len(entries))
(DEST/'AtlasRects.json').write_text(json.dumps(dict(atlases=atlases),indent=2),encoding='utf-8')
