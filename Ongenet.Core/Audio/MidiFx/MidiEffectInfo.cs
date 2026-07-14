using System;

namespace Ongenet.Core.Audio.MidiFx;

/// <summary>Catalogue entry for a MIDI effect type, mirroring <c>EffectInfo</c>.</summary>
public sealed record MidiEffectInfo(
    string Id,
    string DisplayName,
    Func<IMidiEffect> Create,
    string Category = "Note FX");
