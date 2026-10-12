using System;

namespace IceP.Domain.SlotEngine
{
    /// <summary>A stop at the top visible symbol of a reel. Reels run left to right, 0..4.</summary>
    public readonly struct ReelStop
    {
        public int ReelIndex { get; }
        public int Position { get; }

        public ReelStop(int reelIndex, int position)
        {
            if (reelIndex < 0 || reelIndex >= SlotGrid.ReelCount)
                throw new ArgumentOutOfRangeException(nameof(reelIndex));
            if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
            ReelIndex = reelIndex;
            Position = position;
        }
    }
}
