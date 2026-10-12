using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IceP.Application;
using IceP.Application.Persistence;
using IceP.Domain.Configuration;
using IceP.Domain.SlotEngine;
using IceP.Infrastructure.Configuration;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IceP.Tests
{
    public sealed class SpinLifecycleTests
    {
        private SlotRuntimeConfig config;
        private FakeSaveRepository repository;
        private GameSession session;
        private SpinService service;
        private SequenceRandom random;
        private DateTimeOffset now;

        private sealed class SequenceRandom : IRandomSource
        {
            public int Calls;
            public Action BeforeDraw;
            public int[] Stops = new int[5];
            public bool Throw;
            public int NextInt(int minInclusive, int maxExclusive)
            {
                BeforeDraw?.Invoke();
                if (Throw) throw new InvalidOperationException("Injected outcome failure.");
                return Stops[Calls++ % 5];
            }
        }
        [SetUp]
        public void Setup()
        {
            // Real valid strips/paylines; a deterministic configurable paytable pays 2500
            // for the center S1 x5 line. All other multipliers are zero.
            var asset = Object.Instantiate(AssetDatabase.LoadAssetAtPath<SlotGameConfigSO>(
                "Assets/_Project/Configs/SlotGameConfigSO.asset"));
            try
            {
                foreach (var payout in asset.Paytable)
                { payout.Match3 = 0; payout.Match4 = 0; payout.Match5 = payout.Symbol == SymbolId.S1 ? 50 : 0; }
                config = new LocalSlotConfigProvider(asset).Load();
            }
            finally { Object.DestroyImmediate(asset); }
            now = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
            random = new SequenceRandom();
            ChooseStops(s => s == SymbolId.S1);
            var profile = InitialProfileFactory.Create(config);
            profile.SelectedBet = 1000;
            repository = new FakeSaveRepository(profile);
            Recreate();
        }
        private void Recreate()
        {
            session = new GameSession(repository.Load().Data);
            service = new SpinService(session, config, new SlotEngine(config, random),
                new BalanceService(config), repository, () => now);
        }
        private void ChooseStops(Func<SymbolId, bool> center)
        {
            for (int reel = 0; reel < 5; reel++)
            {
                var strip = config.ReelStrips[reel].Symbols;
                // Avoid equal top/bottom rows so only the center straight line wins.
                random.Stops[reel] = Enumerable.Range(0, strip.Count).First(i => center(strip[(i + 1) % strip.Count]) &&
                    strip[i] != strip[(i + 1) % strip.Count] && strip[(i + 2) % strip.Count] != strip[(i + 1) % strip.Count]);
            }
        }
        private void Free(int count, long bet = 600)
        {
            var profile = repository.Load().Data;
            profile.FreeSpinsRemaining = count; profile.BonusBet = bet;
            repository.Save(profile); Recreate();
        }
        private void ConfigureBonus(long multiplier, bool retrigger)
        {
            config = new SlotRuntimeConfig(config.ConfigVersion, config.ReelCount, config.VisibleRows,
                config.ScatterId, config.Symbols, config.ReelStrips, config.Paylines, config.Paytable,
                config.Economy, new ScatterRules(config.Scatter.Awards, multiplier, retrigger),
                config.DailyBonus, config.Jackpot, config.DebugRandom);
            Recreate();
        }
        private void Consistent(long coins, int free, long bonus, bool pending)
        {
            foreach (var state in new[] { session.Snapshot, repository.Load().Data })
            {
                Assert.That(state.Balance, Is.EqualTo(coins));
                Assert.That(state.FreeSpinsRemaining, Is.EqualTo(free));
                Assert.That(state.BonusBet, Is.EqualTo(bonus));
                Assert.That(state.PendingSpin != null, Is.EqualTo(pending));
                Assert.That(new SaveDataValidator(config).Validate(state), Is.Empty);
            }
        }
        [Test]
        public void PaidSpinPersistsBeforeRngAndSettlesOnlyOnCompletion()
        {
            random.BeforeDraw = () =>
            {
                Assert.That(repository.Load().Data.Balance, Is.EqualTo(19000));
                Assert.That(repository.Load().Data.PendingSpin, Is.Not.Null);
                Assert.That(session.State, Is.EqualTo(GameSessionState.Preparing));
            };
            var started = service.StartSpin();
            Assert.That(started.Success, Is.True, started.Diagnostic);
            Assert.That(started.Spin.PotentialPayout, Is.EqualTo(2500));
            Assert.That(started.Spin.Configuration, Is.SameAs(config));
            Assert.That(started.Spin.Evaluation.Outcome.Stops.Count, Is.EqualTo(5));
            Assert.That(started.Spin.Evaluation.Outcome.Grid, Is.Not.Null);
            Assert.That(started.Spin.JackpotWeekId, Is.EqualTo(now));
            Consistent(19000, 0, 0, true);
            Assert.That(session.State, Is.EqualTo(GameSessionState.Spinning));
            var complete = service.CompleteSpin(started.Spin.SpinId);
            Assert.That(complete.ConfirmedPayout, Is.EqualTo(2500));
            Consistent(21500, 0, 0, false);
            Assert.That(session.State, Is.EqualTo(GameSessionState.Idle));
            Assert.That(repository.Load().Data.LastSettledSpinId, Is.EqualTo(started.Spin.SpinId));
            Assert.That(session.Snapshot.LastWin, Is.EqualTo(2500));
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(2));
        }
        [Test]
        public void InsufficientBalanceRejectsBeforeSavingOrDrawing()
        {
            var profile = repository.Load().Data; profile.Balance = 999; repository.Save(profile); Recreate();
            int saves = repository.SaveAttemptCount;
            Assert.That(service.StartSpin().Error, Is.EqualTo(SpinError.InsufficientBalance));
            Consistent(999, 0, 0, false);
            Assert.That(random.Calls, Is.Zero);
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(saves));
            Assert.That(session.State, Is.EqualTo(GameSessionState.Idle));
        }
        [Test]
        public void InvalidBetAndMissingCompletionDoNotMutateState()
        {
            Assert.That(service.SelectBet(123).Error, Is.EqualTo(SpinError.InvalidBet));
            Assert.That(service.CompleteSpin(Guid.NewGuid()).Error, Is.EqualTo(SpinError.NoActiveSpin));
            Assert.That(service.CompleteSpin(Guid.Empty).Error, Is.EqualTo(SpinError.WrongSpinId));
            Consistent(20000, 0, 0, false);
            Assert.That(repository.SaveAttemptCount, Is.Zero);
        }
        [Test]
        public void DoubleStartAndWrongCompletionDoNotTouchActiveTransaction()
        {
            var id = service.StartSpin().Spin.SpinId;
            Assert.That(service.StartSpin().Error, Is.EqualTo(SpinError.Busy));
            Assert.That(service.CompleteSpin(Guid.NewGuid()).Error, Is.EqualTo(SpinError.WrongSpinId));
            Assert.That(service.SelectBet(200).Error, Is.EqualTo(SpinError.Busy));
            Consistent(19000, 0, 0, true);
            Assert.That(session.Snapshot.PendingSpin.SpinId, Is.EqualTo(id));
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(1));
            Assert.That(random.Calls, Is.EqualTo(5));
        }
        [Test]
        public void DuplicateAndDelayedCompletionNeverSettleNewerSpin()
        {
            var first = service.StartSpin().Spin.SpinId;
            service.CompleteSpin(first);
            Assert.That(service.CompleteSpin(first).AlreadySettled, Is.True);
            Consistent(21500, 0, 0, false);
            var second = service.StartSpin().Spin.SpinId;
            Assert.That(service.CompleteSpin(first).AlreadySettled, Is.True);
            Consistent(20500, 0, 0, true);
            Assert.That(session.Snapshot.PendingSpin.SpinId, Is.EqualTo(second));
            Assert.That(session.State, Is.EqualTo(GameSessionState.Spinning));
            service.CompleteSpin(second);
            Assert.That(service.CompleteSpin(first).Error, Is.EqualTo(SpinError.NoActiveSpin));
            Consistent(23000, 0, 0, false);
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(4));
        }
        [TestCase(false)]
        [TestCase(true)]
        public void DebitFailureRetainsRuntimeAndRetriesIdenticalReservation(bool uncertain)
        {
            Guid? attempted = null;
            repository.BeforeSave = snapshot =>
            {
                if (attempted == null) attempted = snapshot.PendingSpin.SpinId;
                else Assert.That(snapshot.PendingSpin.SpinId, Is.EqualTo(attempted));
            };
            repository.FailSaves = !uncertain; repository.CommitThenFail = uncertain;
            var failed = service.StartSpin();
            Assert.That(failed.Error, Is.EqualTo(SpinError.PersistenceFailure));
            Assert.That(failed.Spin, Is.Null);
            Assert.That(session.Snapshot.Balance, Is.EqualTo(20000));
            Assert.That(session.Snapshot.PendingSpin, Is.Null);
            Assert.That(repository.Load().Data.Balance, Is.EqualTo(uncertain ? 19000 : 20000));
            Assert.That(session.State, Is.EqualTo(GameSessionState.Error));
            Assert.That(random.Calls, Is.Zero);
            Assert.That(service.StartSpin().Error, Is.EqualTo(SpinError.Busy));
            Assert.That(service.RecoverInterruptedSpin().Error, Is.EqualTo(SpinError.Busy));
            repository.FailSaves = false; repository.CommitThenFail = false;
            var retry = service.RetryPersistence();
            Assert.That(retry.Success, Is.True, retry.Diagnostic);
            Consistent(19000, 0, 0, true);
            Assert.That(random.Calls, Is.EqualTo(5));
            Assert.That(repository.Load().Data.PendingSpin.SpinId, Is.EqualTo(retry.Spin.SpinId));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedFreeReservationRetryConsumesExactlyOneSpin(bool uncertain)
        {
            Free(2);
            repository.FailSaves = !uncertain; repository.CommitThenFail = uncertain;
            Assert.That(service.StartSpin().Error, Is.EqualTo(SpinError.PersistenceFailure));
            Assert.That(session.Snapshot.FreeSpinsRemaining, Is.EqualTo(2));
            Assert.That(session.Snapshot.Balance, Is.EqualTo(20000));
            Assert.That(repository.Load().Data.FreeSpinsRemaining, Is.EqualTo(uncertain ? 1 : 2));
            repository.FailSaves = false; repository.CommitThenFail = false;
            Assert.That(service.RetryPersistence().Success, Is.True);
            Consistent(20000, 1, 600, true);
            Assert.That(random.Calls, Is.EqualTo(5));
        }

        [Test]
        public void QuitBeforeDebitCommitKeepsPreviousBalance()
        {
            repository.FailSaves = true;
            Assert.That(service.StartSpin().Success, Is.False);
            repository.FailSaves = false; Recreate();
            Assert.That(service.RecoverInterruptedSpin().Success, Is.True);
            Consistent(20000, 0, 0, false);
            Assert.That(random.Calls, Is.Zero);
        }
        [TestCase(false)]
        [TestCase(true)]
        public void SettlementFailureBlocksUntilExactSnapshotRetry(bool uncertain)
        {
            var id = service.StartSpin().Spin.SpinId;
            repository.FailSaves = !uncertain; repository.CommitThenFail = uncertain;
            Assert.That(service.CompleteSpin(id).Error, Is.EqualTo(SpinError.PersistenceFailure));
            Assert.That(session.Snapshot.Balance, Is.EqualTo(19000));
            Assert.That(session.Snapshot.LastSettledSpinId, Is.Null);
            Assert.That(repository.Load().Data.Balance, Is.EqualTo(uncertain ? 21500 : 19000));
            Assert.That(session.State, Is.EqualTo(GameSessionState.Error));
            Assert.That(service.StartSpin().Error, Is.EqualTo(SpinError.Busy));
            Assert.That(service.CompleteSpin(Guid.NewGuid()).Error, Is.EqualTo(SpinError.WrongSpinId));
            repository.FailSaves = false; repository.CommitThenFail = false;
            Assert.That(service.CompleteSpin(id).Success, Is.True);
            Assert.That(service.CompleteSpin(id).AlreadySettled, Is.True);
            Consistent(21500, 0, 0, false);
            Assert.That(random.Calls, Is.EqualTo(5));
        }
        [Test]
        public void QuitAfterDebitLosesBetAndNeverAwardsPotentialWin()
        {
            service.StartSpin(); Recreate();
            Assert.That(session.State, Is.EqualTo(GameSessionState.Error));
            Assert.That(service.StartSpin().Error, Is.EqualTo(SpinError.Busy));
            Assert.That(service.RecoverInterruptedSpin().Success, Is.True);
            Consistent(19000, 0, 0, false);
            Assert.That(session.Snapshot.LastWin, Is.Zero);
            Assert.That(session.Snapshot.LastSettledSpinId, Is.Null);
            Assert.That(service.StartSpin().Success, Is.True);
        }
        [TestCase(false)]
        [TestCase(true)]
        public void QuitAfterSettlementKeepsPayoutEvenWithAmbiguousConfirmation(bool uncertain)
        {
            var id = service.StartSpin().Spin.SpinId;
            repository.CommitThenFail = uncertain;
            service.CompleteSpin(id);
            repository.CommitThenFail = false;
            Recreate();
            Assert.That(service.RecoverInterruptedSpin().Success, Is.True);
            Assert.That(service.CompleteSpin(id).AlreadySettled, Is.True);
            Consistent(21500, 0, 0, false);
            Assert.That(session.Snapshot.LastWin, Is.EqualTo(2500));
        }
        [TestCase(false)]
        [TestCase(true)]
        public void RecoveryFailureCanRetryWithoutEconomicMutation(bool uncertain)
        {
            service.StartSpin(); Recreate();
            repository.FailSaves = !uncertain; repository.CommitThenFail = uncertain;
            Assert.That(service.RecoverInterruptedSpin().Error, Is.EqualTo(SpinError.PersistenceFailure));
            Assert.That(session.State, Is.EqualTo(GameSessionState.Error));
            Assert.That(session.Snapshot.PendingSpin, Is.Not.Null);
            Assert.That(session.Snapshot.Balance, Is.EqualTo(19000));
            repository.FailSaves = false; repository.CommitThenFail = false;
            Assert.That(service.RecoverInterruptedSpin().Success, Is.True);
            Assert.That(service.RecoverInterruptedSpin().Success, Is.True);
            Consistent(19000, 0, 0, false);
        }
        [TestCase(1)]
        [TestCase(3)]
        public void InterruptedFreeSpinLosesOnlyReservedSpin(int count)
        {
            Free(count);
            Assert.That(service.StartSpin().Spin.Type, Is.EqualTo(PendingSpinType.Free));
            Consistent(20000, count - 1, 600, true);
            Recreate(); service.RecoverInterruptedSpin();
            Consistent(20000, count - 1, count == 1 ? 0 : 600, false);
            Assert.That(session.Snapshot.LastWin, Is.Zero);
        }
        [Test]
        public void FreePayoutAndBetChangesPreserveBonusUntilChainEnds()
        {
            Free(2);
            Assert.That(service.SelectBet(200).Success, Is.True);
            var first = service.StartSpin();
            Assert.That(first.Spin.EffectiveBet, Is.EqualTo(600));
            Assert.That(first.Spin.JackpotWeekId, Is.Null);
            Assert.That(first.Spin.PotentialPayout, Is.EqualTo(1500));
            service.CompleteSpin(first.Spin.SpinId);
            Consistent(21500, 1, 600, false);
            Recreate();
            var last = service.StartSpin(); service.CompleteSpin(last.Spin.SpinId);
            Consistent(23000, 0, 0, false);
            Assert.That(service.StartSpin().Spin.Type, Is.EqualTo(PendingSpinType.Paid));
            Consistent(22800, 0, 0, true);
        }
        [Test]
        public void PaidScatterStartsBonusAndFreeScatterRetriggers()
        {
            ChooseStops(s => s == SymbolId.SCATTER);
            var paid = service.StartSpin();
            Assert.That(paid.Spin.Evaluation.Scatter.AwardedFreeSpins, Is.EqualTo(20));
            service.CompleteSpin(paid.Spin.SpinId);
            Consistent(19000, 20, 1000, false);
            var free = service.StartSpin();
            Consistent(19000, 19, 1000, true);
            service.CompleteSpin(free.Spin.SpinId);
            Consistent(19000, 39, 1000, false);
        }

        [Test]
        public void FreeMultiplierAndDisabledRetriggerUseConfiguration()
        {
            ConfigureBonus(3, false); Free(2);
            var win = service.StartSpin();
            Assert.That(win.Spin.PotentialPayout, Is.EqualTo(4500));
            service.CompleteSpin(win.Spin.SpinId);
            Consistent(24500, 1, 600, false);
            ChooseStops(s => s == SymbolId.SCATTER);
            var scatter = service.StartSpin();
            Assert.That(scatter.Spin.Evaluation.Scatter.ScatterCount, Is.EqualTo(5));
            Assert.That(scatter.Spin.Evaluation.Scatter.AwardedFreeSpins, Is.Zero);
            service.CompleteSpin(scatter.Spin.SpinId);
            Consistent(24500, 0, 0, false);
        }

        [Test]
        public void FailedFreeSettlementRetryNeverDuplicatesScatterReward()
        {
            Free(2); ChooseStops(s => s == SymbolId.SCATTER);
            var id = service.StartSpin().Spin.SpinId;
            repository.CommitThenFail = true;
            Assert.That(service.CompleteSpin(id).Success, Is.False);
            Assert.That(session.Snapshot.FreeSpinsRemaining, Is.EqualTo(1));
            Assert.That(repository.Load().Data.FreeSpinsRemaining, Is.EqualTo(21));
            repository.CommitThenFail = false;
            Assert.That(service.RetryPersistence().Success, Is.True);
            Assert.That(service.CompleteSpin(id).AlreadySettled, Is.True);
            Consistent(20000, 21, 600, false);
        }

        [Test]
        public void RepositoryExceptionIsReportedAndDoesNotExposeAnOutcome()
        {
            repository.BeforeSave = _ => throw new System.IO.IOException("Injected repository exception.");
            Assert.That(service.StartSpin().Error, Is.EqualTo(SpinError.PersistenceFailure));
            Consistent(20000, 0, 0, false);
            Assert.That(session.State, Is.EqualTo(GameSessionState.Error));
            Assert.That(random.Calls, Is.Zero);
            repository.BeforeSave = null;
            Assert.That(service.RetryPersistence().Success, Is.True);
            Consistent(19000, 0, 0, true);
        }

        [Test]
        public void PaidAndFreeOperationsPreserveExistingJackpotAndOtherProfileState()
        {
            var profile = repository.Load().Data;
            profile.JackpotWeekId = now; profile.JackpotWinsThisWeek = 1;
            profile.DailyStreakDay = 1; profile.DailyLastClaimUtc = now;
            profile.Settings.MusicVolume = 0.25;
            repository.Save(profile); Recreate();
            var id = service.StartSpin().Spin.SpinId; service.CompleteSpin(id);
            foreach (var state in new[] { repository.Load().Data, session.Snapshot })
            {
                Assert.That(state.JackpotWeekId, Is.EqualTo(now));
                Assert.That(state.JackpotWinsThisWeek, Is.EqualTo(1));
                Assert.That(state.DailyLastClaimUtc, Is.EqualTo(now));
                Assert.That(state.DailyStreakDay, Is.EqualTo(1));
                Assert.That(state.Settings.MusicVolume, Is.EqualTo(0.25));
            }
        }
        [Test]
        public void CoinOverflowDoesNotSavePartialSettlement()
        {
            var profile = repository.Load().Data; profile.Balance = long.MaxValue; repository.Save(profile); Recreate();
            var id = service.StartSpin().Spin.SpinId;
            int saves = repository.SaveAttemptCount;
            Assert.That(service.CompleteSpin(id).Error, Is.EqualTo(SpinError.ArithmeticOverflow));
            Consistent(long.MaxValue - 1000, 0, 0, true);
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(saves));
            Assert.That(session.State, Is.EqualTo(GameSessionState.Error));
        }
        [Test]
        public void FreeCounterOverflowDoesNotSavePartialSettlement()
        {
            Free(int.MaxValue); ChooseStops(s => s == SymbolId.SCATTER);
            var id = service.StartSpin().Spin.SpinId;
            int saves = repository.SaveAttemptCount;
            Assert.That(service.CompleteSpin(id).Error, Is.EqualTo(SpinError.ArithmeticOverflow));
            Consistent(20000, int.MaxValue - 1, 600, true);
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(saves));
        }
        [Test]
        public void OutcomeExceptionAfterDebitBlocksAndCanRecoverWithoutSecondDebit()
        {
            random.Throw = true;
            var result = service.StartSpin();
            Assert.That(result.Error, Is.EqualTo(SpinError.OutcomeGenerationFailure));
            Assert.That(result.Spin, Is.Null);
            Consistent(19000, 0, 0, true);
            Assert.That(session.State, Is.EqualTo(GameSessionState.Error));
            Assert.That(service.StartSpin().Error, Is.EqualTo(SpinError.Busy));
            Assert.That(service.RecoverInterruptedSpin().Success, Is.True);
            Consistent(19000, 0, 0, false);
        }
        [Test]
        public void ConcurrentAndReentrantStartsAreRejectedWhilePreparing()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            repository.BeforeSave = _ =>
            {
                Assert.That(session.State, Is.EqualTo(GameSessionState.Preparing));
                Assert.That(service.StartSpin().Error, Is.EqualTo(SpinError.Busy));
                entered.Set();
                if (!release.Wait(5000)) throw new TimeoutException();
            };
            var task = Task.Run(() => service.StartSpin());
            try
            {
                Assert.That(entered.Wait(5000), Is.True);
                Assert.That(service.StartSpin().Error, Is.EqualTo(SpinError.Busy));
            }
            finally { release.Set(); }
            Assert.That(task.Wait(5000), Is.True);
            Assert.That(task.Result.Success, Is.True, task.Result.Diagnostic);
            Consistent(19000, 0, 0, true);
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(1));
        }
        [TestCase(GameSessionState.Preparing)]
        [TestCase(GameSessionState.Spinning)]
        [TestCase(GameSessionState.Settling)]
        [TestCase(GameSessionState.Error)]
        public void NavigationRejectsEveryNonIdleState(GameSessionState target)
        {
            var navigation = new GameNavigationService(session);
            navigation.NavigateToSlot();
            session.Transition(GameSessionState.Preparing);
            if (target == GameSessionState.Spinning || target == GameSessionState.Settling)
                session.Transition(GameSessionState.Spinning);
            if (target == GameSessionState.Settling) session.Transition(target);
            if (target == GameSessionState.Error) session.Transition(target);
            Assert.That(navigation.TryNavigateToMainMenu(), Is.False);
            navigation.NavigateToMainMenu();
            Assert.That(navigation.CurrentScreen, Is.EqualTo(GameScreen.Slot));
            Assert.That(session.CanStartSpin || session.CanSelectBet || session.CanNavigateBack, Is.False);
        }
        [Test]
        public void BackIsAllowedBetweenFreeSpinsAndBlockedDuringRealSaves()
        {
            Free(2);
            var nav = new GameNavigationService(session); nav.NavigateToSlot();
            Assert.That(nav.TryNavigateToMainMenu(), Is.True);
            nav.NavigateToSlot();
            repository.BeforeSave = _ => Assert.That(nav.TryNavigateToMainMenu(), Is.False);
            var id = service.StartSpin().Spin.SpinId;
            Assert.That(nav.TryNavigateToMainMenu(), Is.False);
            service.CompleteSpin(id);
            Assert.That(nav.TryNavigateToMainMenu(), Is.True);
        }
        [Test]
        public void SessionRejectsInvalidTransitionsAndDetachedSnapshotMutations()
        {
            Assert.Throws<InvalidOperationException>(() => session.Transition(GameSessionState.Spinning));
            var copy = session.Snapshot; copy.Balance = 0;
            Assert.That(session.Snapshot.Balance, Is.EqualTo(20000));
            var id = service.StartSpin().Spin.SpinId;
            Assert.Throws<InvalidOperationException>(() => session.Transition(GameSessionState.Idle));
            Assert.That(session.State, Is.EqualTo(GameSessionState.Spinning));
            service.CompleteSpin(id);
            Assert.Throws<InvalidOperationException>(() => session.Transition(GameSessionState.Idle));
        }
        [Test]
        public void BalanceCalculationsNeverMutateInputAndRejectNegativePayouts()
        {
            var initial = session.Snapshot;
            var money = new BalanceService(config);
            Assert.That(money.CanAfford(initial, 1000), Is.True);
            var pending = new PendingSpin { SpinId = Guid.NewGuid(), EffectiveBet = 1000, Type = PendingSpinType.Paid };
            var reserved = money.Reserve(initial, pending);
            Assert.That(initial.Balance, Is.EqualTo(20000));
            Assert.That(initial.PendingSpin, Is.Null);
            Assert.Throws<ArgumentException>(() => money.Settle(reserved, pending.SpinId, -1, 0));
            Assert.Throws<ArgumentException>(() => money.Settle(reserved, pending.SpinId, 0, -1));
            var settled = money.Settle(reserved, pending.SpinId, 2500, 0);
            Assert.That(reserved.Balance, Is.EqualTo(19000));
            Assert.That(reserved.PendingSpin, Is.Not.Null);
            Assert.That(settled.Balance, Is.EqualTo(21500));
            initial.Balance = -1;
            Assert.That(money.CanAfford(initial, 1000), Is.False);
            Assert.Throws<ArgumentException>(() => money.Reserve(initial, pending));
        }
        [Test]
        public void LoadedInvalidStateAndMultipleOwnersAreRejected()
        {
            var bad = session.Snapshot; bad.Balance = -1;
            Assert.Throws<ArgumentException>(() => new SpinService(new GameSession(bad), config,
                new SlotEngine(config, random), new BalanceService(config), repository, () => now));
            Assert.Throws<InvalidOperationException>(() => new SpinService(session, config,
                new SlotEngine(config, random), new BalanceService(config), repository, () => now));
        }
        [Test]
        public void WeekIdentityIsCapturedAtAcceptanceAcrossUtcBoundary()
        {
            now = now.AddTicks(-1);
            var started = service.StartSpin().Spin;
            Assert.That(started.JackpotWeekId, Is.EqualTo(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero)));
            now = now.AddHours(1);
            service.CompleteSpin(started.SpinId);
            Assert.That(started.JackpotWeekId.Value.Day, Is.EqualTo(5));
            Assert.That(session.Snapshot.JackpotWinsThisWeek, Is.Zero);
            Assert.That(session.Snapshot.JackpotWeekId, Is.Null);
        }
    }
}
