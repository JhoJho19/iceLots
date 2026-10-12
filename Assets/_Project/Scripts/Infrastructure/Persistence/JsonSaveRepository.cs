using System;
using System.IO;
using System.Text;
using IceP.Application.Persistence;
using IceP.Domain.Configuration;
using Newtonsoft.Json;

namespace IceP.Infrastructure.Persistence
{
    internal enum SaveWriteStage { BeforeTempWrite, TempWritten, BackupPrepared, PrimaryRemoved, BeforePromotion }

    // One instance/writer per directory. No file work occurs in Domain, Application or Presentation.
    public sealed class JsonSaveRepository : ISaveRepository
    {
        private readonly string directory;
        private readonly SaveJsonCodec codec;
        private readonly Action<SaveWriteStage> checkpoint;
        private string Primary => Path.Combine(directory, "save.json");
        private string Backup => Primary + ".bak";
        private string Temporary => Primary + ".tmp";
        private string BackupTemporary => Backup + ".tmp";

        public JsonSaveRepository(string storageDirectory, SlotRuntimeConfig config) : this(storageDirectory, config, null) { }
        internal JsonSaveRepository(string storageDirectory, SlotRuntimeConfig config, Action<SaveWriteStage> checkpoint)
        {
            directory = Path.GetFullPath(storageDirectory ?? throw new ArgumentNullException(nameof(storageDirectory)));
            codec = new SaveJsonCodec(new SaveDataValidator(config));
            this.checkpoint = checkpoint;
        }

        public SaveLoadResult Load()
        {
            try
            {
                var primary = Read(Primary);
                if (primary.Status == SaveLoadStatus.Loaded || primary.Error == PersistenceError.UnsupportedVersion ||
                    primary.Error == PersistenceError.IoFailure) return primary;
                var backup = Read(Backup);
                if (backup.Status == SaveLoadStatus.Loaded)
                    return new SaveLoadResult(SaveLoadStatus.RecoveredBackup, backup.Data, diagnostic:
                        "Recovered save.json.bak; an earlier economic snapshot may have been restored. " + primary.Diagnostic);
                if (backup.Error == PersistenceError.UnsupportedVersion || backup.Error == PersistenceError.IoFailure) return backup;
                if (primary.Status == SaveLoadStatus.Missing && backup.Status == SaveLoadStatus.Missing)
                {
                    // A temp-only directory is an interrupted creation, not a normal first launch.
                    if (!File.Exists(Temporary) && !File.Exists(BackupTemporary)) return new SaveLoadResult(SaveLoadStatus.Missing);
                }
                return new SaveLoadResult(SaveLoadStatus.Failed, error: PersistenceError.RecoveryRequired,
                    diagnostic: "No valid committed snapshot. Explicit profile reset is required. " + primary.Diagnostic + " " + backup.Diagnostic);
            }
            catch (Exception exception) when (IsStorageException(exception)) { return LoadFailure(exception); }
        }

