using System;

namespace IceP.Application
{
    public enum GameScreen { MainMenu, Slot }

    public sealed class GameNavigationService
    {
        private readonly GameSession session;
        public GameNavigationService(GameSession session)
        { this.session = session ?? throw new ArgumentNullException(nameof(session)); }
        public bool CanNavigateBack { get { lock (session.Gate) return session.CanNavigateBack && !session.CommandExecuting; } }
        public GameScreen CurrentScreen { get; private set; } = GameScreen.MainMenu;
        public event Action<GameScreen> ScreenChanged;

        public void NavigateToSlot()
        {
            lock (session.Gate)
                if (session.CanStartSpin && !session.CommandExecuting) Navigate(GameScreen.Slot);
        }
        public void NavigateToMainMenu() => TryNavigateToMainMenu();
        public bool TryNavigateToMainMenu()
        {
            lock (session.Gate)
            {
                if (!CanNavigateBack) return false;
                Navigate(GameScreen.MainMenu);
                return true;
            }
        }

        private void Navigate(GameScreen screen)
        {
            if (CurrentScreen == screen) return;
            CurrentScreen = screen;
            ScreenChanged?.Invoke(screen);
        }
    }
}
