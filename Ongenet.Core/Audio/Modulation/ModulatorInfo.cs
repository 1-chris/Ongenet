using System;

namespace Ongenet.Core.Audio.Modulation;

/// <summary>Catalogue entry for a modulator type, mirroring <see cref="Effects.EffectInfo"/>.</summary>
public sealed record ModulatorInfo(
    string Id,
    string DisplayName,
    Func<IModulator> Create,
    string Category = "Modulators");
