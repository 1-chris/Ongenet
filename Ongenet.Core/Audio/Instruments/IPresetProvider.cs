using System.Collections.Generic;

namespace Ongenet.Core.Audio.Instruments;

/// <summary>
/// Implemented by instruments that ship a set of built-in presets, so the instrument inspector can
/// offer a preset picker. A preset is simply a named bulk-assignment of the instrument's parameters,
/// so selecting one updates the same values that are edited (and saved) individually — nothing extra
/// needs to be persisted.
/// </summary>
public interface IPresetProvider
{
    /// <summary>The names of the available presets, in display order. Index 0 is the default/init preset.</summary>
    IReadOnlyList<string> PresetNames { get; }

    /// <summary>Applies the preset at <paramref name="index"/> to this instrument's parameters.</summary>
    void LoadPreset(int index);
}
