# Plan.md — Unity Social Slot Development Plan

**Basis:** `TZ.md`, version 1.0.
**Approach:** iterations with a verifiable result after each stage, following the supplied reference `PLAN.md`.
**Priority:** correct mathematics, persistence, and the Spin lifecycle → UI/animations → polish.

## 1. Goal

Build a local social slot game in Unity 6 with a 5 × 3 grid, eight symbols (including Scatter), 20 paylines, Free Spins, a balance, Daily Bonus, and a personal Jackpot of the Week. The game starts from one scene and has two screens:

- Main menu: `Play`, `Settings`, `Daily bonus`.
- Slot: reels, bets, balance, Spin, Back, and an always-visible JackpotHUD.

The main architectural requirement is a pure C# Domain, Application for transactions and state, Presentation for UI/animations, Infrastructure for configuration and persistence, and `SlotBootstrapper` as the composition root. No network server; `ScriptableObject` now, with possible `Remote Config` support later.

**Accepted interruption policy:** save the Spin cost **before** animation and the reward **after** successful animation completion. Returning to the menu during a Spin is prohibited. If the process stops before the Spin finishes, the bet (or 1 Free Spin) is lost and no winnings are awarded. This decision must be reflected in code and tests.

## 2. Tools and Working Rules

- Unity 6, C#; Unity Test Framework (`EditMode`, `PlayMode`).
- UniTask where needed; any tween library belongs only in Presentation.
- ScriptableObject for local configuration; Unity `Application.persistentDataPath` for repository-based persistence.
- Git with small, meaningful commits. AI tools are allowed; verify their output with tests.
- Domain and Application compile into separate `asmdef` assemblies without Unity references.
- Adopt the staged structure, invariants, and testing approach from the reference Battleship project. **Do not port** its network protocol, server, reconnect logic, or poor-network simulation: the local slot does not need them.

## 3. Fixed Architectural Decisions

1. `SlotBootstrapper` composes dependencies when the single scene starts.
2. `ISlotConfigProvider` converts `SlotGameConfigSO` into a validated pure C# `SlotRuntimeConfig`.
3. `SlotVisualConfigSO` contains Sprite/Prefab references and visual parameters and stays outside Domain.
4. `OutcomeGenerator` selects random stops; `WinEvaluator` and `ScatterEvaluator` calculate rewards; `SlotEngine` does not change the balance.
5. `SpinService` is the sole entry point for spins and follows two-phase debit/settlement persistence.
6. `GameSession` is the only source of `Idle/Preparing/Spinning/Settling/Error` state; navigation is controlled by the use case, not just `Button.interactable`.
7. `BalanceService`, `DailyBonusService`, and `WeeklyJackpotService` have no knowledge of UI.
8. `ISaveRepository` saves a complete versioned snapshot with `PendingSpin` and a backup; after restart, an unfinished Spin is cancelled **without refunding the bet**.
9. `IClock` supplies time for Daily Bonus and the weekly jackpot; `IRandomSource` supports an explicit test seed.
10. All animations display an already calculated result, but **do not confirm a reward until settlement has been saved**.

## 4. Implementation Plan

### 4.1. Prepare the Unity Project, Structure, and Single Scene

1. Check Unity 6 and add Test Framework / UniTask (if needed).
2. Create `Assets/_Project` and separate `Domain`, `Application`, `Infrastructure`, `Presentation`, `Bootstrap`, `Configs`, and `Tests` folders.
3. Create the corresponding `asmdef` files; prohibit Domain/Application dependencies on Unity.
4. Create **one** `Game.unity` with `SlotBootstrapper`, `Canvas`, `EventSystem`, `MainMenuScreen`, `SlotScreen`, `SettingsPopup`, and `DailyBonusPopup`.
5. Set the initial UI state: only Main Menu is visible; the slot is inactive.
6. Add a basic `.gitignore`, a README draft, and a valid empty startup flow.

**Completion criterion:** the scene starts with the main menu, and `Play` switches screens without loading a second scene; asmdef assemblies compile.

### 4.2. DTOs and Configuration (ScriptableObject → Runtime)

