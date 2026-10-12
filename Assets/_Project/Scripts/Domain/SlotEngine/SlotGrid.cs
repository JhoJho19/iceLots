using System;
using IceP.Domain.Configuration;

namespace IceP.Domain.SlotEngine
{
    /// <summary>Immutable 5 x 3 grid. Coordinates are [reel, row]: reels 0..4 left to right,
    /// rows 0..2 top to bottom.</summary>
    public sealed class SlotGrid
    {
        public const int ReelCount = 5;
        public const int RowCount = 3;
        private readonly SymbolId[,] symbols;

        public SlotGrid(SymbolId[,] symbols)
        {
            if (symbols == null) throw new ArgumentNullException(nameof(symbols));
            if (symbols.GetLength(0) != ReelCount || symbols.GetLength(1) != RowCount)
                throw new ArgumentException("Expected dimensions [5, 3] (reel, row).", nameof(symbols));
            foreach (var symbol in symbols)
                if (!Enum.IsDefined(typeof(SymbolId), symbol))
                    throw new ArgumentException("Grid contains an unknown symbol.", nameof(symbols));
            this.symbols = (SymbolId[,])symbols.Clone();
        }

        public SymbolId this[int reelIndex, int rowIndex]
        {
            get
            {
                if (reelIndex < 0 || reelIndex >= ReelCount)
                    throw new ArgumentOutOfRangeException(nameof(reelIndex));
                if (rowIndex < 0 || rowIndex >= RowCount)
                    throw new ArgumentOutOfRangeException(nameof(rowIndex));
                return symbols[reelIndex, rowIndex];
            }
        }
    }
}
