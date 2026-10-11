using System;
using IceP.Domain.Configuration;
using UnityEngine;

namespace IceP.Infrastructure.Configuration
{
    [Serializable]
    public sealed class SymbolDefinitionData
    {
        public SymbolId Id;
        public int RequiredCountPerReel;
    }

    [Serializable]
    public sealed class GridConfigData
    {
        public int ReelCount = 5;
        public int VisibleRows = 3;
        public SymbolId ScatterId = SymbolId.SCATTER;
        public SymbolDefinitionData[] Symbols = Array.Empty<SymbolDefinitionData>();
    }

    [Serializable]
    public sealed class ReelStripData
    {
        public SymbolId[] Symbols = Array.Empty<SymbolId>();
    }

    [Serializable]
    public sealed class PaylineData
    {
        public int[] Rows = Array.Empty<int>();
    }

    [Serializable]
    public sealed class SymbolPayoutData
    {
        public SymbolId Symbol;
        public long Match3;
        public long Match4;
        public long Match5;
    }

    [Serializable]
    public sealed class EconomyConfigData
    {
        public long StartingBalance = 20000;
        public long DefaultBet = 200;
        public long[] AvailableBets = { 200, 400, 600, 800, 1000 };
    }

    [Serializable]
    public sealed class ScatterAwardData
    {
        public int ScatterCount;
        public int FreeSpins;
    }

    [Serializable]
    public sealed class ScatterRulesData
    {
        public ScatterAwardData[] Awards = Array.Empty<ScatterAwardData>();
        public long FreeSpinsMultiplier = 1;
        public bool RetriggerEnabled = true;
    }

    [Serializable]
    public sealed class DailyBonusRulesData
    {
        public bool Enabled = true;
        public long[] Rewards = { 400, 600, 800, 1000, 1200, 1600, 3000 };
        [Tooltip("Seconds after 00:00 UTC, from 0 to 86399.")]
        public int ResetSecondsUtc;
        public StreakResetBehavior StreakResetBehavior = StreakResetBehavior.ResetAfterMissedDay;
    }

    [Serializable]
    public sealed class JackpotRulesData
    {
        public bool Enabled = true;
        public string EventId = "weekly-jackpot";
        public string ConfigVersion = "1.0.0";
        public long Amount = 100000;
        public double BaseProbability = 0.0001;
        public long ReferenceBet = 1000;
        public int MaxWinsPerWeek = 1;
        public DayOfWeek ResetWeekday = DayOfWeek.Monday;
        [Tooltip("Seconds after 00:00 UTC, from 0 to 86399.")]
        public int ResetSecondsUtc;
        [Tooltip("Optional ISO 8601 UTC timestamp, e.g. 2026-10-12T00:00:00Z. Empty means no start limit.")]
        public string StartUtc = "";
        [Tooltip("Optional ISO 8601 UTC timestamp. Empty means no end limit.")]
        public string EndUtc = "";
    }

    [Serializable]
    public sealed class DebugRandomSettingsData
    {
        public bool UseFixedSeed;
        public int Seed = 12345;
    }

    [CreateAssetMenu(fileName = "SlotGameConfig", menuName = "IceP/Slot Game Config")]
    public sealed class SlotGameConfigSO : ScriptableObject
    {
        [Header("Grid and symbols")]
        public GridConfigData Grid = new GridConfigData();
        [Header("Explicit reel sequences (no runtime generation)")]
        public ReelStripData[] ReelStrips = Array.Empty<ReelStripData>();
        [Header("Paylines")]
        public PaylineData[] Paylines = Array.Empty<PaylineData>();
        [Header("Paytable: multipliers of the per-line bet")]
        public SymbolPayoutData[] Paytable = Array.Empty<SymbolPayoutData>();
        [Header("Bets and economy")]
        public EconomyConfigData Economy = new EconomyConfigData();
        [Header("Scatter and Free Spins")]
        public ScatterRulesData Scatter = new ScatterRulesData();
        [Header("Daily Bonus")]
        public DailyBonusRulesData DailyBonus = new DailyBonusRulesData();
        [Header("Jackpot of the Week")]
        public JackpotRulesData Jackpot = new JackpotRulesData();
        [Header("Runtime configuration version")]
        public string ConfigVersion = "1.0.0";
        [Header("Debug random settings (no RNG implementation at this stage)")]
        public DebugRandomSettingsData DebugRandom = new DebugRandomSettingsData();
    }
}
