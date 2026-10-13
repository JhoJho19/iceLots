# IceP Documentation

## Architecture

Unity 6000.3.10f1. The main scene and the only scene enabled in the build is `Assets/_Project/Scenes/SC.unity`. The main menu, `GameScreen` slot screen, settings, and daily bonus switch within a single scene.

Game sources are in `Assets/_Project/Scripts`. `Domain` contains immutable configuration and slot mathematics; `Application` contains game use cases, confirmed session state, and persistence contracts. These two assemblies have no Unity dependencies. `Infrastructure` implements ScriptableObject loading, JSON storage, clocks, and random number generators. `Presentation` is split into plain C# presenters, display models, and Unity components that receive input and render UI. `Bootstrap` creates and wires dependencies. `Editor` contains UI authoring commands; `Tests` contains verification and simulations. The separate `View/SpriteaAnimation.cs` script belongs to the earlier UI and uses UniTask.

Assembly dependencies: `Application → Domain`; `Infrastructure → Application, Domain`; `Presentation → Application, Domain, uGUI`; `Bootstrap → Domain, Application, Infrastructure, Presentation`. Presenters call services; views do not award currency or write saves.

At startup, `SlotBootstrapper` validates configuration and UI, loads or creates the profile, creates the shared `GameSession`, recovers an interrupted spin, and connects presenters. Configuration is defined in `Assets/_Project/Configs/SlotGameConfigSO.asset` and `SlotVisualConfigSO.asset`. The profile is stored in `Application.persistentDataPath`; Editor tests can override the directory through `ICEP_TEST_SAVE_DIRECTORY`.

A spin reserves the bet/free spin and saves `PendingSpin`, generates the outcome, plays animation, and then confirms the payout through a successful save. Services share the session command gate; write failures retain the proposed snapshot for an exact retry. An unfinished spin is recovered on the next launch. The jackpot uses a separate RNG and a weekly limit; the daily bonus uses injected UTC clocks. Audio preferences are persisted; audio playback is not implemented yet.

## Scene Components

The table below lists all custom MonoBehaviour components, including `PaylineView`, which inherits MonoBehaviour through `Graphic`. Paths were verified against script GUIDs and saved scene hierarchies. All these components are in `SC.unity`; `Assets/Editor/UIAtlasSlicing/UIAtlasVerification.unity` and `Assets/Settings/Scenes/URP2DSceneTemplate.unity` contain no custom project components. Standard Unity and package components are excluded from the class catalog.

| Class | File Relative to Scripts | Objects in SC.unity | Responsibility |
| --- | --- | --- | --- |
| `SlotBootstrapper` | `Bootstrap/SlotBootstrapper.cs` | `SlotBootstrapper` | Creates services and presenters, loads/recovers state, updates UI, and releases subscriptions. |
| `GameNavigationView` | `Presentation/GameNavigationView.cs` | `Canvas` | Controls screen and modal visibility, menu buttons, balance, and the bonus indicator. |
| `SlotView` | `Presentation/SlotView.cs` | `Canvas/GameScreen` | Handles Spin/bet/Back/Retry input, slot HUD, animation startup, and confirmed win display. |
| `SettingsView` | `Presentation/SettingsView.cs` | `Canvas/SettingsPopup` | Music/effects toggles and volume controls, availability, and settings messages. |
| `DailyBonusView` | `Presentation/DailyBonusView.cs` | `Canvas/DailyBonusPopup` | Displays seven rewards, card states, Claim/Retry, and messages. |
| `JackpotView` | `Presentation/JackpotView.cs` | `Canvas/GameScreen/JackpotHUD` | Displays jackpot amount/status/timer and the celebration effect. |
| `ReelAnimationCoordinator` | `Presentation/ReelAnimationCoordinator.cs` | `Canvas/GameScreen/ImageGameField/SlotWheels` | Starts/cancels five reels together and signals completion for the spin ID. |
| `ReelView` | `Presentation/ReelView.cs` | `Canvas/GameScreen/ImageGameField/SlotWheels/SlotWheel`, `SlotWheel (1)`, `SlotWheel (2)`, `SlotWheel (3)`, `SlotWheel (4)` under the same parent | Scrolls the symbol strip through five Image cells and stops at the specified position. |
| `PaylineView` | `Presentation/PaylineView.cs` | `Canvas/GameScreen/ImageGameField/WinningPaylines` | Draws winning paylines and highlights symbols, including Scatter. |
| `UISpriteAnimation` | `View/SpriteaAnimation.cs` | `Canvas/GameScreen/ImageBG`; `Canvas/GameScreen/ImageMascot` | Frame-by-frame Image animation using UniTask, FPS/loop settings, and cancellation on disable. The class name differs from the filename. |

