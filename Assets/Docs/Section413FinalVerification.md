# Section 4.13 — final integration and verification

Executed 2026-10-13 in the original `C:/UnityGames/IceP` project, Unity **6000.3.10f1**, Windows Editor. No project copy, SDK download, package installation, target switch, production probability override or mathematical change was used. Scope ends at 4.13; 4.14 was not started.

**Assessment:** implementation corrections and Editor integration are verified; Windows x64 player compilation succeeded. Full release acceptance remains conditional on a graphical-player process-kill walkthrough. Real OS termination was tested on separate processes running the production economic services, not on the graphical Unity player during reel animation. This distinction is intentional and must not be omitted.

## Reviewed baseline and preserved work

Read TZ.md and Plan.md, the attached request, section 4.9 report, existing 4.10 implementation/tests, section 4.11 report, section 4.12 verification and the actual simulation report. Inspected SC.unity, its live hierarchy, bootstrap composition, runtime presenters/views, SpinService, shared GameSession command gate, persistence implementation/codec/validator/fakes, existing EditMode and PlayMode fixtures, build settings and assembly boundaries.

At entry, SC.unity, SlotBootstrapper.cs and SlotPresenter.cs were already modified, and the 4.11/4.12 HUD, tests and reports were untracked. Those legitimate changes were preserved. SlotPresenter.cs received **no new 4.13 edits**. Baseline copies of the three modified files are retained under ignored `Logs/Section413/`. No branch reset or unrelated changes were discarded.

Baseline: 496 ordinary EditMode passes, 40 PlayMode passes, plus one previously executed explicit million-spin validation. Sections 4.9 and 4.11 have reports; a separate 4.10 report was not present, so its source, authored hierarchy and tests were reviewed directly.

## Files changed by this stage

Modified:

- `Assets/_Project/Scenes/SC.unity` — 26 scene property changes relative to the preserved entry snapshot: Back margin/pivot, three separated Slot HUD containers, bounded font fitting and menu balance fitting.
- `Assets/_Project/Scripts/Bootstrap/SlotBootstrapper.cs` — missing UI references now use the safe initialization-failure path; existing 4.11 composition remains intact.
- `Assets/_Project/Scripts/Presentation/GameNavigationView.cs` — null-safe failure display disables every remaining navigation control.
- `Assets/_Project/Scripts/Presentation/SlotView.cs` — idempotent listener installation and cancellation when the component itself is disabled.
- `Assets/Docs/Plan.md` — records verified items and explicitly leaves the graphical-player kill requirement open.

Created:

- `Assets/_Project/Scripts/Tests/PlayMode/FinalIntegrationTests.cs` and `.meta` — seven integration tests.
- `Tools/ReleaseQA/ProcessLifecycleProbe.cs` — standalone process probe using original compiled Domain/Application/Infrastructure assemblies; outside Assets and excluded from players.
- `Tools/ReleaseQA/RunProcessLifecycleProbe.ps1` — builds the probe with the installed Unity Mono compiler, kills only owned probe PIDs and verifies two subsequent restarts per scenario.
- `Assets/Docs/Section413FinalVerification.md` and `.meta` — this report.

Generated evidence and build outputs are under ignored `Logs/Section413/` and `Builds/Section413/`. Unity's automatic build serialization changes to URP/Graphics/Player settings were restored because those files were clean at entry. Temporary PerformanceTestRun resources were removed by the test framework. No production configuration, reel strip, paytable, assembly definition or save schema changed.

## Corrections

**Overlapping Balance/LastWin:** entry SlotHUD had scale `(2,2,2)`. Three 420/400-wide children at x = −236/0/236 had intersecting text containers. Larger payouts exposed the defect. HUD now has unit scale, three 620 × 90 containers at x = −650/0/650 with 30-unit gaps, centered at y = 505. The apparent normal font size is retained through 64-point monetary text (60 for Free Spins), with a 32-point lower bound. Exact grouped integers are retained; exceptionally long values can wrap inside their own container. No saved value is abbreviated or altered. All requested value pairs and Int64.MaxValue were rendered; every monetary character remains visible. HUD text stays above JackpotHUD and inside the reference Canvas height.

