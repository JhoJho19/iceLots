using IceP.Application.Persistence;

namespace IceP.Tests
{
    // Deterministic future transaction-test double; no filesystem or Unity dependencies.
    public sealed class FakeSaveRepository : ISaveRepository
    {
        private SaveData committed;
        public int SaveAttemptCount { get; private set; }
        public int? FailOnSaveAttempt { get; set; }
        public bool FailLoads { get; set; }
        public bool FailSaves { get; set; }
        // Simulate an ambiguous result: storage committed but caller received an error.
        public bool CommitThenFail { get; set; }
        public System.Action<SaveData> BeforeSave { get; set; }
        public FakeSaveRepository(SaveData initial = null) { committed = initial?.Copy(); }
        public SaveLoadResult Load()
        {
            if (FailLoads) return new SaveLoadResult(SaveLoadStatus.Failed, error: PersistenceError.IoFailure, diagnostic: "Simulated load failure.");
            return committed == null ? new SaveLoadResult(SaveLoadStatus.Missing) : new SaveLoadResult(SaveLoadStatus.Loaded, committed);
        }
        public SaveWriteResult Save(SaveData snapshot)
        {
            SaveAttemptCount++;
            BeforeSave?.Invoke(snapshot.Copy());
            if (CommitThenFail)
            {
                committed = snapshot.Copy();
                return new SaveWriteResult(PersistenceError.IoFailure, "Simulated uncertain commit.");
            }
            if (FailSaves || FailOnSaveAttempt == SaveAttemptCount)
                return new SaveWriteResult(PersistenceError.IoFailure, "Simulated save failure.");
            if (snapshot == null) return new SaveWriteResult(PersistenceError.InvalidData, "Snapshot is required.");
            committed = snapshot.Copy();
            return new SaveWriteResult();
        }
        public SaveWriteResult Reset(SaveData initialProfile) => Save(initialProfile);
    }
}
