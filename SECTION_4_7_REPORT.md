# Section 4.7 — Application Daily Bonus

Implemented only Plan.md section 4.7, after reading Assets/Docs/TZ.md and Assets/Docs/Plan.md and inspecting the existing session, Spin, balance, navigation, persistence, configuration and bootstrap implementations. No Daily Bonus presentation logic or section 4.8 work was added.

## Files created and modified in this task

All paths below are relative to `C:/UnityGames/IceP`.

Created:

- `Assets/_Project/Scripts/Application/IClock.cs`
- `Assets/_Project/Scripts/Application/DailyBonusService.cs`
- `Assets/_Project/Scripts/Application/DailyBonusStatus.cs` (status DTO, claim result and error enum)
- `Assets/_Project/Scripts/Infrastructure/SystemClock.cs`
- `Assets/_Project/Scripts/Tests/EditMode/FakeClock.cs`
- `Assets/_Project/Scripts/Tests/EditMode/DailyBonusTests.cs`
- Corresponding Unity-generated `.meta` files for those six C# files
- `SECTION_4_7_REPORT.md`

Modified:

- `Assets/_Project/Scripts/Application/GameSession.cs`: shared command-execution flag and one Daily Bonus owner per session
- `Assets/_Project/Scripts/Application/SpinService.cs`: uses the shared execution flag instead of its private flag
- `Assets/_Project/Scripts/Application/BalanceService.cs`: checked, detached whole-snapshot `Credit` calculation
- `Assets/_Project/Scripts/Bootstrap/SlotBootstrapper.cs`: constructs and exposes the clock and Daily Bonus service, sharing session, balance service and repository
- `Assets/_Project/Scripts/Tests/PlayMode/GameSceneSmokeTests.cs`: service assertions and one isolated claim/reload integration test

The working directory already contained modified/untracked code from earlier stages, scene changes and project settings. Those were preserved. This task did not edit SC.unity, configuration assets, Domain, persistence schema, asmdefs, navigation or Presentation.

## Clock and public API

`IceP.Application.IClock` is pure C#:

```csharp
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
```

`IceP.Infrastructure.SystemClock` returns `DateTimeOffset.UtcNow`. `FakeClock` supports assigning, advancing or rewinding its current instant and counts reads. There is no mutable global clock. The Application layer uses neither UnityEngine.Time nor DateTime.Now.

```csharp
new DailyBonusService(GameSession session, SlotRuntimeConfig config,
    BalanceService balance, ISaveRepository repository, IClock clock);

DailyBonusStatus GetStatus();
DailyBonusClaimResult Claim();
DailyBonusClaimResult RetryPersistence();
```

`GetStatus()` takes a coherent session snapshot and reads the clock once. It never saves, changes balance or resets saved streak progress. It returns an immutable DTO with Enabled, CanClaim, UnavailabilityReason, RewardDay, RewardAmount, the configured read-only Rewards sequence, CurrentStreakDay, LastClaimUtc, NextAvailableUtc and NextResetUtc.

RewardDay/RewardAmount describe the reward available now or the upcoming reward if already claimed. CurrentStreakDay is the last confirmed saved day, even when a missed period means the next reward will reset to day 1. NextAvailableUtc is null when the reward is already available, when disabled, or when the date range cannot represent another reset. A Busy status may have no scheduled availability: it depends on resolving the transaction. NextResetUtc is the next configured boundary after the observed UTC instant.

The claim result provides Success, Error, Diagnostic, RewardDay, AwardedCoins, ConfirmedBalance and ClaimUtc. Rejections report zero awarded coins and no successful claim timestamp. A concurrent rejection that cannot acquire the gate returns a null ConfirmedBalance to avoid blocking or exposing an unconfirmed value; other failures report the session's last confirmed balance.

Errors distinguish Disabled, AlreadyClaimed, Busy, ClockInconsistent, ArithmeticOverflow, PersistenceFailure and InvalidState.

## Reward periods and seven-day streak

