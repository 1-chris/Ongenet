using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.MidiFx;

namespace Ongenet.Core.Models.Audio;

/// <summary>Stored pre-freeze track state so unfreeze can restore instruments, FX, and clips.</summary>
public sealed class FreezeSnapshot
{
    public TrackKind Kind { get; set; }
    public List<InstrumentSlot> Instruments { get; } = new();
    public List<IAudioEffect> Effects { get; } = new();
    public List<IMidiEffect> MidiEffects { get; } = new();
    public List<Clip> Clips { get; } = new();
}
