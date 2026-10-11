# Отчёт о нарезке UIAtlas и UIAtlas1

Дата: 8 октября 2026. Unity: 6000.3.10f1.

Импорт и нарезка применены в работающем Unity Editor через `SpriteRect`, `SpriteDataProviderFactories`, `ISpriteEditorDataProvider` и `ISpriteNameFileIdDataProvider`. Выполнены `Apply`, `SaveAndReimport` и повторный `AssetDatabase.ImportAsset`.

| Атлас | Исходный размер | Sprite | С Border |
|---|---|---:|---:|
| `Assets/Sprites/UiAtlas.png` | 1448 × 1086 | 89 | 20 |
| `Assets/Sprites/UiAtlas1.png` | 1448 × 1086 | 33 | 3 |

Всего: **122 Sprite**, из них **23 с поддержкой 9-slicing**. Имена уникальны также между атласами.

## Импорт

`Texture Type = Sprite (2D and UI)`, `Sprite Mode = Multiple`, `Mesh Type = Full Rect`, Pivot = Center. Сжатие и Crunch отключены; формат RGBA32; Max Size = 2048; NPOT Scale = None; mipmaps отключены; Alpha Source = From Input; Alpha Is Transparency включён; Bilinear; Clamp. Настройки без сжатия заданы также для Standalone, Android, iPhone и WebGL. Размер и исходная прозрачность PNG сохранены.

## Проверка

- Проверено наличие всех 122 Sprite через `AssetDatabase.LoadAllAssetsAtPath` с уникальными fileID. Размеры, имена, Border и четырёхвершинный Full Rect проверены в Unity.
- Все прямоугольники внутри текстур, пересечений нет. Границы выбраны по визуальным элементам и их фактическим контурам, а не равномерной сетке.
- Создана отдельная сцена `Assets/Editor/UIAtlasSlicing/UIAtlasVerification.unity`. Она содержит все элементы в исходном пиксельном размере и отдельные Canvas с вариантами Sliced. Сцена не добавлялась в Build Settings.
- 23 элемента проверены с `Image.Type = Sliced` при размерах: исходном, ширина ×1.5 / высота ×1.2, ширина ×2 / высота ×1.5. Угловой снег, кристаллы, скругления и встроенная кнопка закрытия сохраняют размер; центр и прямые участки рамки растягиваются.
- Просмотрены рендеры обоих атласов, контактные листы на шахматном фоне и все страницы Sliced. Основные контуры, надписи, снег, кристаллы и декоративные выступы сохранены. На проверенных размерах заметных швов рамок не обнаружено.
- В консоли Unity после импорта нет ошибок и предупреждений.
- SHA-256 обоих исходных PNG совпадает с исходным. Хэши всех существовавших сцен и префабов также совпадают. До начала работы в `SC.unity` уже были пользовательские изменения; они сохранены без вмешательства.
- Активная сцена после проверки снова `Assets/Scenes/SC.unity`; тестовая сцена закрыта. Существующие компоненты UI не редактировались.

## Ограничения исходного изображения

В PNG имеется низкоальфовый цветной фон вокруг объектов; некоторые ореолы и антиалиасинг соприкасаются. Для таких мест нельзя одновременно сохранить каждый пиксель обоих ореолов, исключить любой пиксель соседа и иметь непересекающиеся прямоугольники. Исходное изображение не ретушировалось. Границы проходят между основными контурами, но абсолютная пиксельная изоляция слабого фона/ореолов не гарантируется.

Самые тесные места — `Button_Plus_Round_Green`, `Button_Plus_Round_Amber` и `Button_Plus_Round_Red`: в исходнике их альфовые контуры соприкасаются. На границах прямоугольников есть соответственно 7, 10 и 12 пикселей с alpha > 128. Это остаточное ограничение исходника; результат не следует считать безусловно идеальным по требованию полного сохранения всех ореолов. Точные показатели альфы на границах записаны в `Verification/FinalChecks.json`. При этом объединение всех прямоугольников сохранило все пиксели обоих исходников с alpha > 32; за его пределами остаётся только более слабый фон.

Составные изображения оставлены целиком: заголовок и крестик окна, встроенные значки кнопок, бегунок прогресса и счётчики ресурсов уже нарисованы поверх общего фона. Вырезание их отдельных прямоугольников обрезало бы фон и пересеклось с родительским элементом. Для независимых интерактивных частей нужны исходные слои/раздельные элементы.