1. Create pure data types: `SlotRuntimeConfig`, `SymbolId`, `ReelStrip`, `Payline`, `Paytable`, `DailyBonusRules`, and `JackpotRulesConfig`.
2. Create `SlotGameConfigSO`, `SlotVisualConfigSO`, `ISlotConfigProvider`, and `LocalSlotConfigProvider`.
3. Populate the initial configuration: 5 reels with 30 positions each, frequencies `6/5/5/4/4/3/2/1`; 20 paylines; the paytable from TZ; bets `200/400/600/800/1000`; bonus rules.
4. Explicitly define **five actual 30-symbol strips with different orders**, not just weights; use an editor utility for initial creation if needed, then persist the result in the asset.
5. Implement `ConfigValidator`: paylines/strips/totals, probabilities, bonus amounts, long limits, and required asset IDs.
6. Keep the configuration snapshot immutable throughout each Spin and any active Free Spins chain.

**Completion criterion:** changing the paytable, reel contents, bet, or jackpot in the SO changes the corresponding runtime model without gameplay code edits; invalid data is diagnosed before startup.

### 4.3. Pure Slot Engine: Outcome Generation

1. Define `IRandomSource`, a production implementation, and a seed-based test implementation.
2. Implement `OutcomeGenerator` with five independent reel stops and cyclic sampling of three symbols on each reel.
3. Create `SlotGrid`, `ReelStop`, `SpinOutcome`, and pure result DTOs.
4. Create fixed Spin examples with known stops for tests.
5. Separately verify displayed rows with wrap-around at `stop=29` and `stop=28`.

**Completion criterion:** any 5 × 3 outcome can be reproduced in a test without UI/Unity; stop positions uniquely determine the matrix.

### 4.4. Pure Slot Engine: Paylines, Scatter, Free Spins, and Mathematics

1. Implement `WinEvaluator` for 20 paylines and 3/4/5 matching regular symbols **from left to right**.
2. Pay only the longest matching prefix on each line; sum wins across multiple paylines.
3. Implement `ScatterEvaluator` and the award table `3→8`, `4→12`, `5→20`.
4. Implement `JackpotRules` (probability and eligibility for a paid Spin), keeping this calculation separate from the reels.
5. Use integer coins represented by `long`; check for negative values and overflow.
6. Record analytical reference values in the test report: base RTP ~85.6059%, RTP with Free Spins ~92.0889%, frequency of 3+ Scatter ~0.856%.

**Completion criterion:** fully correct payout calculations for predefined matrices, with unit tests for all key rules.

### 4.5. Persistence and Recovery

1. Define `SaveData` with `SaveVersion`, balance, bet, bonuses, weekly state, last settled spin, and pending spin.
2. Implement `ISaveRepository` and `JsonSaveRepository` using `Application.persistentDataPath`.
3. Add writes through a temporary file, a backup, and snapshot validation.
4. Implement an initial balance of `20 000`, a bet of `200`, and safe creation of new profiles.
5. Handle missing, corrupted, and outdated saves; provide versioning and a clear reset flow.
6. Add `FakeSaveRepository`, able to fail on a specified write attempt, for error tests.

**Completion criterion:** balance and bonuses survive a restart; invalid/truncated data does not cause exceptions in normal UI use; write errors are reproducible in tests.

### 4.6. Application: Spin Lifecycle and Interruption Protection

1. Implement `GameSession` and transitions `Idle → Preparing → Spinning → Settling → Idle`, with `Error` on failed saves.
2. Implement `SpinService.StartSpin` with checks for bet, balance, state, and `SpinId`.
3. For a paid Spin, save the reduced balance and `PendingSpin` in one operation **before generation/animation**. For a Free Spin, reserve/decrement the remaining count.
4. Generate `SpinOutcome` and a potential jackpot, but do not credit rewards until animation finishes.
5. Implement `CompleteSpin(SpinId)` with a single settlement: winnings, retrigger, weekly jackpot, and pending removal are saved in one snapshot.
6. Prohibit a second start, bet changes, and Back outside Idle, including API calls regardless of UI state.
7. When loading a state with `PendingSpin`, remove pending without another debit, without jackpot/free-spin winnings, and without refunding the bet.
8. Handle settlement write errors: do not return to Idle or announce the reward as awarded; allow retrying the same operation by SpinId.
9. Add tests for closing after debit, closing after settlement, repeated Complete calls, unsaved debit, and interrupted Free Spins.

