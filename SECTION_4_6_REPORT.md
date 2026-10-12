# Section 4.6 — Application Spin lifecycle and interruption safety

Completed on 2026-10-12. Scope is section 4.6; section 4.7 was not started.

## Prerequisite: section 4.5

Reviewed the existing uncommitted persistence work, SaveData/validator, strict JSON codec, repository's commit/backup sequence, fake and bootstrap. Existing changes were retained. Before implementing 4.6, the original project passed **257/257 EditMode** and **3/3 PlayMode** tests through the connected Unity 6000.3.10f1 editor. Console contained no errors/warnings at that prerequisite check.

The UTC fix was already present: `SaveJsonCodec.Decode` uses `DateParseHandling.None`, so JSON dates remain strings during strict shape validation. Encoding uses invariant round-trip `O`; conversion to `DateTimeOffset` preserves the instant and zero offset. Existing full-profile and pending paid/free round-trip tests passed. No production persistence correction or schema change was required. Added an explicit regression for zero offsets and fractional ticks in claim, week and pending timestamps; it also passes.

Repository review: promotion of temp to primary is its commit point, with no fallible work afterwards except best-effort cleanup. Application still treats every reported failure as potentially ambiguous. It retains the complete proposed snapshot and resaves that exact state rather than repeating an economic mutation.

## Files changed by this stage

All code paths below are relative to `Assets/_Project/Scripts/`.

Created (each with its Unity-generated `.meta` file):

- `Application/GameSession.cs`: authoritative state/snapshot, guards and validated internal transitions.
- `Application/BalanceService.cs`: pure checked reservation and settlement calculations.
- `Application/SpinResult.cs`: immutable outcome wrapper and typed command result/errors.
- `Application/SpinService.cs`: accept, completion, persistence retry, recovery and bet selection.
- `Application/AssemblyInfo.cs`: internal state-machine access for the EditMode test assembly.
- `Tests/EditMode/SpinLifecycleTests.cs`: 38 deterministic lifecycle test cases.

Modified:

- `Application/GameNavigationService.cs`: mandatory GameSession dependency and Back guard.
- `Bootstrap/SlotBootstrapper.cs`: compose services, recover before navigation, retry failed startup recovery, expose the session snapshot rather than another retained profile store.
- `Presentation/GameNavigationPresenter.cs`: reflect Application Back availability; bootstrap refreshes it each frame.
- `Tests/EditMode/FakeSaveRepository.cs`: optional save callback and commit-then-fail mode to test reentrancy, concurrency and uncertain commit results.
- `Tests/EditMode/PersistenceTests.cs`: additional UTC fractional-tick/offset regression.
- `Tests/PlayMode/GameSceneSmokeTests.cs`: two added scene integration tests; original three smoke tests retained.
- `SECTION_4_5_REPORT.md`: recorded prerequisite verification and existing UTC correction.

Created documentation/evidence:

- `SECTION_4_6_REPORT.md` (this file).
- `Logs/Section4_6/Verification.json` (local, ignored by Git): final Unity tool results and prerequisite run IDs.

The persistence files, Infrastructure asmdef and PlayMode asmdef were already uncommitted from 4.5. No changes to Domain, JackpotRules, production persistence implementation, configuration assets, UI layout, scenes or packages were needed for 4.6. Unity test tooling temporarily changed Enter Play Mode options; that setting was restored. Existing scene/settings line-ending status may still appear in Git without a content diff.

## State machine and ownership

GameSession owns one detached complete SaveData snapshot. Public `Snapshot` always returns a deep copy. Monetary values have no second independent authoritative store. SpinService is the only command owner of a session; constructing a second owner is rejected. Presentation receives immutable domain results and capability flags.

Normal transitions: `Idle → Preparing → Spinning → Settling → Idle`.

Additional validated transitions:

- Any other state may enter Error after a critical failure.
- Error → Preparing for retry of debit, recovery or bet selection.
- Error → Settling for retry of settlement.
- Preparing → Idle after successful recovery or bet selection.

Same-state and all other transitions are rejected. Idle requires no PendingSpin. A loaded pending record begins in Error and must be recovered. `CanStartSpin`, `CanSelectBet`, and `CanNavigateBack` require Idle and no pending record. Insufficient funds/invalid command IDs are typed rejections rather than critical state changes.

Commands share the session gate. A competing or reentrant Spin command is rejected as Busy rather than queued. State changes and snapshot commits happen under that gate; navigation checks use the same gate.

## Public API

```csharp
SpinOperationResult StartSpin();
SpinOperationResult CompleteSpin(Guid spinId);
SpinOperationResult SelectBet(long bet);
SpinOperationResult RetryPersistence();
SpinOperationResult RecoverInterruptedSpin();
```

