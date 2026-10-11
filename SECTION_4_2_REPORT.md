# Section 4.2 — DTO и конфигурация

Реализован только раздел 4.2. Использованы спецификации `Assets/Docs/TZ.md` и
`Assets/Docs/Plan.md`; в корне эти документы отсутствуют. Рабочая сцена —
`Assets/_Project/Scenes/SC.unity`. Новая сцена не создавалась.

## Структура и поток данных

`SlotGameConfigSO` хранит редактируемые Inspector-данные математики и экономики.
`LocalSlotConfigProvider`, реализующий Application-интерфейс `ISlotConfigProvider`,
копирует их в чистый `SlotRuntimeConfig`, проверяет его через Domain
`ConfigValidator` и возвращает снимок либо выбрасывает `ConfigValidationException`.
Диагностика содержит путь поля и причину ошибки; автоматической подмены значений нет.

`SlotVisualConfigSO` хранит Sprite-сопоставления, фон, рамку и параметры будущих
анимаций/эффектов. `VisualConfigValidator` находится в Infrastructure и проверяет
Unity-ссылки отдельно. Sprite/Prefab/ScriptableObject не попадают в runtime DTO.

`SlotBootstrapper` в SC имеет ссылки на оба asset, создаёт provider, получает и
сохраняет валидный снимок, проверяет visual-конфиг и затем подключает навигацию.
Успех логируется с версией конфигурации. При ошибке Play блокируется и
инициализация прекращается. Игровые сервисы и движок не создаются.

Все свойства runtime-моделей доступны только для чтения. Коллекции копируются и
оборачиваются в read-only коллекции; вложенные объекты также неизменяемы.
Изменение исходного SO или повторный `Load()` не меняет ранее полученный снимок.
Будущая сессия может удерживать этот снимок на весь Spin и цепочку Free Spins;
сама сессия и политика обновления конфигурации в этом этапе не реализуются.

Для Remote Config потребуется другой `ISlotConfigProvider.Load()`, возвращающий
тот же валидированный DTO. Его выбор и сборка останутся в Bootstrap. Domain и
Application не меняют зависимости и не знают о Unity или сети. Сетевой код,
Firebase и дополнительные библиотеки не добавлены.

## Значения default-asset

- Сетка 5 × 3, восемь ID `S1`–`S7`, `SCATTER`.
- Пять разных явно сериализованных лент по 30 позиций, частоты в каждой:
  `6 / 5 / 5 / 4 / 4 / 3 / 2 / 1`. Генерации последовательностей при запуске нет.
- Точные 20 paylines и paytable из TZ, включая `S7: 57 / 330 / 1700`.
- Баланс 20 000, ставки 200/400/600/800/1000, начальная выбранная ставка 200.
- Scatter: 3→8, 4→12, 5→20; множитель Free Spins 1, retrigger включён.
- Daily Bonus включён, награды 400/600/800/1000/1200/1600/3000, сброс в 00:00 UTC,
  политика по умолчанию `ResetAfterMissedDay`.
- Jackpot включён, 100 000 монет, вероятность 0.0001, reference bet 1000,
  лимит 1 выигрыш за неделю, понедельник 00:00 UTC, EventId `weekly-jackpot`.
  Версии основного конфига и jackpot — `1.0.0`, даты начала/конца не ограничены.
- Фиксированный seed выключен; поле Seed редактируемое, значение 12345.

Частоты представлены редактируемой таблицей требуемых количеств. При изменении
частот нужно согласованно изменить пять лент: суммарно 30 позиций и один Scatter.
Размер 5 × 3 и weekly limit 1 остаются ограничениями версии 1 согласно TZ.
Версии трактуются как непустые строковые идентификаторы. UTC reset задаётся числом
секунд от полуночи; optional даты принимают ISO 8601 с `Z` или `+00:00`.

Все восемь symbol Sprite взяты из существующего `IconsAtlass.png`:
S1 — красная рыба, S2 — золотая, S3 — зелёная, S4 — синяя, S5 — крючок,
S6 — поплавок, S7 — вертолёт, SCATTER — табличка Scatter. Фон — `IceBG`, рамка —
`Frame`. Длительность будущего Spin — 1.5 s, stagger — 0.15 s; это только данные.

## Проверки валидатора

- Наличие конфигурации и обязательных групп; непустой ConfigVersion.
- Сетка 5 × 3, ScatterId, ровно восемь известных уникальных symbol ID.
- Положительные количества символов, сумма 30 и один Scatter на ленту.
- Ровно пять лент по 30 символов, известные ID, совпадение с таблицей частот,
  разные явно заданные последовательности.
- Ровно 20 уникальных paylines, пять row indices в каждой, диапазон 0–2.
- Paytable для семи обычных символов, отсутствие Scatter/дубликатов/неизвестных
  ID; неотрицательные `long`-множители для совпадений 3/4/5.
- Неотрицательный начальный баланс, непустой список положительных уникальных
  ставок, делимость на число линий и наличие DefaultBet в списке.
- Scatter thresholds 3/4/5 без дубликатов, неотрицательные награды и положительный
  множитель Free Spins.
- Семь неотрицательных Daily Bonus наград, валидная политика серии и UTC reset
  в пределах суток.
