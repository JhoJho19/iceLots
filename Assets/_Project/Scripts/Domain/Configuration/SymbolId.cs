namespace IceP.Domain.Configuration
{
    public enum SymbolId { S1 = 0, S2 = 1, S3 = 2, S4 = 3, S5 = 4, S6 = 5, S7 = 6, SCATTER = 7 }

    public readonly struct SymbolDefinition
    {
        public SymbolId Id { get; }
        public int RequiredCountPerReel { get; }

        public SymbolDefinition(SymbolId id, int requiredCountPerReel)
        {
            Id = id;
            RequiredCountPerReel = requiredCountPerReel;
        }
    }
}