## 9-slicing и обычные Sprite

9-slicing: `Button_Rect_*`, `Button_Pill_*`, `Button_Square_*`, `Panel_Bar_*` из UiAtlas; `Window_Ice_Close`, `Panel_Ice_Vertical`, `Panel_Ice_Horizontal` из UiAtlas1. Звёздочка соответствует Blue, Green, Amber, Red, Gray. Точные Border приведены ниже.

При Sliced используйте `pixelsPerUnitMultiplier = 1` и не уменьшайте прямоугольник ниже суммы левого/правого и нижнего/верхнего Border: иначе uGUI уменьшит углы. Внутренняя текстура масштабируется по правилам 9-slicing; её рисунок не остаётся пиксельно неизменным при растяжении.

Остальные 99 Sprite имеют Border = 0. Круги, шестиугольники, ромбы, стрелки и ленты меняют форму при неравномерном растяжении; надписи и встроенные значки деформируются; группы вкладок и переключатели содержат фиксированные разделители/бегунки; счётчики и прогресс содержат встроенные цифры/символы. `Panel_Landscape_Ice` содержит пейзаж, `Window_Congratulations` — заголовок, `Panel_Wood_Snow` — древесный рисунок. У древесной панели пробный Sliced заметно растягивал текстуру, поэтому в итоговом импорте Border убран. Для этих Sprite используйте исходный размер либо равномерный масштаб с сохранением пропорций.

## Полный список Sprite

Rect указан в координатах Unity: x, y от нижнего левого угла, width, height. Border: Left, Bottom, Right, Top, в пикселях.

### Assets/Sprites/UiAtlas.png

