namespace Ongenet.Core.Models.Audio;

/// <summary>
/// A time-bounded pitch blob within an audio clip's source buffer. Positions are in source
/// sample frames; <see cref="PitchCents"/> is the correction offset applied during playback.
/// </summary>
public sealed class PitchNoteSegment
{
    /// <summary>First source sample frame (inclusive).</summary>
    public long StartSample { get; set; }

    /// <summary>Last source sample frame (exclusive).</summary>
    public long EndSample { get; set; }

    /// <summary>Pitch correction in cents relative to the detected pitch.</summary>
    public double PitchCents { get; set; }

    /// <summary>Relative loudness used when blending overlapping segments.</summary>
    public float Amplitude { get; set; } = 1f;
}
