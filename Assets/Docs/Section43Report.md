# Этап 4.3 — генерация результата

Реализован только раздел 4.3 Plan.md. Выплаты, Scatter-награды, экономика,
сессия Spin и анимации не добавлялись. Сцена SC.unity и конфигурационные assets
не менялись.

## Модели и генерация

`IRandomSource.NextInt(minInclusive, maxExclusive)` возвращает равномерный целый
результат в полуоткрытом непустом диапазоне. Интерфейс находится в Domain.

`OutcomeGenerator` получает SlotRuntimeConfig и IRandomSource через конструктор,
проверяет конфигурацию один раз, затем для каждого результата делает ровно пять
запросов RNG. Каждый запрос выбирает остановку соответствующей ленты. Строки
берутся из позиций stop, (stop + 1) % length, (stop + 2) % length. Ленты не
перестраиваются и не перемешиваются.

`ReelStop` — неизменяемое значение с ReelIndex и Position. `SlotGrid` копирует
входную матрицу и предоставляет только чтение через grid[reelIndex, rowIndex].
Барабаны 0–4 идут слева направо, строки 0–2 сверху вниз. `SpinOutcome` содержит
SlotGrid и защищённую от изменения копию пяти остановок в порядке барабанов.
Генератор проверяет верхнюю границу остановок по длине ленты; ReelStop самостоятельно
проверяет индекс барабана и неотрицательность позиции.

## RNG и bootstrap

SystemRandomSource в Infrastructure хранит один System.Random. Конструктор без
seed использует стандартную инициализацию System.Random; конструктор с seed
предназначен для явно включённого детерминированного режима и тестов.
RandomSourceFactory выбирает конструктор по DebugRandom.UseFixedSeed. При false
DebugRandom.Seed не передаётся в RNG и не влияет на генерацию. Тест проверяет
сам выбор конструктора, без вероятностного сравнения случайных результатов.

SlotBootstrapper создаёт RNG и OutcomeGenerator после валидации конфигурации.
Экземпляры переиспользуются. Генерация не вызывается при bootstrap и не связана
с кнопками UI. Нет статического состояния RNG и зависимости Domain от Unity.

## Проверка этапа 4.2

Исправления не потребовались. SlotRuntimeConfig предоставляет все пять лент;
LocalSlotConfigProvider сохраняет каждую позицию в исходном порядке; ReelStrip
копирует исходные символы и оборачивает их в read-only коллекцию. ConfigValidator
проверяет пять лент по 30 допустимых символов, распределение и сетку 5 × 3.
Новые тесты дополнительно проверяют полный порядок реальных лент и невозможность
изменения результата после изменения исходного ScriptableObject.

## Пример с реальными лентами

Для остановок **[0, 28, 29, 27, 5]** из SlotGameConfigSO.asset:

| Строка | Барабан 0 | Барабан 1 | Барабан 2 | Барабан 3 | Барабан 4 |
|---|---|---|---|---|---|
| Верх (0) | S4 | S1 | S4 | SCATTER | SCATTER |
| Центр (1) | S2 | S2 | S1 | S6 | S1 |
| Низ (2) | S1 | S5 | S1 | S3 | S4 |

Матрица закреплена точным тестом. Для всех пяти реальных лент также закреплены
полные матрицы при stop=0, stop=28 и stop=29.

## Файлы

Созданы:

- Assets/_Project/scripts/Domain/SlotEngine/IRandomSource.cs
- Assets/_Project/scripts/Domain/SlotEngine/ReelStop.cs
- Assets/_Project/scripts/Domain/SlotEngine/SlotGrid.cs
- Assets/_Project/scripts/Domain/SlotEngine/SpinOutcome.cs
- Assets/_Project/scripts/Domain/SlotEngine/OutcomeGenerator.cs
- Assets/_Project/scripts/Infrastructure/Randomness/SystemRandomSource.cs
- Assets/_Project/scripts/Infrastructure/Randomness/RandomSourceFactory.cs
- Assets/_Project/scripts/Tests/EditMode/OutcomeGeneratorTests.cs
- Assets/Docs/Section43Report.md
- Соответствующие .meta каждого нового файла, Domain/SlotEngine.meta и Infrastructure/Randomness.meta.

Изменены:

- Assets/_Project/scripts/Bootstrap/SlotBootstrapper.cs — сборка RNG и генератора.
- Assets/_Project/scripts/Infrastructure/AssemblyInfo.cs — доступ тестов к внутренней фабрике.
- Assets/_Project/scripts/Tests/PlayMode/GameSceneSmokeTests.cs — проверка создания генератора.

## Результаты проверки (12 октября 2026)

Unity 6000.3.10f1 успешно скомпилировал проект: C# ошибок и предупреждений в
журнале компиляции нет. Оба запуска тестов завершились с кодом 0.

| Набор | Пройдено | Ошибки | Пропущено |
|---|---:|---:|---:|
| Существующие EditMode ConfigurationTests | 82 | 0 | 0 |
| Новые EditMode OutcomeGeneratorTests | 24 | 0 | 0 |
| Все EditMode | 106 | 0 | 0 |
| PlayMode GameSceneSmokeTests | 1 | 0 | 0 |

PlayMode проверил сцену Assets/_Project/Scenes/SC.unity: bootstrap, главное меню,
Settings/Daily Bonus, три цикла Play/Back без смены сцены и повторную загрузку.
LogAssert.NoUnexpectedReceived завершился успешно. Ручная визуальная проверка
открытого редактора не выполнялась; проверка меню и навигации автоматическая.

Исходный проект был занят открытым Unity Editor, поэтому batch-запуск на нём
был отклонён сообщением «another Unity instance is running with this project open».
Успешные тесты проведены во временной копии Assets, Packages и ProjectSettings
в Logs/Section43/ValidationProject, без изменения пакетов или настроек исходника.
После проверки временная копия удалена; журналы и результаты сохранены:

- Logs/Section43/EditMode.xml
- Logs/Section43/PlayMode.xml
- Logs/Section43/ValidationEditMode.log
- Logs/Section43/ValidationPlayMode.log
- Logs/Section43/ValidatedSources.json — сверка исходников с тестированной копией.

Журналы не полностью пусты: Unity Licensing сообщает «Access token is unavailable;
failed to update», но разрешает запуск по существующей лицензии; Mono при выходе
выводит «abort_threads: Failed aborting ...». Эти сообщения относятся к окружению
редактора; неожиданных игровых errors/warnings в smoke-тесте не зарегистрировано.
Живое окно Console исходного редактора отдельно не инспектировалось.

Пользовательские изменения Assets/Scenes.meta и ProjectSettings не исправлялись.
Раздел 4.4 не реализован.

## Ограничения

Последовательность System.Random с seed воспроизводится на одинаковой реализации
.NET/Mono; идентичность между разными версиями runtime не обещается.
Генератор и RNG рассчитаны на последовательные вызовы. RNG предназначен для
локальной игры на виртуальные монеты, без криптографических гарантий.
