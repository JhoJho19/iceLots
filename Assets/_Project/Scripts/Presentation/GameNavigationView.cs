using UnityEngine;
using UnityEngine.UI;

namespace IceP.Presentation
{
    public sealed class GameNavigationView : MonoBehaviour
    {
        [SerializeField] private GameObject mainMenuScreen;
        [SerializeField] private GameObject slotScreen;
        [SerializeField] private GameObject settingsPopup;
        [SerializeField] private GameObject dailyBonusPopup;
        [SerializeField] private CanvasGroup mainMenuGroup;
        [SerializeField] private Button playButton;
        [SerializeField] private Button backButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button dailyBonusButton;
        [SerializeField] private Button closeSettingsButton;
        [SerializeField] private Button closeDailyBonusButton;
        [SerializeField] private SettingsView settingsView;
        [SerializeField] private DailyBonusView dailyBonusView;
        [SerializeField] private GameObject dailyAvailabilityIndicator;
        [SerializeField] private Text balanceLabel;
        [SerializeField] private Text menuMessage;

        public SettingsView SettingsView => settingsView;
        public DailyBonusView DailyBonusView => dailyBonusView;
        public bool HasOpenPopup => settingsPopup.activeSelf || dailyBonusPopup.activeSelf;
        public bool DailyAvailable => dailyAvailabilityIndicator.activeSelf;
        public string BalanceText => balanceLabel.text;
        public void ShowDailyAvailability(bool available) => dailyAvailabilityIndicator.SetActive(available);
        public void ShowBalance(long balance) => balanceLabel.text = $"Coins: {balance:N0}";
        public void ShowMenuMessage(string message) => menuMessage.text = message;

        // Startup can fail because a serialized reference is missing. Keep the remaining
        // controls safe without requiring the very references that failed validation.
        public void ShowInitializationFailure()
        {
            if (mainMenuScreen) mainMenuScreen.SetActive(true);
            if (slotScreen) slotScreen.SetActive(false);
            if (settingsPopup) settingsPopup.SetActive(false);
            if (dailyBonusPopup) dailyBonusPopup.SetActive(false);
            if (playButton) playButton.interactable = false;
            if (settingsButton) settingsButton.interactable = false;
            if (dailyBonusButton) dailyBonusButton.interactable = false;
            if (backButton) backButton.interactable = false;
            if (menuMessage) menuMessage.text = "Unable to initialize. Controls unavailable.";
        }

        public Button PlayButton => playButton;
        public Button BackButton => backButton;
        public Button SettingsButton => settingsButton;
        public Button DailyBonusButton => dailyBonusButton;
        public Button CloseSettingsButton => closeSettingsButton;
        public Button CloseDailyBonusButton => closeDailyBonusButton;

        public bool HasRequiredReferences => mainMenuScreen && slotScreen &&
            settingsPopup && dailyBonusPopup && mainMenuGroup && playButton && backButton &&
            settingsButton && dailyBonusButton && closeSettingsButton && closeDailyBonusButton &&
            settingsView && settingsView.HasRequiredReferences && dailyBonusView && dailyBonusView.HasRequiredReferences &&
            dailyAvailabilityIndicator && balanceLabel && menuMessage;

        public void ShowScreen(bool showSlot)
        {
            ClosePopups();
            mainMenuScreen.SetActive(!showSlot);
            slotScreen.SetActive(showSlot);
        }

        public void ShowSettings()
        {
            ClosePopups();
            settingsPopup.SetActive(true);
            mainMenuGroup.interactable = false;
            closeSettingsButton.Select();
        }

        public void ShowDailyBonus()
        {
            ClosePopups();
            dailyBonusPopup.SetActive(true);
            mainMenuGroup.interactable = false;
            closeDailyBonusButton.Select();
        }

        public void ClosePopups()
        {
            settingsPopup.SetActive(false);
            dailyBonusPopup.SetActive(false);
            mainMenuGroup.interactable = true;
        }
    }
}
