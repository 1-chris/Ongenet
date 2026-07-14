using System;

namespace Ongenet.Core.Audio.Hardware;

/// <summary>
/// Reports whether external hardware routing (MIDI/CV) is available on the current host.
/// Browser and other unsupported targets degrade gracefully via no-op device implementations.
/// </summary>
public static class HardwareAvailability
{
    /// <summary>True on desktop hosts that can route to external MIDI/CV hardware.</summary>
    public static bool IsSupported =>
        !OperatingSystem.IsBrowser() &&
        (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux());

    /// <summary>Whether MIDI output routing is expected to work on this host.</summary>
    public static bool IsMidiOutputSupported => IsSupported;

    /// <summary>Whether CV I/O is expected to work (Linux/macOS targets with CV plumbing).</summary>
    public static bool IsCvSupported =>
        IsSupported && (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux());
}
