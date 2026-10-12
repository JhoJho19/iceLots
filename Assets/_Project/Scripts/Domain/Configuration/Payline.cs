using System;
using System.Collections.Generic;
using System.Linq;

namespace IceP.Domain.Configuration
{
    public sealed class Payline
    {
        // One top-to-bottom row index per reel, in left-to-right reel order.
        public IReadOnlyList<int> Rows { get; }

        public Payline(IEnumerable<int> rows)
        {
            Rows = Array.AsReadOnly((rows ?? Enumerable.Empty<int>()).ToArray());
        }
    }
}