        public SaveWriteResult Save(SaveData snapshot)
        {
            try
            {
                var json = codec.Encode(snapshot?.Copy());
                // Reject incompatible files even if another valid snapshot exists.
                var primary = Read(Primary);
                var backup = Read(Backup);
                foreach (var existing in new[] { primary, backup })
                    if (existing.Error == PersistenceError.UnsupportedVersion || existing.Error == PersistenceError.IoFailure)
                        return new SaveWriteResult(existing.Error, existing.Diagnostic);
                if (primary.Status != SaveLoadStatus.Loaded && backup.Status != SaveLoadStatus.Loaded &&
                    (primary.Status != SaveLoadStatus.Missing || backup.Status != SaveLoadStatus.Missing ||
                     File.Exists(Temporary) || File.Exists(BackupTemporary)))
                    return new SaveWriteResult(PersistenceError.RecoveryRequired, "Existing invalid save requires explicit Reset.");
                Directory.CreateDirectory(directory);
                checkpoint?.Invoke(SaveWriteStage.BeforeTempWrite);
                WriteFlushed(Temporary, json);
                codec.Decode(File.ReadAllText(Temporary));
                checkpoint?.Invoke(SaveWriteStage.TempWritten);
                if (primary.Status == SaveLoadStatus.Loaded)
                {
                    // Stage a verified backup while the current primary is still intact.
                    WriteFlushed(BackupTemporary, codec.Encode(primary.Data));
                    codec.Decode(File.ReadAllText(BackupTemporary));
                    if (File.Exists(Backup)) File.Delete(Backup);
                    File.Move(BackupTemporary, Backup);
                }
                checkpoint?.Invoke(SaveWriteStage.BackupPrepared);
                // Portable rename strategy. During this short gap the previous commit lives in backup.
                if (File.Exists(Primary)) File.Delete(Primary);
                checkpoint?.Invoke(SaveWriteStage.PrimaryRemoved);
                checkpoint?.Invoke(SaveWriteStage.BeforePromotion);
                File.Move(Temporary, Primary);
                // No fallible work after commit: success refers to the promoted snapshot.
                return new SaveWriteResult();
            }
            catch (SaveFormatException exception) { return new SaveWriteResult(exception.Error, exception.Message); }
            catch (Exception exception) when (IsStorageException(exception))
            { return new SaveWriteResult(PersistenceError.IoFailure, "Save failed before promotion: " + exception.Message); }
            finally { Cleanup(Temporary); Cleanup(BackupTemporary); }
        }

        public SaveWriteResult Reset(SaveData initialProfile)
        {
            try
            {
                codec.Encode(initialProfile?.Copy());
                // Preserve evidence before explicitly discarding the active profile.
                var archive = Path.Combine(directory, "reset-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(archive);
                foreach (var file in new[] { Primary, Backup, Temporary, BackupTemporary })
                    if (File.Exists(file)) File.Copy(file, Path.Combine(archive, Path.GetFileName(file)));
                foreach (var file in new[] { Primary, Backup, Temporary, BackupTemporary })
                    if (File.Exists(file)) File.Delete(file);
                return Save(initialProfile);
            }
            catch (SaveFormatException exception) { return new SaveWriteResult(exception.Error, exception.Message); }
            catch (Exception exception) when (IsStorageException(exception))
            { return new SaveWriteResult(PersistenceError.IoFailure, "Explicit reset failed; inspect reset archive: " + exception.Message); }
        }

        private SaveLoadResult Read(string path)
        {
            try
            {
                // Read rather than File.Exists: permission errors must not masquerade as first launch.
                var json = File.ReadAllText(path);
                return new SaveLoadResult(SaveLoadStatus.Loaded, codec.Decode(json));
            }
            catch (FileNotFoundException) { return new SaveLoadResult(SaveLoadStatus.Missing); }
            catch (DirectoryNotFoundException) { return new SaveLoadResult(SaveLoadStatus.Missing); }
            catch (SaveFormatException exception)
            { return new SaveLoadResult(SaveLoadStatus.Failed, error: exception.Error, diagnostic: Path.GetFileName(path) + ": " + exception.Message); }
            catch (JsonException exception)
            { return new SaveLoadResult(SaveLoadStatus.Failed, error: PersistenceError.InvalidData, diagnostic: Path.GetFileName(path) + ": " + exception.Message); }
            catch (Exception exception) when (exception is OverflowException || exception is FormatException || exception is ArgumentException)
            { return new SaveLoadResult(SaveLoadStatus.Failed, error: PersistenceError.InvalidData, diagnostic: Path.GetFileName(path) + ": " + exception.Message); }
            catch (Exception exception) when (IsStorageException(exception)) { return LoadFailure(exception); }
        }

        private static void WriteFlushed(string path, string json)
        {
            var bytes = new UTF8Encoding(false, true).GetBytes(json);
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }
        private static bool IsStorageException(Exception e) => e is IOException || e is UnauthorizedAccessException ||
            e is System.Security.SecurityException || e is NotSupportedException;
        private static SaveLoadResult LoadFailure(Exception e) => new SaveLoadResult(SaveLoadStatus.Failed,
            error: PersistenceError.IoFailure, diagnostic: "Save I/O failed: " + e.Message);
        private static void Cleanup(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) when (IsStorageException(e)) { /* Uncommitted temp files are ignored on load. */ }
        }
    }
}
