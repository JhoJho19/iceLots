namespace IceP.Domain.SlotEngine
{
    public interface IRandomSource
    {
        /// <summary>Returns a uniformly distributed integer in [minInclusive, maxExclusive).
        /// The range must be nonempty.</summary>
        int NextInt(int minInclusive, int maxExclusive);
    }
}