The reward period is the integer floor of `(UTC ticks - configured reset-time ticks) / ticks per day`. Subtracting the reset offset before floor division avoids comparing calendar dates when the reset is not midnight. At the exact boundary the new period begins; one second before it remains the old period. Month/year/leap transitions need no special cases. Clock values with a nonzero offset are normalized to UTC before calculation and persistence.

The first claim is immediately available and gives configured day 1. For subsequent claims:

- Same period: AlreadyClaimed; no save or state mutation.
- One period later: advance one day, wrapping 7 to 1.
- Gap of two or more periods under ResetAfterMissedDay: day 1.
- Existing PreserveStreak policy: retain the sequence through a gap and advance one reward day on the next successful claim; missed rewards are not paid retroactively.

All seven amounts come from validated DailyBonusRules. The existing configuration validator already rejects negative rewards and requires seven entries. Zero rewards are permitted and still consume that period's claim opportunity.

If the clock is earlier than the last successful claim, ClockInconsistent prevents another grant, without changing the saved timestamp or progress. The next time-based availability is the reset following the last claimed period. After advancing across several periods, the configured missed-day policy applies.

## Persistence, duplicates and retry

Claim executes under the existing `GameSession.Gate` and shared `CommandExecuting` guard:

1. Reject another command, every non-Idle state and unresolved PendingSpin.
2. Check Enabled; capture the UTC instant once and verify eligibility.
3. Calculate reward day/amount from the immutable configuration.
4. Use BalanceService.Credit to make a detached copy with checked balance arithmetic.
5. Set DailyLastClaimUtc and DailyStreakDay on that proposed complete snapshot; validate it.
6. Retain the proposal and enter Preparing.
7. Call ISaveRepository.Save once with a detached copy of the whole proposal.
8. Only after positive confirmation, commit the proposal into GameSession, return the confirmed award and enter Idle.

No separate balance/date/streak saves or additional player profile exist. Settings, FreeSpinsRemaining, BonusBet, selected bet, jackpot metadata, last win and previous settled Spin ID survive the copy unchanged. Overflow is rejected before a write and leaves the session Idle with its confirmed state intact.

A failed/throwing/null repository response enters Error. It returns no confirmed reward and keeps the previous runtime balance/date/streak, even if storage might already contain the proposed snapshot. Claim, Spin start, bet changes and Back remain blocked. Spin retry/recovery cannot clear the Daily Bonus error: Spin has no retained proposal for this transaction.

Call `DailyBonus.RetryPersistence()` to retry. It writes the EXACT retained balance, timestamp and streak. It does not read the clock, recalculate a day/reward, repeat the credit or reload an older snapshot. Repeated failures keep that proposal; success commits it and unlocks the session. Calling retry without a failed Daily Bonus proposal returns InvalidState. A second DailyBonusService cannot attach to the session and lose its retry ownership.

After process restart the repository's complete committed snapshot is authoritative. A claim that reached storage before an ambiguous error has its timestamp/streak and is unavailable in the same reward period. If it never committed, the previous snapshot remains and a new claim may be attempted. No pending daily transaction is persisted separately.

## Spin integration and correction to stage 4.6

GameSession remains the only authoritative snapshot and the only synchronization gate. Daily Bonus uses Preparing for its write and Error for unresolved persistence. Claims are rejected during Preparing, Spinning, Settling, Error and startup PendingSpin recovery. Spin settlement and Daily Bonus cannot overwrite each other's snapshots; the next accepted command reads the newly confirmed GameSession snapshot.

The small stage-4.6 correction moves SpinService's private reentrancy flag to GameSession. C# monitors permit same-thread reentry, so a clock/repository callback could otherwise call a different service while a command is executing, including before Preparing. Both services now reject that cross-service reentry through the shared flag. No Spin lifecycle transitions, settlement math or retry phases were refactored. Regression tests cover reentry from the clock while the session is still Idle, during actual writes, concurrent requests and all existing Spin tests.

