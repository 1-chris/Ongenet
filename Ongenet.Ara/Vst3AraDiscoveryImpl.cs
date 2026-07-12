using System;
using System.Collections.Generic;

namespace Ongenet.Ara;

/// <summary>Internal VST3 factory scan for ARA Main Factory classes (no SDK required).</summary>
public static class Vst3AraDiscoveryImpl
{
    public static IReadOnlyList<string> ReadAraFactoryNames(string modulePath)
    {
        // Implemented in Ongenet.Vst to avoid a circular reference; called via reflection-free delegate.
        return Vst3AraScanner?.Invoke(modulePath) ?? Array.Empty<string>();
    }

    /// <summary>Registered by Ongenet.Vst at startup.</summary>
    public static Func<string, IReadOnlyList<string>>? Vst3AraScanner { get; set; }
}
