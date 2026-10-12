using System;
using IceP.Application.Persistence;

namespace IceP.Application
{
    public enum GameSessionState { Idle, Preparing, Spinning, Settling, Error }

    // All economic services use this gate and the same authoritative snapshot.
    public sealed class GameSession
    {
        internal readonly object Gate = new object();
        internal bool HasOwner;
        internal bool HasDailyBonusOwner;
        internal bool HasJackpotOwner;
        internal bool CommandExecuting;
        private SaveData snapshot;
        private GameSessionState state;
        public GameSession(SaveData initial)
        {
            snapshot = (initial ?? throw new ArgumentNullException(nameof(initial))).Copy();
            state = snapshot.PendingSpin == null ? GameSessionState.Idle : GameSessionState.Error;
        }
        public GameSessionState State { get { lock (Gate) return state; } }
        public SaveData Snapshot { get { lock (Gate) return snapshot.Copy(); } }
        public bool CanStartSpin { get { lock (Gate) return state == GameSessionState.Idle && snapshot.PendingSpin == null; } }
        public bool CanNavigateBack => CanStartSpin;
        public bool CanSelectBet => CanStartSpin;
        internal void Commit(SaveData value) { snapshot = value.Copy(); }
        internal void Transition(GameSessionState next)
        {
            bool valid = next == GameSessionState.Error ||
                (state == GameSessionState.Idle && next == GameSessionState.Preparing) ||
                (state == GameSessionState.Preparing && (next == GameSessionState.Spinning || next == GameSessionState.Idle)) ||
                (state == GameSessionState.Spinning && next == GameSessionState.Settling) ||
                (state == GameSessionState.Settling && next == GameSessionState.Idle) ||
                (state == GameSessionState.Error && (next == GameSessionState.Preparing || next == GameSessionState.Settling));
            if (!valid || next == state) throw new InvalidOperationException($"Invalid session transition {state} -> {next}.");
            if (next == GameSessionState.Idle && snapshot.PendingSpin != null)
                throw new InvalidOperationException("Unresolved PendingSpin cannot enter Idle.");
            state = next;
        }
    }
}