**Back safety margin:** current entry scene already used upper-right anchors, and a stable native 4:3 before-capture did not reproduce the old clipping report. The CanvasScaler uses **Expand**, reference **2436 × 1125**; its serialized `.85` match value is inactive in that mode. Transient Game View/Canvas sizing must not be mistaken for a final rendered layout. Back now uses upper-right anchors **and pivot**, with explicit 64-unit right/top inset, retaining the existing 166 × 144 size and sprite. Final native captures and EventSystem raycasts verify visibility/clickability across all requested resolutions. Application navigation guards are unchanged.

**Component disable:** disabling SlotView alone previously left its separate reel coordinator running, permitting a later successful animation callback. SlotView.OnDisable now cancels the coordinator. Cancellation emits no successful completion, never awards money and leaves the durable reservation for restart recovery. This is covered using an actual reserved Free Spin.

**Repeated Initialize:** installing SlotView listeners twice previously duplicated button and animation subscriptions. Reinitialization still refreshes visual configuration, but listener wiring happens once. The test observes exactly one SpinRequested and one successful AnimationFinished.

**Missing UI references:** the early bootstrap validation path previously logged an error without disabling the menu. It now disables the remaining controls and shows a readable error through null-safe view operations. Missing UI, invalid grid configuration and missing required symbol sprite are tested without exception cascades or partially connected presenters.

## Executed tests

| Run | Passed | Failed | Skipped | Duration | Unity job |
|---|---:|---:|---:|---:|---|
| Complete ordinary EditMode | **496** | **0** | **1 explicit** | 5.6804916 s | `385a2fdb426140b2b4674d95129868dc` |
| Complete PlayMode (40 existing + 7 new) | **47** | **0** | **0** | 91.0994895 s | `be044f15be484e19bb3a60d3ee883215` |
| Final native-capture revalidation after improving capture synchronization | **1** | **0** | **0** | 22.9824981 s | `d78d7cb76c1e46a3905b50ea9c3cb230` |

After correcting temporary Game View preset cleanup, all seven FinalIntegrationTests were rerun: job `63b2bd7d167143a788ba51fe0f31de7d` reports **succeeded**, 7 completed, no failures. A domain reload dropped its detailed result payload, so no additional measured test-duration claim is made. This rerun does not increase the unique PlayMode count of 47.

The last targeted run is a rerun of one of the 47 tests, not an extra test in the suite. No EditMode cases were added because economic, mathematical and deterministic concurrency boundaries already had substantial coverage. The million-spin test remained explicit and was not rerun.

Seven new PlayMode tests cover the complete pointer/raycast workflow, disabling the Slot component during a Free Spin, reinitialization without duplicate listeners, missing UI references, invalid configuration/missing sprite, backup/unsupported-version startup, and five-resolution rendering with exact monetary values.

Initial new-test run: 1 passed / 4 failed. Three failures were test synchronization errors (raycasts before activated UI updated); one compared pixel-scaled TextGenerator font size to an unscaled minimum. Pointer tests now yield a real frame and use the actual raycast target; font checks account for pixelsPerUnit. A later review found some asynchronous screenshots were black despite valid geometry assertions. Capture now waits for stable frames, checks decoded dimensions/nonblank pixels and retries boundedly. Final runs pass; no failures remain open.

Final Editor cleanup also exposed an `Invalid index 6 7 22` QA error: RemoveCustomSize expects a total index, not an index within custom presets. Test teardown now uses GetTotalCount. The persisted original preset list (last written before this task) was reloaded, this stage's temporary sizes were removed with total indices, and the original selection was restored. The preset file itself was never saved/overwritten. The corrected integration group rerun succeeded; this error was in verification cleanup, not gameplay.

## Full workflow through actual authored controls

`EntireWalkthroughUsesOneSessionAndReloadsOneConfirmedProfile` loads SC into an isolated save directory. It sends EventSystem pointer clicks through the real GraphicRaycaster to Settings, Close, Daily Bonus, Claim, Close, Play, bet increase, Spin and Back. Toggle/Slider changes use their real UI events.

