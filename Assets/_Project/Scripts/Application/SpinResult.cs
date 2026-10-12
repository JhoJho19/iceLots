using System;
using IceP.Application.Persistence;
using IceP.Domain.Configuration;
using IceP.Domain.SlotEngine;

namespace IceP.Application
{
    public enum SpinError
    {
        None, Busy, InvalidBet, InsufficientBalance, NoActiveSpin, WrongSpinId,
        PersistenceFailure, ArithmeticOverflow, OutcomeGenerationFailure, InvalidState
    }
    public sealed class SpinResult
    {
        public Guid SpinId { get; }
        public PendingSpinType Type { get; }
        public long EffectiveBet { get; }
        public DateTimeOffset? JackpotWeekId { get; }
        public SlotRuntimeConfig Configuration { get; }
        public EvaluatedSpinOutcome Evaluation { get; }
        public JackpotSpinResult Jackpot { get; }
        public long PotentialPayout => checked(Evaluation.FinalPayout + (Jackpot?.JackpotAmount ?? 0));
        internal SpinResult(PendingSpin pending, SlotRuntimeConfig config, EvaluatedSpinOutcome evaluation,
            JackpotSpinResult jackpot = null)
        {
            SpinId = pending.SpinId; Type = pending.Type; EffectiveBet = pending.EffectiveBet;
            JackpotWeekId = pending.JackpotWeekId; Configuration = config; Evaluation = evaluation;
            Jackpot = jackpot;
        }
    }
    public sealed class SpinOperationResult
    {
        public bool Success => Error == SpinError.None;
        public SpinError Error { get; }
        public string Diagnostic { get; }
        public SpinResult Spin { get; }
        public bool AlreadySettled { get; }
        public Guid? SettledSpinId { get; }
        public long ConfirmedPayout { get; }
        internal SpinOperationResult(SpinError error = SpinError.None, string diagnostic = null,
            SpinResult spin = null, bool alreadySettled = false, Guid? settledSpinId = null, long confirmedPayout = 0)
        {
            Error = error; Diagnostic = diagnostic; Spin = spin; AlreadySettled = alreadySettled;
            SettledSpinId = settledSpinId; ConfirmedPayout = confirmedPayout;
        }
    }
}