## Catalog of Other Classes and Types

Table paths are relative to `Assets/_Project/Scripts`, except the separately noted `Tools` section. Every class has a description and file, including nested classes and multiple classes in one `.cs`. Structs, interfaces, and enums are included for completeness. Plain C# classes and ScriptableObjects are not attached to scene objects.

### Domain/Configuration

| Type | File | Responsibility |
| --- | --- | --- |
| `SlotRuntimeConfig` | `Domain/Configuration/SlotRuntimeConfig.cs` | Immutable snapshot of mathematical and economic rules. |
| `DebugRandomSettings` | `Domain/Configuration/SlotRuntimeConfig.cs` | Fixed-seed flag and seed value. |
| `ConfigValidator` | `Domain/Configuration/ConfigValidator.cs` | Checks configuration consistency and game constraints. |
| `ConfigValidationException` | `Domain/Configuration/ConfigValidationException.cs` | Exception containing invalid-configuration diagnostics. |
| `EconomyConfig` | `Domain/Configuration/EconomyConfig.cs` | Starting balance, default bet, and allowed bets. |
| `DailyBonusRules` | `Domain/Configuration/DailyBonusRules.cs` | Seven rewards, UTC reset time, and attendance streak rules. |
| `StreakResetBehavior` (enum) | `Domain/Configuration/DailyBonusRules.cs` | Reset or preserve the streak after a missed day. |
| `JackpotRulesConfig` | `Domain/Configuration/JackpotRulesConfig.cs` | Jackpot event, amount, probability, dates, and weekly rules. |
| `ReelStrip` | `Domain/Configuration/ReelStrip.cs` | Immutable reel symbol sequence. |
| `Payline` | `Domain/Configuration/Payline.cs` | Row indices of a winning line across five reels. |
| `Paytable` | `Domain/Configuration/Paytable.cs` | Payout multiplier table for symbol matches. |
| `SymbolPayout` (struct) | `Domain/Configuration/Paytable.cs` | Payouts for 3/4/5 matches of one symbol. |
| `ScatterRules` | `Domain/Configuration/ScatterRules.cs` | Free Spins awards, multiplier, and retrigger permission. |
| `ScatterAward` (struct) | `Domain/Configuration/ScatterRules.cs` | Number of Free Spins awarded for a specified Scatter count. |
| `SymbolId` (enum) | `Domain/Configuration/SymbolId.cs` | Identifiers S1–S7 and SCATTER. |
| `SymbolDefinition` (struct) | `Domain/Configuration/SymbolId.cs` | Required count of a symbol on a strip. |

### Domain/SlotEngine

| Type | File | Responsibility |
| --- | --- | --- |
| `SlotEngine` | `Domain/SlotEngine/SlotEngine.cs` | Combines outcome generation and mathematical spin evaluation. |
| `OutcomeGenerator` | `Domain/SlotEngine/OutcomeGenerator.cs` | Selects reel stops through RNG and builds the grid. |
| `IRandomSource` (interface) | `Domain/SlotEngine/IRandomSource.cs` | Contract for a uniform random integer within a specified range. |
| `SlotGrid` | `Domain/SlotEngine/SlotGrid.cs` | Immutable 5 × 3 grid; coordinates are [reel, row]. |
| `ReelStop` (struct) | `Domain/SlotEngine/ReelStop.cs` | Reel index and stop position. |
| `SpinOutcome` | `Domain/SlotEngine/SpinOutcome.cs` | Symbol grid and five stops, without economic state. |
| `SpinEvaluationContext` (struct) | `Domain/SlotEngine/SpinEvaluationContext.cs` | Evaluation bet and paid/free spin flag. |
| `EvaluatedSpinOutcome` | `Domain/SlotEngine/EvaluatedSpinOutcome.cs` | Spin outcome together with its context and win/Scatter calculations. |
| `WinEvaluator` | `Domain/SlotEngine/WinEvaluator.cs` | Finds payline matches and calculates payouts. |
| `WinEvaluation` | `Domain/SlotEngine/WinEvaluation.cs` | Winning paylines and the base win amount. |
| `LineWin` | `Domain/SlotEngine/WinEvaluation.cs` | Symbol, payline, match count, multiplier, payout, and winning cells. |
| `GridCoordinate` (struct) | `Domain/SlotEngine/WinEvaluation.cs` | Cell coordinates for win display. |
| `ScatterEvaluator` | `Domain/SlotEngine/ScatterEvaluator.cs` | Counts Scatter symbols and determines the Free Spins award. |
| `ScatterEvaluation` | `Domain/SlotEngine/ScatterEvaluator.cs` | Scatter count and awarded Free Spins. |
| `JackpotRules` | `Domain/SlotEngine/JackpotRules.cs` | Jackpot probability based on the bet, event, and eligibility. |

