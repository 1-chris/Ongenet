namespace Ongenet.Core.Models.Audio;

/// <summary>
/// The kind of material a track carries.
/// </summary>
public enum TrackKind
{
    /// <summary>Recorded or imported audio.</summary>
    Audio,

    /// <summary>A virtual instrument driven by note data.</summary>
    Instrument,

    /// <summary>Raw MIDI note data.</summary>
    Midi
}
