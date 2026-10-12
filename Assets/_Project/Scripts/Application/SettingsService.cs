using System;
using System.Threading;
using IceP.Application.Persistence;
using IceP.Domain.Configuration;

namespace IceP.Application
{
    public enum SettingsError { None, Busy, InvalidValue, PersistenceFailure, InvalidState }

    public sealed class SettingsResult
    {
        public SettingsError Error { get; }
        public bool Success => Error == SettingsError.None;
        public string Diagnostic { get; }
        internal SettingsResult(SettingsError error = SettingsError.None, string diagnostic = null)
        { Error = error; Diagnostic = diagnostic; }
    }

    // Shares the command gate and whole profile with Spin/Daily Bonus. No second writer/store.
    public sealed class SettingsService
    {
        private readonly GameSession session;
        private readonly ISaveRepository repository;
        private readonly SaveDataValidator validator;
        private SaveData proposed;
        public UserSettings Current => session.Snapshot.Settings;
        public bool CanUpdate { get { lock (session.Gate) return session.CanStartSpin && !session.CommandExecuting; } }
        public bool CanRetry { get { lock (session.Gate) return proposed != null && session.State == GameSessionState.Error && !session.CommandExecuting; } }

        public SettingsService(GameSession session, SlotRuntimeConfig config, ISaveRepository repository)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
            validator = new SaveDataValidator(config);
            lock (session.Gate)
            {
                if (validator.Validate(session.Snapshot).Count != 0) throw new ArgumentException("Invalid initial profile.");
                if (session.HasSettingsOwner) throw new InvalidOperationException("Session already has SettingsService.");
                session.HasSettingsOwner = true;
            }
        }

        public SettingsResult Update(UserSettings settings) => Execute(() =>
        {
            if (!session.CanStartSpin || proposed != null) return new SettingsResult(SettingsError.Busy);
            var next = session.Snapshot; // Always copy the newest confirmed profile under the shared gate.
            next.Settings = settings?.Copy();
            var errors = validator.Validate(next);
            if (errors.Count != 0) return new SettingsResult(SettingsError.InvalidValue, string.Join("\n", errors));
            var old = session.Snapshot.Settings;
            if (old.MusicEnabled == next.Settings.MusicEnabled && old.SoundEnabled == next.Settings.SoundEnabled &&
                old.MusicVolume == next.Settings.MusicVolume && old.SoundVolume == next.Settings.SoundVolume)
                return new SettingsResult();
            proposed = next;
            session.Transition(GameSessionState.Preparing);
            return Persist();
        });

        public SettingsResult RetryPersistence() => Execute(() =>
        {
            if (proposed == null || session.State != GameSessionState.Error) return new SettingsResult(SettingsError.InvalidState);
            session.Transition(GameSessionState.Preparing);
            return Persist();
        });

        private SettingsResult Persist()
        {
            try
            {
                var write = repository.Save(proposed.Copy());
                if (write == null || !write.Success) return Block(write?.Diagnostic ?? "No write confirmation.");
            }
            catch (Exception e) { return Block(e.Message); }
            session.Commit(proposed);
            proposed = null;
            session.Transition(GameSessionState.Idle);
            return new SettingsResult();
        }
        private SettingsResult Block(string diagnostic)
        {
            session.Transition(GameSessionState.Error);
            return new SettingsResult(SettingsError.PersistenceFailure, diagnostic);
        }
        private SettingsResult Execute(Func<SettingsResult> command)
        {
            if (!Monitor.TryEnter(session.Gate)) return new SettingsResult(SettingsError.Busy);
            try
            {
                if (session.CommandExecuting) return new SettingsResult(SettingsError.Busy);
                session.CommandExecuting = true;
                try { return command(); }
                finally { session.CommandExecuting = false; }
            }
            finally { Monitor.Exit(session.Gate); }
        }
    }
}