### Application

| Type | File | Responsibility |
| --- | --- | --- |
| `GameSession` | `Application/GameSession.cs` | Confirmed profile, lifecycle state, and shared command gate. |
| `GameSessionState` (enum) | `Application/GameSession.cs` | Idle, Preparing, Spinning, Settling, Error. |
| `BalanceService` | `Application/BalanceService.cs` | Checks bet/balance, reserves resources, credits rewards, and calculates a new profile. |
| `SpinService` | `Application/SpinService.cs` | Bet selection, spin acceptance/completion, saving, write retries, and recovery. |
| `SpinService.WritePhase` (enum) | `Application/SpinService.cs` | Internal write stage used for exact retries. |
| `SpinResult` | `Application/SpinResult.cs` | ID, type, bet, mathematical outcome, and jackpot result of an accepted spin. |
| `SpinOperationResult` | `Application/SpinResult.cs` | Operation success/error, accepted spin, or confirmed payout. |
| `SpinError` (enum) | `Application/SpinResult.cs` | Reasons for rejecting a spin operation. |
| `DailyBonusService` | `Application/DailyBonusService.cs` | Availability, daily reward claims, attendance streak, and write retries. |
| `DailyBonusStatus` | `Application/DailyBonusStatus.cs` | Availability, day, rewards, streak, and next claim time. |
| `DailyBonusClaimResult` | `Application/DailyBonusStatus.cs` | Confirmed reward claim or error. |
| `DailyBonusError` (enum) | `Application/DailyBonusStatus.cs` | Reasons for daily bonus unavailability/rejection. |
| `WeeklyJackpotService` | `Application/WeeklyJackpotService.cs` | Weekly eligibility, random draw, and application of winnings to the proposed profile. |
| `JackpotStatus` | `Application/WeeklyJackpotService.cs` | Event state, current week, and eligibility. |
| `JackpotSpinResult` | `Application/WeeklyJackpotService.cs` | Participation, win flag, and jackpot amount for a specific spin. |
| `JackpotState` (enum) | `Application/WeeklyJackpotService.cs` | Active, WonThisWeek, Inactive. |
| `SettingsService` | `Application/SettingsService.cs` | Validates/saves audio preferences and performs exact retries after errors. |
| `SettingsResult` | `Application/SettingsService.cs` | Settings operation result with diagnostics. |
| `SettingsError` (enum) | `Application/SettingsService.cs` | Reasons for rejecting a settings change. |
| `GameNavigationService` | `Application/GameNavigationService.cs` | Current screen, screen-change events, and navigation restrictions based on session state. |
| `GameScreen` (enum) | `Application/GameNavigationService.cs` | MainMenu or Slot. |
| `IClock` (interface) | `Application/IClock.cs` | Injectable current UTC time. |
| `ISlotConfigProvider` (interface) | `Application/ISlotConfigProvider.cs` | Loads a validated configuration snapshot. |

### Application/Persistence

