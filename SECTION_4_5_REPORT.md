# Section 4.5 — Persistence and Recovery

Scope: section 4.5 only. No SpinService, GameSession, economic transactions, bonus claims, jackpot drawing, animation changes or automatic PendingSpin resolution.

## Files

Created under `Assets/_Project/Scripts/` (with Unity `.meta` files for new folders/scripts):

- `Application/Persistence/SaveData.cs`: complete state, PendingSpin and settings DTOs.
- `Application/Persistence/ISaveRepository.cs`: synchronous contract and structured results.
- `Application/Persistence/InitialProfileFactory.cs`: validated configuration-based defaults.
- `Application/Persistence/SaveDataValidator.cs`: pure C# state/compatibility validation.
- `Infrastructure/Persistence/SaveJsonCodec.cs`: strict JSON shape/type checks and serialization.
- `Infrastructure/Persistence/JsonSaveRepository.cs`: files, backup, recovery and explicit reset.
- `Tests/EditMode/FakeSaveRepository.cs`: deterministic in-memory test double.
- `Tests/EditMode/PersistenceTests.cs`: isolated filesystem and fake tests.

Modified:

- `Bootstrap/SlotBootstrapper.cs`: load/create/persist/retain verified profile before navigation.
- `Infrastructure/IceP.Infrastructure.asmdef`: reference the already-installed `Unity.Newtonsoft.Json` assembly.
- `Tests/PlayMode/GameSceneSmokeTests.cs`: retain the existing navigation smoke test, isolate storage, check fresh-profile creation and initialization failures.
- `Tests/PlayMode/IceP.Tests.PlayMode.asmdef`: reference Application DTOs used by the smoke tests.
- This report, `SECTION_4_5_REPORT.md`.

No new third-party package was added. Newtonsoft JSON 3.0.2 was already resolved by the project. The explicit assembly reference is necessary to detect missing JSON fields and avoid accepting default-initialized economy values. Domain, engine, configuration assets, scene, UI layout and configuration providers remain unchanged.

## Schema and compatibility

`SaveVersion = 1`. All listed JSON properties are required, including properties whose value may be null. Unknown or duplicate properties, extra trailing content, wrong primitive types, integer overflow, truncated objects and malformed identifiers/dates are rejected before state is exposed. Serialization does not use polymorphic type metadata. Monetary fields use `long`; counters use `int`.

| Field | C# type / semantics |
|---|---|
| SaveVersion | int; only 1 supported |
| Balance, SelectedBet, BonusBet, LastWin | long |
| FreeSpinsRemaining, DailyStreakDay, JackpotWinsThisWeek | int |
| DailyLastClaimUtc | DateTimeOffset?; null before first claim |
| JackpotWeekId | DateTimeOffset?; configured week-start instant in UTC; null before any recorded weekly state |
| LastSettledSpinId | Guid?; null before settlement; empty GUID forbidden |
| PendingSpin | nullable PendingSpin DTO |
| Settings | required UserSettings DTO; SettingsVersion=1, MusicEnabled/SoundEnabled booleans, MusicVolume/SoundVolume doubles in [0,1] |

GUIDs use canonical `D` text. Dates use invariant round-trip `O` text with explicit zero UTC offset. A week ID is an instant at the configured reset weekday/time, rather than a locale-dependent week number. Fresh profiles have no week recorded and zero weekly wins, so jackpot is initially eligible. No date advancement, weekly reset or reward processing occurs during loading.

State validation is separate from configuration validation: repository construction and profile creation require valid SlotRuntimeConfig; invalid saved state yields persistence diagnostics. Balance/last win/counts must be non-negative; selected and active bonus bets must remain in AvailableBets; claimed date/streak and jackpot week/win count must agree. BonusBet exists exactly while a Free Spin queue or pending Free Spin exists, including the last reserved Free Spin when remaining count is zero. Settings and enum ranges are checked.

