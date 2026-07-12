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

    /// <summary>Hybrid lane — hosts both audio and MIDI clips on one track.</summary>
    Hybrid,

    /// <summary>FL-style pattern playlist lane — hosts <see cref="PatternClip"/> blocks, not audio/MIDI clips.</summary>
    Pattern,

    /// <summary>A bus that sums the output of its child tracks/groups, with its own strip and effects.</summary>
    Group,

    /// <summary>An auxiliary return bus fed by track sends (reverb, delay, etc.).</summary>
    Return,

    /// <summary>The single root bus all audio routes through before the device output.</summary>
    Master
}