| Type | File | Responsibility |
| --- | --- | --- |
| `SaveData` | `Application/Persistence/SaveData.cs` | Versioned profile: balance, bets, bonuses, jackpot, settings, and an unfinished spin. |
| `PendingSpin` | `Application/Persistence/SaveData.cs` | Persisted reservation for an unfinished spin, used during recovery. |
| `UserSettings` | `Application/Persistence/SaveData.cs` | Music/effects enable flags and volumes, plus settings version. |
| `PendingSpinType` (enum) | `Application/Persistence/SaveData.cs` | Paid or Free. |
| `SaveDataValidator` | `Application/Persistence/SaveDataValidator.cs` | Checks profile version, fields, and economic invariants. |
| `InitialProfileFactory` | `Application/Persistence/InitialProfileFactory.cs` | Creates the initial profile from configuration. |
| `ISaveRepository` (interface) | `Application/Persistence/ISaveRepository.cs` | Profile loading, saving, and explicit reset. |
| `SaveLoadResult` | `Application/Persistence/ISaveRepository.cs` | Loaded profile, source/status, and diagnostics. |
| `SaveWriteResult` | `Application/Persistence/ISaveRepository.cs` | Write confirmation or storage error. |
| `SaveLoadStatus` (enum) | `Application/Persistence/ISaveRepository.cs` | Missing file, loaded profile, backup recovery, or failure. |
| `PersistenceError` (enum) | `Application/Persistence/ISaveRepository.cs` | Data, version, recovery, and I/O errors. |

### Infrastructure

| Type | File | Responsibility |
| --- | --- | --- |
| `LocalSlotConfigProvider` | `Infrastructure/Configuration/LocalSlotConfigProvider.cs` | Converts the game SO into a validated immutable snapshot. |
| `SlotGameConfigSO` | `Infrastructure/Configuration/SlotGameConfigSO.cs` | Editable game ScriptableObject assigned to the bootstrap component. |
| `SymbolDefinitionData` | `Infrastructure/Configuration/SlotGameConfigSO.cs` | Serializable symbol definition and count. |
| `GridConfigData` | `Infrastructure/Configuration/SlotGameConfigSO.cs` | Grid dimensions and Scatter identifier. |
| `ReelStripData` | `Infrastructure/Configuration/SlotGameConfigSO.cs` | Serializable symbol strip. |
| `PaylineData` | `Infrastructure/Configuration/SlotGameConfigSO.cs` | Serializable payline rows. |
| `SymbolPayoutData` | `Infrastructure/Configuration/SlotGameConfigSO.cs` | Multipliers for 3/4/5 symbol matches. |
| `EconomyConfigData` | `Infrastructure/Configuration/SlotGameConfigSO.cs` | Inspector settings for balance and bets. |
| `ScatterAwardData` | `Infrastructure/Configuration/SlotGameConfigSO.cs` | Free Spins awarded for a Scatter count. |
| `ScatterRulesData` | `Infrastructure/Configuration/SlotGameConfigSO.cs` | Award list, multiplier, and bonus retrigger setting. |
| `DailyBonusRulesData` | `Infrastructure/Configuration/SlotGameConfigSO.cs` | Serializable daily bonus rewards and rules. |
| `JackpotRulesData` | `Infrastructure/Configuration/SlotGameConfigSO.cs` | Serializable jackpot event parameters. |
| `DebugRandomSettingsData` | `Infrastructure/Configuration/SlotGameConfigSO.cs` | Inspector settings for a fixed seed. |
| `SlotVisualConfigSO` | `Infrastructure/Configuration/SlotVisualConfigSO.cs` | Visual ScriptableObject: sprites, artwork, and animation parameters. |
| `SymbolVisualData` | `Infrastructure/Configuration/SlotVisualConfigSO.cs` | Mapping between a symbol identifier and its sprite. |
| `VisualConfigValidator` | `Infrastructure/Configuration/VisualConfigValidator.cs` | Checks the visual SO and its consistency with the game configuration. |
| `JsonSaveRepository` | `Infrastructure/Persistence/JsonSaveRepository.cs` | File-based JSON storage with a temporary file, backup, and recovery. |
| `SaveWriteStage` (enum) | `Infrastructure/Persistence/JsonSaveRepository.cs` | Write checkpoints for injecting failures in tests. |
| `SaveJsonCodec` | `Infrastructure/Persistence/SaveJsonCodec.cs` | Strict profile serialization/deserialization and JSON shape validation. |
| `SaveFormatException` | `Infrastructure/Persistence/SaveJsonCodec.cs` | Save-format error with a PersistenceError category. |
| `RandomSourceFactory` | `Infrastructure/Randomness/RandomSourceFactory.cs` | Creates separate reel and jackpot RNGs with debug-seed support. |
| `SystemRandomSource` | `Infrastructure/Randomness/SystemRandomSource.cs` | IRandomSource implementation using System.Random. |
| `SystemClock` | `Infrastructure/SystemClock.cs` | IClock implementation using system UTC time. |

