using System;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>Applies rack macro knob values to bound instrument parameters.</summary>
public static class RackMacroApplier
{
    /// <summary>Target id format: <c>{slotIndex}:{parameterName}</c>.</summary>
    public static void Apply(Track track)
    {
        if (track.Rack.Macros.Count == 0 || track.Instruments.Count == 0) return;

        foreach (var macro in track.Rack.Macros)
        {
            if (string.IsNullOrWhiteSpace(macro.TargetParameterId)) continue;
            var parts = macro.TargetParameterId.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2) continue;
            if (!int.TryParse(parts[0], out var slotIndex)) continue;
            if (slotIndex < 0 || slotIndex >= track.Instruments.Count) continue;

            var paramName = parts[1];
            foreach (var p in track.Instruments[slotIndex].Instrument.Parameters)
            {
                if (!string.Equals(p.Name, paramName, StringComparison.OrdinalIgnoreCase)) continue;
                if (p is FloatParameter f)
                {
                    f.Value = f.Min + macro.Value * (f.Max - f.Min);
                    break;
                }
            }
        }
    }
}