Observed: fresh balance 20,000; music/sound disabled and volumes .35/.65 persisted; opening Daily Bonus awards nothing; Day 1 Claim adds 400 exactly once; selecting bet 400 and accepting Spin leaves a durable 20,000 balance before animation. Same GameSession and scene handle remain in use. API Back rejects the active Spin. Five reels animate, settle against the generated matrix, confirmed balance becomes `20,000 + PotentialPayout`, LastWin updates, paylines reflect the evaluated result and PendingSpin clears. Back then returns without reloading. Scene reconstruction reloads the confirmed money, settings, daily state and bonus count, and duplicate completion recognizes the already settled Spin.

Deterministic existing fixtures additionally exercise losing results, multiple winning lines, five Scatter, retriggers, simultaneous normal+jackpot wins and exact visual stops. Randomness in the broad production walkthrough does not determine its assertions; fixed RNG fixtures verify particular winning/losing outcomes.

## Spin lifecycle, concurrency and settlement audit

The retained production flow is Idle → Preparing → Spinning → Settling → Idle. The whole reservation is synchronously saved before engine/jackpot draws and before view animation. CompleteSpin is called by the successful five-reel barrier only; confirmed UI and jackpot celebration follow successful settlement persistence. Interrupted/disabled/destroyed views emit cancellation, not successful settlement.

Executed SpinLifecycleTests, SlotPresentationTests, SlotUiTests, DailyBonusTests, SettingsTests, WeeklyJackpotTests, JackpotPresentationTests and JackpotHudTests verify: rejected commands consume no RNG; one paid debit/free reservation; duplicate/stale SpinIds cannot settle a newer transaction; Scatter and jackpot commit once in the same snapshot; PendingSpin clears; failed acceptance/settlement retain exact proposals; ambiguous commit-then-fail retries do not double-debit/credit; all writers share the same session/gate; command reentrancy and another service's Error are rejected; settings cannot overwrite newer economic snapshots; Daily Bonus cannot mutate an active Spin; Back cannot bypass the transaction guard. Free queues permit ordinary menu navigation without creating a new session.

## Interruption evidence and its limits

**Actual OS process kills:** `RunProcessLifecycleProbe.ps1` launched four separate console processes with copied, original production assemblies and a JSON export of the actual runtime configuration. The probe asserts that the exported configuration validates and round-trips exactly, including immutable payout/Scatter values. It invokes the production SlotEngine, SpinService, WeeklyJackpotService and JsonSaveRepository. Deterministic test-only reel/jackpot sources produce five Scatter and a potential jackpot; production assets remain unchanged. Once a committed checkpoint exists, PowerShell calls Process.Kill on that owned PID. No quit callback is executed.

Evidence: `Logs/Section413/ProcessProbe-20261013-114122/`, containing ready, save/backup and verified snapshots. Each recovery was run in **two fresh processes**.

| Killed checkpoint | PID | Confirmed after both restarts |
|---|---:|---|
| Before Spin acceptance | 24416 | 20,000 balance, no pending/payout/Free Spins/jackpot |
| Accepted paid Spin, before settlement | 7256 | 19,000 balance, pending cleared, no payout/Scatter reward, weekly wins 0 |
| Accepted Free Spin from queue of 3 | 12792 | 20,000 balance, queue 2, BonusBet 800, no payout/retrigger/jackpot |
| Committed five-Scatter + jackpot settlement | 9060 | 119,000 balance, LastWin 100,000, queue 20, BonusBet 1,000, weekly wins 1; duplicate settlement adds nothing |

**Runtime reconstruction / Presentation tests:** existing paid-spin view destruction/disable tests and the new Free Spin component-disable test run actual reels, stop/cancel callbacks and reconstruct SC. Stake/free-spin loss, no potential jackpot allowance consumption and no stale settlement are confirmed. Pause/resume in a live process completes normally, without refund, duplicate draw or pause-triggered payout. These are not OS kills.

**Not executed:** force-killing the graphical Unity Player while reels visibly animate, physical power loss or device OS suspend/kill. Native desktop UI automation is unavailable in this session, and launching the ordinary Player without an isolated profile would risk using the real player's save. No claim of that graphical-player walkthrough or power-loss durability is made. Plan 4.13 item 3 remains open for that explicit acceptance check.

## Persistence and startup

