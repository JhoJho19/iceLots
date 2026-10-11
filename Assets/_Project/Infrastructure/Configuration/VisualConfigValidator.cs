using System;
using System.Collections.Generic;
using IceP.Domain.Configuration;

namespace IceP.Infrastructure.Configuration
{
    public static class VisualConfigValidator
    {
        public static IReadOnlyList<string> Validate(SlotVisualConfigSO visual, SlotRuntimeConfig runtime)
        {
            var errors = new List<string>();
            if (!visual) { errors.Add("SlotVisualConfigSO: asset reference is required."); return errors.AsReadOnly(); }
            if (runtime == null) { errors.Add("VisualConfig: runtime symbol definitions are required."); return errors.AsReadOnly(); }
            var mappings = visual.Symbols ?? Array.Empty<SymbolVisualData>();
            if (mappings.Length != runtime.Symbols.Count) errors.Add("Visual.Symbols: one mapping per runtime symbol is required.");
            var symbols = new HashSet<SymbolId>();
            for (var i = 0; i < mappings.Length; i++)
            {
                var mapping = mappings[i];
                if (mapping == null) { errors.Add($"Visual.Symbols[{i}]: mapping is required."); continue; }
                if (!Enum.IsDefined(typeof(SymbolId), mapping.Symbol)) errors.Add($"Visual.Symbols[{i}].Symbol: unknown symbol.");
                if (!symbols.Add(mapping.Symbol)) errors.Add($"Visual.Symbols[{i}].Symbol: duplicate mapping.");
                if (!mapping.Sprite) errors.Add($"Visual.Symbols[{i}].Sprite: sprite for {mapping.Symbol} is required.");
            }
            foreach (var symbol in runtime.Symbols)
                if (!symbols.Contains(symbol.Id)) errors.Add($"Visual.Symbols: missing mapping for {symbol.Id}.");
            if (!visual.SlotBackground) errors.Add("Visual.SlotBackground: sprite is required.");
            if (!visual.SlotFrame) errors.Add("Visual.SlotFrame: sprite is required.");
            if (float.IsNaN(visual.SpinDurationSeconds) || float.IsInfinity(visual.SpinDurationSeconds) || visual.SpinDurationSeconds <= 0)
                errors.Add("Visual.SpinDurationSeconds: must be finite and positive.");
            if (float.IsNaN(visual.ReelStopStaggerSeconds) || float.IsInfinity(visual.ReelStopStaggerSeconds) || visual.ReelStopStaggerSeconds < 0)
                errors.Add("Visual.ReelStopStaggerSeconds: must be finite and non-negative.");
            if (visual.EnableWinEffect && !visual.WinEffectPrefab) errors.Add("Visual.WinEffectPrefab: required when EnableWinEffect is true.");
            if (visual.EnableSpinAudio && !visual.SpinAudio) errors.Add("Visual.SpinAudio: required when EnableSpinAudio is true.");
            return errors.AsReadOnly();
        }

        public static void ValidateAndThrow(SlotVisualConfigSO visual, SlotRuntimeConfig runtime)
        {
            var errors = Validate(visual, runtime);
            if (errors.Count > 0) throw new ConfigValidationException(errors);
        }
    }
}
