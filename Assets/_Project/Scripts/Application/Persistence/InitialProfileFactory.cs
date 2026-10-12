using IceP.Domain.Configuration;

namespace IceP.Application.Persistence
{
    public static class InitialProfileFactory
    {
        public static SaveData Create(SlotRuntimeConfig config)
        {
            ConfigValidator.ValidateAndThrow(config);
            return new SaveData
            {
                SaveVersion = SaveData.CurrentVersion,
                Balance = config.Economy.StartingBalance,
                SelectedBet = config.Economy.DefaultBet,
                Settings = new UserSettings
                {
                    SettingsVersion = 1, MusicEnabled = true, SoundEnabled = true,
                    MusicVolume = 1, SoundVolume = 1
                }
            };
        }
    }
}
