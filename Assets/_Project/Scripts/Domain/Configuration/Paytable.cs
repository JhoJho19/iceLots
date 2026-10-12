using System;
using System.Collections.Generic;
using System.Linq;

namespace IceP.Domain.Configuration
{
    public readonly struct SymbolPayout
    {
        public SymbolId Symbol { get; }
        public long Match3 { get; }
        public long Match4 { get; }
        public long Match5 { get; }

        public SymbolPayout(SymbolId symbol, long match3, long match4, long match5)
        {
            Symbol = symbol;
            Match3 = match3;
            Match4 = match4;
            Match5 = match5;
        }
    }

    public sealed class Paytable
    {
        public IReadOnlyList<SymbolPayout> Entries { get; }

        public Paytable(IEnumerable<SymbolPayout> entries)
        {
            Entries = Array.AsReadOnly((entries ?? Enumerable.Empty<SymbolPayout>()).ToArray());
        }
    }
}
