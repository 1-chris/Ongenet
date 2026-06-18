using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Effects;

/// <summary>Catalogue of available effects, mirroring the instrument registry.</summary>
public interface IEffectRegistry
{
    IReadOnlyList<EffectInfo> Available { get; }
    IAudioEffect Create(string id);

    /// <summary>Adds a dynamically-discovered effect (idempotent by id) and raises <see cref="Changed"/>.</summary>
    void Register(EffectInfo info);

    /// <summary>Raised when the set of available effects changes (so the UI can refresh).</summary>
    event Action? Changed;
}
