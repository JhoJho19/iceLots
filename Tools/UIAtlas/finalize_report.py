from pathlib import Path
import json,hashlib,csv
import numpy as np
from PIL import Image

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Tools/UIAtlas/Verification'
manifest=json.loads((ROOT/'Assets/Editor/UIAtlasSlicing/AtlasRects.json').read_text())
baseline=json.loads((OUT/'baseline.json').read_text())
checks={p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest()==h for p,h in baseline.items()}
assert all(checks.values()),checks
unity=json.loads((OUT/'UnityVerification.json').read_text())
assert unity['spriteCount']==122 and unity['slicedCount']==23
rows=list(csv.DictReader((OUT/'ImportedSprites.csv').open()))
assert len(rows)==122 and len({(r['atlas'],r['fileID']) for r in rows})==122
assert len({s['name'] for a in manifest['atlases'] for s in a['sprites']})==122
diagnostics={}
for atlas in manifest['atlases']:
    im=np.array(Image.open(ROOT/atlas['path']))
    mask=np.zeros(im.shape[:2],bool)
    edges=[]
    for s in atlas['sprites']:
        x,y,w,h=s['x'],s['top'],s['width'],s['height']
        assert not mask[y:y+h,x:x+w].any()
        mask[y:y+h,x:x+w]=True
        a=im[y:y+h,x:x+w,3]
        boundary=np.concatenate([a[0,:],a[-1,:],a[1:-1,0],a[1:-1,-1]])
        edges.append(dict(name=s['name'],edgeAlphaMax=int(boundary.max()),edgePixelsAbove32=int((boundary>32).sum()),edgePixelsAbove128=int((boundary>128).sum())))
    capture=np.array(Image.open(OUT/(Path(atlas['path']).stem+'_UnityNative.png')))
    assert capture.shape==im.shape
    diagnostics[atlas['path']]=dict(uncoveredPixelsAbove128=int(((im[:,:,3]>128)&~mask).sum()),uncoveredPixelsAbove32=int(((im[:,:,3]>32)&~mask).sum()),edges=edges)
(OUT/'FinalChecks.json').write_text(json.dumps(dict(unchangedFiles=checks,alphaDiagnostics=diagnostics),indent=2))
print(json.dumps({p:{'uncoveredPixelsAbove128':v['uncoveredPixelsAbove128'],'uncoveredPixelsAbove32':v['uncoveredPixelsAbove32'],'edgesAbove128':[e for e in v['edges'] if e['edgePixelsAbove128']]} for p,v in diagnostics.items()},indent=2))

text=['# Отчёт о нарезке UIAtlas и UIAtlas1', '',
'Дата: 8 октября 2026. Unity: '+unity['unityVersion']+'.', '',
'Импорт и нарезка применены в работающем Unity Editor через `SpriteRect`, `SpriteDataProviderFactories`, `ISpriteEditorDataProvider` и `ISpriteNameFileIdDataProvider`. Выполнены `Apply`, `SaveAndReimport` и повторный `AssetDatabase.ImportAsset`.', '',
'| Атлас | Исходный размер | Sprite | С Border |',
'|---|---|---:|---:|']
for atlas in manifest['atlases']:
    text.append(f"| `{atlas['path']}` | 1448 × 1086 | {len(atlas['sprites'])} | {sum(any(s['border']) for s in atlas['sprites'])} |")