### Presentation: Plain C# Types

Components in this folder are described in the scene-object table above. Presenters are created programmatically and release subscriptions through Dispose.

| Type | File | Responsibility |
| --- | --- | --- |
| `GameNavigationPresenter` | `Presentation/GameNavigationPresenter.cs` | Connects screen changes to the View and updates Back availability. |
| `MainMenuPresenter` | `Presentation/MainMenuPresenter.cs` | Play, mutually exclusive modals, balance, and bonus indicator; owns popup presenters. |
| `SettingsPresenter` | `Presentation/SettingsPresenter.cs` | Passes preferences to the service, debounces slider writes for 0.4 s, and handles flush/Retry. |
| `DailyBonusPresenter` | `Presentation/DailyBonusPresenter.cs` | Refreshes the bonus popup and handles Claim/Retry and confirmed results. |
| `SlotPresenter` | `Presentation/SlotPresenter.cs` | Spin/bet/Back/Retry, linking animation completion to spin settlement and confirmed payout display. |
| `SlotPresenter.RetryOperation` (enum) | `Presentation/SlotPresenter.cs` | Operation to continue after a write retry. |
| `JackpotPresenter` | `Presentation/JackpotPresenter.cs` | Refreshes HUD/timer and celebrates each confirmed jackpot once. |
| `DailyBonusDisplay` | `Presentation/DailyBonusDisplay.cs` | Projects daily bonus status into reward-card states. |
| `RewardDisplayState` (enum) | `Presentation/DailyBonusDisplay.cs` | Claimed, Available, Current, Future. |
| `SlotDisplay` | `Presentation/SlotDisplay.cs` | Confirmed balance/bet/Free Spins and control availability. |
| `JackpotDisplay` | `Presentation/JackpotDisplay.cs` | Text representation of amount, status, and remaining time. |
| `SlotVisualSettings` | `Presentation/SlotVisualSettings.cs` | Sprite lookup function and reel-scrolling parameters. |
| `ReelCompletionBarrier` | `Presentation/ReelCompletionBarrier.cs` | Accepts each of five reel completions once for the current spin ID. |
| `ISlotView` (interface) | `Presentation/ISlotView.cs` | Input/animation events and slot display commands. |
| `IJackpotView` (interface) | `Presentation/IJackpotView.cs` | Contract for the HUD and jackpot celebration. |

### Editor

| Class | File | Responsibility |
| --- | --- | --- |
| `MenuUiAuthoring` | `Editor/MenuUI/MenuUiAuthoring.cs` | Command for creating/configuring the menu, settings, and daily rewards in the scene. |
| `SlotUiAuthoring` | `Editor/SlotUI/SlotUiAuthoring.cs` | Command for preparing the slot HUD, reels, paylines, and controls. |
| `JackpotUiAuthoring` | `Editor/SlotUI/JackpotUiAuthoring.cs` | Command for preparing JackpotHUD and its references. |
| `UIAtlasSlicer` | `Editor/UIAtlasSlicing/UIAtlasSlicer.cs` | Slices atlases using reviewed rectangles and creates a UI verification scene. |
| `UIAtlasSlicer.Manifest` | `Editor/UIAtlasSlicing/UIAtlasSlicer.cs` | Root JSON manifest containing the atlas list. |
| `UIAtlasSlicer.Atlas` | `Editor/UIAtlasSlicing/UIAtlasSlicer.cs` | Texture path/dimensions and its sprite list. |
| `UIAtlasSlicer.Slice` | `Editor/UIAtlasSlicing/UIAtlasSlicer.cs` | Sprite name, rectangle, and border, with coordinate conversion. |

### Tests/EditMode

