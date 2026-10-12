using System;

namespace IceP.Domain.SlotEngine
{
    /// <summary>Application supplies the paid bet or the original triggering BonusBet.
    /// This value has no balance or bonus-session lifecycle.</summary>
    public readonly struct SpinEvaluationContext
    {
        public bool IsFreeSpin { get; }
        public long EvaluationBet { get; }
        private SpinEvaluationContext(long bet, bool isFreeSpin)
        {
            if (bet <= 0) throw new ArgumentOutOfRangeException(nameof(bet));
            EvaluationBet = bet;
            IsFreeSpin = isFreeSpin;
        }
        public static SpinEvaluationContext Paid(long totalBet) => new SpinEvaluationContext(totalBet, false);
        public static SpinEvaluationContext Free(long bonusBet) => new SpinEvaluationContext(bonusBet, true);
    }
}
