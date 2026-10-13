# Section 4.11 — Persistent JackpotHUD

Verified on 2026-10-13 in the original `C:\UnityGames\IceP` project, Unity 6000.3.10f1. No temporary project copy was used. Scope is section 4.11 only; section 4.12 was not started.

## Prerequisite: section 4.10

Before changing files, inspected SlotBootstrapper, SlotPresenter/SlotView, ReelView, ReelAnimationCoordinator, ReelCompletionBarrier, PaylineView, SlotDisplay, SC.unity and existing Presentation tests. Verified SpinService integration, five separately animated reels, staggered stops, confirmed balance/bet controls, Free Spins, payline/Scatter highlighting, CompleteSpin after all reels finish, failed settlement/Retry, and Application/UI Back restrictions.

Actual baseline run before implementation:

| Mode | Passed | Failed | Skipped | Job |
|---|---:|---:|---:|---|
| EditMode | 446 | 0 | 0 | b8e1226c56a14d0da164b5a454cb8ee2 |
| PlayMode | 27 | 0 | 0 | df0e2a6475bc46b583b349c925777f39 |

The functional 4.10 prerequisites were complete before implementing 4.11. A pre-existing aspect-ratio limitation discovered during the later visual review is documented below.

## Files and authored hierarchy

Created (with new Unity .meta files):

- `Assets/_Project/Scripts/Presentation/JackpotDisplay.cs`
- `Assets/_Project/Scripts/Presentation/IJackpotView.cs`
- `Assets/_Project/Scripts/Presentation/JackpotPresenter.cs`
- `Assets/_Project/Scripts/Presentation/JackpotView.cs`
- `Assets/_Project/Scripts/Editor/SlotUI/JackpotUiAuthoring.cs`
- `Assets/_Project/Scripts/Tests/EditMode/JackpotPresentationTests.cs`
- `Assets/_Project/Scripts/Tests/PlayMode/JackpotHudTests.cs`
- `Assets/Docs/Section411Report.md`

Modified `SlotBootstrapper.cs`, `SlotPresenter.cs`, and `Assets/_Project/Scenes/SC.unity`. Domain, Application, Infrastructure, production probabilities, reel strips, existing asset GUIDs and existing test files were preserved. No scene or graphical/audio asset was created.

```text
Canvas (existing)
  GameScreen (existing SlotView and artwork)
    ImageGameField / SlotWheels (existing five reels)
    SlotHUD (existing balance, last win, Free Spins)
    SlotControls (existing Spin, bet, Retry, status)
    BackButton (existing)
    JackpotHUD (new Image, Outline, JackpotView)
      Title
      Amount
      Status
      Availability
      Reset
SlotBootstrapper (existing; serialized jackpotView reference added)
```

Banner: centered anchors, position (0, 400), size 1250 × 110 in the existing Canvas coordinates; above the reel field and below the existing counter labels. Existing CanvasScaler conventions are retained. The authoring menu is idempotent and reuses the existing HUD if called again.

## Responsibilities and lifecycle

`JackpotDisplay` formats the Application DTO, invariant grouped coin amounts, state, availability explanation and reset countdown. It contains no week calculation, economic mutation, persistence or RNG.

`JackpotView` owns serialized UI references, state colors and a two-second gold/scale pulse with `JACKPOT WON!`. The amount, status and reset remain visible during the effect. Large amounts use bounded font fitting. OnDisable/OnDestroy stop the coroutine and restore the title, scale and colors. Cancellation never settles a Spin or changes money.

`JackpotPresenter` owns GetStatus queries and subscriptions. Bootstrap supplies the same existing WeeklyJackpotService and IClock used by gameplay. It refreshes on construction, Slot navigation, view activation, confirmed paid/free settlement, Retry confirmation, focus and resume. Bootstrap forwards Time.unscaledDeltaTime to one throttled Tick; while Slot is visible, GetStatus is queried approximately once per second. GetStatus reads the confirmed in-memory session, not disk. No countdown update writes a save or consumes RNG. Disposal detaches view/navigation/settlement subscriptions and cancels effects. Reopening Slot retains the same service and presenter.

The countdown subtracts IClock.UtcNow from Application-provided NextResetUtc, showing days/hours/minutes/seconds. Null is displayed as `Next reset unavailable`; a due/past timestamp is clamped to `Reset due`. Reset weekdays, UTC times and WeekId remain Application-owned. A future StartUtc is shown as a UTC availability date; disabled events remain visible as Inactive.

## Spin and settlement integration

SlotPresenter emits `SettlementConfirmed(activeSpin, result)` only in its existing successful Confirm path, after CompleteSpin or successful settlement Retry has returned a matching SettledSpinId. JackpotPresenter validates Success and matching ID, refreshes authoritative status, and requests celebration only for a participated, winning paid Spin. A set of celebrated SpinIds suppresses repeated notifications.

The potential result cannot trigger the effect. Failed settlement retains the confirmed in-memory status and balance; Retry commits the original proposal without another StartSpin, animation or draw. No second payout, balance update, CompleteSpin or save was added.

Free Spins retain the banner and use the existing queue/BonusBet logic. They do not participate in jackpot or request celebration. Their confirmed completion still refreshes status for time changes.

