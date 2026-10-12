using System;
using IceP.Domain.Configuration;
using UnityEngine;

namespace IceP.Infrastructure.Configuration
{
    [Serializable]
    public sealed class SymbolVisualData
    {
        public SymbolId Symbol;
        public Sprite Sprite;
    }

    [CreateAssetMenu(fileName = "SlotVisualConfig", menuName = "IceP/Slot Visual Config")]
    public sealed class SlotVisualConfigSO : ScriptableObject
    {
        [Header("Symbol mapping")]
        public SymbolVisualData[] Symbols = Array.Empty<SymbolVisualData>();
        [Header("Slot artwork")]
        public Sprite SlotBackground;
        public Sprite SlotFrame;
        [Header("Reel animation parameters (data only)")]
        public float SpinDurationSeconds = 1.5f;
        public float ReelStopStaggerSeconds = 0.15f;
        public float ReelCellsPerSecond = 18f;
        public float ReelStopEasePower = 2f;
        public Sprite Resolve(SymbolId id)
        {
            foreach (var mapping in Symbols) if (mapping.Symbol == id) return mapping.Sprite;
            throw new ArgumentOutOfRangeException(nameof(id));
        }
        [Header("Optional effects and audio (required only when enabled)")]
        public bool EnableWinEffect;
        public GameObject WinEffectPrefab;
        public bool EnableSpinAudio;
        public AudioClip SpinAudio;
    }
}
