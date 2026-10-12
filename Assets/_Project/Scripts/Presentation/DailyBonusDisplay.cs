using IceP.Application;

namespace IceP.Presentation
{
    public enum RewardDisplayState { Claimed, Available, Current, Future }

    // Pure display projection of authoritative status, including wrap and missed-day reset.
    public sealed class DailyBonusDisplay
    {
        public DailyBonusStatus Status { get; }
        public RewardDisplayState[] Days { get; }
        public DailyBonusDisplay(DailyBonusStatus status)
        {
            Status = status;
            Days = new RewardDisplayState[status.Rewards.Count];
            bool claimedPeriod = status.NextAvailableUtc.HasValue;
            int completed = claimedPeriod ? status.CurrentStreakDay : status.RewardDay - 1;
            for (int i = 0; i < Days.Length; i++)
                Days[i] = i < completed ? RewardDisplayState.Claimed :
                    i + 1 == status.RewardDay ? (status.CanClaim ? RewardDisplayState.Available : RewardDisplayState.Current) : RewardDisplayState.Future;
        }
    }
}
