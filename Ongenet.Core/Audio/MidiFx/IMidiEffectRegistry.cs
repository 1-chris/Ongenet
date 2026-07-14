using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.MidiFx;

/// <summary>Catalogue of available MIDI (note) effects, mirroring the audio effect registry.</summary>
public interface IMidiEffectRegistry
{
    IReadOnlyList<MidiEffectInfo> Available { get; }
    IMidiEffect Create(string id);
    void Register(MidiEffectInfo info);
    bool Unregister(string id);
    void SetFallbackCreate(Func<string, IMidiEffect?> fallback);
    event Action? Changed;
}
