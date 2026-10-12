using System;
using IceP.Domain.Configuration;

namespace IceP.Domain.SlotEngine
{
    /// <summary>Pure orchestration using one immutable configuration snapshot and an injected RNG.
    /// Serialize generation calls as required by OutcomeGenerator. Evaluation consumes no RNG.</summary>
    public sealed class SlotEngine
    {
        private readonly OutcomeGenerator generator;
        private readonly WinEvaluator wins;
        private readonly ScatterEvaluator scatter;
        private readonly long freeSpinsMultiplier;

        public SlotEngine(SlotRuntimeConfig config, IRandomSource random)
        {
            generator = new OutcomeGenerator(config, random);
            wins = new WinEvaluator(config);
            scatter = new ScatterEvaluator(config);
            freeSpinsMultiplier = config.Scatter.FreeSpinsMultiplier;
        }

        public EvaluatedSpinOutcome Generate(SpinEvaluationContext context)
        {
            // Reject an invalid request before consuming any reel stops.
            wins.ValidateBet(context.EvaluationBet);
            return Evaluate(generator.Generate(), context);
        }

        public EvaluatedSpinOutcome Evaluate(SpinOutcome outcome, SpinEvaluationContext context)
        {
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));
            return new EvaluatedSpinOutcome(outcome, context,
                wins.Evaluate(outcome.Grid, context.EvaluationBet),
                scatter.Evaluate(outcome.Grid, context.IsFreeSpin),
                context.IsFreeSpin ? freeSpinsMultiplier : 1);
        }
    }
}
