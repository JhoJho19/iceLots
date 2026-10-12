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
            if (!MenuAvailable || !(session.CanStartSpin || daily.CanRetry)) return;
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
            view.ClosePopups(); Refresh(); view.DailyBonusButton.Select();
        }
        private void ScreenChanged(GameScreen _) => Refresh();
        public void Tick(float delta)
        {
            if (view.SettingsView.gameObject.activeInHierarchy) settingsPresenter.Tick(delta);
            refreshTime += delta;
            if (refreshTime >= 1) { refreshTime = 0; Refresh(); }
        }
        public void FlushSettings() => settingsPresenter.Close();
        public void Refresh()
        {
            var status = daily.GetStatus();
            view.ShowDailyAvailability(status.CanClaim);
            view.ShowBalance(session.Snapshot.Balance);
            view.PlayButton.interactable = session.CanStartSpin;
            view.SettingsButton.interactable = settings.CanUpdate || settings.CanRetry;
            view.DailyBonusButton.interactable = session.CanStartSpin || daily.CanRetry;
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
