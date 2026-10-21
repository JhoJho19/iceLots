# IceP
No real money slot MVP

## How to run

1. Open the project in Unity **6000.3.10f1** and let Unity finish importing assets.
2. Open `Assets/_Project/Scenes/SC.unity` and press **Play**.
3. In the main menu, select **Play** to open the slot; use **Back** to return.

## Tools and libraries

- **UniTask** — asynchronous UI sprite animation with cancellable delays.
- **Unity uGUI** — Canvas UI, buttons, text, sliders, toggles, and custom payline graphics.
- **Unity Input System** — UI input through `InputSystemUIInputModule`.
- **Universal Render Pipeline (URP), 2D Renderer** — rendering the 2D scene.
- **Unity 2D Sprite tools** — sprite import and atlas slicing with custom editor utilities.
- **Newtonsoft.Json** — profile serialization, strict JSON validation, and save recovery.
- **Unity Test Framework / NUnit** — EditMode and PlayMode tests, plus mathematical simulations.
- **Unity MCP** — editor integration for development and verification.

Reel animation uses custom `ReelView` and `ReelAnimationCoordinator` components.

Music and sound effects use the clips in `Assets/_Project/Audio`. Adjust clip assignments, base volumes, and win thresholds in `Assets/_Project/Configs/AudioConfigSO.asset`; the main scene already references it. Player music/effects sliders apply immediately and save with the profile.

## Architecture

- Game code lives in `Assets/_Project/Scripts`.
- `Domain` defines immutable configuration and slot mathematics without Unity dependencies.
- `Application` implements game flows and persistence contracts without Unity dependencies.
- `Infrastructure` supplies ScriptableObject configuration, JSON storage, clocks, and random sources.
- `Presentation` contains C# presenters, display models, and Unity UI components.
- Presenters handle user actions and call application services; views render UI and emit events.
- `SlotBootstrapper` composes dependencies, loads the profile, and recovers interrupted spins.
- `GameSession` holds confirmed player state and coordinates service commands.
- Spins reserve the bet or a free spin, save `PendingSpin`, animate, then persist settlement.
- Failed writes retain the proposed state for an exact retry.
- Weekly jackpots use a separate random source; daily bonuses use injected UTC clocks.
- `SC.unity` contains the menu, slot, and popups; navigation switches visibility in one scene.
- `Editor` contains UI authoring tools; `Tests` contains EditMode, PlayMode, and simulation checks.
- [Documentation.md](Assets/Docs/Documentation.md) lists all project types and scene component locations.
