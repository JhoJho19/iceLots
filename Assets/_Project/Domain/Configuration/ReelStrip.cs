using System;
using System.Collections.Generic;
using System.Linq;

namespace IceP.Domain.Configuration
{
    public sealed class ReelStrip
    {
        public IReadOnlyList<SymbolId> Symbols { get; }

        public ReelStrip(IEnumerable<SymbolId> symbols)
        {
            Symbols = Array.AsReadOnly((symbols ?? Enumerable.Empty<SymbolId>()).ToArray());
        }
    }
}