Bootstrap creates one SystemClock and one BalanceService, injects the same balance/session/repository into both economic services and supplies the same clock to Spin's existing UTC delegate. DailyBonus and Clock are exposed for future UI integration. Startup Spin recovery and Main Menu connection remain as before. Bootstrap neither claims a reward nor opens a Daily Bonus popup.

## Executed verification

All tests ran in the ORIGINAL project `C:/UnityGames/IceP`, connected Unity **6000.3.10f1** Windows Editor on **2026-10-12**. No separate validation copy was used. PlayMode tests used unique temporary save directories through the existing ICEP_TEST_SAVE_DIRECTORY isolation; the real player profile was not accessed by the tests.

| Run | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| All EditMode: existing 296 + new 40 Daily Bonus cases | 336 | 0 | 0 |
| All PlayMode: existing 5 + new claim/reload case | 6 | 0 | 0 |

EditMode job: `0c38dcb06ff34dad9fffbba565eca6d4`, Passed, 5.1942568 seconds reported test duration.

PlayMode job: `704b12489dd2432990b4ed3cb5fe59cb`, Passed, 4.5879651 seconds reported test duration.

Unity asset refresh and script compilation/domain reload completed successfully, with no C# compiler errors. All existing mathematical engine, persistence/recovery and Spin lifecycle tests remained green. PlayMode loaded SC.unity, verified configuration/Main Menu/navigation, preserved startup recovery tests, claimed the reward programmatically, reloaded the scene, checked persisted timestamp/streak/balance, rejected a duplicate and verified Spin subsequently reserves the credited balance.

Final Console review showed only the THREE deliberately injected and expected initialization errors from the existing corrupt-save, initial-write-failure and recovery-write-failure tests, with no warnings or unexpected errors. A transient MCP plugin WebSocket-not-initialised warning occurred during the earlier compilation/domain reload; it did not recur in the final Console review and was not a gameplay/compiler warning. `git diff --check` passed; Git reported only existing LF/CRLF normalization notices for previously modified scene/settings files.

New EditMode coverage includes configured rewards through multiple cycles, disabled/zero/negative rewards, read-only status, duplicate claims, exact and non-midnight UTC boundaries, calendar/leap transitions, offset normalization, both configured streak policies, clock rollback/forward gaps, restart, before-commit and commit-then-fail errors, repeated exact retry without clock reads, repository exceptions, whole-profile preservation, concurrency/reentry, active Spin/settlement/recovery/Error and overflow.

## Deterministic example

Default configured rewards; reset 00:00 UTC; ResetAfterMissedDay:

```text
Initial balance: 20000
Monday 2026-10-12, 10:00 UTC

Claim Day 1:
Reward: 400
Balance: 20400

Second claim on Monday:
Rejected: AlreadyClaimed
Balance: 20400

Tuesday 2026-10-13, 00:00 UTC:
Claim Day 2
Reward: 600
Balance: 21000

Wednesday: No claim

Thursday 2026-10-15:
Claim Day 1
Reward: 400
Balance: 21400
```

The exact reset and missed-day calculations in this example are covered by deterministic FakeClock tests inspecting both repository and GameSession state.

## Remaining risks and limitations

- Device time is an offline clock, not trusted server time. A player who edits save files or manipulates the clock can bypass offline restrictions. Ordinary calls cannot duplicate a saved reward in its recorded period or claim while time is before its timestamp.
- Persistence retains stage-4.5 limitations: backup recovery can restore earlier economic state; filesystem/hardware failures and multi-process writers are outside this gate. One composed writer set per save directory is required.
- Failed proposals live in memory until retry or process death. After restart only the complete committed save is authoritative; no durable distributed transaction journal or separate Daily Bonus file was introduced.
- Configuration compatibility/migration behavior is inherited. Changing reset rules can reinterpret historical reward periods; no historic daily-rule version is stored in the save. Normal operation uses one immutable config for the session.
- Saves are synchronous. UI status rendering and retry controls belong to later Presentation work and were not added.
- Editor compilation and tests were executed. No target-player build, physical OS kill/power-loss test or mobile/WebGL validation was performed.

Section 4.8 was not started.
