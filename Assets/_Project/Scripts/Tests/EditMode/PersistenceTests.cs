using System;
using System.IO;
using IceP.Application.Persistence;
using IceP.Domain.Configuration;
using IceP.Infrastructure.Configuration;
using IceP.Infrastructure.Persistence;
using NUnit.Framework;
using UnityEditor;

namespace IceP.Tests
{
    public sealed class PersistenceTests
    {
        private string directory;
        private SlotRuntimeConfig config;
        private JsonSaveRepository repository;
        private string Primary => Path.Combine(directory, "save.json");
        private string Backup => Primary + ".bak";
        private static DateTimeOffset Week => new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
        private static Guid SpinId => Guid.Parse("e3bc16ab-86ec-40cc-a12b-3f6023be3951");

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "IceP-Persistence-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            config = new LocalSlotConfigProvider(AssetDatabase.LoadAssetAtPath<SlotGameConfigSO>(
                "Assets/_Project/Configs/SlotGameConfigSO.asset")).Load();
            repository = new JsonSaveRepository(directory, config);
        }

        [TearDown]
        public void TearDown() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        private SaveData Fresh() => InitialProfileFactory.Create(config);
        private void Commit(SaveData data)
        {
            var result = repository.Save(data);
            Assert.That(result.Success, Is.True, result.Diagnostic);
        }
        private SaveData Rich()
        {
            var data = Fresh();
            data.Balance = 1234567890123L;
            data.SelectedBet = 800;
            data.FreeSpinsRemaining = 11;
            data.BonusBet = 600;
            data.DailyLastClaimUtc = Week.AddDays(2).AddHours(7);
            data.DailyStreakDay = 4;
            data.JackpotWeekId = Week;
            data.JackpotWinsThisWeek = 1;
            data.LastSettledSpinId = Guid.Parse("ecf7a64d-5c48-4c7c-93c4-ced0399ba92b");
            data.LastWin = 5000000000L;
            data.Settings.MusicEnabled = false;
            data.Settings.SoundEnabled = false;
            data.Settings.MusicVolume = 0.25;
            data.Settings.SoundVolume = 0.75;
            return data;
        }

        private PendingSpin Pending(PendingSpinType type) => new PendingSpin
        {
            SpinId = SpinId, Type = type, EffectiveBet = 600,
            ConfigVersion = "historic-config-1", AcceptedUtc = Week.AddHours(1),
            JackpotWeekId = type == PendingSpinType.Paid ? Week : (DateTimeOffset?)null
        };

        [Test]
        public void MissingSaveAndFreshProfileUseConfiguredDefaults()
        {
            Assert.That(repository.Load().Status, Is.EqualTo(SaveLoadStatus.Missing));
            var fresh = Fresh();
            Assert.That(fresh.SaveVersion, Is.EqualTo(1));
            Assert.That(fresh.Balance, Is.EqualTo(config.Economy.StartingBalance));
            Assert.That(fresh.SelectedBet, Is.EqualTo(config.Economy.DefaultBet));
            Assert.That(fresh.FreeSpinsRemaining, Is.Zero);
            Assert.That(fresh.BonusBet, Is.Zero);
            Assert.That(fresh.DailyLastClaimUtc, Is.Null);
            Assert.That(fresh.DailyStreakDay, Is.Zero);
            Assert.That(fresh.JackpotWeekId, Is.Null);
            Assert.That(fresh.JackpotWinsThisWeek, Is.Zero);
            Assert.That(fresh.PendingSpin, Is.Null);
            Assert.That(fresh.LastSettledSpinId, Is.Null);
            Assert.That(fresh.LastWin, Is.Zero);
            Assert.That(repository.Save(fresh).Success, Is.True);
        }

        [Test]
        public void FactoryReadsNonDefaultEconomyWithoutDuplicatingConstants()
        {
            var asset = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<SlotGameConfigSO>(
                "Assets/_Project/Configs/SlotGameConfigSO.asset"));
            try
            {
                asset.Economy.StartingBalance = 87654;
                asset.Economy.DefaultBet = 400;
                var fresh = InitialProfileFactory.Create(new LocalSlotConfigProvider(asset).Load());
                Assert.That(fresh.Balance, Is.EqualTo(87654));
                Assert.That(fresh.SelectedBet, Is.EqualTo(400));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void CompleteSnapshotRoundTripsAllStateAndSettings()
        {
            var input = Rich();
            Assert.That(repository.Save(input).Success, Is.True);
            var loaded = new JsonSaveRepository(directory, config).Load();
            Assert.That(loaded.Status, Is.EqualTo(SaveLoadStatus.Loaded));
            var data = loaded.Data;
            Assert.That(data.SaveVersion, Is.EqualTo(input.SaveVersion));
            Assert.That(data.Balance, Is.EqualTo(input.Balance));
            Assert.That(data.SelectedBet, Is.EqualTo(800));
            Assert.That(data.FreeSpinsRemaining, Is.EqualTo(11));
            Assert.That(data.BonusBet, Is.EqualTo(600));
            Assert.That(data.DailyLastClaimUtc, Is.EqualTo(input.DailyLastClaimUtc));
            Assert.That(data.DailyStreakDay, Is.EqualTo(4));
            Assert.That(data.JackpotWeekId, Is.EqualTo(Week));
            Assert.That(data.JackpotWinsThisWeek, Is.EqualTo(1));
            Assert.That(data.LastSettledSpinId, Is.EqualTo(input.LastSettledSpinId));
            Assert.That(data.LastWin, Is.EqualTo(5000000000L));
            Assert.That(data.PendingSpin, Is.Null);
            Assert.That(data.Settings.SettingsVersion, Is.EqualTo(1));
            Assert.That(data.Settings.MusicEnabled, Is.False);
            Assert.That(data.Settings.SoundEnabled, Is.False);
            Assert.That(data.Settings.MusicVolume, Is.EqualTo(0.25));
            Assert.That(data.Settings.SoundVolume, Is.EqualTo(0.75));
        }

        [Test]
        public void UtcDatesRetainZeroOffsetAndRoundTripFractionalTicks()
        {
            var input = Rich();
            input.DailyLastClaimUtc = Week.AddHours(7).AddTicks(1234567);
            input.PendingSpin = Pending(PendingSpinType.Free);
            input.PendingSpin.AcceptedUtc = Week.AddTicks(7654321);
            Commit(input);
            var loaded = repository.Load();
            Assert.That(loaded.Status, Is.EqualTo(SaveLoadStatus.Loaded), loaded.Diagnostic);
            Assert.That(loaded.Data.DailyLastClaimUtc.Value.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(loaded.Data.DailyLastClaimUtc.Value.Ticks, Is.EqualTo(input.DailyLastClaimUtc.Value.Ticks));
            Assert.That(loaded.Data.JackpotWeekId.Value.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(loaded.Data.PendingSpin.AcceptedUtc.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(loaded.Data.PendingSpin.AcceptedUtc.Ticks, Is.EqualTo(input.PendingSpin.AcceptedUtc.Ticks));
            Assert.That(File.ReadAllText(Primary), Does.Contain("+00:00"));
        }

        [TestCase(PendingSpinType.Paid)]
        [TestCase(PendingSpinType.Free)]
        public void PendingSurvivesReconstructionWithoutEconomicChanges(PendingSpinType type)
        {
            var data = Fresh();
            data.Balance = 19400; // Already committed debit, prepared by future caller.
            data.PendingSpin = Pending(type);
            if (type == PendingSpinType.Free) data.BonusBet = 600; // Last reserved Free Spin: queue is zero.
            Assert.That(repository.Save(data).Success, Is.True);
            var loaded = new JsonSaveRepository(directory, config).Load().Data;
            Assert.That(loaded.Balance, Is.EqualTo(19400));
            Assert.That(loaded.FreeSpinsRemaining, Is.Zero);
            Assert.That(loaded.LastWin, Is.Zero);
            Assert.That(loaded.LastSettledSpinId, Is.Null);
            Assert.That(loaded.JackpotWinsThisWeek, Is.Zero);
            Assert.That(loaded.PendingSpin.SpinId, Is.EqualTo(SpinId));
            Assert.That(loaded.PendingSpin.Type, Is.EqualTo(type));
            Assert.That(loaded.PendingSpin.EffectiveBet, Is.EqualTo(600));
            Assert.That(loaded.PendingSpin.ConfigVersion, Is.EqualTo("historic-config-1"));
            Assert.That(loaded.PendingSpin.JackpotWeekId, Is.EqualTo(data.PendingSpin.JackpotWeekId));
            Assert.That(loaded.PendingSpin.AcceptedUtc, Is.EqualTo(Week.AddHours(1)));
        }

        [Test]
        public void PrimaryTakesPrecedenceOverBackupAndUncommittedTemp()
        {
            var data = Fresh();
            Commit(data);
            data.Balance = 18000;
            Commit(data);
            File.WriteAllText(Primary + ".tmp", "{\"Balance\":999999}");
            Assert.That(repository.Load().Data.Balance, Is.EqualTo(18000));
            Assert.That(repository.Load().Status, Is.EqualTo(SaveLoadStatus.Loaded));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MissingOrCorruptedPrimaryRecoversLastValidBackup(bool corrupt)
        {
            Commit(Rich());
            Commit(Fresh());
            if (corrupt) File.WriteAllText(Primary, "{truncated"); else File.Delete(Primary);
            var result = repository.Load();
            Assert.That(result.Status, Is.EqualTo(SaveLoadStatus.RecoveredBackup));
            Assert.That(result.Diagnostic, Does.Contain("earlier economic snapshot"));
            Assert.That(result.Data.Balance, Is.EqualTo(Rich().Balance));
            Assert.That(repository.Save(result.Data).Success, Is.True);
            Assert.That(repository.Load().Status, Is.EqualTo(SaveLoadStatus.Loaded));
        }

        [Test]
        public void CorruptPrimaryAndBackupRequireExplicitReset()
        {
            File.WriteAllText(Primary, "{");
            File.WriteAllText(Backup, "[]");
            Assert.That(repository.Load().Error, Is.EqualTo(PersistenceError.RecoveryRequired));
            Assert.That(repository.Save(Fresh()).Error, Is.EqualTo(PersistenceError.RecoveryRequired));
            Assert.That(File.ReadAllText(Primary), Is.EqualTo("{"));
            Assert.That(repository.Reset(Fresh()).Success, Is.True);
            Assert.That(repository.Load().Data.Balance, Is.EqualTo(20000));
            var archives = Directory.GetDirectories(directory, "reset-*");
            Assert.That(archives.Length, Is.EqualTo(1));
            Assert.That(File.ReadAllText(Path.Combine(archives[0], "save.json")), Is.EqualTo("{"));
        }

        [Test]
        public void TempOnlyIsNotACommittedProfileOrNormalFirstLaunch()
        {
            Commit(Rich());
            File.Move(Primary, Primary + ".tmp");
            Assert.That(repository.Load().Error, Is.EqualTo(PersistenceError.RecoveryRequired));
            Assert.That(repository.Save(Fresh()).Success, Is.False);
        }

        [TestCase(0)]
        [TestCase(2)]
        [TestCase(999)]
        public void UnsupportedVersionIsNotReplacedOrRolledBackToBackup(int version)
        {
            Commit(Fresh());
            Commit(Fresh());
            var json = File.ReadAllText(Primary).Replace("\"SaveVersion\": 1", "\"SaveVersion\": " + version);
            File.WriteAllText(Primary, json);
            Assert.That(repository.Load().Error, Is.EqualTo(PersistenceError.UnsupportedVersion));
            Assert.That(repository.Save(Fresh()).Error, Is.EqualTo(PersistenceError.UnsupportedVersion));
            Assert.That(File.ReadAllText(Primary), Is.EqualTo(json));
        }

        [Test]
        public void UnsupportedBackupIsProtectedEvenWhenPrimaryIsValid()
        {
            Commit(Fresh());
            File.WriteAllText(Backup, "{\"SaveVersion\":2}");
            Assert.That(repository.Load().Status, Is.EqualTo(SaveLoadStatus.Loaded));
            Assert.That(repository.Save(Rich()).Error, Is.EqualTo(PersistenceError.UnsupportedVersion));
            Assert.That(repository.Load().Data.Balance, Is.EqualTo(20000));
        }

        [TestCase("truncate")]
        [TestCase("missing")]
        [TestCase("string-money")]
        [TestCase("fraction-money")]
        [TestCase("overflow")]
        [TestCase("null-settings")]
        [TestCase("missing-settings-field")]
        [TestCase("bad-guid")]
        [TestCase("bad-date")]
        [TestCase("non-utc-date")]
        [TestCase("duplicate")]
        [TestCase("trailing")]
        [TestCase("unknown-field")]
        public void MalformedSerializedStateIsRejected(string mutation)
        {
            Commit(Fresh());
            var json = File.ReadAllText(Primary);
            switch (mutation)
            {
                case "truncate": json = json.Substring(0, json.Length / 2); break;
                case "missing": json = json.Replace("\"Balance\": 20000,", ""); break;
                case "string-money": json = json.Replace("\"Balance\": 20000", "\"Balance\": \"20000\""); break;
                case "fraction-money": json = json.Replace("\"Balance\": 20000", "\"Balance\": 1.5"); break;
                case "overflow": json = json.Replace("\"Balance\": 20000", "\"Balance\": 9223372036854775808"); break;
                case "null-settings": json = json.Substring(0, json.IndexOf("\"Settings\"", StringComparison.Ordinal)) + "\"Settings\":null}"; break;
                case "missing-settings-field": json = json.Replace("\"SoundEnabled\": true,", ""); break;
                case "bad-guid": json = json.Replace("\"LastSettledSpinId\": null", "\"LastSettledSpinId\": \"bad\""); break;
                case "bad-date": json = json.Replace("\"DailyLastClaimUtc\": null", "\"DailyLastClaimUtc\": \"2026-99-99\""); break;
                case "non-utc-date": json = json.Replace("\"DailyLastClaimUtc\": null", "\"DailyLastClaimUtc\": \"2026-10-12T01:00:00.0000000+01:00\""); break;
                case "duplicate": json = json.Replace("\"Balance\": 20000", "\"Balance\": 20000,\"Balance\": 200"); break;
                case "trailing": json += "{}"; break;
                case "unknown-field": json = json.Replace("\"Balance\": 20000", "\"Surprise\": 1,\"Balance\": 20000"); break;
            }
            File.WriteAllText(Primary, json);
            var load = repository.Load();
            Assert.That(load.Status, Is.EqualTo(SaveLoadStatus.Failed), mutation);
            Assert.That(load.Data, Is.Null);
            Assert.That(load.Diagnostic, Is.Not.Empty);
        }

        [TestCase("balance")]
        [TestCase("bet")]
        [TestCase("free-count")]
        [TestCase("bonus-bet")]
        [TestCase("bonus-without-queue")]
        [TestCase("daily-day")]
        [TestCase("daily-claim")]
        [TestCase("jackpot-count")]
        [TestCase("jackpot-week")]
        [TestCase("settled-id")]
        [TestCase("last-win")]
        [TestCase("pending-id")]
        [TestCase("pending-bet")]
        [TestCase("pending-enum")]
        [TestCase("pending-version")]
        [TestCase("pending-utc")]
        [TestCase("pending-week")]
        [TestCase("pending-settled")]
        [TestCase("free-pending-jackpot")]
        [TestCase("settings-version")]
        [TestCase("settings-volume")]
        [TestCase("settings-nan")]
        public void InvalidSnapshotsCannotReplaceExistingCommit(string mutation)
        {
            Commit(Fresh());
            var data = Fresh();
            switch (mutation)
            {
                case "balance": data.Balance = -1; break;
                case "bet": data.SelectedBet = 123; break;
                case "free-count": data.FreeSpinsRemaining = -1; break;
                case "bonus-bet": data.BonusBet = -1; break;
                case "bonus-without-queue": data.BonusBet = 200; break;
                case "daily-day": data.DailyStreakDay = 8; break;
                case "daily-claim": data.DailyLastClaimUtc = Week; break;
                case "jackpot-count": data.JackpotWinsThisWeek = 2; break;
                case "jackpot-week": data.JackpotWeekId = Week.AddDays(1); break;
                case "settled-id": data.LastSettledSpinId = Guid.Empty; break;
                case "last-win": data.LastWin = -1; break;
                case "settings-version": data.Settings.SettingsVersion = 2; break;
                case "settings-volume": data.Settings.SoundVolume = 1.01; break;
                case "settings-nan": data.Settings.MusicVolume = double.NaN; break;
                default:
                    data.PendingSpin = Pending(PendingSpinType.Paid);
                    switch (mutation)
                    {
                        case "pending-id": data.PendingSpin.SpinId = Guid.Empty; break;
                        case "pending-bet": data.PendingSpin.EffectiveBet = 0; break;
                        case "pending-enum": data.PendingSpin.Type = (PendingSpinType)99; break;
                        case "pending-version": data.PendingSpin.ConfigVersion = " "; break;
                        case "pending-utc": data.PendingSpin.AcceptedUtc = Week.ToOffset(TimeSpan.FromHours(3)); break;
                        case "pending-week": data.PendingSpin.JackpotWeekId = Week.AddDays(-7); break;
                        case "pending-settled": data.LastSettledSpinId = SpinId; break;
                        case "free-pending-jackpot": data.PendingSpin.Type = PendingSpinType.Free; data.BonusBet = 600; break;
                    }
                    break;
            }
            Assert.That(repository.Save(data).Success, Is.False, mutation);
            Assert.That(repository.Load().Data.Balance, Is.EqualTo(20000));
            Assert.That(repository.Load().Data.PendingSpin, Is.Null);
            Assert.That(File.Exists(Primary + ".tmp"), Is.False);
        }

        [TestCase((int)SaveWriteStage.BeforeTempWrite)]
        [TestCase((int)SaveWriteStage.TempWritten)]
        [TestCase((int)SaveWriteStage.BackupPrepared)]
        [TestCase((int)SaveWriteStage.PrimaryRemoved)]
        [TestCase((int)SaveWriteStage.BeforePromotion)]
        public void FailureAtEachWriteStagePreservesCompletePreviousSnapshot(int stage)
        {
            var original = Rich();
            original.PendingSpin = Pending(PendingSpinType.Free);
            Commit(original);
            var failing = new JsonSaveRepository(directory, config, current =>
            { if ((int)current == stage) throw new IOException("Injected " + stage); });
            var result = failing.Save(Fresh());
            Assert.That(result.Error, Is.EqualTo(PersistenceError.IoFailure));
            var loaded = new JsonSaveRepository(directory, config).Load().Data;
            Assert.That(loaded.Balance, Is.EqualTo(original.Balance));
            Assert.That(loaded.FreeSpinsRemaining, Is.EqualTo(11));
            Assert.That(loaded.PendingSpin.SpinId, Is.EqualTo(SpinId));
            Assert.That(loaded.DailyStreakDay, Is.EqualTo(4));
            Assert.That(loaded.LastWin, Is.EqualTo(original.LastWin));
            Assert.That(File.Exists(Primary + ".tmp"), Is.False);
            Assert.That(repository.Save(Fresh()).Success, Is.True);
        }

        [Test]
        public void ActualTempWriteFailurePreservesPrimary()
        {
            Commit(Rich());
            using (var locked = new FileStream(Primary + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
                Assert.That(repository.Save(Fresh()).Error, Is.EqualTo(PersistenceError.IoFailure));
            Assert.That(repository.Load().Data.Balance, Is.EqualTo(Rich().Balance));
        }

        [Test]
        public void ReadFailureIsNotMistakenForFirstLaunchOrCorruption()
        {
            Commit(Fresh());
            using (var locked = new FileStream(Primary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.That(repository.Load().Error, Is.EqualTo(PersistenceError.IoFailure));
                Assert.That(repository.Save(Rich()).Error, Is.EqualTo(PersistenceError.IoFailure));
            }
            Assert.That(repository.Load().Data.Balance, Is.EqualTo(20000));
        }

        [TestCase("SaveVersion")]
        [TestCase("Balance")]
        [TestCase("SelectedBet")]
        [TestCase("FreeSpinsRemaining")]
        [TestCase("BonusBet")]
        [TestCase("DailyLastClaimUtc")]
        [TestCase("DailyStreakDay")]
        [TestCase("JackpotWeekId")]
        [TestCase("JackpotWinsThisWeek")]
        [TestCase("LastSettledSpinId")]
        [TestCase("LastWin")]
        [TestCase("PendingSpin")]
        public void EveryRequiredTopLevelFieldMustBePresent(string field)
        {
            Commit(Fresh());
            var json = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(Primary),
                "\"" + field + "\"\\s*:\\s*[^,\\r\\n]+,?", "");
            File.WriteAllText(Primary, json);
            Assert.That(repository.Load().Status, Is.EqualTo(SaveLoadStatus.Failed));
            Assert.That(repository.Load().Diagnostic, Does.Contain(field));
        }

        [TestCase("Balance", "-1", "Balance")]
        [TestCase("SelectedBet", "123", "SelectedBet")]
        [TestCase("FreeSpinsRemaining", "-1", "FreeSpinsRemaining")]
        [TestCase("BonusBet", "123", "BonusBet")]
        [TestCase("DailyStreakDay", "99", "DailyStreakDay")]
        [TestCase("JackpotWinsThisWeek", "2", "JackpotWinsThisWeek")]
        [TestCase("LastWin", "-1", "LastWin")]
        public void LoadedEconomicValuesAreValidatedWithoutCorrection(string field, string value, string diagnostic)
        {
            Commit(Fresh());
            var json = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(Primary),
                "\"" + field + "\"\\s*:\\s*[0-9]+", "\"" + field + "\": " + value);
            File.WriteAllText(Primary, json);
            Assert.That(repository.Load().Status, Is.EqualTo(SaveLoadStatus.Failed));
            Assert.That(repository.Load().Diagnostic, Does.Contain(diagnostic));
            Assert.That(File.ReadAllText(Primary), Is.EqualTo(json));
        }

        [Test]
        public void UnsupportedSettingsVersionRequiresExplicitReset()
        {
            Commit(Fresh());
            var json = File.ReadAllText(Primary).Replace("\"SettingsVersion\": 1", "\"SettingsVersion\": 2");
            File.WriteAllText(Primary, json);
            Assert.That(repository.Load().Error, Is.EqualTo(PersistenceError.UnsupportedVersion));
            Assert.That(repository.Save(Fresh()).Success, Is.False);
        }

        [Test]
        public void ActualBackupWriteFailurePreservesPrimary()
        {
            Commit(Rich());
            Directory.CreateDirectory(Backup + ".tmp");
            Assert.That(repository.Save(Fresh()).Error, Is.EqualTo(PersistenceError.IoFailure));
            Assert.That(repository.Load().Data.Balance, Is.EqualTo(Rich().Balance));
        }

        [Test]
        public void InputAndReturnedNestedDtosDoNotMutateJsonCommit()
        {
            var data = Rich();
            data.PendingSpin = Pending(PendingSpinType.Free);
            Commit(data);
            data.Balance = 0;
            data.Settings.MusicVolume = 0;
            data.PendingSpin.EffectiveBet = 1000;
            var result = repository.Load();
            var loaded = result.Data;
            loaded.Balance = 1;
            loaded.Settings.MusicVolume = 1;
            loaded.PendingSpin.ConfigVersion = "mutated";
            Assert.That(result.Data.Settings.MusicVolume, Is.EqualTo(0.25));
            Assert.That(repository.Load().Data.Balance, Is.EqualTo(Rich().Balance));
            Assert.That(repository.Load().Data.PendingSpin.EffectiveBet, Is.EqualTo(600));
            Assert.That(repository.Load().Data.PendingSpin.ConfigVersion, Is.EqualTo("historic-config-1"));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void FakeFailsSpecificAttemptWithoutCommitAndRetrySucceeds(int attempt)
        {
            var fake = new FakeSaveRepository { FailOnSaveAttempt = attempt };
            if (attempt == 2) Assert.That(fake.Save(Rich()).Success, Is.True);
            Assert.That(fake.Save(Fresh()).Success, Is.False);
            Assert.That(fake.SaveAttemptCount, Is.EqualTo(attempt));
            Assert.That(fake.Load().Status, Is.EqualTo(attempt == 1 ? SaveLoadStatus.Missing : SaveLoadStatus.Loaded));
            if (attempt == 2) Assert.That(fake.Load().Data.Balance, Is.EqualTo(Rich().Balance));
            Assert.That(fake.Save(Fresh()).Success, Is.True);
            Assert.That(fake.Load().Data.Balance, Is.EqualTo(20000));
        }

        [Test]
        public void FakeLoadAndPersistentWriteFailureAreExplicitAndDetached()
        {
            var data = Rich();
            data.PendingSpin = Pending(PendingSpinType.Free);
            var fake = new FakeSaveRepository();
            fake.Save(data);
            data.Settings.SoundVolume = 0;
            data.PendingSpin.EffectiveBet = 1000;
            var loaded = fake.Load().Data;
            loaded.Settings.SoundVolume = 1;
            loaded.PendingSpin.EffectiveBet = 200;
            Assert.That(fake.Load().Data.Settings.SoundVolume, Is.EqualTo(0.75));
            Assert.That(fake.Load().Data.PendingSpin.EffectiveBet, Is.EqualTo(600));
            fake.FailLoads = true;
            Assert.That(fake.Load().Error, Is.EqualTo(PersistenceError.IoFailure));
            fake.FailLoads = false;
            fake.FailSaves = true;
            Assert.That(fake.Save(Fresh()).Success, Is.False);
            Assert.That(fake.Load().Data.Balance, Is.EqualTo(Rich().Balance));
        }
    }
}
