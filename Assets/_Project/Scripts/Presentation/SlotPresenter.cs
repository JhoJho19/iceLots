using System;
using System.Collections.Generic;
using IceP.Application;

namespace IceP.Presentation
{
    public sealed class SlotPresenter : IDisposable
    {
        private enum RetryOperation { None, Accept, Bet, Settlement }
        private readonly GameSession session;
        private readonly SpinService spins;
        private readonly GameNavigationService navigation;
        private readonly ISlotView view;
        private readonly IReadOnlyList<long> bets;
        private SpinResult active;
        private bool completionReceived, disposed;
        private RetryOperation retry;
        private readonly float autoSpinDelay;
        private float delayRemaining;
        private bool nextSpinPending;
        public bool IsAutoSpinEnabled { get; private set; }
        public SpinResult ActiveSpin => active;
        public event Action<SpinResult, SpinOperationResult> SettlementConfirmed;

        public SlotPresenter(GameSession session, SpinService spins, GameNavigationService navigation,
            IReadOnlyList<long> bets, ISlotView view, float autoSpinDelay = .4f)
        {
            this.session = session; this.spins = spins; this.navigation = navigation;
            this.bets = bets; this.view = view;
            if (float.IsNaN(autoSpinDelay) || float.IsInfinity(autoSpinDelay) || autoSpinDelay < 0)
                throw new ArgumentOutOfRangeException(nameof(autoSpinDelay));
            this.autoSpinDelay = autoSpinDelay;
            view.SpinRequested += Spin; view.BetStepRequested += StepBet;
            view.AutoRequested += ToggleAutoSpin; view.Suspended += StopAutoSpin;
            view.BackRequested += Back; view.RetryRequested += Retry;
            view.AnimationFinished += Finished; view.Destroyed += Dispose; navigation.ScreenChanged += ScreenChanged;
            Refresh();
        }
        public void Refresh() { if (!disposed) view.Render(new SlotDisplay(session, navigation, retry != RetryOperation.None, IsAutoSpinEnabled)); }
        private void ScreenChanged(GameScreen screen)
        { if (screen != GameScreen.Slot) StopAutoSpin(); Refresh(); }
        public void ToggleAutoSpin()
        {
            if (disposed) return;
            if (IsAutoSpinEnabled) { StopAutoSpin(); return; }
            // Activation during a manual Spin is rejected; never queue a competing request.
            if (!session.CanStartSpin || active != null || navigation.CurrentScreen != GameScreen.Slot) return;
            IsAutoSpinEnabled = true;
            StartSpin();
        }
        public void StopAutoSpin()
        {
            IsAutoSpinEnabled = false; nextSpinPending = false; delayRemaining = 0;
            Refresh(); // Does not touch an accepted transaction or its animation.
        }
        public void Tick(float unscaledDeltaTime)
        {
            if (disposed || !IsAutoSpinEnabled || !nextSpinPending) return;
            if (navigation.CurrentScreen != GameScreen.Slot || !session.CanStartSpin)
            { StopAutoSpin(); return; }
            delayRemaining -= Math.Max(0, unscaledDeltaTime);
            if (delayRemaining > 0) return;
            nextSpinPending = false;
            StartSpin();
        }
        private void Spin()
        {
            if (IsAutoSpinEnabled) return;
            StartSpin();
        }
        private void StartSpin()
        {
            if (disposed || !session.CanStartSpin || navigation.CurrentScreen != GameScreen.Slot) return;
            view.ClearWins(); view.ShowMessage("");
            var result = spins.StartSpin();
            if (!result.Success) { Fail(result, RetryOperation.Accept); return; }
            Accepted(result.Spin);
        }
        private void Accepted(SpinResult spin)
        {
            active = spin; completionReceived = false; retry = RetryOperation.None;
            Refresh(); // The saved debit/reservation is visible BEFORE animation begins.
            view.ShowMessage(spin.Type == IceP.Application.Persistence.PendingSpinType.Free ? "Free Spin" : "Spinning…");
            view.Animate(spin);
        }
        private void Finished(Guid id, bool success)
        {
            if (disposed || active == null || active.SpinId != id || completionReceived) return;
            completionReceived = true;
            if (!success) { StopAutoSpin(); view.ShowMessage("Spin animation interrupted. Winnings have not been confirmed."); Refresh(); return; }
            var result = spins.CompleteSpin(id);
            if (!result.Success) { Fail(result, RetryOperation.Settlement); return; }
            Confirm(result);
        }
        private void Confirm(SpinOperationResult result)
        {
            // Retain the original outcome on save failure; never redraw or animate during settlement retry.
            if (active != null && result.SettledSpinId == active.SpinId)
            {
                view.ShowConfirmedWin(active);
                SettlementConfirmed?.Invoke(active, result);
                int awarded = active.Evaluation.Scatter.AwardedFreeSpins;
                view.ShowMessage(awarded > 0 ? $"Win: {result.ConfirmedPayout:N0} · +{awarded} Free Spins!" : $"Win: {result.ConfirmedPayout:N0}");
            }
            active = null; retry = RetryOperation.None; Refresh();
            if (IsAutoSpinEnabled && !disposed && navigation.CurrentScreen == GameScreen.Slot)
            {
                var state = session.Snapshot;
                if (!session.CanStartSpin || (state.FreeSpinsRemaining == 0 && state.Balance < state.SelectedBet))
                { StopAutoSpin(); return; }
                delayRemaining = autoSpinDelay; nextSpinPending = true;
            }
        }
        private void StepBet(int direction)
        {
            if (disposed || IsAutoSpinEnabled || !session.CanSelectBet || navigation.CurrentScreen != GameScreen.Slot) return;
            long selected = session.Snapshot.SelectedBet; int index = 0;
            for (int i = 0; i < bets.Count; i++) if (bets[i] == selected) { index = i; break; }
            int next = Math.Max(0, Math.Min(bets.Count - 1, index + Math.Sign(direction)));
            if (next == index) return;
            var result = spins.SelectBet(bets[next]);
            if (!result.Success) Fail(result, RetryOperation.Bet);
            else { view.ShowMessage(""); Refresh(); }
        }
        private void Back()
        {
            if (disposed) return;
            if (!navigation.TryNavigateToMainMenu()) view.ShowMessage("Finish the Spin and confirm its save before returning.");
            Refresh();
        }
        private void Retry()
        {
            if (disposed || retry == RetryOperation.None || session.State != GameSessionState.Error) return;
            var operation = retry;
            var result = spins.RetryPersistence();
            if (!result.Success) { Fail(result, operation); return; }
            retry = RetryOperation.None;
            if (operation == RetryOperation.Accept && result.Spin != null) Accepted(result.Spin);
            else if (operation == RetryOperation.Settlement) Confirm(result);
            else { view.ShowMessage("Saved."); Refresh(); }
        }
        private void Fail(SpinOperationResult result, RetryOperation operation)
        {
            StopAutoSpin();
            retry = result.Error == SpinError.PersistenceFailure ? operation : RetryOperation.None;
            view.ShowMessage(result.Error == SpinError.PersistenceFailure
                ? "Save not confirmed. Gameplay is blocked. Retry the same save."
                : result.Error == SpinError.InsufficientBalance ? "Not enough coins. Choose a lower bet."
                : "Command rejected: " + result.Error);
            Refresh();
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            StopAutoSpin();
            view.AutoRequested -= ToggleAutoSpin; view.Suspended -= StopAutoSpin;
            view.SpinRequested -= Spin; view.BetStepRequested -= StepBet; view.BackRequested -= Back;
            view.RetryRequested -= Retry; view.AnimationFinished -= Finished;
            view.Destroyed -= Dispose;
            navigation.ScreenChanged -= ScreenChanged; view.CancelAnimation();
        }
    }
}
