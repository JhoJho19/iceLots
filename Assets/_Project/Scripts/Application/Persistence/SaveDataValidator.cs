using System;
using System.Collections.Generic;
using System.Linq;
using IceP.Domain.Configuration;

namespace IceP.Application.Persistence
{
    public sealed class SaveDataValidator
    {
        private readonly SlotRuntimeConfig config;
        public SaveDataValidator(SlotRuntimeConfig config)
        {
            ConfigValidator.ValidateAndThrow(config);
            this.config = config;
        }

        public IReadOnlyList<string> Validate(SaveData data)
        {
            var errors = new List<string>();
            if (data == null) { errors.Add("SaveData is required."); return errors; }
            void Check(bool valid, string message) { if (!valid) errors.Add(message); }
            bool Bet(long value) => config.Economy.AvailableBets.Contains(value);
            bool Utc(DateTimeOffset value) => value.Offset == TimeSpan.Zero && value != DateTimeOffset.MinValue;
            bool Week(DateTimeOffset value) => Utc(value) && value.DayOfWeek == config.Jackpot.ResetWeekday &&
                value.TimeOfDay == config.Jackpot.ResetTimeUtc;
            Check(data.SaveVersion == SaveData.CurrentVersion, $"Unsupported SaveVersion {data.SaveVersion}.");
            Check(data.Balance >= 0, "Balance must be non-negative.");
            Check(Bet(data.SelectedBet), "SelectedBet is incompatible with current AvailableBets.");
            Check(data.FreeSpinsRemaining >= 0, "FreeSpinsRemaining must be non-negative.");
            var freePending = data.PendingSpin?.Type == PendingSpinType.Free;
            Check(data.BonusBet == 0 || Bet(data.BonusBet), "BonusBet is incompatible with current AvailableBets.");
            Check((data.FreeSpinsRemaining > 0 || freePending) == (data.BonusBet > 0),
                "BonusBet must exist exactly while a free-spin queue or pending Free Spin exists.");
            Check(data.DailyStreakDay >= 0 && data.DailyStreakDay <= config.DailyBonus.Rewards.Count,
                "DailyStreakDay is outside the configured reward cycle.");
            Check(data.DailyLastClaimUtc.HasValue == (data.DailyStreakDay > 0),
                "DailyLastClaimUtc and DailyStreakDay must describe the same claim state.");
            Check(!data.DailyLastClaimUtc.HasValue || Utc(data.DailyLastClaimUtc.Value), "DailyLastClaimUtc must be UTC.");
            bool historicEvent = data.JackpotEventId != null;
            Check(!historicEvent || !string.IsNullOrWhiteSpace(data.JackpotEventId), "JackpotEventId cannot be empty.");
            Check(!historicEvent || data.JackpotWeekId.HasValue, "JackpotEventId requires a week.");
            Check(!data.JackpotWeekId.HasValue || (historicEvent ? Utc(data.JackpotWeekId.Value) : Week(data.JackpotWeekId.Value)), "JackpotWeekId must be a configured UTC week boundary or identified historic UTC boundary.");
            Check(data.JackpotWinsThisWeek >= 0 && (historicEvent || data.JackpotWinsThisWeek <= config.Jackpot.MaxWinsPerWeek),
                "JackpotWinsThisWeek exceeds the configured weekly limit.");
            Check(data.JackpotWeekId.HasValue || data.JackpotWinsThisWeek == 0, "Jackpot wins require JackpotWeekId.");
            Check(!data.LastSettledSpinId.HasValue || data.LastSettledSpinId != Guid.Empty, "LastSettledSpinId must be non-empty.");
            Check(data.LastWin >= 0, "LastWin must be non-negative.");
            if (data.PendingSpin != null)
            {
                var pending = data.PendingSpin;
                Check(pending.SpinId != Guid.Empty, "PendingSpin.SpinId must be non-empty.");
                Check(data.LastSettledSpinId != pending.SpinId, "PendingSpin cannot already be settled.");
                Check(Enum.IsDefined(typeof(PendingSpinType), pending.Type), "PendingSpin.Type is invalid.");
                Check(Bet(pending.EffectiveBet), "PendingSpin.EffectiveBet is incompatible with current AvailableBets.");
                Check(!string.IsNullOrWhiteSpace(pending.ConfigVersion), "PendingSpin.ConfigVersion is required.");
                Check(Utc(pending.AcceptedUtc), "PendingSpin.AcceptedUtc must be a valid UTC timestamp.");
                Check(pending.JackpotEventId == null || !string.IsNullOrWhiteSpace(pending.JackpotEventId), "PendingSpin.JackpotEventId cannot be empty.");
                Check(pending.JackpotEventId == null || pending.JackpotWeekId.HasValue, "PendingSpin.JackpotEventId requires a week.");
                Check(!pending.JackpotWeekId.HasValue || (pending.JackpotEventId == null ?
                    Week(pending.JackpotWeekId.Value) : Utc(pending.JackpotWeekId.Value)), "PendingSpin.JackpotWeekId is invalid.");
                Check(!pending.JackpotWeekId.HasValue || (pending.AcceptedUtc >= pending.JackpotWeekId.Value &&
                    pending.AcceptedUtc - pending.JackpotWeekId.Value < TimeSpan.FromDays(7)),
                    "PendingSpin.JackpotWeekId must contain AcceptedUtc.");
                Check(pending.Type != PendingSpinType.Free ||
                    (pending.EffectiveBet == data.BonusBet && !pending.JackpotWeekId.HasValue),
                    "Pending Free Spin must use BonusBet and cannot participate in jackpot.");
                Check(pending.Type != PendingSpinType.Paid || (data.FreeSpinsRemaining == 0 && data.BonusBet == 0),
                    "Pending paid Spin cannot coexist with a free-spin queue.");
            }
            Check(data.Settings != null, "Settings is required.");
            if (data.Settings != null)
            {
                Check(data.Settings.SettingsVersion == 1, "Unsupported SettingsVersion.");
                Check(Volume(data.Settings.MusicVolume), "Settings.MusicVolume must be finite and within [0,1].");
                Check(Volume(data.Settings.SoundVolume), "Settings.SoundVolume must be finite and within [0,1].");
            }
            return errors;
        }

        private static bool Volume(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= 1;
    }
}
