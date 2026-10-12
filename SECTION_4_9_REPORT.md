# Section 4.9 — Main Menu, Settings, Daily Bonus

Implemented only Plan.md section 4.9, after reading TZ.md, Plan.md and the attached request. Section 4.10 remains untouched. No Spin button, reel/win/Scatter animations, Free Spins UI, JackpotHUD, audio playback, Remote Config or new gameplay rules were added.

## Files

Created (under `Assets/_Project/Scripts/`, each with a Unity `.meta`):

- `Application/SettingsService.cs`
- `Presentation/MainMenuPresenter.cs`
- `Presentation/SettingsPresenter.cs`
- `Presentation/SettingsView.cs`
- `Presentation/DailyBonusPresenter.cs`
- `Presentation/DailyBonusView.cs`
- `Presentation/DailyBonusDisplay.cs`
- `Editor/MenuUI/MenuUiAuthoring.cs`
- `Editor/MenuUI/IceP.MenuUI.Editor.asmdef`
- `Tests/EditMode/SettingsTests.cs`
- `Tests/EditMode/DailyBonusPresentationTests.cs`
- `Tests/PlayMode/MainMenuUiTests.cs`

Also created `Editor/MenuUI.meta` and this report. The authoring command is editor-only, explicit and idempotent: it adds serialized controls to the existing scene once and never runs during bootstrap.

Modified:

- `Assets/_Project/Scenes/SC.unity`
- `Assets/_Project/Scripts/Application/GameSession.cs`
- `Assets/_Project/Scripts/Application/GameNavigationService.cs`
- `Assets/_Project/Scripts/Application/DailyBonusService.cs`
- `Assets/_Project/Scripts/Bootstrap/SlotBootstrapper.cs`
- `Assets/_Project/Scripts/Presentation/GameNavigationPresenter.cs`
- `Assets/_Project/Scripts/Presentation/GameNavigationView.cs`
- `Assets/_Project/Scripts/Tests/EditMode/IceP.Tests.EditMode.asmdef`

Existing scene/config/sprite/script GUIDs are preserved. No prefab or sprite assets were changed and no additional scene was created. Unity-generated scene block ordering was normalized to keep the existing blocks in their original order and make the diff reviewable.

## Scene inspection and hierarchy

The actual scene already contained one Canvas, one EventSystem with InputSystemUIInputModule, MainMenuScreen, **GameScreen** (the Slot screen), PlayButton, SettingsButton, DailyBonusButton, BackButton, SettingsPopup and DailyBonusPopup. Both popups contained only Title, Placeholder and CloseButton. Existing GameNavigationView serialized all navigation references; GameNavigationPresenter implemented navigation and empty popup opening.

The existing main menu and GameScreen backgrounds/layout remain. Added to MainMenuScreen: Balance and MenuStatus text. Added AvailableIndicator under the existing DailyBonusButton. Balance is a confirmed menu display; Slot gameplay presentation remains unchanged.

SettingsPopup retains its fullscreen modal backdrop and gains AudioPreferences (760×650): existing Title, existing Placeholder repurposed as status/error text, Music toggle, MusicVolume slider/percentage, Sound Effects toggle, EffectsVolume slider/percentage, existing CloseButton and RetrySettings. The SettingsView component is attached to the existing popup root.

DailyBonusPopup retains its fullscreen backdrop and gains DailyRewards (900×730): existing Title, seven RewardDay cards in rows of four and three, RewardStatus, existing Placeholder repurposed as confirmation/error text, ClaimButton, RetryDailyBonus and existing CloseButton. Day seven has a wider card. DailyBonusView is attached to the existing root.

Only popup title/placeholder/close controls were reparented and repositioned. No existing GameObject was renamed or removed. The existing CanvasScaler retains its reference resolution (2436×1125) and uses Expand so fixed content fits narrower aspect ratios. Existing blue button color/font/sprite references are reused for Claim and Retry; new cards and slider/toggle graphics use simple colors consistent with the existing dark UI. No art/audio assets were generated.

The existing modal raycast backdrop blocks pointer interaction, while the menu CanvasGroup disables its controls. Intent handlers additionally reject Play and another popup while a modal is open. Closing returns to the same menu/session and selects the originating button.

## Settings persistence and audio integration

The versioned profile already had UserSettings with default music/sound enabled and both volumes at 1. SettingsService reuses that field and the existing SaveDataValidator. Values must be finite and in [0,1]; invalid/null/unsupported settings are rejected without saving. Unchanged settings do not write.

