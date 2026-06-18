namespace Ongenet.Core.Models.Audio;

/// <summary>
/// A single note within a MIDI clip. Position and length are in beats, measured <b>relative to
/// the clip's start</b>, so moving or resizing the clip never has to rewrite its notes.
/// </summary>
public sealed class MidiNote
{
    /// <summary>MIDI note number, 0–127 (60 = middle C).</summary>
    public int Note { get; set; }

    /// <summary>Start position within the clip, in beats from the clip's start.</summary>
    public double StartBeat { get; set; }

    /// <summary>Length of the note, in beats.</summary>
    public double LengthBeats { get; set; }

    /// <summary>Velocity, 0..1.</summary>
    public float Velocity { get; set; } = 0.8f;

    /// <summary>End position within the clip, in beats.</summary>
    public double EndBeat => StartBeat + LengthBeats;
}
