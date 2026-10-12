using System;
using IceP.Application;

namespace IceP.Presentation
{
    public sealed class GameNavigationPresenter : IDisposable
    {
        private readonly GameNavigationService navigation;
        private readonly GameNavigationView view;

        public GameNavigationPresenter(GameNavigationService navigation, GameNavigationView view)
        {
            this.navigation = navigation;
            this.view = view;
            navigation.ScreenChanged += ShowScreen;
            view.PlayButton.onClick.AddListener(navigation.NavigateToSlot);
            view.BackButton.onClick.AddListener(navigation.NavigateToMainMenu);
            view.SettingsButton.onClick.AddListener(view.ShowSettings);
            view.DailyBonusButton.onClick.AddListener(view.ShowDailyBonus);
            view.CloseSettingsButton.onClick.AddListener(CloseSettings);
            view.CloseDailyBonusButton.onClick.AddListener(CloseDailyBonus);
            ShowScreen(navigation.CurrentScreen);
        }

        private void ShowScreen(GameScreen screen)
        {
            view.ShowScreen(screen == GameScreen.Slot);
            if (screen == GameScreen.Slot) view.BackButton.Select();
            else view.PlayButton.Select();
        }

        private void CloseSettings()
        {
            view.ClosePopups();
            view.SettingsButton.Select();
        }

        private void CloseDailyBonus()
        {
            view.ClosePopups();
            view.DailyBonusButton.Select();
        }

        public void Dispose()
        {
            navigation.ScreenChanged -= ShowScreen;
            view.PlayButton.onClick.RemoveListener(navigation.NavigateToSlot);
            view.BackButton.onClick.RemoveListener(navigation.NavigateToMainMenu);
            view.SettingsButton.onClick.RemoveListener(view.ShowSettings);
            view.DailyBonusButton.onClick.RemoveListener(view.ShowDailyBonus);
            view.CloseSettingsButton.onClick.RemoveListener(CloseSettings);
            view.CloseDailyBonusButton.onClick.RemoveListener(CloseDailyBonus);
        }
    }
}
