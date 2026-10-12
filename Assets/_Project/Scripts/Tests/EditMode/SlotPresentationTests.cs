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