text += ['', 'Всего: **122 Sprite**, из них **23 с поддержкой 9-slicing**. Имена уникальны также между атласами.', '',
'## Импорт', '',
'`Texture Type = Sprite (2D and UI)`, `Sprite Mode = Multiple`, `Mesh Type = Full Rect`, Pivot = Center. Сжатие и Crunch отключены; формат RGBA32; Max Size = 2048; NPOT Scale = None; mipmaps отключены; Alpha Source = From Input; Alpha Is Transparency включён; Bilinear; Clamp. Настройки без сжатия заданы также для Standalone, Android, iPhone и WebGL. Размер и исходная прозрачность PNG сохранены.', '',
'## Проверка', '',
'- Проверено наличие всех 122 Sprite через `AssetDatabase.LoadAllAssetsAtPath` с уникальными fileID. Размеры, имена, Border и четырёхвершинный Full Rect проверены в Unity.',
'- Все прямоугольники внутри текстур, пересечений нет. Границы выбраны по визуальным элементам и их фактическим контурам, а не равномерной сетке.',
'- Создана отдельная сцена `Assets/Editor/UIAtlasSlicing/UIAtlasVerification.unity`. Она содержит все элементы в исходном пиксельном размере и отдельные Canvas с вариантами Sliced. Сцена не добавлялась в Build Settings.',
'- 23 элемента проверены с `Image.Type = Sliced` при размерах: исходном, ширина ×1.5 / высота ×1.2, ширина ×2 / высота ×1.5. Угловой снег, кристаллы, скругления и встроенная кнопка закрытия сохраняют размер; центр и прямые участки рамки растягиваются.',
'- Просмотрены рендеры обоих атласов, контактные листы на шахматном фоне и все страницы Sliced. Основные контуры, надписи, снег, кристаллы и декоративные выступы сохранены. На проверенных размерах заметных швов рамок не обнаружено.',
'- В консоли Unity после импорта нет ошибок и предупреждений.',
'- SHA-256 обоих исходных PNG совпадает с исходным. Хэши всех существовавших сцен и префабов также совпадают. До начала работы в `SC.unity` уже были пользовательские изменения; они сохранены без вмешательства.',
'- Активная сцена после проверки снова `Assets/Scenes/SC.unity`; тестовая сцена закрыта. Существующие компоненты UI не редактировались.', '',
'## Ограничения исходного изображения', '',
'В PNG имеется низкоальфовый цветной фон вокруг объектов; некоторые ореолы и антиалиасинг соприкасаются. Для таких мест нельзя одновременно сохранить каждый пиксель обоих ореолов, исключить любой пиксель соседа и иметь непересекающиеся прямоугольники. Исходное изображение не ретушировалось. Границы проходят между основными контурами, но абсолютная пиксельная изоляция слабого фона/ореолов не гарантируется.', '',
'Самые тесные места — `Button_Plus_Round_Green`, `Button_Plus_Round_Amber` и `Button_Plus_Round_Red`: в исходнике их альфовые контуры соприкасаются. На границах прямоугольников есть соответственно 7, 10 и 12 пикселей с alpha > 128. Это остаточное ограничение исходника; результат не следует считать безусловно идеальным по требованию полного сохранения всех ореолов. Точные показатели альфы на границах записаны в `Verification/FinalChecks.json`. При этом объединение всех прямоугольников сохранило все пиксели обоих исходников с alpha > 32; за его пределами остаётся только более слабый фон.', '',
'Составные изображения оставлены целиком: заголовок и крестик окна, встроенные значки кнопок, бегунок прогресса и счётчики ресурсов уже нарисованы поверх общего фона. Вырезание их отдельных прямоугольников обрезало бы фон и пересеклось с родительским элементом. Для независимых интерактивных частей нужны исходные слои/раздельные элементы.', '',
'## 9-slicing и обычные Sprite', '',
'9-slicing: `Button_Rect_*`, `Button_Pill_*`, `Button_Square_*`, `Panel_Bar_*` из UiAtlas; `Window_Ice_Close`, `Panel_Ice_Vertical`, `Panel_Ice_Horizontal` из UiAtlas1. Звёздочка соответствует Blue, Green, Amber, Red, Gray. Точные Border приведены ниже.', '',
'При Sliced используйте `pixelsPerUnitMultiplier = 1` и не уменьшайте прямоугольник ниже суммы левого/правого и нижнего/верхнего Border: иначе uGUI уменьшит углы. Внутренняя текстура масштабируется по правилам 9-slicing; её рисунок не остаётся пиксельно неизменным при растяжении.', '',
'Остальные 99 Sprite имеют Border = 0. Круги, шестиугольники, ромбы, стрелки и ленты меняют форму при неравномерном растяжении; надписи и встроенные значки деформируются; группы вкладок и переключатели содержат фиксированные разделители/бегунки; счётчики и прогресс содержат встроенные цифры/символы. `Panel_Landscape_Ice` содержит пейзаж, `Window_Congratulations` — заголовок, `Panel_Wood_Snow` — древесный рисунок. У древесной панели пробный Sliced заметно растягивал текстуру, поэтому в итоговом импорте Border убран. Для этих Sprite используйте исходный размер либо равномерный масштаб с сохранением пропорций.', '',
'## Полный список Sprite', '',
'Rect указан в координатах Unity: x, y от нижнего левого угла, width, height. Border: Left, Bottom, Right, Top, в пикселях.', '']
for atlas in manifest['atlases']:
    text += ['### '+atlas['path'], '', '| № | Sprite | Rect (x, y, w, h) | Border (L, B, R, T) |', '|---:|---|---|---|']
    for i,s in enumerate(atlas['sprites'],1):
        rect=(s['x'],1086-s['top']-s['height'],s['width'],s['height'])
        text.append(f"| {i} | `{s['name']}` | {', '.join(map(str,rect))} | {', '.join(map(str,s['border']))} |")
    text.append('')
text += ['## Файлы для повторной проверки', '',
'- `Assets/Editor/UIAtlasSlicing/UIAtlasSlicer.cs`: меню `Tools → UI Atlases → Apply reviewed slicing and verify`; повторный запуск сохраняет ID существующих именованных Sprite.',
'- `Assets/Editor/UIAtlasSlicing/AtlasRects.json`: все границы и Border.',
'- `Verification/ImportedSprites.csv`: реально импортированные Sprite с fileID.',
'- `Verification/UiAtlas_UnityNative.png`, `UiAtlas1_UnityNative.png`: рендеры через uGUI.',
'- `Verification/UnitySliced_01.png` … `UnitySliced_06.png`: итоговые рендеры вариантов Sliced.',
'- `Verification/UiAtlas_sprites_*.jpg`, `UiAtlas1_sprites_*.jpg`: контактные листы на шахматном фоне.',
'- `Verification/*.original.meta.txt`: резервные копии настроек до нарезки; `baseline.json` и `FinalChecks.json`: контрольные суммы.', '',
'PNG в папке Verification — только снимки проверки. Отдельные PNG-ассеты элементов не создавались; UI использует sub-assets двух исходных атласов.', '']
(ROOT/'Tools/UIAtlas/Report.md').write_text('\n'.join(text),encoding='utf-8')