| Class/Type | File | Responsibility |
| --- | --- | --- |
| `ConfigurationTests` | `Tests/EditMode/ConfigurationTests.cs` | Configuration validity, constraints, and snapshot independence. |
| `OutcomeGeneratorTests` | `Tests/EditMode/OutcomeGeneratorTests.cs` | Stop/grid generation and RNG contract. |
| `OutcomeGeneratorTests.ControlledRandom` | `Tests/EditMode/OutcomeGeneratorTests.cs` | Controlled RNG values for expected outcomes. |
| `SlotEngineTests` | `Tests/EditMode/SlotEngineTests.cs` | Winning paylines, Scatter, multipliers, and spin evaluation. |
| `SlotEngineTests.StopsRandom` | `Tests/EditMode/SlotEngineTests.cs` | Predefined reel stops. |
| `SlotMathematicsTests` | `Tests/EditMode/SlotMathematicsTests.cs` | Independent checks of probabilities and mathematical rules. |
| `PersistenceTests` | `Tests/EditMode/PersistenceTests.cs` | JSON, validation, file failures, backup, and recovery. |
| `SpinLifecycleTests` | `Tests/EditMode/SpinLifecycleTests.cs` | Spin reservation/completion/recovery, idempotency, and Retry. |
| `SpinLifecycleTests.SequenceRandom` | `Tests/EditMode/SpinLifecycleTests.cs` | Random-value sequence for lifecycle scenarios. |
| `DailyBonusTests` | `Tests/EditMode/DailyBonusTests.cs` | UTC periods, streaks, reward claims, and write errors. |
| `DailyBonusTests.FixedRandom` | `Tests/EditMode/DailyBonusTests.cs` | Fixed RNG for related spins. |
| `WeeklyJackpotTests` | `Tests/EditMode/WeeklyJackpotTests.cs` | Probability, weekly limit, event, and jackpot persistence. |
| `WeeklyJackpotTests.Bits` | `Tests/EditMode/WeeklyJackpotTests.cs` | Controlled stream of jackpot random values. |
| `WeeklyJackpotTests.Reels` | `Tests/EditMode/WeeklyJackpotTests.cs` | Controlled reel stops. |
| `SettingsTests` | `Tests/EditMode/SettingsTests.cs` | Preference validation, saving, Retry, and protection of the current profile. |
| `SettingsTests.FixedRandom` | `Tests/EditMode/SettingsTests.cs` | Fixed RNG for gameplay scenarios in settings tests. |
| `DailyBonusPresentationTests` | `Tests/EditMode/DailyBonusPresentationTests.cs` | Reward-card states and daily bonus display. |
| `SlotPresentationTests` | `Tests/EditMode/SlotPresentationTests.cs` | Presenter/service interaction, animation completion, and Retry. |
| `SlotPresentationTests.Random` | `Tests/EditMode/SlotPresentationTests.cs` | Controlled RNG for presenter tests. |
| `SlotPresentationTests.View` | `Tests/EditMode/SlotPresentationTests.cs` | Fake ISlotView, events, and display-command tracking. |
| `JackpotPresentationTests` | `Tests/EditMode/JackpotPresentationTests.cs` | HUD status/timer and celebration only after confirmed payout. |
| `JackpotPresentationTests.Bits` | `Tests/EditMode/JackpotPresentationTests.cs` | Controlled jackpot RNG. |
| `JackpotPresentationTests.Hud` | `Tests/EditMode/JackpotPresentationTests.cs` | Fake IJackpotView tracking renders/celebrations. |
| `JackpotPresentationTests.SlotFake` | `Tests/EditMode/JackpotPresentationTests.cs` | Fake ISlotView for related slot events. |
| `FakeClock` | `Tests/EditMode/FakeClock.cs` | Adjustable UTC clock for tests. |
| `FakeSaveRepository` | `Tests/EditMode/FakeSaveRepository.cs` | In-memory storage with controlled write results. |
| `SlotAnalyticalModel` | `Tests/EditMode/SlotAnalyticalModel.cs` | Analytical RTP, Scatter/payline probabilities, and expected bonus length. |
| `SimulationRandom` | `Tests/EditMode/SlotSimulation.cs` | Reproducible SplitMix64 with rejection sampling and a call counter. |
| `RunningMoments` | `Tests/EditMode/SlotSimulation.cs` | Online calculation of mean, variance, and standard error. |
| `SlotSimulationOptions` | `Tests/EditMode/SlotSimulation.cs` | Spin count, bet, seed, bonus limits, and jackpot schedule. |
| `SlotSimulationResult` | `Tests/EditMode/SlotSimulation.cs` | Statistics for payouts, RTP, symbols, bonus chains, and jackpot. |
| `SlotSimulationRunner` | `Tests/EditMode/SlotSimulation.cs` | Runs simulations using the actual mathematical/application rules. |
| `JackpotSimulationMode` (enum) | `Tests/EditMode/SlotSimulation.cs` | No jackpot, no weekly cap, or a weekly limit. |
| `SlotSimulationReport` | `Tests/EditMode/SlotSimulationReport.cs` | Markdown report, confidence intervals, and comparison with analytical results. |
| `SlotSimulationTests` | `Tests/EditMode/SlotSimulationTests.cs` | Simulation regressions, a short smoke test, and an explicitly run large simulation. |
| `SlotSimulationTests.ScriptedReels` | `Tests/EditMode/SlotSimulationTests.cs` | Specified stops for bonus scenarios. |
| `SlotSimulationTests.Bits` | `Tests/EditMode/SlotSimulationTests.cs` | Controlled jackpot random values. |

