using System;
using IceP.Application;

namespace IceP.Presentation
{
    public sealed class DailyBonusPresenter : IDisposable
    {
        public event Action<DailyBonusClaimResult> RewardConfirmed;
        private readonly DailyBonusService service;
        private readonly DailyBonusView view;
        private readonly Action changed;
        public DailyBonusPresenter(DailyBonusService service, DailyBonusView view, Action changed)
        {
            this.service = service; this.view = view; this.changed = changed;
            view.ClaimButton.onClick.AddListener(Claim);
            view.RetryButton.onClick.AddListener(Retry);
        }
        public void Open()
        {
            view.ShowMessage(service.CanRetry ? "Reward not confirmed. Retry to finish." : "");
            Refresh();
        }
        public void Refresh() => view.Show(new DailyBonusDisplay(service.GetStatus()), service.CanRetry);
        private void Claim() => ShowResult(service.Claim());
        private void Retry() => ShowResult(service.RetryPersistence());
        private void ShowResult(DailyBonusClaimResult result)
        {
            if (result.Success && result.AwardedCoins > 0)
            {
                view.PlayRewardCoins(result.RewardDay);
                RewardConfirmed?.Invoke(result);
            }
            view.ShowMessage(result.Success ? $"Received {result.AwardedCoins:N0} coins!" :
                result.Error == DailyBonusError.PersistenceFailure ? "Reward not confirmed. Retry to finish." : "Reward unavailable: " + result.Error);
            Refresh();
            changed();
        }
        public void Dispose()
        {
            view.ClaimButton.onClick.RemoveListener(Claim);
            view.RetryButton.onClick.RemoveListener(Retry);
        }
    }
}
