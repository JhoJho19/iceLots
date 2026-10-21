using System;
using System.Linq;
using IceP.Application.Persistence;
using IceP.Domain.Configuration;
using IceP.Domain.SlotEngine;

namespace IceP.Application
{
    public enum JackpotState { Active, WonThisWeek, Inactive }

    public sealed class JackpotStatus
    {
        public string EventId { get; }
        public bool Enabled { get; }
        public long JackpotAmount { get; }
        public JackpotState State { get; }
        public DateTimeOffset CurrentWeekId { get; }
        public int WinsThisWeek { get; }
        public int MaxWinsPerWeek { get; }
        public bool IsEligible { get; }
        public DateTimeOffset? NextResetUtc { get; }
        public DateTimeOffset? StartUtc { get; }
        public DateTimeOffset? EndUtc { get; }
        internal JackpotStatus(JackpotRulesConfig rules, DateTimeOffset week, int wins, bool active, bool eligible,
            bool ignoreWeeklyLimit = false)
        {
            EventId = rules.EventId; Enabled = rules.Enabled; JackpotAmount = rules.Amount;
            CurrentWeekId = week; WinsThisWeek = wins; MaxWinsPerWeek = rules.MaxWinsPerWeek;
            IsEligible = eligible; StartUtc = rules.StartUtc; EndUtc = rules.EndUtc;
            NextResetUtc = week.UtcTicks <= DateTime.MaxValue.Ticks - TimeSpan.TicksPerDay * 7 ?
                week.AddDays(7) : (DateTimeOffset?)null;
            State = !active ? JackpotState.Inactive : !ignoreWeeklyLimit && wins >= rules.MaxWinsPerWeek ?
                JackpotState.WonThisWeek : JackpotState.Active;
        }
    }

    public sealed class JackpotSpinResult
    {
        public bool Participated { get; }
        public string EventId { get; }
        public string ConfigVersion { get; }
        public DateTimeOffset? WeekId { get; }
        public bool JackpotWon { get; }
        public long JackpotAmount { get; }
        internal bool IsDebug { get; }
        internal JackpotSpinResult(JackpotRulesConfig rules, DateTimeOffset? week, bool participated, bool won,
            bool isDebug = false)
        {
            EventId = rules.EventId; ConfigVersion = rules.ConfigVersion; WeekId = week;
            Participated = participated; JackpotWon = won; JackpotAmount = won ? rules.Amount : 0;
            IsDebug = isDebug;
        }
    }

    // No writer or authoritative state here. Only SpinService can draw and settle,
    // under the shared command gate and after the accepted debit was confirmed.
    public sealed class WeeklyJackpotService
    {
        private readonly GameSession session;
        private readonly SlotRuntimeConfig config;
        private readonly IClock clock;
        private readonly IRandomSource random;
        private readonly JackpotRules probability;
        private bool debugJackpotEvery10Spins;
        private int debugPaidSpins;

        // Runtime-only test mode. Changing it starts a fresh ten-spin sequence.
        public bool DebugJackpotEvery10Spins
        {
            get { lock (session.Gate) return debugJackpotEvery10Spins; }
            set
            {
                lock (session.Gate)
                {
                    if (debugJackpotEvery10Spins == value) return;
                    debugJackpotEvery10Spins = value;
                    debugPaidSpins = 0;
                }
            }
        }

        public WeeklyJackpotService(GameSession session, SlotRuntimeConfig config, IClock clock, IRandomSource random)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            ConfigValidator.ValidateAndThrow(config);
            this.config = config;
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            probability = new JackpotRules(config.Jackpot);
            lock (session.Gate)
            {
                var errors = new SaveDataValidator(config).Validate(session.Snapshot);
                if (errors.Count != 0) throw new ArgumentException(string.Join("\n", errors), nameof(session));
                if (session.HasJackpotOwner) throw new InvalidOperationException("GameSession already has a WeeklyJackpotService.");
                session.HasJackpotOwner = true;
            }
        }

        public JackpotStatus GetStatus()
        {
            lock (session.Gate)
            {
                var now = clock.UtcNow.ToUniversalTime();
                var week = WeekAt(now, config.Jackpot);
                var state = session.Snapshot;
                int wins = WinsAt(state, week);
                bool active = IsActive(now);
                return new JackpotStatus(config.Jackpot, week, wins, active,
                    active && (debugJackpotEvery10Spins ||
                        (wins < config.Jackpot.MaxWinsPerWeek && !IsEarlierWeek(state, week))),
                    debugJackpotEvery10Spins);
            }
        }

        internal void ValidateBinding(GameSession value, SlotRuntimeConfig rules)
        {
            if (!ReferenceEquals(session, value) || !ReferenceEquals(config, rules))
                throw new ArgumentException("Jackpot must share the Spin session and immutable configuration.");
        }

