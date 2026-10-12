using System;

namespace IceP.Application.Persistence
{
    public enum PendingSpinType { Paid = 0, Free = 1 }

    // Mutable transfer objects. Repository boundaries always deep-copy them.
    [Serializable]
    public sealed class PendingSpin
    {
        public Guid SpinId { get; set; }
        public PendingSpinType Type { get; set; }
        public long EffectiveBet { get; set; }
        public string ConfigVersion { get; set; }
        public DateTimeOffset? JackpotWeekId { get; set; }
        public string JackpotEventId { get; set; }
        public DateTimeOffset AcceptedUtc { get; set; }
        public PendingSpin Copy() => (PendingSpin)MemberwiseClone();
    }

    [Serializable]
    public sealed class UserSettings
    {
        public int SettingsVersion { get; set; }
        public bool MusicEnabled { get; set; }
        public bool SoundEnabled { get; set; }
        public double MusicVolume { get; set; }
        public double SoundVolume { get; set; }
        public UserSettings Copy() => (UserSettings)MemberwiseClone();
    }

    [Serializable]
    public sealed class SaveData
    {
        public const int CurrentVersion = 1;
        public int SaveVersion { get; set; }
        public long Balance { get; set; }
        public long SelectedBet { get; set; }
        public int FreeSpinsRemaining { get; set; }
        public long BonusBet { get; set; }
        public DateTimeOffset? DailyLastClaimUtc { get; set; }
        public int DailyStreakDay { get; set; }
        // Week identity is the UTC instant of its configured reset boundary.
        public DateTimeOffset? JackpotWeekId { get; set; }
        public int JackpotWinsThisWeek { get; set; }
        // Optional additive v1 field: absent legacy saves retain their allowance.
        public string JackpotEventId { get; set; }
        public Guid? LastSettledSpinId { get; set; }
        public long LastWin { get; set; }
        public PendingSpin PendingSpin { get; set; }
        public UserSettings Settings { get; set; }

        public SaveData Copy()
        {
            var copy = (SaveData)MemberwiseClone();
            copy.PendingSpin = PendingSpin?.Copy();
            copy.Settings = Settings?.Copy();
            return copy;
        }
    }
}
