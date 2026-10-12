using System;
using IceP.Domain.Configuration;

namespace IceP.Domain.SlotEngine
{
    /// <summary>Probability only. Application supplies weekly eligibility and draws/settles later.</summary>
    public sealed class JackpotRules
    {
        private readonly JackpotRulesConfig config;
        public JackpotRules(JackpotRulesConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            if (config.ReferenceBet <= 0) throw new ArgumentOutOfRangeException(nameof(config), "ReferenceBet must be positive.");
            if (double.IsNaN(config.BaseProbability) || double.IsInfinity(config.BaseProbability) ||
                config.BaseProbability < 0 || config.BaseProbability > 1)
                throw new ArgumentOutOfRangeException(nameof(config), "BaseProbability must be finite and within [0, 1].");
        }

        /// <param name="acceptedAtUtc">Optional supplied event time; no system clock is consulted.
        /// Start is inclusive and end is exclusive. Weekly state is supplied by Application.</param>
        public double CalculateProbability(SpinEvaluationContext context, bool weeklyEligible = true,
            bool alreadyWonThisWeek = false, DateTimeOffset? acceptedAtUtc = null)
        {
            if (context.EvaluationBet <= 0) throw new ArgumentOutOfRangeException(nameof(context));
            if (!config.Enabled || context.IsFreeSpin || !weeklyEligible || alreadyWonThisWeek) return 0;
            if (acceptedAtUtc.HasValue &&
                ((config.StartUtc.HasValue && acceptedAtUtc.Value < config.StartUtc.Value) ||
                 (config.EndUtc.HasValue && acceptedAtUtc.Value >= config.EndUtc.Value))) return 0;
            return Math.Min(1.0, config.BaseProbability * context.EvaluationBet / config.ReferenceBet);
        }
    }
}