SettingsService is pure Application C#. Under GameSession.Gate and CommandExecuting it checks Idle/no PendingSpin, copies the latest confirmed whole profile, replaces only Settings and persists through the composed ISaveRepository. It never captures an economic snapshot when the popup opens. Thus a preference intent obtained before a later Daily Bonus or Spin settlement cannot overwrite newer balance, bonus, Free Spins or jackpot metadata. A duplicate settings owner is rejected.

Only positive repository confirmation commits the snapshot. Failed, thrown, null or ambiguous write results retain the exact proposed snapshot and block the session in Error. RetryPersistence writes that proposal unchanged; it does not rebuild economic state or accept another settings command. Current returns a copied, confirmed UserSettings object suitable for future audio integration; there was no existing playback controller to connect. No PlayerPrefs, alternate store, placeholder audio system or audio assets were added.

Toggles commit when changed. Sliders update percentages immediately and debounce writes until 0.4 seconds without another change. Close, application pause and quit flush the pending value. Scene unload flushes a captured preference intent without reading destroyed UI objects. Saving still passes through Application; failures never expose proposed values as confirmed. Hard process termination before any flush can lose the last sub-0.4-second edit.

## Daily Bonus display and Claim

DailyBonusPresenter queries DailyBonusService.GetStatus on open and refresh. DailyBonusDisplay is a pure display projection with Claimed/Available/Current/Future states. It uses RewardDay and Application-provided NextAvailableUtc to distinguish an already claimed period, day-seven wrap and a missed-day reset; it performs no clock, reward, eligibility or streak calculation.

All seven coin values come from status.Rewards. The popup shows confirmed progression, current/next reward, enabled/busy/blocked state, streak and next availability in UTC. Available cards are gold; claimed cards are green; future/current unavailable cards are dark purple. Disabled/busy/already claimed status disables Claim.

Opening never claims. Claim invokes DailyBonusService.Claim once. Only a confirmed result produces the received-coins message. Displayed balance is refreshed from the authoritative confirmed session, cards progress, Claim disables and the menu indicator clears. A duplicate Claim remains rejected by Application, including forced onClick invocation.

On persistence failure the popup shows a minimal 'not confirmed' message, retains the confirmed balance/progress and exposes Retry. The added CanRetry property only reports whether this service owns an unresolved proposal. Retry calls DailyBonusService.RetryPersistence, preserving the original reward, timestamp, streak and snapshot. Closing/reopening a failed popup retains access to the appropriate Retry. Another feature cannot bypass Error or commit over the unresolved proposal.

## Presenter/View and bootstrap responsibilities

Views contain serialized controls and render text/color/interactable/modal state. They do not access repositories, alter SaveData or award currency. Presenters handle user intent, query status, call Application and manage subscriptions. DailyBonusDisplay can be tested without Unity objects. No object-name search, singleton, global UI manager or service locator was introduced in runtime code. Object inspection/search in the authoring utility and tests is editor/test-only.

GameNavigationPresenter retains screen visibility, Back and selection. Its old popup/Play listeners were moved to MainMenuPresenter to prevent duplicate handlers. MainMenuPresenter handles Play, mutually exclusive modal opening/closing, confirmed balance and availability. Each presenter subscribes once and removes listeners on disposal; popup opening creates no services/listeners.

SlotBootstrapper remains the sole runtime composition root. It creates one SettingsService sharing the loaded config/repository/session, alongside existing Spins, DailyBonus and WeeklyJackpot. Recovery order remains load/validate → compose → recover PendingSpin → connect presentation. Initialization failure blocks controls and displays a short menu error. Initialization recovery connects presenters only after success.

The indicator refreshes at initial composition, screen changes/return to menu, successful claim, application resume/focus and a one-second unscaled visual timer. GetStatus uses the existing injected IClock; no UI local-calendar check or persistence polling occurs. Back availability keeps its existing cheap per-frame refresh.

## Corrections to earlier sections

- GameNavigationService previously allowed unguarded NavigateToSlot, including active/error commands. It now checks the shared gate, Idle/no PendingSpin and CommandExecuting. Back also rejects command reentrancy. Ordinary Play/Back and waiting Free Spins remain permitted; screen switches never create a new session or reload a scene.
- GameSession adds an ownership flag for SettingsService; DailyBonusService adds a read-only CanRetry capability. Existing Spin/Daily Bonus/jackpot business rules and persistence schema remain unchanged.

