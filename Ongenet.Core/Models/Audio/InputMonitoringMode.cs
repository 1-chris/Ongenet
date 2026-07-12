namespace Ongenet.Core.Models.Audio;

/// <summary>How live input is monitored on an audio track.</summary>
public enum InputMonitoringMode
{
    /// <summary>No software monitoring.</summary>
    Off = 0,

    /// <summary>Monitor whenever the track is armed.</summary>
    Auto = 1,

    /// <summary>Always monitor input on this track (even when not armed).</summary>
    On = 2
}
