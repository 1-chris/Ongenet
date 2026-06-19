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
    Midi,

    /// <summary>A bus that sums the output of its child tracks/groups, with its own strip and effects.</summary>
    Group,

    /// <summary>The single root bus all audio routes through before the device output.</summary>
    Master
}
