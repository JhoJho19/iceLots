using System;
using System.Collections.Generic;
using System.Linq;

namespace IceP.Domain.SlotEngine
{
    public sealed class SpinOutcome
    {
        public SlotGrid Grid { get; }
        /// <summary>Exactly five stops ordered by reel index, 0..4.</summary>
        public IReadOnlyList<ReelStop> Stops { get; }

        public SpinOutcome(SlotGrid grid, IEnumerable<ReelStop> stops)
        {
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            if (stops == null) throw new ArgumentNullException(nameof(stops));
            var snapshot = stops.ToArray();
            if (snapshot.Length != SlotGrid.ReelCount)
                throw new ArgumentException("Exactly five stops are required.", nameof(stops));
            for (var reel = 0; reel < snapshot.Length; reel++)
                if (snapshot[reel].ReelIndex != reel)
                    throw new ArgumentException("Stops must be ordered by reel index 0..4.", nameof(stops));
            Stops = Array.AsReadOnly(snapshot);
        }
    }
}
