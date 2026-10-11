using System;

namespace IceP.Domain.Configuration
{
    public sealed class JackpotRulesConfig
    {
        public bool Enabled { get; }
        public string EventId { get; }
        public string ConfigVersion { get; }
        public long Amount { get; }
        public double BaseProbability { get; }
        public long ReferenceBet { get; }
        public int MaxWinsPerWeek { get; }
        public DayOfWeek ResetWeekday { get; }
        public TimeSpan ResetTimeUtc { get; }
        public DateTimeOffset? StartUtc { get; }
        public DateTimeOffset? EndUtc { get; }

        public JackpotRulesConfig(bool enabled, string eventId, string configVersion, long amount,
            double baseProbability, long referenceBet, int maxWinsPerWeek, DayOfWeek resetWeekday,
            TimeSpan resetTimeUtc, DateTimeOffset? startUtc, DateTimeOffset? endUtc)
        {
            Enabled = enabled;
            EventId = eventId;
            ConfigVersion = configVersion;
            Amount = amount;
            BaseProbability = baseProbability;
            ReferenceBet = referenceBet;
            MaxWinsPerWeek = maxWinsPerWeek;
            ResetWeekday = resetWeekday;
            ResetTimeUtc = resetTimeUtc;
            StartUtc = startUtc;
            EndUtc = endUtc;
        }
    }
}
