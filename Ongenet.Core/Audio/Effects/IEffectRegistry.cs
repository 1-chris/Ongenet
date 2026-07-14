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

    /// <summary>Removes a dynamically registered effect type (built-ins cannot be removed).</summary>
    bool Unregister(string id);

    /// <summary>
    /// Installs a last-resort factory used when <see cref="Create"/> cannot find a registered id
    /// (e.g. snapshot-load of a deleted user Field effect).
    /// </summary>
    void SetFallbackCreate(Func<string, IAudioEffect?> fallback);

    /// <summary>Raised when the set of available effects changes (so the UI can refresh).</summary>
    event Action? Changed;
}
