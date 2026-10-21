using System;
using IceP.Application;
using IceP.Domain.Configuration;
using UnityEngine;

namespace IceP.Presentation
{
    public sealed class ReelAnimationCoordinator : MonoBehaviour
    {
        [SerializeField] private ReelView[] reels;
        private readonly ReelCompletionBarrier barrier = new ReelCompletionBarrier();
        private SlotVisualSettings visual;
        private Guid id;
        public event Action<Guid, bool> Finished;
        public event Action<SpinResult> Started;
        public event Action<Guid, int> ReelStopped;
        public ReelView[] Reels => reels;
        public bool IsRunning { get; private set; }
        public void Initialize(SlotRuntimeConfig config, SlotVisualSettings settings)
        {
            if (reels == null || reels.Length != 5) throw new InvalidOperationException("Exactly five reels are required.");
            visual = settings;
            for (int i = 0; i < reels.Length; i++) reels[i].Initialize(config.ReelStrips[i], settings);
        }
        public void Begin(SpinResult spin)
        {
            if (IsRunning) throw new InvalidOperationException("Reel animation is already running.");
            id = spin.SpinId; barrier.Begin(id); IsRunning = true;
            for (int i = 0; i < reels.Length; i++)
                reels[i].Begin(id, i, spin.Evaluation.Outcome.Stops[i].Position, visual.Duration + i * visual.Stagger, Stopped);
            Started?.Invoke(spin);
        }
        private void Stopped(Guid spin, int reel)
        {
            if (!IsRunning || spin != id) return;
            ReelStopped?.Invoke(spin, reel);
            if (!barrier.Report(spin, reel)) return;
            IsRunning = false; Finished?.Invoke(spin, true);
        }
        public void Cancel()
        {
            if (!IsRunning) return;
            barrier.Cancel(); IsRunning = false;
            foreach (var reel in reels) reel.Cancel();
            Finished?.Invoke(id, false);
        }
        // Cancellation never emits a successful completion, including teardown and shutdown.
        private void OnDisable() => Cancel();
        private void OnDestroy() => Cancel();
        private void OnApplicationQuit() => Cancel();
    }
}
