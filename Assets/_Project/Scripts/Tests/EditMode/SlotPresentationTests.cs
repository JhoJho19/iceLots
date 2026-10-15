using System;
using IceP.Application;
using IceP.Application.Persistence;
using IceP.Domain.Configuration;
using IceP.Domain.SlotEngine;
using IceP.Infrastructure.Configuration;
using IceP.Presentation;
using NUnit.Framework;
using UnityEditor;

namespace IceP.Tests
{
    public sealed class SlotPresentationTests
    {
        private SlotRuntimeConfig config;
        private FakeSaveRepository repository;
        private GameSession session;
        private SpinService spins;
        private GameNavigationService navigation;
        private SlotPresenter presenter;
        private View view;
        private Random random;
        private sealed class Random : IRandomSource
        {
            public int Calls;
            public int[] Positions;
            public int NextInt(int min, int max) { int index = Calls++ % 5; return Positions == null ? max - 1 : Positions[index]; }
        }
        private sealed class View : ISlotView
        {
            public event Action AutoRequested;
            public event Action Suspended;
            public void Auto() => AutoRequested?.Invoke();
            public void Suspend() => Suspended?.Invoke();
            public event Action SpinRequested;
            public event Action<int> BetStepRequested;
            public event Action BackRequested;
            public event Action RetryRequested;
            public event Action<Guid, bool> AnimationFinished;
            public event Action Destroyed;
            public SlotDisplay Display;
            public SpinResult Animated, Confirmed;
            public int Animations, ClearCount;
            public string Message;
            public Action BeforeAnimation;
            public void Spin() => SpinRequested?.Invoke();
            public void Bet(int direction) => BetStepRequested?.Invoke(direction);
            public void Back() => BackRequested?.Invoke();
            public void Retry() => RetryRequested?.Invoke();
            public void Destroy() => Destroyed?.Invoke();
            public void Finish(Guid id, bool success = true) => AnimationFinished?.Invoke(id, success);
            public void Render(SlotDisplay display) => Display = display;
            public void ShowMessage(string message) => Message = message;
            public void ClearWins() { ClearCount++; Confirmed = null; }
            public void Animate(SpinResult spin) { BeforeAnimation?.Invoke(); Animated = spin; Animations++; }
            public void ShowConfirmedWin(SpinResult spin) => Confirmed = spin;
            public void CancelAnimation() { }
        }
        [SetUp] public void Setup()
        {
            config = new LocalSlotConfigProvider(AssetDatabase.LoadAssetAtPath<SlotGameConfigSO>("Assets/_Project/Configs/SlotGameConfigSO.asset")).Load();
            Recreate(InitialProfileFactory.Create(config));
        }
        private void Recreate(SaveData data)
        {
            presenter?.Dispose(); repository = new FakeSaveRepository(data); session = new GameSession(data); random = new Random();
            spins = new SpinService(session, config, new SlotEngine(config, random), new BalanceService(config), repository,
                () => new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
            navigation = new GameNavigationService(session); navigation.NavigateToSlot(); view = new View();
            presenter = new SlotPresenter(session, spins, navigation, config.Economy.AvailableBets, view);
        }
        [TearDown] public void Cleanup() => presenter.Dispose();
        private void ChooseWinningStops()
        {
            random.Positions = new int[5];
            for (int reel = 0; reel < 5; reel++)
                for (int stop = 0; stop < 30; stop++)
                    if (config.ReelStrips[reel].Symbols[(stop + 1) % 30] == SymbolId.S1) { random.Positions[reel] = stop; break; }
        }
        [Test] public void AutoStartsDisabledAndFirstClickAcceptsExactlyOneSpin()
        {
            Assert.That(presenter.IsAutoSpinEnabled, Is.False);
            view.Auto(); Assert.That(presenter.IsAutoSpinEnabled, Is.True);
            Assert.That(view.Animations, Is.EqualTo(1)); Assert.That(repository.SaveAttemptCount, Is.EqualTo(1));
            Assert.That(view.Display.CanAutoSpin, Is.True);
            Assert.That(view.Display.CanSpin || view.Display.CanSelectBet, Is.False);
        }
        [Test] public void AutoDeterministicHandoffWalkthrough20000Bet1000Win2500StopSecondSpin()
        {
            var data = session.Snapshot; data.SelectedBet = 1000; Recreate(data);
            random.Positions = new[] { 18, 2, 1, 20, 9 };
            view.Auto(); var first = view.Animated;
            Assert.That(session.Snapshot.Balance, Is.EqualTo(19000)); Assert.That(first.PotentialPayout, Is.EqualTo(2500));
            Assert.That(repository.Load().Data.PendingSpin.SpinId, Is.EqualTo(first.SpinId));
            view.Finish(first.SpinId); Assert.That(session.Snapshot.Balance, Is.EqualTo(21500));
            Assert.That(repository.Load().Data.PendingSpin, Is.Null);
            presenter.Tick(.39f); Assert.That(view.Animations, Is.EqualTo(1));
            presenter.Tick(.011f); var second = view.Animated;
            Assert.That(session.Snapshot.Balance, Is.EqualTo(20500)); Assert.That(second.PotentialPayout, Is.EqualTo(2500));
            view.Auto(); Assert.That(presenter.ActiveSpin, Is.SameAs(second)); view.Finish(second.SpinId); presenter.Tick(100);
            Assert.That(session.Snapshot.Balance, Is.EqualTo(23000)); Assert.That(repository.Load().Data.Balance, Is.EqualTo(23000));
            Assert.That(view.Animations, Is.EqualTo(2)); Assert.That(repository.SaveAttemptCount, Is.EqualTo(4));
            Assert.That(view.Display.CanSpin && view.Display.CanSelectBet, Is.True);
        }
        [Test] public void AutoSecondClickStopsWithoutCancellingAcceptedSpin()
        {
            view.Auto(); var spin = presenter.ActiveSpin; view.Auto();
            Assert.That(presenter.IsAutoSpinEnabled, Is.False);
            Assert.That(session.State, Is.EqualTo(GameSessionState.Spinning));
            Assert.That(presenter.ActiveSpin, Is.SameAs(spin));
            view.Finish(spin.SpinId); presenter.Tick(100);
            Assert.That(session.Snapshot.LastSettledSpinId, Is.EqualTo(spin.SpinId));
            Assert.That(view.Animations, Is.EqualTo(1)); Assert.That(view.Display.CanSpin, Is.True);
        }
        [Test] public void AutoRapidClicksAndManualCommandsCannotCreateConcurrentSpins()
        {
            view.Auto();
            for (int i = 0; i < 20; i++) { view.Auto(); view.Spin(); view.Bet(1); }
            Assert.That(view.Animations, Is.EqualTo(1)); Assert.That(repository.SaveAttemptCount, Is.EqualTo(1));
            Assert.That(session.Snapshot.SelectedBet, Is.EqualTo(200));
        }
        [Test] public void AutoActivationDuringManualSpinIsRejected()
        {
            view.Spin(); view.Auto(); presenter.Tick(100); view.Finish(view.Animated.SpinId); presenter.Tick(100);
            Assert.That(presenter.IsAutoSpinEnabled, Is.False); Assert.That(view.Animations, Is.EqualTo(1));
        }
        [Test] public void AutoWaitsForConfirmedSettlementThenFullDelayAndIgnoresDuplicateCompletion()
        {
            view.Auto(); var id = view.Animated.SpinId; presenter.Tick(100);
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(1)); Assert.That(view.Animations, Is.EqualTo(1));
            view.Finish(id); view.Finish(id); view.Finish(Guid.NewGuid());
            Assert.That(view.Confirmed.SpinId, Is.EqualTo(id)); Assert.That(repository.SaveAttemptCount, Is.EqualTo(2));
            presenter.Tick(.2f); Assert.That(view.Animations, Is.EqualTo(1));
            presenter.Tick(.19f); Assert.That(view.Animations, Is.EqualTo(1));
            presenter.Tick(.011f); Assert.That(view.Animations, Is.EqualTo(2));
            presenter.Tick(100); view.Finish(id); Assert.That(view.Animations, Is.EqualTo(2));
        }
        [Test] public void AutoStopCancelsDelayAndLocksBetAndManualSpinDuringDelay()
        {
            view.Auto(); view.Finish(view.Animated.SpinId); view.Spin(); view.Bet(1);
            Assert.That(view.Animations, Is.EqualTo(1)); Assert.That(session.Snapshot.SelectedBet, Is.EqualTo(200));
            view.Auto(); presenter.Tick(100); Assert.That(view.Animations, Is.EqualTo(1));
            Assert.That(view.Display.CanSelectBet, Is.True);
        }
        [Test] public void AutoInsufficientBalanceDoesNotSaveAnimateOrRetry()
        {
            var data = session.Snapshot; data.Balance = 0; Recreate(data);
            view.Auto(); presenter.Tick(100); Assert.That(presenter.IsAutoSpinEnabled, Is.False);
            Assert.That(repository.SaveAttemptCount, Is.Zero); Assert.That(view.Animations, Is.Zero);
            Assert.That(view.Display.CanAutoSpin, Is.False); Assert.That(view.Display.CanBack, Is.True);
        }
        [Test] public void AutoStopsImmediatelyAfterLastAffordableLosingSpin()
        {
            var data = session.Snapshot; data.Balance = 200; Recreate(data); ChooseLosingStops();
            view.Auto(); Assert.That(view.Animated.PotentialPayout, Is.Zero); view.Finish(view.Animated.SpinId);
            Assert.That(presenter.IsAutoSpinEnabled, Is.False); presenter.Tick(100);
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(2)); Assert.That(view.Animations, Is.EqualTo(1));
        }
        [Test] public void AutoAcceptFailureStopsAndRetryOnlyAnimatesOriginalSpin()
        {
            repository.FailSaves = true; view.Auto(); presenter.Tick(100);
            Assert.That(presenter.IsAutoSpinEnabled, Is.False); Assert.That(view.Animations, Is.Zero);
            Assert.That(session.State, Is.EqualTo(GameSessionState.Error));
            repository.FailSaves = false; view.Retry(); view.Finish(view.Animated.SpinId); presenter.Tick(100);
            Assert.That(view.Animations, Is.EqualTo(1)); Assert.That(presenter.IsAutoSpinEnabled, Is.False);
        }
        [Test] public void AutoSettlementFailureRetainsOutcomeAndRetryNeverRestartsAuto()
        {
            ChooseWinningStops(); view.Auto(); var spin = view.Animated; repository.FailSaves = true;
            view.Finish(spin.SpinId); presenter.Tick(100);
            Assert.That(presenter.IsAutoSpinEnabled, Is.False); Assert.That(session.State, Is.EqualTo(GameSessionState.Error));
            Assert.That(presenter.ActiveSpin, Is.SameAs(spin)); Assert.That(view.Confirmed, Is.Null);
            repository.FailSaves = false; view.Retry(); presenter.Tick(100);
            Assert.That(view.Confirmed, Is.SameAs(spin)); Assert.That(view.Animations, Is.EqualTo(1));
            Assert.That(random.Calls, Is.EqualTo(5));
        }
        [Test] public void AutoFreeSpinsAtZeroBalanceUseBonusBetMultiplierAndRetrigger()
        {
            var data = session.Snapshot; data.Balance = 0; data.FreeSpinsRemaining = 2; data.BonusBet = 600; Recreate(data);
            random.Positions = new int[5];
            for (int r = 0; r < 5; r++) for (int s = 0; s < 30; s++)
                if (config.ReelStrips[r].Symbols[s] == SymbolId.SCATTER) { random.Positions[r] = s; break; }
            view.Auto(); var first = view.Animated;
            Assert.That(first.Type, Is.EqualTo(PendingSpinType.Free)); Assert.That(first.EffectiveBet, Is.EqualTo(600));
            Assert.That(first.Evaluation.Context.IsFreeSpin, Is.True);
            Assert.That(session.Snapshot.Balance, Is.Zero);
            view.Finish(first.SpinId); Assert.That(session.Snapshot.FreeSpinsRemaining, Is.EqualTo(21));
            presenter.Tick(.4f); Assert.That(view.Animations, Is.EqualTo(2));
            Assert.That(view.Animated.EffectiveBet, Is.EqualTo(600)); Assert.That(session.Snapshot.FreeSpinsRemaining, Is.EqualTo(20));
            view.Auto(); view.Finish(view.Animated.SpinId); presenter.Tick(100);
            Assert.That(session.Snapshot.FreeSpinsRemaining, Is.EqualTo(40)); Assert.That(view.Animations, Is.EqualTo(2));
        }
        [Test] public void AutoExhaustsFreeQueueThenReturnsToSelectedPaidBet()
        {
            var data = session.Snapshot; data.FreeSpinsRemaining = 1; data.BonusBet = 600; Recreate(data); ChooseLosingStops();
            view.Auto(); Assert.That(view.Animated.EffectiveBet, Is.EqualTo(600)); view.Finish(view.Animated.SpinId);
            presenter.Tick(.4f); Assert.That(view.Animated.Type, Is.EqualTo(PendingSpinType.Paid));
            Assert.That(view.Animated.EffectiveBet, Is.EqualTo(200)); Assert.That(session.Snapshot.Balance, Is.EqualTo(19800));
        }
        [Test] public void AutoBackIsBlockedDuringSpinAndStopsBeforeMenuDuringDelay()
        {
            view.Auto(); view.Back(); Assert.That(navigation.CurrentScreen, Is.EqualTo(GameScreen.Slot));
            Assert.That(presenter.IsAutoSpinEnabled, Is.True); view.Finish(view.Animated.SpinId);
            Assert.That(navigation.TryNavigateToMainMenu(), Is.True); presenter.Tick(100);
            Assert.That(presenter.IsAutoSpinEnabled, Is.False); Assert.That(view.Animations, Is.EqualTo(1));
            navigation.NavigateToSlot(); presenter.Tick(100); Assert.That(presenter.IsAutoSpinEnabled, Is.False);
        }
        [TestCase(false)] [TestCase(true)] public void AutoSuspendedOrDestroyedViewCancelsPendingDelay(bool destroy)
        {
            view.Auto(); view.Finish(view.Animated.SpinId);
            if (destroy) view.Destroy(); else view.Suspend();
            presenter.Tick(100); Assert.That(presenter.IsAutoSpinEnabled, Is.False); Assert.That(view.Animations, Is.EqualTo(1));
        }
        [Test] public void AutoCannotBypassExternalApplicationSpinOrErrorGuard()
        {
            var result = spins.StartSpin(); view.Auto(); Assert.That(presenter.IsAutoSpinEnabled, Is.False);
            spins.CompleteSpin(result.Spin.SpinId); repository.FailSaves = true; spins.StartSpin(); view.Auto();
            Assert.That(presenter.IsAutoSpinEnabled, Is.False); Assert.That(view.Animations, Is.Zero);
        }
        [Test] public void AutoInvalidBetIsRejectedWithoutSavingOrGenerating()
        {
            var invalid = session.Snapshot; invalid.SelectedBet = 123; session.Commit(invalid);
            view.Auto(); presenter.Tick(100); Assert.That(presenter.IsAutoSpinEnabled, Is.False);
            Assert.That(view.Message, Does.Contain("InvalidBet"));
            Assert.That(repository.SaveAttemptCount, Is.Zero); Assert.That(random.Calls, Is.Zero);
        }
        [Test] public void AutoConfiguredDelayIsRespectedAndNegativeTimeDoesNotAdvanceIt()
        {
            presenter.Dispose(); presenter = new SlotPresenter(session, spins, navigation, config.Economy.AvailableBets, view, 2);
            view.Auto(); view.Finish(view.Animated.SpinId); presenter.Tick(-20); presenter.Tick(1.9f);
            Assert.That(view.Animations, Is.EqualTo(1)); presenter.Tick(.11f); Assert.That(view.Animations, Is.EqualTo(2));
        }
        [Test] public void AutoStopAndRestartCannotReuseOldDelayOrCompletion()
        {
            view.Auto(); var old = view.Animated.SpinId; view.Finish(old); presenter.Tick(.2f); view.Auto();
            view.Auto(); var current = view.Animated.SpinId; view.Finish(old); presenter.Tick(100);
            Assert.That(presenter.ActiveSpin.SpinId, Is.EqualTo(current)); Assert.That(view.Animations, Is.EqualTo(2));
        }
        [Test] public void AutoFreeSpinKeepsOriginalConfiguredMultiplier()
        {
            var data = session.Snapshot; data.FreeSpinsRemaining = 2; data.BonusBet = 800; Recreate(data); ChooseWinningStops();
            view.Auto(); var spin = view.Animated;
            Assert.That(spin.Evaluation.AppliedPayoutMultiplier, Is.EqualTo(config.Scatter.FreeSpinsMultiplier));
            Assert.That(spin.Evaluation.FinalPayout, Is.EqualTo(spin.Evaluation.Wins.BaseWin * config.Scatter.FreeSpinsMultiplier));
            view.Finish(spin.SpinId); presenter.Tick(.4f); Assert.That(view.Animated.EffectiveBet, Is.EqualTo(800));
        }
        [Test] public void AutoCancelledAnimationStopsRepetitionWithoutSettlement()
        {
            view.Auto(); var id = view.Animated.SpinId; view.Finish(id, false); view.Finish(id); presenter.Tick(100);
            Assert.That(presenter.IsAutoSpinEnabled, Is.False); Assert.That(repository.SaveAttemptCount, Is.EqualTo(1));
            Assert.That(session.Snapshot.PendingSpin.SpinId, Is.EqualTo(id));
        }
        [Test] public void AutoStateIsPresentationOnlyAndFreshPresenterStartsDisabled()
        {
            view.Auto(); var data = repository.Load().Data;
            Assert.That(typeof(SaveData).GetField("IsAutoSpinEnabled"), Is.Null);
            view.Auto(); view.Finish(view.Animated.SpinId); Recreate(session.Snapshot);
            Assert.That(presenter.IsAutoSpinEnabled, Is.False); presenter.Tick(100); Assert.That(view.Animations, Is.Zero);
        }
        private void ChooseLosingStops()
        {
            random.Positions = new int[5];
            var engine = new SlotEngine(config, random);
            for (int i = 0; i < 30; i++)
            {
                for (int r = 0; r < 5; r++) random.Positions[r] = (i + r * 5) % 30;
                var result = engine.Generate(SpinEvaluationContext.Paid(200));
                if (result.FinalPayout == 0 && result.Scatter.AwardedFreeSpins == 0) { random.Calls = 0; return; }
            }
            Assert.Fail("No deterministic losing fixture found.");
        }
        [Test] public void RapidSpinIntentAcceptsAndAnimatesExactlyOnce()
        {
            view.Spin(); var id = view.Animated.SpinId;
            for (int i = 0; i < 20; i++) view.Spin();
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(1)); Assert.That(random.Calls, Is.EqualTo(5));
            Assert.That(view.Animations, Is.EqualTo(1)); Assert.That(presenter.ActiveSpin.SpinId, Is.EqualTo(id));
        }
        [Test] public void RejectedSpinDoesNotAnimateOrGenerateOutcome()
        {
            var profile = session.Snapshot; profile.Balance = 0; Recreate(profile); view.Spin();
            Assert.That(view.Animations, Is.Zero); Assert.That(random.Calls, Is.Zero); Assert.That(repository.SaveAttemptCount, Is.Zero);
            Assert.That(view.Message, Does.Contain("Not enough")); Assert.That(view.Display.CanSpin, Is.False);
            Assert.That(view.Display.CanBack && view.Display.CanSelectBet, Is.True);
        }
        [Test] public void AcceptedSpinCarriesOriginalIdOutcomeAndSavedDebit()
        {
            view.BeforeAnimation = () => {
                var saved = repository.Load().Data;
                Assert.That(saved.Balance, Is.EqualTo(19800)); Assert.That(saved.PendingSpin, Is.Not.Null);
                Assert.That(view.Display.Balance, Is.EqualTo(19800)); Assert.That(view.Display.CanSpin || view.Display.CanBack, Is.False);
            };
            view.Spin(); Assert.That(view.Animated, Is.SameAs(presenter.ActiveSpin));
            Assert.That(view.Animated.SpinId, Is.EqualTo(session.Snapshot.PendingSpin.SpinId));
            for (int reel = 0; reel < 5; reel++) {
                Assert.That(view.Animated.Evaluation.Outcome.Stops[reel].Position, Is.EqualTo(29));
                for (int row = 0; row < 3; row++) Assert.That(view.Animated.Evaluation.Outcome.Grid[reel, row], Is.EqualTo(config.ReelStrips[reel].Symbols[(29 + row) % 30]));
            }
        }
        [Test] public void AllFiveDistinctReelsAreRequiredBeforeSettlement()
        {
            view.Spin(); var id = view.Animated.SpinId; var barrier = new ReelCompletionBarrier(); barrier.Begin(id);
            for (int reel = 0; reel < 4; reel++) Assert.That(barrier.Report(id, reel), Is.False);
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(1)); Assert.That(session.State, Is.EqualTo(GameSessionState.Spinning));
            Assert.That(barrier.Report(id, 4), Is.True); view.Finish(id);
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(2)); Assert.That(session.State, Is.EqualTo(GameSessionState.Idle));
        }
        [Test] public void DuplicateCompletionCannotSettleTwice()
        {
            view.Spin(); var id = view.Animated.SpinId; view.Finish(id); var saved = session.Snapshot;
            view.Finish(id); view.Finish(id);
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(2)); Assert.That(session.Snapshot.Balance, Is.EqualTo(saved.Balance));
        }
        [Test] public void StaleCompletionCannotSettleNewerSpin()
        {
            view.Spin(); var old = view.Animated.SpinId; view.Finish(old); view.Spin(); var current = view.Animated.SpinId;
            view.Finish(old); Assert.That(session.Snapshot.PendingSpin.SpinId, Is.EqualTo(current));
            Assert.That(session.State, Is.EqualTo(GameSessionState.Spinning)); Assert.That(repository.SaveAttemptCount, Is.EqualTo(3));
        }
        [Test] public void CancelledAnimationNeverSettlesEvenAfterDelayedSuccess()
        {
            view.Spin(); var id = view.Animated.SpinId; view.Finish(id, false); view.Finish(id);
            Assert.That(session.State, Is.EqualTo(GameSessionState.Spinning)); Assert.That(repository.SaveAttemptCount, Is.EqualTo(1));
            Assert.That(view.Confirmed, Is.Null); Assert.That(view.Display.CanSpin || view.Display.CanBack, Is.False);
        }
        [Test] public void DisposedPresenterIgnoresDelayedCallbacksAndCommands()
        {
            view.Spin(); var id = view.Animated.SpinId; presenter.Dispose(); view.Finish(id); view.Retry(); view.Spin(); view.Back();
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(1)); Assert.That(navigation.CurrentScreen, Is.EqualTo(GameScreen.Slot));
        }
        [Test] public void DestroyedViewDetachesPresenterBeforeDelayedCompletion()
        {
            view.Spin(); var id = view.Animated.SpinId; view.Destroy(); view.Finish(id); view.Spin();
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(1)); Assert.That(session.State, Is.EqualTo(GameSessionState.Spinning));
        }
        [Test] public void PotentialPayoutIsNotDisplayedAsConfirmed()
        {
            ChooseWinningStops();
            view.Spin(); var spin = view.Animated;
            Assert.That(spin.PotentialPayout, Is.GreaterThan(0));
            Assert.That(view.Display.LastWin, Is.Zero); Assert.That(view.Confirmed, Is.Null);
            view.Finish(spin.SpinId); Assert.That(view.Confirmed, Is.SameAs(spin));
            Assert.That(view.Display.LastWin, Is.EqualTo(spin.PotentialPayout));
            Assert.That(view.Display.Balance, Is.EqualTo(19800 + spin.PotentialPayout));
        }
        [Test] public void SettlementFailureBlocksAndRetainsOriginalIdAndOutcome()
        {
            ChooseWinningStops();
            view.Spin(); var spin = view.Animated; repository.FailSaves = true; view.Finish(spin.SpinId);
            Assert.That(spin.PotentialPayout, Is.GreaterThan(0));
            Assert.That(session.State, Is.EqualTo(GameSessionState.Error)); Assert.That(presenter.ActiveSpin, Is.SameAs(spin));
            Assert.That(view.Confirmed, Is.Null); Assert.That(view.Display.LastWin, Is.Zero); Assert.That(view.Display.Balance, Is.EqualTo(19800));
            Assert.That(view.Display.CanSpin || view.Display.CanBack || view.Display.CanSelectBet, Is.False);
            Assert.That(view.Display.CanRetry, Is.True); Assert.That(view.Message, Does.Contain("not confirmed"));
            view.Back(); Assert.That(navigation.CurrentScreen, Is.EqualTo(GameScreen.Slot));
        }
        [Test] public void SettlementRetryDoesNotRepeatStartAnimationOrRng()
        {
            ChooseWinningStops();
            view.Spin(); var spin = view.Animated; repository.FailSaves = true; view.Finish(spin.SpinId);
            Assert.That(spin.PotentialPayout, Is.GreaterThan(0));
            view.Retry(); Assert.That(session.State, Is.EqualTo(GameSessionState.Error)); repository.FailSaves = false; view.Retry();
            Assert.That(view.Animations, Is.EqualTo(1)); Assert.That(random.Calls, Is.EqualTo(5));
            Assert.That(session.Snapshot.Balance, Is.EqualTo(19800 + spin.PotentialPayout));
            Assert.That(view.Confirmed, Is.SameAs(spin)); Assert.That(view.Display.CanRetry, Is.False);
            var attempts = repository.SaveAttemptCount; view.Retry(); view.Finish(spin.SpinId);
            Assert.That(repository.SaveAttemptCount, Is.EqualTo(attempts));
        }
        [Test] public void AmbiguousSettlementCommitRetriesExactProposalWithoutDoubleCredit()
        {
            ChooseWinningStops();
            view.Spin(); var spin = view.Animated; repository.CommitThenFail = true; view.Finish(spin.SpinId);
            Assert.That(spin.PotentialPayout, Is.GreaterThan(0));
            var proposed = repository.Load().Data; Assert.That(session.Snapshot.Balance, Is.EqualTo(19800));
            repository.CommitThenFail = false; view.Retry();
            Assert.That(session.Snapshot.Balance, Is.EqualTo(proposed.Balance)); Assert.That(session.Snapshot.LastSettledSpinId, Is.EqualTo(spin.SpinId));
        }
        [Test] public void FailedDebitRetryFinishesOriginalAcceptanceOnlyOnce()
        {
            repository.CommitThenFail = true; view.Spin(); var id = repository.Load().Data.PendingSpin.SpinId;
            Assert.That(view.Animations, Is.Zero); Assert.That(random.Calls, Is.Zero);
            Assert.That(view.Display.Balance, Is.EqualTo(20000)); repository.CommitThenFail = false; view.Retry();
            Assert.That(view.Animated.SpinId, Is.EqualTo(id)); Assert.That(view.Display.Balance, Is.EqualTo(19800));
            Assert.That(random.Calls, Is.EqualTo(5)); Assert.That(view.Animations, Is.EqualTo(1));
        }
        [Test] public void BetControlsUseApplicationSaveAndRespectLimits()
        {
            view.Bet(1); Assert.That(session.Snapshot.SelectedBet, Is.EqualTo(400)); Assert.That(repository.Load().Data.SelectedBet, Is.EqualTo(400));
            for (int i = 0; i < 10; i++) view.Bet(1);
            Assert.That(session.Snapshot.SelectedBet, Is.EqualTo(1000)); Assert.That(repository.SaveAttemptCount, Is.EqualTo(4));
            view.Spin(); view.Bet(-1); Assert.That(session.Snapshot.SelectedBet, Is.EqualTo(1000));
        }
        [Test] public void BetPersistenceRetryCannotAccidentallyStartPaidSpin()
        {
            repository.FailSaves = true; view.Bet(1); Assert.That(view.Display.EffectiveBet, Is.EqualTo(200));
            repository.FailSaves = false; view.Retry();
            Assert.That(session.Snapshot.SelectedBet, Is.EqualTo(400)); Assert.That(random.Calls, Is.Zero); Assert.That(view.Animations, Is.Zero);
        }
        [Test] public void FreeSpinUsesBonusBetAndRequiresExplicitNextClick()
        {
            var profile = session.Snapshot; profile.FreeSpinsRemaining = 3; profile.BonusBet = 600; Recreate(profile);
            view.Bet(1); Assert.That(session.Snapshot.SelectedBet, Is.EqualTo(400)); Assert.That(view.Display.EffectiveBet, Is.EqualTo(600));
            view.Spin(); Assert.That(view.Animated.EffectiveBet, Is.EqualTo(600)); Assert.That(view.Display.Balance, Is.EqualTo(20000));
            Assert.That(view.Display.FreeSpins, Is.EqualTo(2)); var spin = view.Animated; view.Finish(spin.SpinId);
            Assert.That(view.Animations, Is.EqualTo(1)); Assert.That(view.Display.FreeSpins, Is.EqualTo(2 + spin.Evaluation.Scatter.AwardedFreeSpins));
            view.Back(); Assert.That(navigation.CurrentScreen, Is.EqualTo(GameScreen.MainMenu));
        }
        [Test] public void FreeSpinAvailableWithNoPaidBalance()
        {
            var profile = session.Snapshot; profile.Balance = 0; profile.FreeSpinsRemaining = 1; profile.BonusBet = 600; Recreate(profile);
            Assert.That(view.Display.CanSpin, Is.True); view.Spin(); Assert.That(view.Display.Balance, Is.Zero);
        }
        [Test] public void PreparingAndSettlingControlsReadActualApplicationCapabilities()
        {
            repository.BeforeSave = data => {
                presenter.Refresh(); Assert.That(view.Display.CanSpin || view.Display.CanBack || view.Display.CanSelectBet, Is.False);
            };
            view.Spin(); Assert.That(view.Display.CanSpin || view.Display.CanBack || view.Display.CanSelectBet, Is.False);
            view.Finish(view.Animated.SpinId); Assert.That(view.Display.CanBack && view.Display.CanSelectBet, Is.True);
        }
        [Test] public void ExternalConfirmedApplicationChangesRefreshHud()
        {
            var daily = new DailyBonusService(session, config, new BalanceService(config), repository,
                new FakeClock(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero)));
            Assert.That(daily.Claim().Success, Is.True); presenter.Refresh(); Assert.That(view.Display.Balance, Is.EqualTo(20400));
        }
        [Test] public void CompletionBarrierRejectsDuplicateStaleAndCancelledReels()
        {
            var barrier = new ReelCompletionBarrier(); var old = Guid.NewGuid(); var id = Guid.NewGuid();
            barrier.Begin(old); barrier.Report(old, 0); barrier.Begin(id);
            Assert.That(barrier.Report(old, 1), Is.False); Assert.That(barrier.Report(id, -1), Is.False);
            for (int i = 0; i < 4; i++) { Assert.That(barrier.Report(id, i), Is.False); Assert.That(barrier.Report(id, i), Is.False); }
            Assert.That(barrier.Report(id, 4), Is.True); Assert.That(barrier.Report(id, 4), Is.False);
            barrier.Begin(old); barrier.Cancel(); for (int i = 0; i < 5; i++) Assert.That(barrier.Report(old, i), Is.False);
        }
    }
}
