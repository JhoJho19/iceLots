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
            view.MusicVolume.onValueChanged.AddListener(VolumeChanged);
            view.SoundVolume.onValueChanged.AddListener(VolumeChanged);
        }
        public void Open()
        {
            dirty = false;
            view.Show(service.Current);
            view.ShowMessage(service.CanRetry ? "Save not confirmed." : service.CanUpdate ? "" : "Session blocked.");
            Refresh();
        }
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
                "Save not confirmed." : "Settings unavailable: " + result.Error);
            Refresh();
        }
        public void Refresh() => view.ShowAvailability(service.CanUpdate);
        public void Dispose()
        {
            // The Canvas may already be destroyed during scene unload. Flush the captured intent
            // through Application without touching destroyed view objects or reading stale economy.
            if (dirty) { dirty = false; service.Update(pending); }
            view.MusicVolume.onValueChanged.RemoveListener(VolumeChanged);
            view.SoundVolume.onValueChanged.RemoveListener(VolumeChanged);
        }
    }
}