Construction supplies GameSession, immutable SlotRuntimeConfig, existing SlotEngine, BalanceService, ISaveRepository and a UTC time delegate. The delegate makes acceptance/week-boundary tests deterministic; DailyBonusService/IClock work remains in 4.7.

`SpinOperationResult` exposes Success, SpinError, diagnostic, optional SpinResult, AlreadySettled, SettledSpinId and ConfirmedPayout. Start returns a Spin only after accepted state is saved and Domain generation succeeds. Complete returns confirmed payout only after settlement persistence succeeds. Duplicate completion returns persisted identity/LastWin and AlreadySettled without a new write or RNG draw.

SpinResult contains SpinId, type, effective bet, captured WeekId, the immutable configuration snapshot, and EvaluatedSpinOutcome. Its evaluation contains grid, five stops, winning lines, Scatter and potential payout. Potential payout is not committed currency.

BalanceService validates allowed bets/affordability and calculates proposed snapshots. Reserve deducts exactly one paid bet or consumes one saved Free Spin. Settle adds payout and Scatter rewards, updates LastWin/LastSettledSpinId, clears pending and handles BonusBet. Coins use `long`; coin and counter arithmetic uses `checked`. Negative inputs, invalid identity/state and overflow are rejected without modifying input or saving a partial settlement.

## Paid and free lifecycles

Paid acceptance validates Idle/no pending, selected bet and funds, creates a unique Guid, captures UTC acceptance/WeekId/configuration, calculates debit + pending, enters Preparing and saves the complete snapshot. Only after save success does it commit runtime state, generate via SlotEngine and enter Spinning. The engine is not duplicated and no RNG is consumed by rejected requests.

Complete verifies active identity and Spinning, enters Settling, calculates one complete settlement snapshot and saves it. Runtime balance/rewards and Idle become visible only after confirmed persistence. LastSettledSpinId prevents a duplicate payout. An old callback never settles a newer pending transaction; the most recently settled ID is acknowledged without touching it, and older unknown IDs are rejected.

When FreeSpinsRemaining > 0, acceptance uses preserved BonusBet, consumes one Free Spin and deducts no coins. Paid Scatter initializes BonusBet from its triggering paid bet. Completed free Scatter adds the configured retrigger award; SlotEngine applies the configured Free Spins multiplier. Selecting a paid bet between bonus spins saves SelectedBet but preserves BonusBet. When no queued or pending free spin remains, BonusBet returns to inactive value **0**. Each free spin requires an explicit StartSpin call.

Configuration is immutable and retained for the service lifetime and exposed with each Spin; an accepted spin is never recalculated under changed rules. No runtime configuration replacement API was introduced.

## Persistence, failures and restart

Phase A is one whole-snapshot write: debit/reservation + PendingSpin. Phase B is a separate whole-snapshot write: payout + new Free Spins + LastWin/LastSettledSpinId + pending removal. No payout is saved by StartSpin. The actual Spin button and reel animations remain unconnected.

On a save failure or repository exception, runtime stays at its last confirmed snapshot, the session enters Error, and no success/result is reported. The retained proposed snapshot includes the same identity and computed economic values. `RetryPersistence` resaves it unchanged; no second deduction, free reservation, Scatter increment or RNG generation occurs. After a failed debit, outcome generation occurs once following successful retry. Failed settlement can also be retried by `CompleteSpin` with the same active ID. A wrong ID cannot retry it. Failed bet selection similarly blocks until its exact save retry succeeds.

An outcome-generation exception after confirmed acceptance preserves the already debited pending snapshot and enters Error. Explicit recovery can discard that failed outcome without another debit/refund/payout. Settlement arithmetic failure enters Error without a partial write; it cannot be retried as a failed saved snapshot because no valid settlement was prepared. Restart applies the interrupted-spin policy to its pending record.

Startup recovery is Application logic. It clears a saved pending record in a new complete snapshot without changing balance, adding rewards, consuming more free spins or overwriting prior settled metadata. It clears BonusBet only if the queue is exhausted. It does not generate an outcome. The repository only loads/saves snapshots.

| Termination point | Restart behavior |
| --- | --- |
| Before debit commit | Previous committed balance/queue retained; no pending Spin |
| After paid debit, before settlement | Debit retained; no payout; pending cleared through persisted recovery |
| After free reservation, before settlement | That one free spin remains consumed; other free spins retained; no payout/retrigger |
| After settlement commit | Payout, bonus queue and settled metadata retained; no duplicate settlement |
| Recovery save failure | Error/no gameplay navigation; retry the same clearing snapshot without economic mutation |

Bootstrap creates SlotEngine/RNG, session, balance and Spin services, and runs recovery before constructing the navigation presenter. `RetryInitializationRecovery()` safely retries a failed pending recovery and attaches navigation exactly once after success. Corrupt/incompatible loaded data remains an initialization failure; no automatic reset or fresh-profile grant was added.

