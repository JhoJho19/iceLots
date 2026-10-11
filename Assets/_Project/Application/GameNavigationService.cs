using System;

namespace IceP.Application
{
    public enum GameScreen { MainMenu, Slot }

    // Section 4.1 has no session or spin. Session guards belong to section 4.6.
    public sealed class GameNavigationService
    {
        public GameScreen CurrentScreen { get; private set; } = GameScreen.MainMenu;
        public event Action<GameScreen> ScreenChanged;

        public void NavigateToSlot() => Navigate(GameScreen.Slot);
        public void NavigateToMainMenu() => Navigate(GameScreen.MainMenu);

        private void Navigate(GameScreen screen)
        {
            if (CurrentScreen == screen) return;
            CurrentScreen = screen;
            ScreenChanged?.Invoke(screen);
        }
    }
}
