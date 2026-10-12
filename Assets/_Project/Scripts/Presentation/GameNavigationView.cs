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

        public Button PlayButton => playButton;
        public Button BackButton => backButton;
        public Button SettingsButton => settingsButton;
        public Button DailyBonusButton => dailyBonusButton;
        public Button CloseSettingsButton => closeSettingsButton;
        public Button CloseDailyBonusButton => closeDailyBonusButton;

        public bool HasRequiredReferences => mainMenuScreen && slotScreen &&
            settingsPopup && dailyBonusPopup && mainMenuGroup && playButton && backButton &&
            settingsButton && dailyBonusButton && closeSettingsButton && closeDailyBonusButton;

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
