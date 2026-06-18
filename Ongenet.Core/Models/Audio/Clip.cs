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

    /// <summary>
    /// Source audio file for an audio clip loaded from disk, or null. A recorded audio clip is
    /// audio (<see cref="IsAudio"/>) but has no file path — its PCM lives in <see cref="Samples"/>
    /// in memory until project save/load is implemented.
    /// </summary>
    public string? AudioFilePath { get; set; }

    /// <summary>Precomputed waveform peaks for an audio clip, or null. Drives waveform display.</summary>
    public AudioWaveform? Waveform { get; set; }

    /// <summary>Decoded PCM for an audio clip, or null. Used by the engine for playback.</summary>
    public AudioSampleBuffer? Samples { get; set; }

    /// <summary>
    /// The sample's natural tempo in BPM (from its file/folder name or estimated), or null if unknown.
    /// Kept so the clip can be re-fit when the project tempo changes.
    /// </summary>
    public double? SourceTempo { get; set; }

    /// <summary>
    /// When true the engine time-stretches (resamples) the audio so the whole sample spans
    /// <see cref="LengthBeats"/> at the project tempo — i.e. the loop stays locked to the beat grid.
    /// False for one-shots and recordings, which play at their native speed.
    /// </summary>
    public bool StretchToTempo { get; set; }

    /// <summary>
    /// True when this clip is audio (a loaded file or a recorded take) rather than MIDI. Set
    /// explicitly so a recorded, in-memory clip with no <see cref="AudioFilePath"/> still counts as audio.
    /// </summary>
    public bool IsAudio { get; set; }

    /// <summary>The notes of a MIDI clip (empty for audio clips). Positions are clip-relative.</summary>
    public List<MidiNote> Notes { get; } = new();

    /// <summary>True when this clip carries MIDI notes rather than audio.</summary>
    public bool IsMidi => !IsAudio;
}