## Verification

Verification used the **connected running Windows Unity 6000.3.10f1 editor in the original project `C:/UnityGames/IceP`**, on 2026-10-12. The original project was already open, so batch launch exited without opening it; that attempt is not compilation/test evidence. The connected editor imported and compiled the scripts successfully. No validation copy was used in this stage.

Final results:

| Suite | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| EditMode (397 existing + 28 new) | 425 | 0 | 0 |
| PlayMode (7 existing + 7 new) | 14 | 0 | 0 |

EditMode job: `81f7e29871254d5d97df8834fb3e8fbc`; duration 4.5693922 seconds. PlayMode job: `2a1859e7cd5f4082a131ddf284f36506`; duration 10.791379 seconds. Results recorded under ignored `Logs/menu-*-final*-*.json`.

EditMode coverage includes defaults, all four preferences/reload, invalid volumes/version/null, unchanged no-op, stale preference intent after Daily Bonus/Spin, confirmed jackpot preservation, failure/ambiguous commit/exact retry/idempotency, thrown save, shared-gate reentrancy, another service's Error and duplicate ownership. UI projection tests cover configured rewards, no auto-claim, confirmed progression/duplicates, disabled status, day seven wrap, missed day reset, pending failures and injected-clock period transition.

PlayMode covers actual Button/Toggle/Slider interactions, startup/menu/Slot/Back, seven configured labels, one claim/confirmed balance/indicator, repeated modal opening and guarded synthetic clicks, saved settings/reward after reload, slider debounce, resume refresh, active Spin navigation/mutation restrictions, unchanged existing jackpot/Spin services, real locked-file persistence failures and exact Retry, and pending slider flush on scene unload/pause. All scene tests use isolated temporary save directories. Visual validation used a separate ignored `Logs/menu-visual-save` directory; real player progress was not modified.

Actual Game View screenshots were inspected for menu, settings and bonus, including final popup renders at 1920×1080. Visual inspection caught and corrected a white Claim background that made its white label unreadable. Final screenshots and console inspection are stored under ignored Logs. Immediately after the tests, Console contained only the three expected injected initialization failures from existing corrupt-profile/write-failure/recovery-failure smoke tests. After final visual validation, Console contained zero errors or warnings. Compilation/domain reload briefly produced the pre-existing MCP WebSocket-not-initialised warning. Existing Slot Engine, mathematics, persistence/recovery, Spin lifecycle, Daily Bonus and jackpot suites remain green. Git whitespace verification passes. The editor was left outside Play Mode with SC.unity active, the isolated-save environment override removed and the original Enter Play Mode option restored; no ProjectSettings changes remain.

## Concrete walkthrough

The new PlayMode test ConcreteWalkthroughPersistsRewardAndAudioThroughActualControls asserts:

```text
Start application
→ Main Menu visible, balance 20000, Daily Bonus indicator active
→ Open Daily Bonus (opening does not award coins)
→ Day 1 reward: 400 coins; seven configured rewards displayed
→ Claim
→ Confirmed balance 20400; Claim unavailable; indicator cleared
→ Close popup
→ Open Settings
→ Disable music/sound; set music volume 35%, effects volume 65%
→ Close Settings (flush preferences)
→ Play / Back preserves the same session and scene handle
→ Reload the same scene
→ Balance remains 20400; Daily Bonus remains claimed
→ Music volume remains 35%; effects volume remains 65%; toggles remain off
→ Open/close bonus again: Claim still unavailable
→ Play opens the existing GameScreen, with exactly one scene loaded
```

## Remaining limitations

- Audio playback is not yet implemented; confirmed preferences are ready for future integration.
- No target-player build, physical mobile device/aspect-ratio device testing or OS power-loss test was performed. Verification is Windows Editor compilation, automated tests and Game View inspection. CanvasScaler fits content across aspect ratios; very narrow screens scale the existing landscape design down rather than reorganizing it.
- Saves and the local device clock retain existing local-game integrity/rollback limitations. Hard termination before a slider flush may lose its most recent pending edit.
- If a UI write fails during scene destruction/quit, no confirmation is shown; restart uses existing persisted-profile/recovery policy. An ambiguous commit may already be durable, as in existing transactions.
- UI text is English, matching the existing controls; localization, advanced animation/polish and a generic startup recovery/error framework were not introduced.
- Section 4.10 and later gameplay presentation remain pending explicit authorization.
