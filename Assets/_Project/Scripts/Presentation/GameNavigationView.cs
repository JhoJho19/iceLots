using TMPro;
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
        [SerializeField] private Material dailyBonusAvailableMaterial;
        [SerializeField] private Button closeSettingsButton;
        [SerializeField] private Button closeDailyBonusButton;
        [SerializeField] private SettingsView settingsView;
        [SerializeField] private DailyBonusView dailyBonusView;
        [SerializeField] private TMP_Text balanceLabel;
        [SerializeField] private TMP_Text menuMessage;

        public SettingsView SettingsView => settingsView;
        public DailyBonusView DailyBonusView => dailyBonusView;
        public bool HasOpenPopup => settingsPopup.activeSelf || dailyBonusPopup.activeSelf;
        public bool DailyAvailable { get; private set; }
        private Vector3 dailyButtonBaseScale;
        private bool dailyButtonScaleCaptured;
        private float dailyPulseTime;
        public string BalanceText => balanceLabel.text;
        public void ShowDailyAvailability(bool available)
        {
            DailyAvailable = available;
            if (dailyBonusButton && dailyBonusButton.image)
                dailyBonusButton.image.material = available ? dailyBonusAvailableMaterial : null;
            if (!available) ResetDailyPulse();
        }

        public void TickDailyAttention(float delta)
        {
            if (!dailyBonusButton) return;
            if (!dailyButtonScaleCaptured)
            {
                dailyButtonBaseScale = dailyBonusButton.transform.localScale;
                dailyButtonScaleCaptured = true;
            }
            if (!DailyAvailable || !dailyBonusButton.gameObject.activeInHierarchy ||
                !dailyBonusButton.IsInteractable())
            {
                ResetDailyPulse();
                return;
            }
            // One soft pop over 0.8 seconds, followed by a 1.6-second pause.
            dailyPulseTime = (dailyPulseTime + delta) % 2.4f;
            float pulse = dailyPulseTime < .8f
                ? .5f - .5f * Mathf.Cos(dailyPulseTime / .8f * Mathf.PI * 2f) : 0f;
            dailyBonusButton.transform.localScale = dailyButtonBaseScale * (1f + .05f * pulse);
        }

        private void ResetDailyPulse()
        {
            dailyPulseTime = 0f;
            if (dailyBonusButton && dailyButtonScaleCaptured)
                dailyBonusButton.transform.localScale = dailyButtonBaseScale;
        }

        private void OnDisable() => ResetDailyPulse();
        public void ShowBalance(long balance) => balanceLabel.text = $"Coins: {balance:N0}";
        public void ShowMenuMessage(string message) => menuMessage.text = message;

        // Startup can fail because a serialized reference is missing. Keep the remaining
        // controls safe without requiring the very references that failed validation.
        public void ShowInitializationFailure()
        {
            ShowDailyAvailability(false);
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
            balanceLabel && menuMessage;

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