JsonSaveRepository, SaveJsonCodec, SaveDataValidator, save versions and FakeSaveRepository were reviewed and their ordinary tests executed. Primary/backup/temp behavior, malformed/missing fields, unsupported profile/settings versions, actual file-lock/temp/backup write failures, exceptions, immutable whole snapshots, fresh install, exact retained retry, reentrant writers and controlled total-corruption failure pass. The new actual-scene test corrupts primary after two commits, restores the known 12,345 backup, then writes unsupported SaveVersion 99 and confirms startup blocks without replacing that file. Existing startup recovery retry remains green. Initialization does not connect presenters until configuration, profile and pending recovery are verified.

No synchronous persistence or two-phase design was replaced. Durability limits remain: portable delete/promote is not a journaled multi-file transaction; recovery from backup may restore an earlier economic snapshot; there is a short replacement gap; power-loss guarantees depend on filesystem/OS write ordering and are not proven by Flush(true) alone. Uncommitted temp files are not auto-promoted. Local JSON is not tamper-resistant, and local time/jackpot allowance has no server authority.

## Daily Bonus, Free Spins and Weekly Jackpot

Existing deterministic FakeClock/RNG fixtures passed all configured 3/4/5 Scatter awards (8/12/20), locked BonusBet, no coin debit/automatic autoplay, retriggers, queue exhaustion, saved remainder, interrupted reservation loss and no Free Spin jackpot draws. Actual authored Slot controls and reel highlights were exercised.

Daily fixtures pass first claim, configured seven rewards, duplicate/reentrant clicks, UTC boundary, missed-day policy, wrap, restart and exact failure/ambiguous Retry. Actual popup shows all seven rewards and opening does not claim. Settings save/debounce/pending scene-unload flush and its Retry remain passing.

Jackpot fixtures retain separate RNG streams, configured chance/bet, no draw for rejected/free spins, simultaneous line win, weekly cap/reset, event date bounds, captured acceptance WeekId across reset and no allowance consumed by interruption. HUD reflects confirmed state and stays visible; failed settlement does not celebrate, successful Retry celebrates once, duplicate notification cannot repeat it. These deterministic fixtures were rerun, not inferred solely from the old report.

## Native rendered layout

Final evidence: **40 decoded, nonblank PNGs** in `Logs/Section413/Captures-20261013-114422-776/`. Earlier capture folders are diagnostic attempts, not the final visual evidence. Capture uses Unity ScreenCapture queued at ordinary frame end; the recursive screenshot plugin, manual Camera.Render and forced PlayerLoop were not used.

For each resolution, captured Main Menu, Settings, Daily Bonus and five Slot value cases: 20,000/0; 119,000/100,000; 999,999/850,000; 123,456,789/1,234,567; Int64.MaxValue/Int64.MaxValue. Monetary projections are detached in-memory DTOs and never save fabricated money. Automated geometry/character visibility/raycast checks supplement actual image inspection.

| Resolution | Main Menu / popups | Slot result |
|---|---|---|
| 1920 × 1080, 16:9 | Controls, seven cards and Close visible | Five reels/15 symbols, Spin/bet/Back, distinct money, readable HUD |
| 1600 × 1200, 4:3 | Controls, cards, text and Close visible | Back fully inside viewport; large balances do not overlap LastWin |
| 2340 × 1080, 19.5:9 | Controls, cards, text and Close visible | Exact large values fit; reels and persistent jackpot separate |
| 1920 × 1200, 16:10 | Controls, cards, text and Close visible | All slot controls and money remain usable |
| 2436 × 1125, reference | Controls, cards, text and Close visible | HUD stays within reference height; masks show intended 3 rows |

Representative final files: `1600x1200-slot-1.png`, `1920x1080-slot-3.png`, `2340x1080-slot-3.png`, `1920x1200-slot-3.png`, `2436x1125-slot-3.png`; popup files use `-menu`, `-settings`, `-daily`. Retry behavior is exercised through actual controls by existing locked-file tests; the final visual matrix shows normal popup/slot states, not every error/celebration state at every ratio. Existing ice sprites/artwork and button identity remain intact.

## Build, console and lightweight performance

SC.unity is enabled at build index 0. Scene validation reports **0 missing scripts, 0 broken prefabs, 0 issues**. Production runtime sources have no UnityEditor/test utility references; friend-assembly declarations only permit tests to inspect internal APIs. Editor authoring assemblies are Editor-only. Built Managed output contains all five IceP runtime assemblies and no IceP.Tests/Editor/NUnit assemblies. Required SO/visual references are present and validated.

