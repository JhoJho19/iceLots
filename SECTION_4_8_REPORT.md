# Section 4.8 — Application: Jackpot of the Week

Implemented only Plan.md section 4.8 after reading Assets/Docs/TZ.md, Assets/Docs/Plan.md and the attached request. No JackpotHUD, animations, other Presentation features, Remote Config, simulation, scene changes or section 4.9 work was added.

## Files created and modified

Paths are relative to `C:/UnityGames/IceP`.

Created:

- `Assets/_Project/Scripts/Application/WeeklyJackpotService.cs`: service, immutable JackpotStatus/JackpotSpinResult, state enum, exact probability sampler.
- `Assets/_Project/Scripts/Tests/EditMode/WeeklyJackpotTests.cs`: 61 deterministic test cases.
- Unity-generated `.meta` files for both new C# files.
- `SECTION_4_8_REPORT.md`.

Modified (paths below are relative to `Assets/_Project/Scripts/`):

- `Application/GameSession.cs`: one jackpot owner per session.
- `Application/SpinService.cs`: optional constructor injection for existing standalone callers; production bootstrap always injects the jackpot. If a jackpot service is attached to the session, omitting it is rejected. Captures week/event identity, draws after debit confirmation and reel evaluation, applies the weekly counter to the retained settlement snapshot.
- `Application/SpinResult.cs`: immutable jackpot result and checked total PotentialPayout. Evaluation.FinalPayout remains the separate normal payout.
- `Application/Persistence/SaveData.cs`: optional JackpotEventId on saved winning-week state and PendingSpin.
- `Application/Persistence/SaveDataValidator.cs`: validation of event metadata; identified historical UTC boundaries/counts survive schedule or limit changes. Legacy records retain previous validation.
- `Infrastructure/Persistence/SaveJsonCodec.cs`: only the two new event fields may be absent/null in v1 JSON; other shape/type checks remain strict.
- `Infrastructure/Randomness/RandomSourceFactory.cs`: CreateJackpot creates a dedicated RNG stream, including a distinct reproducible debug seed.
- `Bootstrap/SlotBootstrapper.cs`: constructs and exposes WeeklyJackpot with the existing session/config/clock and injects it into Spins.
- `Domain/Configuration/ConfigValidator.cs`: configurable positive weekly limit instead of the previous hardcoded limit of 1.
- `Tests/EditMode/ConfigurationTests.cs`: invalid-limit regression now tests zero; new jackpot tests cover a valid limit of 2.
- `Tests/PlayMode/GameSceneSmokeTests.cs`: bootstrap/status/participation assertions and deterministic settlement/reload smoke test.

The workspace already contained modified/untracked implementation from sections 4.5–4.7 and scene/project settings changes. Those changes were retained. This task did not edit BalanceService, DailyBonusService, Domain JackpotRules, configuration assets, asmdefs, navigation or SC.unity.

## Public API and status

```csharp
new WeeklyJackpotService(GameSession session, SlotRuntimeConfig config,
    IClock clock, IRandomSource random);
JackpotStatus GetStatus();

new SpinService(session, config, engine, balance, repository,
    utcNow, weeklyJackpot);

bootstrapper.WeeklyJackpot.GetStatus();
spinResult.Jackpot;
```

Draw and ApplyWin are internal coordination methods; callers cannot independently award a jackpot. The service has no repository, balance, timer, Singleton or mutable authoritative player state. It shares the session command gate and immutable configuration with SpinService; binding a different session or configuration is rejected.

JackpotStatus exposes EventId, Enabled, JackpotAmount, State (Active/WonThisWeek/Inactive), CurrentWeekId, WinsThisWeek, MaxWinsPerWeek, IsEligible, NextResetUtc, StartUtc and EndUtc. This is the Application-level model for future HUD integration. IsEligible describes event/weekly eligibility; session busy state, paid/free type, valid bet and sufficient funds are checked when accepting a Spin. A potential win does not change status to WonThisWeek.

Each status request takes the shared gate, reads the injected clock once, normalizes it to UTC and reads only confirmed state. It neither consumes RNG nor resets counters, writes saves, awards coins or starts a Spin.

## Weekly lifecycle, eligibility and event identity

WeekId is the UTC timestamp of the latest configured reset at or before acceptance/status time. Starting with that UTC date plus ResetTimeUtc, subtract the modulo-seven difference from ResetWeekday and, if the candidate is still in the future, subtract another seven days. Exact reset timestamps belong to the new week. NextResetUtc is WeekId + seven days, or null if it cannot be represented. Month/year transitions and non-Monday/non-midnight schedules use the same calculation.

Default rules already existed in the configuration and assets: enabled, weekly-jackpot, 100000 coins, .0001 base probability, 1000 reference bet, one win, Monday 00:00 UTC, version 1.0.0. The service reads all parameters from that immutable configuration. StartUtc is inclusive; EndUtc is exclusive. A midweek event start/end does not create a new weekly allowance.