Compatibility policy: no implicit migration or economic correction in v1. Unsupported SaveVersion or SettingsVersion requires explicit reset/migration outside ordinary Save. A newer primary is never replaced by an older backup; Save also refuses to overwrite an unsupported backup. Historical selected/bonus/pending bets not present in current AvailableBets, changed weekly reset boundaries, or a streak outside the current cycle are rejected. PendingSpin.ConfigVersion is opaque historical metadata, not required to equal current ConfigVersion; loading preserves it. This stage does not reconstruct historic rules or freeze a bonus chain across configuration changes. Any future migration must be explicit and preserve the transaction policy.

## PendingSpin

Fields: `Guid SpinId`, `PendingSpinType Type` (Paid=0 / Free=1), `long EffectiveBet`, `string ConfigVersion`, `DateTimeOffset? JackpotWeekId`, `DateTimeOffset AcceptedUtc`.

Metadata represents an already persisted resource reservation. It stores no potential payout, outcome, delegates, configuration object or presentation state. A free pending spin must use BonusBet and have no jackpot week. A paid pending spin cannot coexist with a free-spin queue. A supplied pending jackpot week must contain AcceptedUtc. A pending ID cannot equal LastSettledSpinId.

Load preserves pending metadata and all economic values exactly. It neither deducts nor refunds coins, consumes additional Free Spins, awards winnings, changes weekly wins nor clears pending. Section 4.6 must eventually clear an interrupted spin without additional debit or rewards. Bootstrap retains the unresolved record for that future Application code.

## API and ownership

```csharp
SaveLoadResult Load();
SaveWriteResult Save(SaveData snapshot);
SaveWriteResult Reset(SaveData initialProfile);
```

Load statuses: Missing, Loaded, RecoveredBackup, Failed. Errors: None, InvalidData, UnsupportedVersion, RecoveryRequired, IoFailure. Diagnostics identify the offending field/file or I/O failure. `SaveLoadResult.Data` returns a new deep copy on each access. Save copies before validation/serialization. Nested Settings/PendingSpin are copied; there are no mutable collections or runtime references in the schema. Callers must not mutate inputs concurrently. The repository is synchronous, intended for one writer per storage directory; no unnecessary Task wrappers or service locator.

FakeSaveRepository is a test-support class in the EditMode assembly. Constructor can seed a detached profile; without one Load reports Missing. `FailLoads`, `FailSaves`, `FailOnSaveAttempt` and `SaveAttemptCount` provide deterministic failures. A specific-attempt failure is transient because the count advances; failed saves do not assign committed state. Load/input/constructor snapshots are copied. Reset uses the same save/failure behavior. It intentionally does not duplicate runtime schema validation: transaction tests can isolate persistence outcomes.

## Files and write strategy

Directory is constructor-injected. Production bootstrap supplies `UnityEngine.Application.persistentDataPath`. Names:

```text
save.json          primary committed snapshot
save.json.tmp      uncommitted new snapshot
save.json.bak      previous verified committed snapshot
save.json.bak.tmp  staged backup
reset-<guid>/      originals archived during an explicit reset
```

1. Copy and validate a complete input; serialize before modifying committed files. Refuse unsupported/unreadable existing files and a directory containing only invalid committed files or abandoned temps without a committed profile.
2. Write the temp through FileStream, flush with `Flush(true)`, close, read back and validate.
3. If primary is valid, serialize it to backup-temp, flush/close/read/validate while primary remains intact. Delete old backup and rename backup-temp to backup. If primary is invalid/missing and backup is valid, keep that backup untouched.
4. Delete primary if present and rename temp to primary. The successful rename is the commit point. No fallible work occurs after this point; cleanup cannot turn a committed save into a reported failure.
5. Best-effort cleanup of uncommitted temps; failures to clean up are harmless to load selection.

The portable delete/rename sequence avoids relying on uniform File.Replace support. It deliberately has a brief primary-file gap; the previous committed profile remains in backup. Filesystem operations/permissions are reported as IoFailure rather than interpreted as first launch. A malformed primary falls back to a validated backup, but an inaccessible primary blocks loading conservatively.

| Failure point | Result |
|---|---|
| Input validation/serialization, existing-version check | Committed files unchanged; no new snapshot accepted |
| Temp create/write/flush/readback | Old primary/backup remain usable; partial temp never committed |
| Backup staging/write/validation | Primary stays intact |
| Old backup removal/new backup rename | Primary stays intact even if backup is absent |
| Primary deletion | Previous snapshot already verified in backup; failure leaves primary or backup available |
| After primary deletion, before temp promotion | Load recovers previous backup; attempted new state has not committed |
| Successful promotion | New primary is the full snapshot; backup holds previous state |
| Cleanup failure | Ignore leftover temps; committed selection unchanged |