## Navigation and jackpot boundary

`TryNavigateToMainMenu()` returns false during Preparing, Spinning, Settling, Error or unresolved pending. The existing void `NavigateToMainMenu()` also checks the rule, including direct calls and Button.onClick.Invoke that bypasses button interactability. Back is allowed in Idle between unstarted Free Spins. Existing Play, Settings and Daily Bonus navigation behavior is retained.

Paid PendingSpin/SpinResult retain acceptance SpinId and the configured UTC week-start instant. Free spins have no jackpot WeekId. All existing jackpot fields survive acceptance, settlement and recovery. There is no jackpot draw, fake jackpot payout, weekly reset or new abstract jackpot layer. A future 4.8 implementation can add its winnings and weekly mutation to the same proposed settlement snapshot before the one save; normal and jackpot winnings must remain one settlement transaction.

## Deterministic examples (executed tests)

The lifecycle fixture uses the real valid strips and 20 paylines with a test paytable whose sole winning center S1×5 line pays configured multiplier 50 at line bet 1000/20. Deterministic stops produce **2500** coins; production configuration is unchanged.

```text
Initial balance: 20000
Selected bet: 1000
StartSpin: balance 19000, PendingSpin active, state Spinning
Calculated potential win: 2500 (not yet credited)
CompleteSpin: balance 21500, PendingSpin null, LastWin 2500,
              LastSettledSpinId = accepted SpinId, state Idle
Duplicate CompleteSpin: balance remains 21500; no new save
```

Interrupted example using the same fixture:

```text
StartSpin commits balance 19000 + PendingSpin; potential win 2500
Discard runtime without CompleteSpin
Recreate services from repository; session blocked for recovery
Persist recovery: balance remains 19000, PendingSpin null, LastWin 0
No win or additional bet is applied; new Spin is allowed
```

## Executed verification

All runs below used the **original `C:/UnityGames/IceP` project**, connected Unity **6000.3.10f1**, Windows editor. No temporary validation copy was required this stage; earlier 4.5 copy-run artifacts do not represent these results. Test save directories were isolated from the player's save.

| Run | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Prerequisite all EditMode | 257 | 0 | 0 |
| Prerequisite existing PlayMode | 3 | 0 | 0 |
| Final all EditMode (including engine/mathematics, persistence, 38 new lifecycle cases and 1 UTC regression) | 296 | 0 | 0 |
| Final PlayMode (original 3 + 2 new integration cases) | 5 | 0 | 0 |

Unity compilation/import succeeded after refreshing all assets; no script compiler errors remain. Initial scripts-only refresh did not import newly created assets; its stale compilation/run was not used as final evidence. The first run of the two new smoke tests failed because their strict final log assertion had not declared the routine startup Log; adding the same expected log used by the existing smoke test fixed this. Final PlayMode passed on rerun.

Final Console review showed only the **three deliberately injected, expected initialization errors** from corrupt-save, first-write-failure and recovery-write-failure smoke tests, with no warnings. During an earlier domain reload a transient MCP plugin WebSocket-not-initialised warning was observed; it was not a gameplay warning and did not recur in the final run. `git diff --check` passed.

The SC integration test loads the scene, starts via Application, invokes Back through both UI and API, reloads with an accepted debit, verifies recovery and permits another spin. The recovery-failure test prevents temp creation with a directory at the temp path, verifies Error and disabled navigation, removes the blocker and verifies successful retry without economic changes. EditMode also covers paid/free ambiguous save results, real concurrency/reentrancy, duplicate/stale completion, shutdown before debit and after settlement, BonusBet/retriggers/multiplier, overflow, outcome/repository exceptions, profile/jackpot preservation and UTC weekly boundaries.

## Remaining limits

- Local JSON backup restoration can roll economic state back to an earlier snapshot. It is not a financial transaction journal or universal crash-proof store; hardware loss, directory metadata durability and platform rename/flush behavior remain limitations inherited from 4.5.
- One writer per save directory is required. External writes through another repository or edits to local files are outside this command protocol. No multi-process or tamper protection is claimed.
- Tests simulate runtime termination/recreation and scene reload; an actual OS kill/power-loss test and target-player build were not executed. Windows Editor results do not establish mobile/WebGL durability.
- Retry candidates and potential outcomes live in memory. After process death, the committed pending record is lost under the documented policy; a committed settlement survives. No animation-based or implicit completion is added.
- Current configuration compatibility rules are inherited: incompatible historic bets/reset rules block loading; serializing historic bonus-rule snapshots across application versions is not implemented. The existing BonusBet and remaining free-spin queue survive compatible reloads, while current immutable rules are loaded at bootstrap.
- Persistence is synchronous for these small snapshots. No async queue, UI error/retry controls, Spin UI, weekly jackpot service, daily bonus service or later-plan work was added.