The accepted paid Spin captures its UTC timestamp, WeekId and event identity before Phase A. Eligibility and probability use that acceptance instant even if the debit repository callback, animation or persistence retry crosses a reset/event boundary. JackpotSpinResult exposes Participated, EventId, ConfigVersion, WeekId, JackpotWon and potential JackpotAmount (zero on a loss/exclusion). SpinResult also retains the original immutable SlotRuntimeConfig.

An eligible trial requires a paid Spin, enabled and active event, valid configured bet, remaining weekly allowance, no backward week and a positively confirmed debit with matching PendingSpin. Rejections, insufficient balance, Free Spins, disabled/inactive events and exhausted allowance never consume jackpot RNG.

The saved week is the most recent **winning** week, not a status cache. A later nonoverlapping week reports zero wins without mutating that record. A successful new-week win replaces it. Earlier weeks are conservatively blocked, so moving the clock backward through ordinary commands cannot revisit an already paid week. Status polling cannot erase this protection.

For a Spin accepted Sunday and completed Monday, settlement records Sunday's captured week. Monday's status sees zero wins and remains eligible. A newer saved winning week is never overwritten by stale metadata: such a Spin is excluded from drawing; ApplyWin also rejects a stale winning result defensively.

Event changes intentionally share the profile's allowance over overlapping weekly periods. JackpotEventId identifies the historical event but renaming an event does not grant another draw after exhaustion. Changing a reset schedule or lowering MaxWinsPerWeek does not invalidate identified completed wins. Overlapping periods conservatively retain their count; a new allowance requires a boundary at least seven days after the recorded winning boundary. This can delay availability after schedule changes and avoids an event-history system.

SaveVersion remains 1 with additive optional event fields. Older v1 JSON lacking these fields loads without losing its counter; absent identity conservatively uses the same allowance. Legacy unidentified records still require their boundary/count to satisfy the old validation rules. A legacy save combined with an incompatible schedule/limit change requires an explicit migration; no silent reset is introduced. Newly accepted PendingSpin has identity metadata, so an interrupted Spin can be recovered after a schedule change without reconstructing its result.

## Probability and independent RNG

The service calls the existing Domain JackpotRules.CalculateProbability; the formula is not duplicated:

```text
min(1, BaseProbability * TotalBet / ReferenceBet)
200 → .00002; 400 → .00004; 600 → .00006; 800 → .00008; 1000 → .00010
```

One eligible Spin performs one Bernoulli trial. The integer-only IRandomSource supplies fair bits via NextInt(0,2), using its existing exclusive-upper-bound contract. Compare each bit against the binary expansion of the calculated double and stop at the first difference; matching the entire finite expansion is a loss. This samples U < p exactly for the supplied IEEE-754 probability, without a fixed-grid threshold or conversion of tiny valid probabilities to zero. Precision is the Domain calculation's double precision. Expected consumption is fewer than two integer calls per trial; the smallest positive double takes at most 1074. Probability 0/1 is deterministic and consumes no RNG. A trial can therefore require multiple NextInt calls; it is still one decision.

Invalid/out-of-range RNG output fails outcome generation and follows the interrupted-Spin policy. There is no UnityEngine.Random, statistical correctness test or cryptographic RNG requirement.

Bootstrap uses a dedicated SystemRandomSource. Production uses a fresh Guid-derived seed to avoid same-tick default Random seeds on Mono; debug mode explicitly derives a separate seed from the configured seed. Jackpot draws cannot advance the reel RNG. Tests inject controlled bits/reel stops and verify default probabilities, clamping, thresholds/equality, endpoints, the smallest positive double and distinct reproducible debug streams.

## StartSpin, settlement and failures

StartSpin retains the existing gate and two-phase lifecycle:

1. Validate Idle, bet, paid/free resource and balance; capture ID/time/week/event.
2. Reserve debit or one Free Spin in a whole snapshot; save Phase A.
3. Commit only after positive persistence confirmation.
4. Generate SlotEngine evaluation, then perform the eligible jackpot trial once.
5. Return an immutable runtime SpinResult and enter Spinning. No prize/counter is saved yet.

Debit failure consumes neither reel nor jackpot RNG. An exact debit retry eventually generates the outcome once using the retained acceptance metadata. Outcome-generation failure after debit retains the deducted resource and can use existing recovery, without inventing a prize.

CompleteSpin validates SpinId and uses the already calculated normal/jackpot result. Checked long arithmetic sums both payouts. BalanceService.Settle constructs the existing complete settlement, including Free Spin rewards, BonusBet, LastSettledSpinId, total LastWin and cleared PendingSpin. ApplyWin adds the captured week/event and one win to the same snapshot. There is one save and no independent jackpot transaction.