### Tests/PlayMode

| Class | File | Responsibility |
| --- | --- | --- |
| `GameSceneSmokeTests` | `Tests/PlayMode/GameSceneSmokeTests.cs` | SC startup, navigation, errors, and bootstrap recovery. |
| `GameSceneSmokeTests.WinningBits` | `Tests/PlayMode/GameSceneSmokeTests.cs` | RNG for a guaranteed jackpot win in a test. |
| `MainMenuUiTests` | `Tests/PlayMode/MainMenuUiTests.cs` | Actual menu buttons, reward/settings popups, debounce, and persistence. |
| `SlotUiTests` | `Tests/PlayMode/SlotUiTests.cs` | Slot controls, reels, paylines, animation completion/cancellation, and Retry. |
| `SlotUiTests.Stops` | `Tests/PlayMode/SlotUiTests.cs` | Specified stops for visual scenarios. |
| `SlotUiTests.MemorySave` | `Tests/PlayMode/SlotUiTests.cs` | Fake storage for slot UI tests. |
| `JackpotHudTests` | `Tests/PlayMode/JackpotHudTests.cs` | Actual jackpot display, timer, and celebration. |
| `JackpotHudTests.Clock` | `Tests/PlayMode/JackpotHudTests.cs` | Adjustable UTC clock for HUD tests. |
| `JackpotHudTests.Bits` | `Tests/PlayMode/JackpotHudTests.cs` | Controlled RNG with call tracking. |
| `JackpotHudTests.MemorySave` | `Tests/PlayMode/JackpotHudTests.cs` | Fake storage for HUD tests. |
| `FinalIntegrationTests` | `Tests/PlayMode/FinalIntegrationTests.cs` | Full gameplay scenarios, reload/persistence, and UI checks at different resolutions. |

### Tools/ReleaseQA

This code is outside Assets, runs in a separate process, and is excluded from Unity Player. All classes listed here are defined in `Tools/ReleaseQA/ProcessLifecycleProbe.cs`.

| Class | Responsibility |
| --- | --- |
| `ProcessLifecycleProbe` | Checks production services and profile recovery after forcefully terminating an isolated process. |
| `ProcessLifecycleProbe.ImmutableValues` | JSON converter for immutable configuration structs to read exported rules exactly. |
| `ProcessLifecycleProbe.Clock` | Fixed UTC clock for the verification scenario. |
| `ProcessLifecycleProbe.Bits` | RNG returning the lower bound for a controlled jackpot. |
| `ProcessLifecycleProbe.Stops` | Selects Scatter stops for a bonus-spin check. |

`AssemblyInfo.cs` in Domain/Application/Infrastructure contains assembly attributes and declares no classes. External packages and generated Library/build code are not custom project sources.

## Verification and Current Status

Run tests in Unity Test Runner: `IceP.Tests.EditMode` and `IceP.Tests.PlayMode`. The expensive `SlotSimulationTests.GenerateSection412Report` simulation must be run explicitly; ordinary runs include a short simulation. This test can regenerate a separate results report.

The previous verification on 2026-10-13 recorded 496 passing EditMode tests and 47 passing PlayMode tests, a Windows x64 build, and visual checks at five resolutions. Forced termination was verified on separate processes using production services and JsonSaveRepository. Terminating the graphical Unity Player directly during animation has not yet been verified; full acceptance of stage 4.13 remains conditional. Verification artifacts are in the ignored `Logs/Section413/` and `Builds/Section413/` directories. Gameplay tests were not rerun for this documentation update.
