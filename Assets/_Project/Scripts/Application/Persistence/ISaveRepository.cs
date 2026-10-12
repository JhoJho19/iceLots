namespace IceP.Application.Persistence
{
    public enum SaveLoadStatus { Missing, Loaded, RecoveredBackup, Failed }
    public enum PersistenceError { None, InvalidData, UnsupportedVersion, RecoveryRequired, IoFailure }

    public sealed class SaveLoadResult
    {
        private readonly SaveData snapshot;
        public SaveLoadStatus Status { get; }
        public PersistenceError Error { get; }
        public string Diagnostic { get; }
        public SaveData Data => snapshot?.Copy();
        public SaveLoadResult(SaveLoadStatus status, SaveData data = null,
            PersistenceError error = PersistenceError.None, string diagnostic = null)
        {
            Status = status;
            snapshot = data?.Copy();
            Error = error;
            Diagnostic = diagnostic;
        }
    }

    public sealed class SaveWriteResult
    {
        public bool Success => Error == PersistenceError.None;
        public PersistenceError Error { get; }
        public string Diagnostic { get; }
        public SaveWriteResult(PersistenceError error = PersistenceError.None, string diagnostic = null)
        { Error = error; Diagnostic = diagnostic; }
    }

    // Synchronous, single writer per directory. Save is a whole-state commit.
    // Input must not be mutated concurrently; implementations retain no caller-owned references.
    // Load/Data returns detached deep copies. Failure never grants permission to use unverified state.
    public interface ISaveRepository
    {
        SaveLoadResult Load();
        SaveWriteResult Save(SaveData snapshot);
        // Destructive profile reset: call only following an explicit application reset decision.
        SaveWriteResult Reset(SaveData initialProfile);
    }
}
