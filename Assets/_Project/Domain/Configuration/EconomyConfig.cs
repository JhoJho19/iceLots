using System;
using System.Collections.Generic;
using System.Linq;

namespace IceP.Domain.Configuration
{
    public sealed class EconomyConfig
    {
        public long StartingBalance { get; }
        public long DefaultBet { get; }
        public IReadOnlyList<long> AvailableBets { get; }

        public EconomyConfig(long startingBalance, long defaultBet, IEnumerable<long> availableBets)
        {
            StartingBalance = startingBalance;
            DefaultBet = defaultBet;
            AvailableBets = Array.AsReadOnly((availableBets ?? Enumerable.Empty<long>()).ToArray());
        }
    }
}
