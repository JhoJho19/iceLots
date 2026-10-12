using System;
using System.Collections.Generic;
using System.Linq;
using IceP.Domain.Configuration;

namespace IceP.Domain.SlotEngine
{
    public readonly struct GridCoordinate
    {
        public int ReelIndex { get; }
        public int RowIndex { get; }
        internal GridCoordinate(int reelIndex, int rowIndex)
        {
            ReelIndex = reelIndex;
            RowIndex = rowIndex;
        }
    }

    public sealed class LineWin
    {
        /// <summary>Zero-based index in the configuration's payline collection.</summary>
        public int PaylineIndex { get; }
        public SymbolId Symbol { get; }
        public int MatchCount { get; }
        public long PayoutMultiplier { get; }
        public long Payout { get; }
        public IReadOnlyList<GridCoordinate> Coordinates { get; }

        internal LineWin(int paylineIndex, SymbolId symbol, int matchCount, long multiplier,
            long payout, IEnumerable<GridCoordinate> coordinates)
        {
            PaylineIndex = paylineIndex;
            Symbol = symbol;
            MatchCount = matchCount;
            PayoutMultiplier = multiplier;
            Payout = payout;
            Coordinates = Array.AsReadOnly(coordinates.ToArray());
        }
    }

    public sealed class WinEvaluation
    {
        public IReadOnlyList<LineWin> WinningLines { get; }
        /// <summary>Line payout before any Free Spins multiplier; no Scatter or jackpot coins.</summary>
        public long BaseWin { get; }
        internal WinEvaluation(IEnumerable<LineWin> lines, long baseWin)
        {
            WinningLines = Array.AsReadOnly(lines.ToArray());
            BaseWin = baseWin;
        }
    }
}
