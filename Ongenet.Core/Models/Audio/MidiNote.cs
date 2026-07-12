using System;

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

    /// <summary>Pitch slide in semitones applied at note end (FL-style slide).</summary>
    public double SlideSemitones { get; set; }

    /// <summary>Portamento time in milliseconds (0 = off).</summary>
    public int PortamentoMs { get; set; }

    /// <summary>Optional note group id for multi-note editing.</summary>
    public Guid? NoteGroupId { get; set; }

    /// <summary>Playback probability 0..1 (1 = always play).</summary>
    public float Chance { get; set; } = 1f;

    /// <summary>Humanize timing offset in PPQ ticks (applied at schedule time).</summary>
    public int HumanizeTicks { get; set; }

    /// <summary>End position within the clip, in beats.</summary>
    public double EndBeat => StartBeat + LengthBeats;
}
