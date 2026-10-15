using System;
using IceP.Application;

namespace IceP.Presentation
{
    // Only confirmed Application data belongs in the HUD. Potential payouts remain in SpinResult.
    public sealed class SlotDisplay
    {
        public long Balance { get; }
        public long EffectiveBet { get; }
        public long LastWin { get; }
        public int FreeSpins { get; }
        public bool CanSpin { get; }
        public bool CanSelectBet { get; }
        public bool CanBack { get; }
        public bool CanRetry { get; }
        public bool IsAutoSpinEnabled { get; }
        public bool CanAutoSpin { get; }
        public SlotDisplay(GameSession session, GameNavigationService navigation, bool retry, bool autoSpin = false)
        {
            var state = session.Snapshot;
            Balance = state.Balance; LastWin = state.LastWin; FreeSpins = state.FreeSpinsRemaining;
            EffectiveBet = state.PendingSpin?.EffectiveBet ?? (FreeSpins > 0 ? state.BonusBet : state.SelectedBet);
            CanSpin = session.CanStartSpin && (FreeSpins > 0 || Balance >= state.SelectedBet);
            IsAutoSpinEnabled = autoSpin;
            CanAutoSpin = autoSpin || (navigation.CurrentScreen == GameScreen.Slot && CanSpin);
            CanSpin &= !autoSpin;
            CanSelectBet = session.CanSelectBet && !autoSpin; CanBack = navigation.CanNavigateBack;
            CanRetry = retry && session.State == GameSessionState.Error;
        }
    }
}
