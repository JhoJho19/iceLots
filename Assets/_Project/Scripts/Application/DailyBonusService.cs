using System;
using System.Threading;
using IceP.Application.Persistence;
using IceP.Domain.Configuration;

namespace IceP.Application
{
    public sealed class DailyBonusService
    {
        private readonly GameSession session;
        private readonly DailyBonusRules rules;
        private readonly BalanceService balance;
        private readonly ISaveRepository repository;
        private readonly SaveDataValidator validator;
        private readonly IClock clock;
        // Only an unconfirmed transaction is retained here; GameSession remains authoritative.
        private SaveData proposed;
        private long proposedReward;

        public DailyBonusService(GameSession session, SlotRuntimeConfig config, BalanceService balance,
            ISaveRepository repository, IClock clock)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.balance = balance ?? throw new ArgumentNullException(nameof(balance));
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            validator = new SaveDataValidator(config);
            rules = config.DailyBonus;
            lock (session.Gate)
            {
                var errors = validator.Validate(session.Snapshot);
                if (errors.Count != 0) throw new ArgumentException(string.Join("\n", errors), nameof(session));
                if (session.HasDailyBonusOwner) throw new InvalidOperationException("GameSession already has a DailyBonusService.");
                session.HasDailyBonusOwner = true;
            }
        }

        public DailyBonusStatus GetStatus()
        {
            lock (session.Gate)
                return StatusAt(clock.UtcNow.ToUniversalTime(), session.CommandExecuting);
        }

        public DailyBonusClaimResult Claim() => Execute(() =>
        {
            if (!session.CanStartSpin || proposed != null)
                return Failure(DailyBonusError.Busy, "Session is blocked; complete or retry its active transaction.");
            if (!rules.Enabled) return Failure(DailyBonusError.Disabled, "Daily Bonus is disabled.");
            var now = clock.UtcNow.ToUniversalTime(); // Capture once; retries never read the clock.
            var status = StatusAt(now, false);
            if (!status.CanClaim) return Failure(status.UnavailabilityReason, "Reward is unavailable: " + status.UnavailabilityReason);
            var next = balance.Credit(session.Snapshot, status.RewardAmount);
            next.DailyLastClaimUtc = now;
            next.DailyStreakDay = status.RewardDay;
            var errors = validator.Validate(next);
            if (errors.Count != 0) return Failure(DailyBonusError.InvalidState, string.Join("\n", errors));
            proposed = next;
            proposedReward = status.RewardAmount;
            session.Transition(GameSessionState.Preparing);
            return Persist();
        });

        // A repository failure can mean it committed. Keep the session in Error and retry
        // precisely this snapshot, with the original timestamp, streak and balance.
        public DailyBonusClaimResult RetryPersistence() => Execute(() =>
        {
            if (proposed == null || session.State != GameSessionState.Error)
                return Failure(DailyBonusError.InvalidState, "No failed Daily Bonus snapshot to retry.");
            session.Transition(GameSessionState.Preparing);
            return Persist();
        });

        private DailyBonusClaimResult Persist()
        {
            SaveWriteResult save;
            try { save = repository.Save(proposed.Copy()); }
            catch (Exception e) { return Block(e.ToString()); }
            if (save == null || !save.Success)
                return Block(save?.Diagnostic ?? "Repository returned no write confirmation.");
            session.Commit(proposed);
            var result = new DailyBonusClaimResult(DailyBonusError.None, confirmedBalance: proposed.Balance,
                rewardDay: proposed.DailyStreakDay, awardedCoins: proposedReward, claimUtc: proposed.DailyLastClaimUtc);
            proposed = null;
            proposedReward = 0;
            session.Transition(GameSessionState.Idle);
            return result;
        }

        private DailyBonusStatus StatusAt(DateTimeOffset now, bool commandExecuting)
        {
            var state = session.Snapshot;
            long period = PeriodAt(now);
            long gap = state.DailyLastClaimUtc.HasValue ? period - PeriodAt(state.DailyLastClaimUtc.Value) : 0;
            int day = !state.DailyLastClaimUtc.HasValue ? 1 :
                gap > 1 && rules.StreakResetBehavior == StreakResetBehavior.ResetAfterMissedDay ? 1 :
                state.DailyStreakDay % rules.Rewards.Count + 1;
            var reason = !rules.Enabled ? DailyBonusError.Disabled :
                !session.CanStartSpin || commandExecuting ? DailyBonusError.Busy :
                state.DailyLastClaimUtc.HasValue && now < state.DailyLastClaimUtc.Value ? DailyBonusError.ClockInconsistent :
                state.DailyLastClaimUtc.HasValue && gap == 0 ? DailyBonusError.AlreadyClaimed : DailyBonusError.None;
            var nextAvailable = state.DailyLastClaimUtc.HasValue && gap <= 0 ?
                ResetAfter(PeriodAt(state.DailyLastClaimUtc.Value)) : null;
            return new DailyBonusStatus(rules.Enabled, reason, day, rules.Rewards, state.DailyStreakDay,
                state.DailyLastClaimUtc, rules.Enabled ? nextAvailable : null, ResetAfter(period));
        }

        // Floor division of UTC ticks shifted by the configured reset, including the
        // exact boundary. No local date, 24-hour rolling cooldown, or running timer.
        private long PeriodAt(DateTimeOffset instant)
        {
            long ticks = instant.UtcDateTime.Ticks - rules.ResetTimeUtc.Ticks;
            return ticks >= 0 ? ticks / TimeSpan.TicksPerDay : (ticks - TimeSpan.TicksPerDay + 1) / TimeSpan.TicksPerDay;
        }
        private DateTimeOffset? ResetAfter(long period)
        {
            long ticks = (period + 1) * TimeSpan.TicksPerDay + rules.ResetTimeUtc.Ticks;
            return ticks > DateTime.MaxValue.Ticks ? (DateTimeOffset?)null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }

        private DailyBonusClaimResult Execute(Func<DailyBonusClaimResult> command)
        {
            if (!Monitor.TryEnter(session.Gate))
                return new DailyBonusClaimResult(DailyBonusError.Busy, "Another command is running.");
            try
            {
                if (session.CommandExecuting) return Failure(DailyBonusError.Busy, "Reentrant command rejected.");
                session.CommandExecuting = true;
                try { return command(); }
                catch (OverflowException e) { return Failure(DailyBonusError.ArithmeticOverflow, e.Message); }
                catch (Exception e) { return Failure(DailyBonusError.InvalidState, e.ToString()); }
                finally { session.CommandExecuting = false; }
            }
            finally { Monitor.Exit(session.Gate); }
        }
        private DailyBonusClaimResult Block(string diagnostic)
        {
            session.Transition(GameSessionState.Error);
            return Failure(DailyBonusError.PersistenceFailure, diagnostic);
        }
        private DailyBonusClaimResult Failure(DailyBonusError error, string diagnostic) =>
            new DailyBonusClaimResult(error, diagnostic, session.Snapshot.Balance);
    }
}