- Jackpot EventId и ConfigVersion, неотрицательная сумма, конечная вероятность
  в [0,1] (включая проверку NaN/Infinity), положительный reference bet, лимит 1,
  допустимый weekday/reset; корректные UTC-даты и `EndUtc > StartUtc`.
- Checked Int64-границы: максимальная ставка на линию × максимальный множитель
  × все линии, множитель Free Spins, прибавление jackpot и начального баланса
  с максимальной дневной наградой. Будущие транзакции должны дополнительно
  проверять переполнение на фактическом накопленном балансе.
- Visual: ровно одно уникальное сопоставление для каждого symbol, наличие всех
  symbol Sprite, фона и рамки, конечные допустимые duration/stagger. Prefab
  эффекта и AudioClip обязательны только при включении соответствующих опций.

## Созданные файлы

Пути ниже относительно корня проекта:

```text
Assets/_Project/Domain/Configuration/SymbolId.cs
Assets/_Project/Domain/Configuration/ReelStrip.cs
Assets/_Project/Domain/Configuration/Payline.cs
Assets/_Project/Domain/Configuration/Paytable.cs
Assets/_Project/Domain/Configuration/EconomyConfig.cs
Assets/_Project/Domain/Configuration/ScatterRules.cs
Assets/_Project/Domain/Configuration/DailyBonusRules.cs
Assets/_Project/Domain/Configuration/JackpotRulesConfig.cs
Assets/_Project/Domain/Configuration/SlotRuntimeConfig.cs
Assets/_Project/Domain/Configuration/ConfigValidationException.cs
Assets/_Project/Domain/Configuration/ConfigValidator.cs
Assets/_Project/Application/ISlotConfigProvider.cs
Assets/_Project/Infrastructure/Configuration/SlotGameConfigSO.cs
Assets/_Project/Infrastructure/Configuration/SlotVisualConfigSO.cs
Assets/_Project/Infrastructure/Configuration/LocalSlotConfigProvider.cs
Assets/_Project/Infrastructure/Configuration/VisualConfigValidator.cs
Assets/_Project/Configs/SlotGameConfigSO.asset
Assets/_Project/Configs/SlotVisualConfigSO.asset
Assets/_Project/Tests/EditMode/IceP.Tests.EditMode.asmdef
Assets/_Project/Tests/EditMode/ConfigurationTests.cs
SECTION_4_2_REPORT.md
```

Также созданы `.meta` для каждого из 20 новых Unity-файлов и метаданные папок
`Domain/Configuration.meta`, `Infrastructure/Configuration.meta`,
`Tests/EditMode.meta`. Существующие GUID не заменялись.

## Изменённые файлы

```text
Assets/_Project/Bootstrap/SlotBootstrapper.cs
Assets/_Project/Bootstrap/IceP.Bootstrap.asmdef
Assets/_Project/Scenes/SC.unity
Assets/_Project/Tests/PlayMode/GameSceneSmokeTests.cs
Assets/_Project/Tests/PlayMode/IceP.Tests.PlayMode.asmdef
README.md
```

В asmdef добавлены прямые ссылки Bootstrap→Domain и PlayMode Tests→Domain для
типизированного runtime DTO. Остальные runtime-направления не изменены;
`noEngineReferences` Domain/Application остаётся true. Новый EditMode asmdef
ссылается только на Domain, Application, Infrastructure и Test Framework.
Сцена изменена только для назначения двух config-asset на существующий bootstrap.

## Результаты проверок — 2026-10-11

- Unity 6000.3.10f1: компиляция успешна, `scriptCompilationFailed = false`.
- Domain и Application не имеют UnityEngine/UnityEditor assembly references.
- EditMode: **82 passed, 0 failed, 0 skipped** (0.695 s).
- PlayMode: **1 passed, 0 failed, 0 skipped** (2.006 s).
  Проверены успешная загрузка конфигурации и bootstrap, Main Menu startup,
  Settings/Daily bonus/Close, модальная блокировка, три цикла Play/Back без
  загрузки новой сцены и повторная загрузка SC без дублирования bootstrap.
- Финальный успешный прогон: Console без ошибок и предупреждений.
- Android player build и проверка на устройстве не выполнялись.

Промежуточный smoke-тест искал старое имя `SlotScreen`, тогда как пользовательский
объект в SC называется `GameScreen`. Тест исправлен на сериализованные ссылки;
сценовое имя и оформление сохранены. Ожидаемый лог успешной инициализации теперь
явно учитывается тестом. Один повторный запуск Test Runner завершился внутренней
ошибкой подготовки/выхода из PlayMode; прогон из стабильного EditMode завершён
успешно. Временное изменение Enter Play Mode Options тестовым инструментом
восстановлено к исходному значению.

Недостающих обязательных визуальных ассетов нет. Опциональные WinEffectPrefab и
SpinAudio пока не назначены; соответствующие опции выключены. Это предусмотрено
границами этапа. Единственная сцена остаётся SC согласно указанию пользователя;
имя Game.unity из исходного плана не возвращалось. Остальные ранее существовавшие
настройки, ассеты и изменения пользователя не затронуты.

Section 4.1 сохранён. Section 4.3 и последующие этапы не реализованы и требуют
явного разрешения пользователя.