        internal static DateTimeOffset WeekAt(DateTimeOffset instant, JackpotRulesConfig rules)
        {
            var utc = instant.ToUniversalTime();
            var boundary = new DateTimeOffset(utc.UtcDateTime.Date, TimeSpan.Zero).Add(rules.ResetTimeUtc);
            int days = ((int)utc.DayOfWeek - (int)rules.ResetWeekday + 7) % 7;
            boundary = boundary.AddDays(-days);
            return boundary > utc ? boundary.AddDays(-7) : boundary;
        }

        private bool IsActive(DateTimeOffset now) => config.Jackpot.Enabled &&
            (!config.Jackpot.StartUtc.HasValue || now >= config.Jackpot.StartUtc.Value) &&
            (!config.Jackpot.EndUtc.HasValue || now < config.Jackpot.EndUtc.Value);

        private static bool IsEarlierWeek(SaveData state, DateTimeOffset week) =>
            state.JackpotWeekId.HasValue && week < state.JackpotWeekId.Value;

        // One persisted week is a high-water mark. Earlier weeks are conservatively
        // blocked; changing event identity/reset schedule never grants an extra
        // allowance in an overlapping seven-day period. No unbounded history.
        private static int WinsAt(SaveData state, DateTimeOffset week) =>
            state.JackpotWeekId.HasValue && week - state.JackpotWeekId.Value < TimeSpan.FromDays(7) ?
                state.JackpotWinsThisWeek : 0;

        internal JackpotSpinResult Draw(PendingSpin pending, SaveData confirmed)
        {
            var week = pending.JackpotWeekId;
            if (debugJackpotEvery10Spins)
            {
                bool paid = pending.Type == PendingSpinType.Paid && week.HasValue &&
                    confirmed.PendingSpin?.SpinId == pending.SpinId &&
                    config.Economy.AvailableBets.Contains(pending.EffectiveBet);
                bool debugWon = false;
                if (paid)
                {
                    debugPaidSpins = (debugPaidSpins + 1) % 10;
                    debugWon = debugPaidSpins == 0 && IsActive(pending.AcceptedUtc);
                }
                return new JackpotSpinResult(config.Jackpot, week,
                    paid && IsActive(pending.AcceptedUtc), debugWon, isDebug: true);
            }
            bool eligible = pending.Type == PendingSpinType.Paid && week.HasValue &&
                confirmed.PendingSpin?.SpinId == pending.SpinId && IsActive(pending.AcceptedUtc) &&
                config.Economy.AvailableBets.Contains(pending.EffectiveBet) &&
                !IsEarlierWeek(confirmed, week.Value) && WinsAt(confirmed, week.Value) < config.Jackpot.MaxWinsPerWeek;
            bool won = eligible && Sample(probability.CalculateProbability(
                SpinEvaluationContext.Paid(pending.EffectiveBet), acceptedAtUtc: pending.AcceptedUtc), random);
            return new JackpotSpinResult(config.Jackpot, week, eligible, won);
        }

        // Exact Bernoulli for the supplied IEEE-754 double, without rounding to a
        // fixed integer grid. Compare fair random bits to p's binary expansion.
        // Expected <2 integer draws; at most 1074 for the smallest positive double.
        // Each eligible Spin performs one trial (which can use several random bits).
        // 0 and 1 consume no RNG. Equality is a loss, i.e. U < p.
        internal static bool Sample(double p, IRandomSource random)
        {
            if (double.IsNaN(p) || p < 0 || p > 1) throw new ArgumentOutOfRangeException(nameof(p));
            if (p == 0 || p == 1) return p == 1;
            while (p > 0)
            {
                p *= 2;
                int thresholdBit = p >= 1 ? 1 : 0;
                p -= thresholdBit;
                int bit = random.NextInt(0, 2);
                if (bit < 0 || bit > 1) throw new InvalidOperationException("RNG returned a value outside [0,2).");
                if (bit != thresholdBit) return bit < thresholdBit;
            }
            return false;
        }

        internal void ApplyWin(SaveData proposed, JackpotSpinResult result)
        {
            // Test rewards use normal payout settlement but never consume the real weekly allowance.
            // Capture the mode in the result so toggling during animation cannot change settlement.
            if (!result.JackpotWon || result.IsDebug) return;
            var week = result.WeekId.Value;
            if (IsEarlierWeek(proposed, week))
                throw new InvalidOperationException("Cannot overwrite newer jackpot settlement metadata.");
            proposed.JackpotWinsThisWeek = checked(WinsAt(proposed, week) + 1);
            proposed.JackpotWeekId = week;
            proposed.JackpotEventId = result.EventId;
        }
    }
}