| № | Sprite | Rect (x, y, w, h) | Border (L, B, R, T) |
|---:|---|---|---|
| 1 | `Button_Rect_Blue` | 0, 945, 297, 141 | 108, 40, 112, 55 |
| 2 | `Button_Rect_Green` | 297, 945, 283, 141 | 108, 40, 112, 55 |
| 3 | `Button_Rect_Amber` | 580, 945, 284, 141 | 108, 40, 112, 55 |
| 4 | `Button_Rect_Red` | 864, 945, 287, 141 | 108, 40, 112, 55 |
| 5 | `Button_Rect_Gray` | 1151, 945, 297, 141 | 108, 40, 112, 55 |
| 6 | `Button_Pill_Blue` | 0, 831, 301, 114 | 100, 32, 100, 35 |
| 7 | `Button_Pill_Green` | 301, 831, 288, 114 | 100, 32, 100, 35 |
| 8 | `Button_Pill_Amber` | 589, 831, 285, 114 | 100, 32, 100, 35 |
| 9 | `Button_Pill_Red` | 874, 831, 283, 114 | 100, 32, 100, 35 |
| 10 | `Button_Pill_Gray` | 1157, 831, 291, 114 | 100, 32, 100, 35 |
| 11 | `Button_Round_Blue` | 0, 709, 115, 122 | 0, 0, 0, 0 |
| 12 | `Button_Round_Green` | 115, 709, 98, 122 | 0, 0, 0, 0 |
| 13 | `Button_Round_Amber` | 213, 709, 96, 122 | 0, 0, 0, 0 |
| 14 | `Button_Round_Red` | 309, 709, 99, 122 | 0, 0, 0, 0 |
| 15 | `Button_Round_Gray` | 408, 709, 101, 122 | 0, 0, 0, 0 |
| 16 | `Button_Square_Blue` | 509, 709, 94, 122 | 35, 28, 35, 30 |
| 17 | `Button_Square_Green` | 603, 709, 92, 122 | 35, 28, 35, 30 |
| 18 | `Button_Square_Amber` | 695, 709, 90, 122 | 35, 28, 35, 30 |
| 19 | `Button_Square_Red` | 785, 709, 91, 122 | 35, 28, 35, 30 |
| 20 | `Button_Square_Gray` | 876, 709, 90, 122 | 35, 28, 35, 30 |
| 21 | `Button_Hex_Blue` | 966, 709, 97, 122 | 0, 0, 0, 0 |
| 22 | `Button_Hex_Green` | 1063, 709, 94, 122 | 0, 0, 0, 0 |
| 23 | `Button_Hex_Amber` | 1157, 709, 92, 122 | 0, 0, 0, 0 |
| 24 | `Button_Hex_Red` | 1249, 709, 92, 122 | 0, 0, 0, 0 |
| 25 | `Button_Hex_Gray` | 1341, 709, 107, 122 | 0, 0, 0, 0 |
| 26 | `Button_Play_Round_Blue` | 0, 572, 134, 137 | 0, 0, 0, 0 |
| 27 | `Button_Play_Round_Green` | 134, 572, 116, 137 | 0, 0, 0, 0 |
| 28 | `Button_Play_Round_Amber` | 250, 572, 115, 137 | 0, 0, 0, 0 |
| 29 | `Button_Play_Round_Red` | 365, 572, 119, 137 | 0, 0, 0, 0 |
| 30 | `Button_Play_Round_Gray` | 484, 572, 122, 137 | 0, 0, 0, 0 |
| 31 | `Button_Left_Round_Blue` | 606, 572, 119, 137 | 0, 0, 0, 0 |
| 32 | `Button_Right_Round_Blue` | 725, 572, 117, 137 | 0, 0, 0, 0 |
| 33 | `Button_Up_Round_Blue` | 842, 572, 119, 137 | 0, 0, 0, 0 |
| 34 | `Button_Down_Round_Blue` | 961, 572, 122, 137 | 0, 0, 0, 0 |
| 35 | `Button_Left_Diamond_Blue` | 1083, 572, 135, 137 | 0, 0, 0, 0 |
| 36 | `Button_Right_Diamond_Blue` | 1218, 572, 139, 137 | 0, 0, 0, 0 |
| 37 | `Button_Plus_Round_Blue` | 0, 471, 94, 101 | 0, 0, 0, 0 |
| 38 | `Button_Plus_Round_Green` | 94, 471, 77, 101 | 0, 0, 0, 0 |
| 39 | `Button_Plus_Round_Amber` | 171, 471, 78, 101 | 0, 0, 0, 0 |
| 40 | `Button_Plus_Round_Red` | 249, 471, 78, 101 | 0, 0, 0, 0 |
| 41 | `Button_Plus_Round_Gray` | 327, 471, 81, 101 | 0, 0, 0, 0 |
| 42 | `Button_Minus_Round_Blue` | 408, 471, 79, 101 | 0, 0, 0, 0 |
| 43 | `Button_Minus_Round_Green` | 487, 471, 77, 101 | 0, 0, 0, 0 |
| 44 | `Button_Minus_Round_Amber` | 564, 471, 77, 101 | 0, 0, 0, 0 |
| 45 | `Button_Minus_Round_Red` | 641, 471, 76, 101 | 0, 0, 0, 0 |
| 46 | `Button_Minus_Round_Gray` | 717, 471, 85, 101 | 0, 0, 0, 0 |
| 47 | `Button_Check_Round_Blue` | 802, 471, 85, 101 | 0, 0, 0, 0 |
| 48 | `Button_Check_Round_Green` | 887, 471, 79, 101 | 0, 0, 0, 0 |
| 49 | `Button_Check_Round_Amber` | 966, 471, 78, 101 | 0, 0, 0, 0 |
| 50 | `Button_Check_Round_Red` | 1044, 471, 77, 101 | 0, 0, 0, 0 |
| 51 | `Button_Check_Round_Gray` | 1121, 471, 81, 101 | 0, 0, 0, 0 |
| 52 | `Button_Close_Round_Blue` | 1202, 471, 80, 101 | 0, 0, 0, 0 |
| 53 | `Button_Close_Round_Red` | 1282, 471, 77, 101 | 0, 0, 0, 0 |
| 54 | `Button_Close_Round_Gray` | 1359, 471, 89, 101 | 0, 0, 0, 0 |
| 55 | `Toggle_Off_Blue` | 0, 379, 202, 92 | 0, 0, 0, 0 |
| 56 | `Toggle_Off_Green` | 202, 379, 195, 92 | 0, 0, 0, 0 |
| 57 | `Toggle_Off_Amber` | 397, 379, 196, 92 | 0, 0, 0, 0 |
| 58 | `Toggle_Off_Red` | 593, 379, 190, 92 | 0, 0, 0, 0 |
| 59 | `Toggle_Off_Gray` | 783, 379, 193, 92 | 0, 0, 0, 0 |
| 60 | `Indicator_Green` | 976, 379, 98, 92 | 0, 0, 0, 0 |
| 61 | `Indicator_Blue` | 1074, 379, 92, 92 | 0, 0, 0, 0 |
| 62 | `Indicator_Amber` | 1166, 379, 92, 92 | 0, 0, 0, 0 |
| 63 | `Indicator_Red` | 1258, 379, 90, 92 | 0, 0, 0, 0 |
| 64 | `Indicator_Gray` | 1348, 379, 100, 92 | 0, 0, 0, 0 |
| 65 | `Tabs_Three_Blue` | 0, 283, 326, 96 | 0, 0, 0, 0 |
| 66 | `Tabs_Three_Green` | 326, 283, 283, 96 | 0, 0, 0, 0 |
| 67 | `Tabs_Three_Amber` | 609, 283, 286, 96 | 0, 0, 0, 0 |
| 68 | `Tabs_Three_Red` | 895, 283, 266, 96 | 0, 0, 0, 0 |
| 69 | `Tabs_Three_Gray` | 1161, 283, 287, 96 | 0, 0, 0, 0 |
| 70 | `Panel_Bar_Blue` | 0, 195, 360, 88 | 110, 28, 105, 34 |
| 71 | `Panel_Bar_Green` | 360, 195, 352, 88 | 110, 28, 105, 34 |
| 72 | `Panel_Bar_Amber` | 712, 195, 280, 88 | 110, 28, 105, 34 |
| 73 | `Ribbon_Blue` | 992, 109, 95, 174 | 0, 0, 0, 0 |
| 74 | `Ribbon_Green` | 1087, 109, 88, 174 | 0, 0, 0, 0 |
| 75 | `Ribbon_Amber` | 1175, 109, 89, 174 | 0, 0, 0, 0 |
| 76 | `Ribbon_Red` | 1264, 109, 86, 174 | 0, 0, 0, 0 |
| 77 | `Ribbon_Gray` | 1350, 109, 98, 174 | 0, 0, 0, 0 |
| 78 | `Panel_Bar_Red` | 0, 109, 398, 86 | 120, 28, 120, 34 |
| 79 | `Panel_Bar_Gray` | 398, 109, 391, 86 | 120, 28, 120, 34 |
| 80 | `Button_Arrow_Blue` | 0, 0, 164, 109 | 0, 0, 0, 0 |
| 81 | `Button_Arrow_Green` | 164, 0, 142, 109 | 0, 0, 0, 0 |
| 82 | `Button_Arrow_Amber` | 306, 0, 143, 109 | 0, 0, 0, 0 |
| 83 | `Button_Arrow_Red` | 449, 0, 146, 109 | 0, 0, 0, 0 |
| 84 | `Button_Arrow_Gray` | 595, 0, 149, 109 | 0, 0, 0, 0 |
| 85 | `Arrow_Left_Ice` | 744, 0, 66, 109 | 0, 0, 0, 0 |
| 86 | `Arrow_Right_Ice` | 810, 0, 59, 109 | 0, 0, 0, 0 |
| 87 | `Arrow_Up_Ice` | 869, 0, 69, 109 | 0, 0, 0, 0 |
| 88 | `Arrow_Down_Ice` | 938, 0, 78, 109 | 0, 0, 0, 0 |
| 89 | `Progress_Star_Blue` | 1016, 0, 432, 109 | 0, 0, 0, 0 |

