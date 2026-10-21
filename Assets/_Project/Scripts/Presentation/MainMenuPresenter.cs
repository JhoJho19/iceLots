using System;
using IceP.Application;

namespace IceP.Presentation
{
    public sealed class MainMenuPresenter : IDisposable
    {
        private readonly GameSession session;
        private readonly GameNavigationService navigation;
        private readonly GameNavigationView view;
        private readonly DailyBonusService daily;
        private readonly SettingsService settings;
        private readonly SettingsPresenter settingsPresenter;
        private readonly DailyBonusPresenter dailyPresenter;
        private float refreshTime;
        private bool balanceRefreshPending;
        public event Action<DailyBonusClaimResult> RewardConfirmed
        {
            add => dailyPresenter.RewardConfirmed += value;
            remove => dailyPresenter.RewardConfirmed -= value;
        }
        public event Action<IceP.Application.Persistence.UserSettings> AudioSettingsChanged
        {
            add => settingsPresenter.AudioSettingsChanged += value;
            remove => settingsPresenter.AudioSettingsChanged -= value;
        }
        public MainMenuPresenter(GameSession session, GameNavigationService navigation, GameNavigationView view,
            DailyBonusService daily, SettingsService settings)
        {
            this.session = session; this.navigation = navigation; this.view = view; this.daily = daily; this.settings = settings;
            settingsPresenter = new SettingsPresenter(settings, view.SettingsView);
            dailyPresenter = new DailyBonusPresenter(daily, view.DailyBonusView, Refresh);
            view.PlayButton.onClick.AddListener(Play);
            view.SettingsButton.onClick.AddListener(OpenSettings);
            view.DailyBonusButton.onClick.AddListener(OpenDaily);
            view.CloseSettingsButton.onClick.AddListener(CloseSettings);
            view.CloseDailyBonusButton.onClick.AddListener(CloseDaily);
            navigation.ScreenChanged += ScreenChanged;
            Refresh();
        }
        private bool MenuAvailable => navigation.CurrentScreen == GameScreen.MainMenu && !view.HasOpenPopup;
        private void Play() { if (MenuAvailable && session.CanStartSpin) navigation.NavigateToSlot(); }
        private void OpenSettings()
        {
            if (!MenuAvailable || !(settings.CanUpdate || settings.CanRetry)) return;
            view.ShowSettings(); settingsPresenter.Open(); Refresh();
        }
        private void OpenDaily()
        {
            if (!MenuAvailable || !(daily.GetStatus().CanClaim || daily.CanRetry)) return;
            view.ShowDailyBonus(); dailyPresenter.Open(); Refresh();
        }
        private void CloseSettings()
        {
            if (!view.SettingsView.gameObject.activeSelf) return;
            settingsPresenter.Close(); view.ClosePopups(); Refresh(); view.SettingsButton.Select();
        }
        private void CloseDaily()
        {
            if (!view.DailyBonusView.gameObject.activeSelf) return;
            view.ClosePopups(); Refresh();
            if (view.DailyBonusButton.interactable) view.DailyBonusButton.Select();
            else view.PlayButton.Select();
        }
        private void ScreenChanged(GameScreen _) => Refresh();
        public void Tick(float delta)
        {
            if (balanceRefreshPending && !view.DailyBonusView.IsRewardCoinsPlaying) Refresh();
            if (view.SettingsView.gameObject.activeInHierarchy) settingsPresenter.Tick(delta);
            refreshTime += delta;
            if (refreshTime >= 1) { refreshTime = 0; Refresh(); }
            view.TickDailyAttention(delta);
        }
        public void FlushSettings() => settingsPresenter.Close();
        public void Refresh()
        {
            var status = daily.GetStatus();
            view.ShowDailyAvailability(status.CanClaim);
            // Keep the old balance visible until the last reward coin has arrived.
            balanceRefreshPending = view.DailyBonusView.IsRewardCoinsPlaying;
            if (!balanceRefreshPending) view.ShowBalance(session.Snapshot.Balance);
            view.PlayButton.interactable = session.CanStartSpin;
            view.SettingsButton.interactable = settings.CanUpdate || settings.CanRetry;
            // Keep a failed reward transaction reachable so its save can be retried.
            view.DailyBonusButton.interactable = status.CanClaim || daily.CanRetry;
            view.ShowMenuMessage(session.State == GameSessionState.Error ? "Save unresolved. Open the affected popup to Retry." : "");
            if (view.DailyBonusView.gameObject.activeInHierarchy) dailyPresenter.Refresh();
            if (view.SettingsView.gameObject.activeInHierarchy) settingsPresenter.Refresh();
        }
        public void Dispose()
        {
            navigation.ScreenChanged -= ScreenChanged;
            view.PlayButton.onClick.RemoveListener(Play);
            view.SettingsButton.onClick.RemoveListener(OpenSettings);
            view.DailyBonusButton.onClick.RemoveListener(OpenDaily);
            view.CloseSettingsButton.onClick.RemoveListener(CloseSettings);
            view.CloseDailyBonusButton.onClick.RemoveListener(CloseDaily);
            settingsPresenter.Dispose(); dailyPresenter.Dispose();
        }
    }
}
