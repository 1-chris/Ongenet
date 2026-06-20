namespace Ongenet.Core.Audio.Midi;

/// <summary>A transport action a MIDI control can be mapped to trigger.</summary>
public enum TransportAction
{
    PlayPause,
    Stop,
    Record,
}

/// <summary>
/// Binds a MIDI control (a note, or a CC treated as a button) to a <see cref="TransportAction"/>.
/// These are global controller setup (not per-project), persisted in the app settings file.
/// </summary>
public sealed class TransportMapping
{
    public required TransportAction Action { get; init; }

    /// <summary>True if triggered by a Note On; false if triggered by a Control Change (value ≥ 64).</summary>
    public bool IsNote { get; init; }

    /// <summary>MIDI channel to match (0..15), or -1 for any channel.</summary>
    public int Channel { get; init; } = -1;

    /// <summary>Note number or controller number (0..127).</summary>
    public required int Number { get; init; }
}
