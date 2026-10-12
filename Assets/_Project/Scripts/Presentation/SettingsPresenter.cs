using System;
using IceP.Application;
using IceP.Application.Persistence;

namespace IceP.Presentation
{
    public sealed class SettingsPresenter : IDisposable
    {
        private readonly SettingsService service;
        private readonly SettingsView view;
        private bool dirty;
        private float quietTime;
        private UserSettings pending;
        public SettingsPresenter(SettingsService service, SettingsView view)
        {
            this.service = service; this.view = view;
            view.MusicEnabled.onValueChanged.AddListener(ToggleChanged);
            view.SoundEnabled.onValueChanged.AddListener(ToggleChanged);
            view.MusicVolume.onValueChanged.AddListener(VolumeChanged);
            view.SoundVolume.onValueChanged.AddListener(VolumeChanged);
            view.RetryButton.onClick.AddListener(Retry);
        }
        public void Open()
        {
            dirty = false;
            view.Show(service.Current);
            view.ShowMessage(service.CanRetry ? "Save not confirmed. Retry to finish." : service.CanUpdate ? "" : "Session blocked.");
            Refresh();
        }
        private void ToggleChanged(bool _) { pending = view.Read(); dirty = true; Commit(); }
        private void VolumeChanged(float _) { pending = view.Read(); dirty = true; quietTime = 0; view.ShowPercent(); }
        // Debounce keyboard/drag changes; never write every frame. Close flushes the last value.
        public void Tick(float delta)
        {
            if (dirty) { quietTime += delta; if (quietTime >= .4f) Commit(); }
            Refresh();
        }
        public void Close() { if (dirty) Commit(); }
        private void Commit()
        {
            dirty = false;
            var result = service.Update(pending);
            view.Show(service.Current); // Failed or ambiguous proposals are never displayed as confirmed.
            view.ShowMessage(result.Success ? "Saved" : result.Error == SettingsError.PersistenceFailure ?
                "Save not confirmed. Retry to finish." : "Settings unavailable: " + result.Error);
            Refresh();
        }
        private void Retry()
        {
            var result = service.RetryPersistence();
            view.Show(service.Current);
            view.ShowMessage(result.Success ? "Saved" : "Save not confirmed. Retry to finish.");
            Refresh();
        }
        public void Refresh() => view.ShowAvailability(service.CanUpdate, service.CanRetry);
        public void Dispose()
        {
            // The Canvas may already be destroyed during scene unload. Flush the captured intent
            // through Application without touching destroyed view objects or reading stale economy.
            if (dirty) { dirty = false; service.Update(pending); }
            view.MusicEnabled.onValueChanged.RemoveListener(ToggleChanged);
            view.SoundEnabled.onValueChanged.RemoveListener(ToggleChanged);
            view.MusicVolume.onValueChanged.RemoveListener(VolumeChanged);
            view.SoundVolume.onValueChanged.RemoveListener(VolumeChanged);
            view.RetryButton.onClick.RemoveListener(Retry);
        }
    }
}