Only positive save confirmation commits balance/counter and returns ConfirmedPayout. A failed or commit-then-fail write keeps the session in Error, preserves the exact proposal, and reports zero confirmed winnings. CompleteSpin/retry resaves that same proposal without RNG, time/config lookup, counter increment, re-credit or outcome generation. Duplicate completion uses the existing LastSettledSpinId idempotency. A durable ambiguous settlement survives restart; an uncommitted proposal does not.

Interrupted runtime outcomes are not persisted. Startup recovery clears PendingSpin, retaining the accepted debit/free-spin consumption and discarding both potential monetary payouts and Scatter rewards. Weekly allowance is untouched. No animation/outcome is resumed or regenerated. Arithmetic overflow remains a blocked Spin under the existing error policy; recovery after restart discards its unresolved pending outcome.

Daily Bonus and Spins continue to serialize through GameSession.Gate and CommandExecuting. A claim is blocked during Spin/failed settlement; settlement copies daily metadata unchanged. A later claim copies jackpot/Free Spins state unchanged. No separate economic profile was introduced.

## Corrections to earlier stages

- Stage 4.2 validation rejected every MaxWinsPerWeek except 1. The request requires a configurable limit, so positive limits are now accepted; zero/negative values remain invalid. Tests cover the default and exactly two wins with a configured limit of 2.
- Stage 4.5's unidentified week state and strict JSON shape required the two additive optional identity fields and historic-boundary validation described above. Regression tests cover legacy JSON, real JSON round trips, changed event/schedule/limit, interrupted recovery and stale-week protection.
- Stage 4.6 settlement now includes the captured jackpot payout/counter; its transaction/retry mechanism was retained.

## Verification

Final verification ran in the **original project `C:/UnityGames/IceP`**, connected Windows Unity **6000.3.10f1**, on **2026-10-12**. New scripts imported and compiled successfully.

| Final run | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| EditMode: existing 336 + new 61 jackpot cases | 397 | 0 | 0 |
| PlayMode: existing 6 + new settlement/reload case | 7 | 0 | 0 |

EditMode job: `f06381a904714511a814f647e308f488`, reported test duration 4.5799291 seconds. PlayMode job: `35b30783a52a4fab915b4d6003b413ba`, reported test duration 5.3272732 seconds. Both completed with Passed status.

Final Console inspection contained only the three deliberately injected, expected initialization errors from corrupt-save, initial-write-failure and recovery-write-failure smoke tests; no warnings or unexpected errors remained. Compilation/domain reload briefly produced the existing Unity MCP WebSocket-not-initialised warning, which did not appear in the final inspection. `git diff --check` passed; Git emitted LF/CRLF normalization notices. The editor returned to Idle with SC.unity active.

Existing engine mathematics, persistence/recovery, Spin lifecycle and Daily Bonus suites passed. Smoke tests load SC.unity, check navigation/status/eligible production StartSpin, and use an isolated save directory with controlled injected RNG to settle a guaranteed test jackpot and reload the scene. The deterministic test uses a detached idle-session composition against the bootstrapped config/clock/repository; no production probability asset is modified and no concurrent original-session writes occur.

Initial batch launch could not open the already-running original project; a validation-copy attempt stopped at Package Manager network timeouts. That process was stopped, and the existing editor was used through local Unity MCP. The incomplete copy is not test evidence. Local ignored logs/scripts/results live in Logs/; real player saves were not used.

## Deterministic example (asserted against runtime and saved state)

```text
Initial balance: 20000; bet: 1000; weekly wins: 0
StartSpin: saved balance 19000; PendingSpin active
Potential line payout: 2500; potential jackpot payout: 100000
CompleteSpin: confirmed balance 121500; LastWin 102500
JackpotWinsThisWeek: 1; PendingSpin null
Next paid Spin: jackpot draw skipped (weekly limit exhausted)
Next Monday: current week reports zero wins; jackpot available again
```

## Remaining limitations

- Local device clock/save files are not authoritative server data; tampering, multi-process writers, backup rollback and physical storage durability retain existing limitations.
- Only the most recent winning week is stored. Backward clocks and overlapping schedule changes are handled conservatively, not by retaining arbitrary event history. Legacy unidentified incompatible records still need explicit migration.
- Configuration is immutable for this session. No live Remote Config swapping, networking or generalized event framework is implemented. Existing standalone SpinService callers without any attached jackpot retain their prior no-jackpot behavior; bootstrap always composes it.
- The Bernoulli sampler is exact for the calculated double, not for an arbitrary-precision real-number formula.
- Verification covers Windows Editor compilation and automated tests, not a target-player build, physical OS kill/power loss or mobile/WebGL storage.
- HUD, UI refresh/timers, animation, audio and retry controls belong to later sections. Section 4.9 was not started.
