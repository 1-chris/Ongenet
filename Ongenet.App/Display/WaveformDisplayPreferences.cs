using System;

namespace Ongenet.App.Display;

/// <summary>
/// Live waveform display preferences mirrored from <see cref="Services.AppSettings"/>. Custom-drawn
/// waveform controls subscribe to <see cref="Changed"/> so a settings toggle repaints immediately.
/// </summary>
public static class WaveformDisplayPreferences
{
    public static bool BandColorsEnabled { get; private set; } = true;

    public static event Action? Changed;

    public static void Apply(bool bandColorsEnabled)
    {
        if (BandColorsEnabled == bandColorsEnabled) return;
        BandColorsEnabled = bandColorsEnabled;
        Changed?.Invoke();
    }
}
