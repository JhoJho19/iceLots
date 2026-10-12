using System;
using System.Collections.Generic;

namespace IceP.Application
{
    public enum DailyBonusError
    {
        None, Disabled, AlreadyClaimed, Busy, ClockInconsistent,
        ArithmeticOverflow, PersistenceFailure, InvalidState
    }

    // RewardDay/RewardAmount describe the available reward or the next reward when already claimed.
    // CurrentStreakDay is the confirmed saved progress, never an uncommitted proposal.
    public sealed class DailyBonusStatus
    {
        public bool Enabled { get; }
        public bool CanClaim => UnavailabilityReason == DailyBonusError.None;
        public DailyBonusError UnavailabilityReason { get; }
        public int RewardDay { get; }
        public long RewardAmount { get; }
        public IReadOnlyList<long> Rewards { get; }
        public int CurrentStreakDay { get; }
        public DateTimeOffset? LastClaimUtc { get; }
        public DateTimeOffset? NextAvailableUtc { get; }
        public DateTimeOffset? NextResetUtc { get; }

        internal DailyBonusStatus(bool enabled, DailyBonusError reason, int rewardDay,
            IReadOnlyList<long> rewards, int streakDay, DateTimeOffset? lastClaim,
            DateTimeOffset? nextAvailable, DateTimeOffset? nextReset)
        {
            Enabled = enabled;
            UnavailabilityReason = reason;
            RewardDay = rewardDay;
            Rewards = rewards;
            RewardAmount = rewards[rewardDay - 1];
            CurrentStreakDay = streakDay;
            LastClaimUtc = lastClaim;
            NextAvailableUtc = nextAvailable;
            NextResetUtc = nextReset;
        }
    }

    public sealed class DailyBonusClaimResult
    {
        public bool Success => Error == DailyBonusError.None;
        public DailyBonusError Error { get; }
        public string Diagnostic { get; }
        public int RewardDay { get; }
        public long AwardedCoins { get; }
        public long? ConfirmedBalance { get; }
        public DateTimeOffset? ClaimUtc { get; }

        internal DailyBonusClaimResult(DailyBonusError error, string diagnostic = null,
            long? confirmedBalance = null, int rewardDay = 0, long awardedCoins = 0,
            DateTimeOffset? claimUtc = null)
        {
            Error = error;
            Diagnostic = diagnostic;
            ConfirmedBalance = confirmedBalance;
            RewardDay = rewardDay;
            AwardedCoins = awardedCoins;
            ClaimUtc = claimUtc;
        }
    }
}
