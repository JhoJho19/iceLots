using System;
using IceP.Application;

namespace IceP.Presentation
{
    public sealed class GameNavigationPresenter : IDisposable
    {
        private readonly GameNavigationService navigation;
        private readonly GameNavigationView view;
        private readonly bool ownsBack;

        public GameNavigationPresenter(GameNavigationService navigation, GameNavigationView view, bool ownsBack = true)
        {
            this.navigation = navigation;
            this.view = view;
            this.ownsBack = ownsBack;
            navigation.ScreenChanged += ShowScreen;
            if (ownsBack) view.BackButton.onClick.AddListener(navigation.NavigateToMainMenu);
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
            if (ownsBack) view.BackButton.onClick.RemoveListener(navigation.NavigateToMainMenu);
        }
    }
}
