using System;
using IceP.Domain.Configuration;
using UnityEngine;

namespace IceP.Presentation
{
    // Composition Root adapts the visual SO; Presentation has no Infrastructure dependency.
    public sealed class SlotVisualSettings
    {
        public readonly Func<SymbolId, Sprite> Resolve;
        public readonly float Duration, Stagger, CellsPerSecond, EasePower;
        public SlotVisualSettings(Func<SymbolId, Sprite> resolve, float duration, float stagger, float speed, float ease)
        { Resolve = resolve; Duration = duration; Stagger = stagger; CellsPerSecond = speed; EasePower = ease; }
    }
}
