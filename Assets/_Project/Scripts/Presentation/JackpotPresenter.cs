using System;
using System.Collections.Generic;
using IceP.Application;
using IceP.Application.Persistence;

namespace IceP.Presentation
{
    public sealed class JackpotPresenter : IDisposable
    {
        private readonly WeeklyJackpotService jackpot;
        private readonly IClock clock;
        private readonly IJackpotView view;
        private readonly SlotPresenter slot;
        private readonly GameNavigationService navigation;
        private readonly HashSet<Guid> celebrated = new HashSet<Guid>();
        private float elapsed;
        private bool disposed;
        public JackpotPresenter(WeeklyJackpotService jackpot, IClock clock, IJackpotView view,
            SlotPresenter slot, GameNavigationService navigation)
        {
            this.jackpot = jackpot; this.clock = clock; this.view = view; this.slot = slot; this.navigation = navigation;
            view.Activated += Refresh; view.Destroyed += Dispose;
            slot.SettlementConfirmed += OnSettlement;
            navigation.ScreenChanged += ScreenChanged;
            Refresh();
        }
        public void Refresh()
        {
            if (disposed) return;
            view.Render(new JackpotDisplay(jackpot.GetStatus(), clock.UtcNow));
            elapsed = 0;
        }
        public void Tick(float unscaledDeltaTime)
        {
            if (disposed || navigation.CurrentScreen != GameScreen.Slot) return;
            elapsed += unscaledDeltaTime;
            if (elapsed >= 1) Refresh();
        }
        private void ScreenChanged(GameScreen screen) { if (screen == GameScreen.Slot) Refresh(); }
        public void OnSettlement(SpinResult spin, SpinOperationResult result)
        {
            if (disposed || spin == null || result == null || !result.Success || result.SettledSpinId != spin.SpinId) return;
            Refresh();
            if (spin.Type == PendingSpinType.Paid && spin.Jackpot != null && spin.Jackpot.Participated &&
                spin.Jackpot.JackpotWon && celebrated.Add(spin.SpinId)) view.Celebrate();
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; view.Activated -= Refresh; view.Destroyed -= Dispose;
            slot.SettlementConfirmed -= OnSettlement; navigation.ScreenChanged -= ScreenChanged;
            view.CancelCelebration(); celebrated.Clear();
        }
    }
}
