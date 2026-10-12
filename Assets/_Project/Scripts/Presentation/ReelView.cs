using System;
using IceP.Domain.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace IceP.Presentation
{
    public sealed class ReelView : MonoBehaviour
    {
        [SerializeField] private Image[] cells; // three existing cells, followed by two recycled buffer cells
        private ReelStrip strip;
        private SlotVisualSettings visual;
        private float position, origin, distance, elapsed, duration;
        private int target;
        private Action<Guid, int> finished;
        private Guid spinId;
        private int reelIndex;
        public bool IsAnimating { get; private set; }
        public int StopPosition { get; private set; }
        public float ScrollPosition => position;
        public Image Cell(int row) => cells[row];
        public SymbolId DisplayedSymbol(int row) => strip.Symbols[(StopPosition + row) % strip.Symbols.Count];
        public void Initialize(ReelStrip configuredStrip, SlotVisualSettings settings)
        { strip = configuredStrip; visual = settings; Snap(0); }
        public void Begin(Guid id, int index, int stop, float seconds, Action<Guid, int> callback)
        {
            if (strip == null || cells == null || cells.Length != 5) throw new InvalidOperationException("Reel is not initialized.");
            if (stop < 0 || stop >= strip.Symbols.Count) throw new ArgumentOutOfRangeException(nameof(stop));
            spinId = id; reelIndex = index; target = stop; duration = seconds; elapsed = 0;
            origin = position; float delta = (stop - origin + strip.Symbols.Count) % strip.Symbols.Count;
            int cycles = Mathf.Max(1, Mathf.CeilToInt((visual.CellsPerSecond * seconds / visual.EasePower - delta) / strip.Symbols.Count));
            distance = delta + cycles * strip.Symbols.Count;
            finished = callback; IsAnimating = true;
        }
        private void Update()
        {
            if (!IsAnimating) return;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            position = origin + distance * (1 - Mathf.Pow(1 - t, visual.EasePower));
            Draw(position);
            if (t < 1) return;
            var callback = finished; finished = null; IsAnimating = false;
            Snap(target); callback?.Invoke(spinId, reelIndex);
        }
        private void Draw(float value)
        {
            int integer = Mathf.FloorToInt(value); float fraction = value - integer;
            float height = ((RectTransform)transform).rect.height / 3f;
            for (int i = 0; i < cells.Length; i++)
            {
                int row = i < 3 ? i : i == 3 ? -1 : 3;
                int index = (integer + row) % strip.Symbols.Count; if (index < 0) index += strip.Symbols.Count;
                var sprite = visual.Resolve(strip.Symbols[index]);
                if (cells[i].sprite != sprite) cells[i].sprite = sprite;
                cells[i].rectTransform.anchoredPosition = new Vector2(0, (1 - row + fraction) * height);
            }
        }
        public void Snap(int stop) { StopPosition = stop; position = stop; Draw(position); }
        public void Cancel()
        {
            if (!IsAnimating) return;
            IsAnimating = false; finished = null; Snap(target); // authoritative stop survives internal cancellation, without settlement
        }
        private void OnDisable() => Cancel();
        private void OnDestroy() => Cancel();
    }
}
