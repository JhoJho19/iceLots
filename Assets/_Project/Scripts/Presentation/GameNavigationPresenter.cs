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
            view.BackButton.onClick.AddListener(navigation.NavigateToMainMenu);
            ShowScreen(navigation.CurrentScreen);
        }

        private void ShowScreen(GameScreen screen)
        {
            view.ShowScreen(screen == GameScreen.Slot);
            if (screen == GameScreen.Slot) view.BackButton.Select();
            else view.PlayButton.Select();
        }

        public void RefreshAvailability() => view.BackButton.interactable = navigation.CanNavigateBack;

        public void Dispose()
        {
            navigation.ScreenChanged -= ShowScreen;
            view.BackButton.onClick.RemoveListener(navigation.NavigateToMainMenu);
        }
    }
}
