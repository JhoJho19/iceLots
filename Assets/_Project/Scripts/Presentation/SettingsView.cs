using TMPro;
using IceP.Application.Persistence;
using UnityEngine;
using UnityEngine.UI;

namespace IceP.Presentation
{
    public sealed class SettingsView : MonoBehaviour
    {
        [SerializeField] private Toggle musicEnabled;
        [SerializeField] private Toggle soundEnabled;
        [SerializeField] private Slider musicVolume;
        [SerializeField] private Slider soundVolume;
        [SerializeField] private TMP_Text musicPercent;
        [SerializeField] private TMP_Text soundPercent;
        [SerializeField] private TMP_Text message;
        [SerializeField] private Button retryButton;
        public Toggle MusicEnabled => musicEnabled;
        public Toggle SoundEnabled => soundEnabled;
        public Slider MusicVolume => musicVolume;
        public Slider SoundVolume => soundVolume;
        public Button RetryButton => retryButton;
        public string Message => message.text;
        public bool HasRequiredReferences => musicEnabled && soundEnabled && musicVolume && soundVolume && musicPercent && soundPercent && message && retryButton;
        public UserSettings Read() => new UserSettings { SettingsVersion = 1, MusicEnabled = musicEnabled.isOn,
            SoundEnabled = soundEnabled.isOn, MusicVolume = musicVolume.value, SoundVolume = soundVolume.value };
        public void Show(UserSettings settings)
        {
            musicEnabled.SetIsOnWithoutNotify(settings.MusicEnabled);
            soundEnabled.SetIsOnWithoutNotify(settings.SoundEnabled);
            musicVolume.SetValueWithoutNotify((float)settings.MusicVolume);
            soundVolume.SetValueWithoutNotify((float)settings.SoundVolume);
            ShowPercent();
        }
        public void ShowPercent()
        {
            musicPercent.text = $"{musicVolume.value:P0}";
            soundPercent.text = $"{soundVolume.value:P0}";
        }
        public void ShowAvailability(bool canEdit, bool canRetry)
        {
            musicEnabled.interactable = soundEnabled.interactable = musicVolume.interactable = soundVolume.interactable = canEdit;
            retryButton.gameObject.SetActive(canRetry);
            retryButton.interactable = canRetry;
        }
        public void ShowMessage(string value) => message.text = value;
    }
}