**Actual Windows x64 Mono build succeeded**, using current target/settings and DetailedBuildReport + StrictMode. Unity BuildReport: output `C:/UnityGames/IceP/Builds/Section413/IceP.exe`, **221,717,469 bytes**, **0 errors**, **1 warning**, **126.279 seconds**. Editor log independently records Build Finished: Success. MCP build-status state was stale after domain reload; the native BuildReport and completed build log establish success.

Warning: existing `com.unity.pipeline` reports no RuntimePipelineConfig asset and disables its optional Pipeline integration in Player builds. No package/config installation was attempted. The build itself is proven; an interactive standalone-player walkthrough was not executed. Android/iOS/WebGL/macOS/Linux compilation or runtime was not verified.

After the full tests, console had seven **expected LogAssert initialization errors** plus one expected backup-recovery warning, all injected by failure tests. No unexpected exception/MissingReferenceException was observed. Final capture revalidation produced no unexpected gameplay errors. A pre-existing MCP WebSocket-not-initialised warning appeared during compilation; the Pipeline build warning is recorded above.

Lightweight performance check: actual five-reel animation retained a stable scene GameObject count during the integration test. Reel updates reuse cells and do not instantiate per frame. Ordinary navigation retains the same presenters/services; menu refresh queries in-memory status, HUD refresh consumes no RNG/writes, and production logging is not continuous. JSON snapshots allocate during synchronous transactions; no new threading/async persistence or global manager was added. This is a practical integration/code audit, not a device FPS benchmark or deep managed-allocation profile.

Production asset SHA-256 values still match 4.12:

- SlotGameConfigSO: `B73EF6368D8727AACC7022E9D999429006361CA0C3BCAF4D273AC5C37AF992D2`
- SlotVisualConfigSO: `F32E9DF61953D494D8F99449A8E747BA9F8885E7AF77203E84FE6948E18A6AEC`

Mathematical regression remains green. The existing million-spin report is retained: base 85.404745%, Free Spins contribution 6.294165%, total 91.698910%, theoretical total 92.088916%, 95% interval 91.12225–92.27557%. No rebalancing was performed. Final whitespace validation passes; editor save override/Game View selections are restored and SC remains editable outside PlayMode.

## Acceptance status

**Verified and complete:** targeted layout/lifecycle/startup fixes; 496 EditMode and 47 PlayMode cases; full authored UI workflow/reload; deterministic math, Free Spins, Daily Bonus, weekly jackpot, shared command guards, locked-save failures/Retry, startup corruption/recovery, missing references, five-resolution native renders and actual Windows x64 player build.

**Verified with limitations:** real OS kill/restart of production economic-service processes; live Editor pause/resume and destroyed-view recovery; local portable persistence durability; lightweight performance audit; player compilation with the optional Pipeline warning. These are not graphical-player or physical-device certification.

**Not verified / remaining work:** graphical Unity Player kill during a visible paid/free Spin and subsequent interactive restart; power loss; hardware/device suspend; standalone-player interactive walkthrough; unbuilt platforms and sustained device performance. Plan item 3 and unconditional production-readiness are not marked complete. No additional implementation phase was started.

## Reproduction

Run ordinary EditMode and all PlayMode tests in Unity Test Runner. Select `FinalIntegrationTests.RenderedAspectRatiosKeepControlsAndExactLargeAmountsReadable` to regenerate synchronized screenshots. Tests isolate temporary save directories and restore environment/view settings in teardown.

For the process probe, export the actual config from the Editor with `JsonConvert.SerializeObject(new LocalSlotConfigProvider(AssetDatabase.LoadAssetAtPath<SlotGameConfigSO>("Assets/_Project/Configs/SlotGameConfigSO.asset")).Load(), Formatting.Indented)` to `Logs/Section413/config-reference.json`, then run `Tools/ReleaseQA/RunProcessLifecycleProbe.ps1` from the project root. It compiles against current Library/ScriptAssemblies using the installed Unity Mono runtime, validates exact config round-trip and creates a fresh ignored evidence directory for every run. It never includes probe code in the player or accesses the real player's profile.
