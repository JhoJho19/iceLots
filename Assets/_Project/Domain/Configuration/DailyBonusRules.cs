using System;
using System.Collections.Generic;
using System.Linq;

namespace IceP.Domain.Configuration
{
    public enum StreakResetBehavior { ResetAfterMissedDay = 0, PreserveStreak = 1 }

    public sealed class DailyBonusRules
    {
        public bool Enabled { get; }
        public IReadOnlyList<long> Rewards { get; }
        public TimeSpan ResetTimeUtc { get; }
        public StreakResetBehavior StreakResetBehavior { get; }

        public DailyBonusRules(bool enabled, IEnumerable<long> rewards, TimeSpan resetTimeUtc,
            StreakResetBehavior streakResetBehavior)
        {
            Enabled = enabled;
            Rewards = Array.AsReadOnly((rewards ?? Enumerable.Empty<long>()).ToArray());
            ResetTimeUtc = resetTimeUtc;
            StreakResetBehavior = streakResetBehavior;
        }
    }
}