At a weekly boundary, current eligibility comes from GetStatus. A Spin crossing the boundary settles against its captured week, can celebrate that confirmed win, and displays current-week Active when Application reports renewed eligibility. Presentation never changes saved WeekId.

## Verification results

| Final full run | Passed | Failed | Skipped | Job |
|---|---:|---:|---:|---|
| EditMode | 468 | 0 | 0 | d4ae5eeb48494d8080cc836769a91edb |
| PlayMode | 40 | 0 | 0 | e360f0b3cba44f7793251dfb039c638d |

Added 22 EditMode and 13 PlayMode tests. Unity compilation completed without C# errors. All original tests remained in the full runs, including Slot mathematics/engine, outcome generation, persistence/Spin lifecycle, WeeklyJackpotService, Daily Bonus, Settings, main-menu navigation and SC.unity smoke tests. Test scene loads used isolated temporary save directories, FakeClock-equivalent injected clocks and deterministic RNG; production reel strips/probabilities were unchanged.

New tests cover configured/large amounts, all states, disabled/future events, countdown/null/boundary formatting, throttling/unscaled refresh, potential versus confirmed wins, failed settlement/Retry, duplicate notifications, no UI writes/RNG, Free Spins, exact reset/cross-week settlement, repeated navigation, destruction, reload, actual reel animations and control/HUD layout separation.

Actual Game View was inspected at 16:9, 4:3 and 19.5:9. Jackpot text and banner remain visible and separate from the reels and existing counter/control text. Actual deterministic confirmed-win rendering showed `JACKPOT WON!`, `100,000 COINS`, `WON THIS WEEK`, `Already won this week`, with one celebration and confirmed 119,000 balance (20,000 − 1,000 + 100,000).

Local screenshots are temporary verification artifacts, not gameplay assets:

- `Temp/JackpotHUDVerification/active.png` — 16:9 initial Active.
- `Temp/JackpotHUDVerification/active-4x3.png` — 4:3.
- `Temp/JackpotHUDVerification/active-wide.png` — 19.5:9.
- `Temp/JackpotHUDVerification/during-spin.png` — HUD visible while reels animate.
- `Temp/JackpotHUDVerification/confirmed-win-native.png` — confirmed settlement and celebration, captured by native Unity ScreenCapture at end of frame.

Expected error logs from existing corrupted-save/write-failure tests were matched by LogAssert. During an additional MCP screenshot attempt, the plugin's `ScreenshotUtility.CaptureCompositedAfterFrame` at `ScreenshotUtility.cs:197` caused recursive PlayerLoop and profiler errors and produced a blank capture. This capture is not evidence of UI rendering. Native ScreenCapture subsequently produced the valid confirmed-win image with no Console errors/warnings. A focused HUD PlayMode rerun after the capture-tool incident passed 13/13, with zero failed/skipped (job `06f0d4f196d94f40b168f1f511a7b989`). Original Game View size, background execution preference and test-save environment override were restored; Editor was left out of Play Mode.

## Deterministic walkthroughs

The deterministic fixture uses the real scene UI, unchanged configured probability/strips, and an injected RNG returning zero bits to win the Bernoulli trial.

```text
Open Slot → 100,000 COINS / ACTIVE / Application-derived reset countdown.
Start paid Spin at bet 1,000 → saved balance 19,000; banner stays visible.
Potential jackpot wins → still ACTIVE; celebration count 0.
Five reels animate/stagger/stop → CompleteSpin commits original result.
HUD → WON THIS WEEK; amount retained; celebration count 1.
Duplicate confirmed notification → celebration count stays 1; no credit repeats.
Another paid Spin → no participation/draw for the exhausted week.
FakeClock reaches next Monday 00:00 UTC → one Tick displays ACTIVE.
No save/RNG is performed by that refresh; saved week metadata is unchanged.
```

```text
Start winning paid Spin → 19,000 confirmed balance / ACTIVE / no celebration.
Inject settlement save failure → Error; Retry visible; Spin/Back blocked.
One-second refresh → ACTIVE / no celebration / no optimistic balance.
Retry while save still fails → no celebration.
Remove failure and Retry → original SpinId commits; WON THIS WEEK.
Celebration count 1; original draw count unchanged; reels do not replay.
Duplicate Retry/completion → no repeated payout or celebration.
```

Cross-week scenario is also deterministic: accept two seconds before reset, advance four seconds, complete; the old captured week is saved, the win celebrates once and the HUD displays the current week's Active status.

## Earlier-stage corrections and remaining limits

No economic or gameplay corrections to earlier stages were necessary. The 4:3 visual check exposed an existing Back button placement partly outside the right viewport; the new HUD does not cause or overlap this. Large existing balance/last-win labels can also touch with six-digit payouts. Those existing controls/labels were not redesigned because this request restricts scene changes to section 4.11 UI and references. These are earlier-layout limitations, not a claim that every aspect ratio of the entire Slot UI is polished.

The screenshot plugin incident above is an editor/tool limitation; the native capture and HUD tests validate the working scene separately. After the final documentation/scene-whitespace import, the Unity bridge readiness ping did not respond; Editor.log independently confirmed that SC.unity was loaded and the files imported. This late readiness query does not supply an additional test result. No standalone player build, physical-device QA, audio expansion, Monte Carlo simulation or final lifecycle audit was performed. Those are outside section 4.11. Verification screenshots under Temp may be removed by Unity. No section 4.12 work was started.