### Assets/Sprites/UiAtlas1.png

| № | Sprite | Rect (x, y, w, h) | Border (L, B, R, T) |
|---:|---|---|---|
| 1 | `Button_Play_Green` | 15, 897, 344, 157 | 0, 0, 0, 0 |
| 2 | `Button_Spin_Blue` | 359, 897, 317, 157 | 0, 0, 0, 0 |
| 3 | `Button_Settings_Blue` | 676, 917, 139, 144 | 0, 0, 0, 0 |
| 4 | `Button_Sound_Blue` | 815, 917, 143, 144 | 0, 0, 0, 0 |
| 5 | `Button_Music_Blue` | 958, 917, 148, 144 | 0, 0, 0, 0 |
| 6 | `Button_Info_Blue` | 1106, 917, 148, 144 | 0, 0, 0, 0 |
| 7 | `Button_Close_Blue` | 1254, 917, 166, 144 | 0, 0, 0, 0 |
| 8 | `Button_Buy_Amber` | 15, 767, 341, 130 | 0, 0, 0, 0 |
| 9 | `Button_Collect_Blue` | 356, 767, 321, 130 | 0, 0, 0, 0 |
| 10 | `Button_Home_Wood` | 682, 777, 143, 140 | 0, 0, 0, 0 |
| 11 | `Button_Shop_Wood` | 825, 777, 145, 140 | 0, 0, 0, 0 |
| 12 | `Button_Gift_Wood` | 970, 777, 147, 140 | 0, 0, 0, 0 |
| 13 | `Button_Trophy_Wood` | 1117, 777, 147, 140 | 0, 0, 0, 0 |
| 14 | `Button_Menu_Wood` | 1264, 777, 151, 140 | 0, 0, 0, 0 |
| 15 | `Button_Info_Ice` | 20, 636, 337, 131 | 0, 0, 0, 0 |
| 16 | `Button_Locked_Gray` | 357, 636, 322, 131 | 0, 0, 0, 0 |
| 17 | `Toggle_On_Green` | 682, 692, 152, 85 | 0, 0, 0, 0 |
| 18 | `Toggle_Off_Blue_Compact` | 834, 692, 145, 85 | 0, 0, 0, 0 |
| 19 | `Button_Play_Green_Compact` | 979, 666, 115, 111 | 0, 0, 0, 0 |
| 20 | `Button_Play_Amber_Compact` | 1094, 666, 104, 111 | 0, 0, 0, 0 |
| 21 | `Button_Left_Blue` | 1198, 666, 107, 111 | 0, 0, 0, 0 |
| 22 | `Button_Right_Blue` | 1305, 666, 118, 111 | 0, 0, 0, 0 |
| 23 | `Button_Plus_Green` | 687, 591, 116, 101 | 0, 0, 0, 0 |
| 24 | `Button_Minus_Blue` | 803, 591, 120, 101 | 0, 0, 0, 0 |
| 25 | `Progress_Runner_Blue` | 934, 589, 481, 77 | 0, 0, 0, 0 |
| 26 | `Window_Congratulations` | 16, 276, 625, 350 | 0, 0, 0, 0 |
| 27 | `Window_Ice_Close` | 647, 276, 509, 313 | 135, 90, 145, 80 |
| 28 | `Panel_Ice_Vertical` | 1157, 226, 283, 363 | 100, 85, 100, 68 |
| 29 | `Panel_Landscape_Ice` | 17, 29, 666, 247 | 0, 0, 0, 0 |
| 30 | `Panel_Ice_Horizontal` | 695, 161, 403, 115 | 100, 34, 130, 47 |
| 31 | `Panel_Wood_Snow` | 694, 32, 407, 129 | 0, 0, 0, 0 |
| 32 | `Counter_Coins` | 1103, 136, 332, 90 | 0, 0, 0, 0 |
| 33 | `Counter_Gems` | 1103, 32, 332, 104 | 0, 0, 0, 0 |

## Файлы для повторной проверки

- `Assets/Editor/UIAtlasSlicing/UIAtlasSlicer.cs`: меню `Tools → UI Atlases → Apply reviewed slicing and verify`; повторный запуск сохраняет ID существующих именованных Sprite.
- `Assets/Editor/UIAtlasSlicing/AtlasRects.json`: все границы и Border.
- `Verification/ImportedSprites.csv`: реально импортированные Sprite с fileID.
- `Verification/UiAtlas_UnityNative.png`, `UiAtlas1_UnityNative.png`: рендеры через uGUI.
- `Verification/UnitySliced_01.png` … `UnitySliced_06.png`: итоговые рендеры вариантов Sliced.
- `Verification/UiAtlas_sprites_*.jpg`, `UiAtlas1_sprites_*.jpg`: контактные листы на шахматном фоне.
- `Verification/*.original.meta.txt`: резервные копии настроек до нарезки; `baseline.json` и `FinalChecks.json`: контрольные суммы.

PNG в папке Verification — только снимки проверки. Отдельные PNG-ассеты элементов не создавались; UI использует sub-assets двух исходных атласов.