**Completion criterion:** an unrecovered spin always loses **only the resource already debited**, without crediting calculated winnings or causing duplicate debits/credits.

### 4.7. Application: Daily Bonus

1. Implement `IClock` and test clocks.
2. Implement `DailyBonusService` and the seven-day reward sequence from the SO.
3. Use UTC days, manual claims only, and streak reset after a missed day.
4. Save the claim date, streak day, and new balance in one transaction.
5. Prepare a UI DTO with availability, the current/next reward amount, and reset time.

**Completion criterion:** the bonus cannot be claimed twice in one day; the seven-day cycle, missed days, and UTC midnight work correctly.

### 4.8. Application: Jackpot of the Week

1. Implement `WeeklyJackpotService` with `WeekId`, event state, and a win limit.
2. Add amounts and probabilities to configuration, with the baseline example `100 000 coins / 0.01% at a 1 000 bet`.
3. Calculate probability for paid Spins based on the bet; test the `0 / 1` boundaries and different bets.
4. Verify crediting **only at settlement**, with the limit recorded in the same save.
5. Restore eligibility when the next week begins.
6. Provide Presentation with a `JackpotViewModel` containing amount, state, and UTC reset time.

**Completion criterion:** at most one paid-out jackpot per week; Free Spins do not participate; an interrupted Spin does not consume the limit.

### 4.9. Presentation: Main Menu, Settings, and Daily Bonus

1. Build `MainMenuScreen` with **three required buttons**: Play, Settings, Daily bonus.
2. Implement `MainMenuPresenter` and `GameNavigationService`/screen router.
3. Settings: music/sound, volume, and local persistence of user preferences.
4. Daily bonus: seven-day rewards, Claim, already-claimed state, and an indicator on the main menu button.
5. UI calls Application contracts and does not access `SaveRepository` or the balance directly.

**Completion criterion:** all three buttons work, each reward is credited once, and screens switch without a scene reload.

### 4.10. Presentation: Slot, Reels, and Controls

1. Build `SlotScreen` with a 5 × 3 matrix, balance, bet, Spin, Back, last win, and Free Spins remaining.
2. Create `ReelView` with animation of **five separate reels**, staggered stops, and the result from `SpinOutcome`.
3. Add `PaylineView`, winning-symbol highlights, and Scatter animation.
4. Add `SlotPresenter`, which controls `interactable` based on `GameSession` state.
5. On full Spin animation completion, call `CompleteSpin(SpinId)` and display credited winnings only after the save is confirmed.
6. Disable `Back` and bet changes throughout every critical stage, including saving/errors.
7. Optionally implement stopping or speeding up animation without regenerating the outcome. Ensure disabling `SlotScreen` does not automatically complete the Spin.

**Completion criterion:** every spin visually ends at the previously calculated matrix; returning to the menu is impossible during an active Spin; no tween callback credits the balance directly.

### 4.11. Presentation: Persistent JackpotHUD

1. Add JackpotHUD as a permanent part of the `SlotScreen` layout, not a temporary popup.
2. Show the amount, `Active/Won this week/Inactive`, and weekly reset time/date.
3. Update the UI from the ViewModel, using `IClock` once per second if needed without changing Domain.
4. Show a separate celebration animation when winnings are awarded, **without permanently hiding the HUD**.
5. Verify its state during Free Spins, during a spin, and after a week change.

**Completion criterion:** jackpot information remains visible on the slot and updates when parameters/state change.

### 4.12. EditMode Tests, Simulation, and Balancing

1. Combine tests for actual reel strips, win evaluation, Scatter, jackpot, and bonus economics.
2. Write a large deterministic simulation of **100 000–1 000 000+** paid Spins without animations, including Free Spins chains.
3. Separate base-game RTP, bonus RTP, jackpot payout, and total return in the report; test Scatter frequency and variance.
4. Verify the calculated values from TZ and choose statistical tolerances based on the payout distribution.
5. Add fixed-seed regression tests for specific sequences so reel/payout changes are visible in CI.
6. Include at least 5 risky lifecycle/recovery scenarios: double Spin, quit during spin, quit after settlement, failed save, and weekly/daily boundaries.

**Completion criterion:** tests pass reproducibly without opening a scene, and a clear report shows agreement with the mathematics.

### 4.13. PlayMode, Lifecycle, and Final Verification

