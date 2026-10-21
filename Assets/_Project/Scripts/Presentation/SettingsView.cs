using TMPro;
using IceP.Application.Persistence;
using UnityEngine;
using UnityEngine.UI;

namespace IceP.Presentation
{
    public sealed class SettingsView : MonoBehaviour
    {
        [SerializeField] private Slider musicVolume;
        [SerializeField] private Slider soundVolume;
        [SerializeField] private TMP_Text musicPercent;
        [SerializeField] private TMP_Text soundPercent;
        [SerializeField] private TMP_Text message;
        public Slider MusicVolume => musicVolume;
        public Slider SoundVolume => soundVolume;
        public string Message => message.text;
        public bool HasRequiredReferences => musicVolume && soundVolume && musicPercent && soundPercent && message;
        public UserSettings Read() => new UserSettings { SettingsVersion = 1, MusicEnabled = musicVolume.value > 0,
            SoundEnabled = soundVolume.value > 0, MusicVolume = musicVolume.value, SoundVolume = soundVolume.value };
        public void Show(UserSettings settings)
        {
            // Older profiles may have muted audio while retaining a nonzero volume.
            musicVolume.SetValueWithoutNotify(settings.MusicEnabled ? (float)settings.MusicVolume : 0);
            soundVolume.SetValueWithoutNotify(settings.SoundEnabled ? (float)settings.SoundVolume : 0);
            ShowPercent();
        }
        public void ShowPercent()
        {
            musicPercent.text = $"{musicVolume.value:P0}";
            soundPercent.text = $"{soundVolume.value:P0}";
        }
        public void ShowAvailability(bool canEdit)
        {
            musicVolume.interactable = soundVolume.interactable = canEdit;
        }
        public void ShowMessage(string value) => message.text = value;
    }
}
