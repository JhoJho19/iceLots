namespace IceP.Domain.SlotEngine
{
    /// <summary>Composes the unchanged section 4.3 outcome with immutable mathematical results.</summary>
    public sealed class EvaluatedSpinOutcome
    {
        public SpinOutcome Outcome { get; }
        public SpinEvaluationContext Context { get; }
        public WinEvaluation Wins { get; }
        public ScatterEvaluation Scatter { get; }
        public long AppliedPayoutMultiplier { get; }
        public long FinalPayout { get; }

        internal EvaluatedSpinOutcome(SpinOutcome outcome, SpinEvaluationContext context,
            WinEvaluation wins, ScatterEvaluation scatter, long multiplier)
        {
            Outcome = outcome;
            Context = context;
            Wins = wins;
            Scatter = scatter;
            AppliedPayoutMultiplier = multiplier;
            FinalPayout = checked(wins.BaseWin * multiplier);
        }
    }
}
