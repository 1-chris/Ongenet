using System;
using System.Collections.Generic;

namespace Ongenet.Core.Models.Audio;

/// <summary>Instrument rack layout — standard multi-slot or drum pad grid.</summary>
public enum RackKind
{
    Standard,
    DrumPadGrid
}

/// <summary>Macro knob on an instrument rack (maps to a bound parameter id).</summary>
public sealed class RackMacroKnob
{
    public string Label { get; set; } = "Macro";
    public string TargetParameterId { get; set; } = string.Empty;
    public double Value { get; set; }
}

/// <summary>Drum pad slot in a rack — MIDI note trigger to instrument slot index.</summary>
public sealed class DrumPadSlot
{
    public int PadIndex { get; set; }
    public int MidiNote { get; set; } = 36;
    public int InstrumentSlotIndex { get; set; }
    public string Label { get; set; } = "Pad";
}

/// <summary>Rack container settings on an instrument track.</summary>
public sealed class InstrumentRackSettings
{
    public RackKind Kind { get; set; } = RackKind.Standard;
    public List<RackMacroKnob> Macros { get; } = new();
    public List<DrumPadSlot> DrumPads { get; } = new();

    public void EnsureDefaultDrumPads(int count = 16)
    {
        if (DrumPads.Count >= count) return;
        for (var i = DrumPads.Count; i < count; i++)
        {
            DrumPads.Add(new DrumPadSlot
            {
                PadIndex = i,
                MidiNote = 36 + i,
                InstrumentSlotIndex = Math.Min(i, 0),
                Label = $"Pad {i + 1}"
            });
        }
    }

    public void EnsureDefaultMacros(int count = 8)
    {
        while (Macros.Count < count)
            Macros.Add(new RackMacroKnob { Label = $"M{Macros.Count + 1}" });
    }
}
