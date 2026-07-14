using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Modulation;

/// <summary>Catalogue of available modulators, mirroring the effect registry.</summary>
public interface IModulatorRegistry
{
    IReadOnlyList<ModulatorInfo> Available { get; }
    IModulator Create(string id);
    void Register(ModulatorInfo info);
    bool Unregister(string id);
    void SetFallbackCreate(Func<string, IModulator?> fallback);
    event Action? Changed;
}