For first-ever creation there is no previous state to preserve. A failed first save blocks bootstrap; no gameplay starts. A temp-only directory after process death reports RecoveryRequired, not a committed save or silent new-player grant. Retrying an ordinary first-save I/O failure can succeed after safe temp cleanup.

## Recovery and reset

- No primary, backup or abandoned temp: Missing. Application constructs defaults from validated config and persists them before enabling navigation.
- Valid primary: load it; do not promote a newer temp.
- Invalid/missing primary with valid backup: RecoveredBackup with explicit warning. Loading does not rewrite files; the next successful Save reestablishes primary.
- Invalid committed files without valid backup: RecoveryRequired, with field/file diagnostics. No hidden new-profile grant.
- Unsupported primary/settings version: UnsupportedVersion, even with older valid backup. Unsupported backup is protected during writes.
- Unreadable files: IoFailure; do not bypass a permission/read failure as corruption.

After an explicit application/user reset decision, call `repository.Reset(InitialProfileFactory.Create(config))`. Reset validates first, copies existing primary/backup/temp files into a unique reset archive, deletes active files, and saves the requested new profile. Bootstrap never calls Reset automatically. No reset UI was added. Reset itself is not atomic: if it fails midway, report the failure and inspect the archive before proceeding; no gameplay should use an unconfirmed reset result. No balance reconciliation is attempted.

Restoring backup can restore an earlier economic state, including an earlier accepted/settled-spin record. This can roll back economic activity; a snapshot backup is not a transaction journal. Full anti-rollback or tamper protection is out of scope.

## Bootstrap and earlier-section corrections

After local configuration and visual validation: create repository, load; create/persist configured initial profile only on Missing; report recovery; validate loaded state; retain a private copy exposed through detached `LoadedProfile`; create existing OutcomeGenerator and navigation presenter. Corrupt/incompatible/I/O failures retain InitializationError, show the existing menu, disable menu actions and never attach a gameplay navigation presenter. No new services or scenes are created.

The only earlier-test correction is storage isolation: the existing scene smoke test now sets/restores `ICEP_TEST_SAVE_DIRECTORY` and uses a unique temporary directory. The bootstrap honors this development override only in UNITY_EDITOR builds. Player builds always use persistentDataPath. Test cleanup deletes only its own directory/file. Existing navigation behavior is retained.

## Verification

Initial batch validation used `Logs/Section4_5/ValidationProject` because the original project was locked. Before starting section 4.6 on 2026-10-12, the current implementation was independently verified through the connected Unity 6000.3.10f1 editor in the original `C:/UnityGames/IceP` project: all 257 EditMode tests and all 3 existing PlayMode smoke tests passed, with no errors/warnings in the prerequisite Console check. This confirms configuration, mathematics, SaveData validation, JSON round trips, backup recovery, fake repository and bootstrap integration. No real player directory was used by the tests.

The UTC parsing correction is already present in SaveJsonCodec.Decode: `DateParseHandling.None` prevents round-trip UTC strings from becoming Date JTokens before strict serialized-shape validation. Existing rich-profile/pending round-trip tests pass. Section 4.6 also adds a regression explicitly checking zero offsets and fractional ticks; no additional production persistence correction was needed. Final expanded verification is recorded in `SECTION_4_6_REPORT.md`.

## Limits

Windows editor validation does not establish Android/iOS/WebGL/console guarantees. Flush support, rename semantics, permissions and filesystem durability need verification on each target. WebGL browser storage synchronization is not implemented. Directory metadata is not explicitly fsynced; power loss/storage failure can still lose a rename or both snapshots. No claim of universal crash-proof atomicity, disk-corruption protection or multi-process serialization is made. Writes block the caller thread; the snapshots are small and a synchronous contract supports the later accept/settle lifecycle. Reset archives grow only following explicit resets. No Spin lifecycle/recovery algorithm from section 4.6 has been implemented.