1. Verify startup from a clean state, both screens, and the three menu buttons.
2. Verify Back after stopping, its unavailability during a spin, and attempts to bypass this through the API.
3. Verify forcefully terminating the process **during a Spin** and restarting with the bet still debited and no payout.
4. Verify persisted settlement after application shutdown and an unfinished Free Spins queue.
5. Verify normal pause/resume, scene reinitialization, async/tween cancellation, and event unsubscription.
6. Verify loading a defective backup/primary save and operation when writing is disabled.
7. Add minor UI polish and check UI scaling and JackpotHUD visibility.
8. Finally inspect the console for `Exception`, `MissingReferenceException`, stuck tasks, and duplicate subscriptions.

**Completion criterion:** the full user flow from menu to jackpot/free spins works reliably; returning to the menu and closing the application follow TZ.

**Stage 4.13 verification on 2026-10-13:** items 1, 2, and 4–8 were verified in the original Unity project; 496 EditMode and 47 PlayMode tests passed, a Windows x64 build was produced, and five resolutions were checked visually. For item 3, separate processes using production services and JsonSaveRepository were actually forcefully terminated; terminating the graphical Unity Player directly during animation has not yet been verified. Full completion of item 3 or the entire stage is not claimed. Details and limitations: [Documentation.md](Documentation.md#verification-and-current-status).

### 4.14. Documentation and Handoff

1. Complete `README.md`: build, startup, controls, SO configuration, formulas, local jackpot limitations, and the bet-loss policy.
2. Describe `SpinId`/two-phase persistence and the clean layer structure in 10–20 lines.
3. Prepare `AI_NOTES.md` if AI agents were used.
4. State actual time spent, limitations, test results, and large simulation results.
5. Check Git history, absence of temporary files/keys, and startup from a clean checkout.

**Completion criterion:** the repository can be handed to another developer; how to run and verify the game is clear without verbal explanations.

## 5. Time Estimate

Estimates are working hours for one Unity developer **excluding production of final artwork**. They are approximate and are not inherited from the 15-hour network test assignment.

| Stage | Estimate, hours |
|---|---:|
| 4.1. Unity, scene, asmdef | 2–3 |
| 4.2. DTOs, ScriptableObject, configuration, and validation | 3–5 |
| 4.3. OutcomeGenerator and RNG | 2–3 |
| 4.4. WinEvaluator, Scatter, mathematics | 4–6 |
| 4.5. Persistence, backup, migrations | 4–6 |
| 4.6. Application: Spin transactions / interrupted-spin policy | 5–8 |
| 4.7. Daily Bonus | 2–3 |
| 4.8. Jackpot of the Week | 3–4 |
| 4.9. Main menu, settings, and Daily Bonus UI | 3–5 |
| 4.10. Slot UI and animations | 6–10 |
| 4.11. JackpotHUD | 2–3 |
| 4.12. Tests and mathematical simulation | 5–8 |
| 4.13. PlayMode and lifecycle QA | 4–6 |
| 4.14. Documentation and handoff | 2–3 |
| **Total, excluding contingency** | **47–73** |
| **Integration and bug contingency (≈15–20%)** | **7–15** |
| **Total with contingency** | **54–88 hours** |

The minimum engineering vertical slice before UI polish is stages **4.1–4.8**, followed by a simplified `SlotScreen` and critical tests from **4.12**. Do not leave persistence until the end: it is the riskiest system aspect of the chosen shutdown behavior.

## 6. Priorities and Milestones

| Milestone | Stages | Demonstrable Result |
|---|---|---|
| M1: pure core | 4.1–4.4 | Spin generation and evaluation without Unity UI |
| M2: reliable session | 4.5–4.6 | The bet is lost on interruption; no duplicate crediting |
| M3: bonus economics | 4.7–4.8 | Daily Bonus and weekly jackpot through the C# API |
| M4: two complete screens | 4.9–4.11 | Main menu, slot, animations, persistent JackpotHUD |
| M5: verification and handoff | 4.12–4.14 | Simulations, tests, README, clean startup |

## 7. Critical Risks and Mitigations

| Risk | Mitigation |
|---|---|
| The balance is debited only in memory and restored after shutdown | Save debit + `PendingSpin` **before** starting animation; test kill/reload |
| Winnings are saved before Spin completion, violating the accepted policy | Clearly separate accept/debit and completion/settlement; payout only after completion is confirmed and saving succeeds |
| The user bypasses disabled `Back` and interrupts an active session | Check `GameSession` at the Application layer; UI only reflects the restriction |
| A double click or repeated callback credits winnings again | `SpinId`, atomic settlement, prohibition of competing commands, and idempotency checks |
| Write errors/save corruption lose the whole session | Snapshot + temp/backup, versioned schema, safe recovery, and fake-error tests |
| Crossing UTC midnight during a Spin breaks the weekly jackpot | Capture `WeekId` when accepting the Spin and test the boundary scenario |
| Daily Bonus can be claimed twice in one day | Atomically save the reward together with `LastClaimUtc` |
| Payline RTP is confused with Daily Bonus economics | Separate base-line, Free Spins, and jackpot statistics; exclude Daily Bonus from RTP |
| A fixed seed remains enabled in production | Use test RNG only with an explicitly enabled debug flag, false by default |
| Logic depends on Tween/MonoBehaviour | Pure SlotEngine; Presentation only displays DTOs |
| Remote Config breaks Sprite references and active bonuses | `SlotRuntimeConfig` with `SymbolId`, local visual configuration, and a snapshot/version throughout the Spin and bonus chain |
| The jackpot amount is mistaken for a shared pool across all players | Document the personal local jackpot without server synchronization from the outset |
| UI/polish causes scope growth | Complete stages against criteria; keep final art, online services, and extra mechanics outside the MVP |

## 8. Verification Scenarios (Minimum Manual Checklist)

1. **New player:** Main Menu, starting balance of 20 000, three buttons; Play opens the slot.
2. **Regular Spin:** the bet is debited on acceptance; the result matches the displayed matrix; crediting happens only after completion.
3. **Attempting Back during a Spin:** the button is disabled; a programmatic `NavigateToMenu` is rejected; the Spin is not interrupted.
4. **Closing during a paid Spin:** after restart, the bet is lost and no Line Win/Scatter/Jackpot is added.
5. **Closing after settlement:** the balance and all earned Free Spins/Jackpot are saved correctly.
6. **Free Spin interrupted by a crash:** one Free Spin is spent; no winnings/retrigger; the remaining queue is preserved.
7. **Daily Bonus:** Claim cannot be duplicated; claiming on the next date works; a missed day resets the streak.
8. **Jackpot:** probability/amount come from the SO; a guaranteed test win is credited once per week; Free Spins do not draw the event.
9. **JackpotHUD:** visible before, during, and after a Spin; remains visible after a win; displays state/amount/reset.
10. **Errors:** writing is prohibited → Spin does not start or settlement does not announce success; no false crediting.
11. **Configuration:** changing the paytable/jackpot settings in the SO changes behavior after the next bootstrap without C# edits.
12. **Stress test:** a fixed seed reproduces measured metrics over 1 000 000+ Spins, requires no Canvas, and creates no Unity objects.

## 9. Definition of Done

Done when all criteria in `TZ.md` §14 are met, key `EditMode`/`PlayMode` tests pass, both screens are demonstrated from one Unity scene, and the critical guarantees are verified:

- Pure C# Domain and separate Application logic;
- Configuration through ScriptableObject → `ISlotConfigProvider` → `SlotRuntimeConfig`;
- Correct 20 paylines, Scatter, Free Spins, and balance;
- Daily Bonus and personal Jackpot of the Week, with a persistent slot HUD;
- Returning to Main Menu is prohibited during a Spin;
- Sudden shutdown before settlement loses the bet/Free Spin without crediting potential winnings;
- Repeated commands do not cause duplicate actions;
- Saving and loading withstand interrupted Spins and typical errors;
- Tests and the mathematical simulation report are available in the repository;
- README describes architecture, limitations, and startup from a clean checkout.

## 10. Optional Work after MVP (Excluded from the Estimate)

- Integrate Firebase Remote Config as an alternative `ISlotConfigProvider`, with fallback, versioning, and updates between Spins.
- Add real live events / a global jackpot through a backend and server-authoritative outcomes.
- Add Wild symbols, multipliers, extra bonus games, and an expanded paytable.
- Extend UI to the final art style, sound, vibration, localization, and accessibility.
- Add analytics events/telemetry if privacy and consent requirements are defined.
