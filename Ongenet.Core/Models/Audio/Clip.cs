using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Models.Audio;

/// <summary>
/// A region of material placed on a <see cref="Track"/> at a position on the timeline.
/// Positions and lengths are measured in beats so they stay tempo-independent; seconds
/// are derived from <see cref="Tempo"/> only when the audio engine needs them.
/// </summary>
public sealed class Clip
{
    /// <summary>Stable identity for selection and lookups.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Display name.</summary>
    public string Name { get; set; } = "Clip";

    /// <summary>Start position on the timeline, in beats from the project origin.</summary>
    public double StartBeat { get; set; }

    /// <summary>Length of the clip, in beats.</summary>
    public double LengthBeats { get; set; }

    /// <summary>End position on the timeline, in beats.</summary>
    public double EndBeat => StartBeat + LengthBeats;

    /// <summary>Source audio file for an audio clip, or null for a non-audio clip.</summary>
    public string? AudioFilePath { get; set; }

    /// <summary>Precomputed waveform peaks for an audio clip, or null. Drives waveform display.</summary>
    public AudioWaveform? Waveform { get; set; }

    /// <summary>Decoded PCM for an audio clip, or null. Used by the engine for playback.</summary>
    public AudioSampleBuffer? Samples { get; set; }

    /// <summary>True when this clip plays back an audio file.</summary>
    public bool IsAudio => AudioFilePath is not null;

    /// <summary>The notes of a MIDI clip (empty for audio clips). Positions are clip-relative.</summary>
    public List<MidiNote> Notes { get; } = new();

    /// <summary>True when this clip carries MIDI notes rather than audio (the default for non-audio clips).</summary>
    public bool IsMidi => AudioFilePath is null;
}
