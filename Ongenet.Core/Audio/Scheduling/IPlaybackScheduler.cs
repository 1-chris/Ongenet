using System;
using System.Collections.Generic;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio.Scheduling;

/// <summary>
/// Builds sample-accurate note and audio-clip schedules for one playback mode
/// (arrangement, session, or hybrid).
/// </summary>
public interface IPlaybackScheduler
{
    PlaybackSchedule Build(PlaybackScheduleContext context);
}

/// <summary>Inputs shared by all schedulers.</summary>
public sealed class PlaybackScheduleContext
{
    public required Project Project { get; init; }
    public required IReadOnlyList<Track> Tracks { get; init; }
    public required double StartBeat { get; init; }
    public required int SampleRate { get; init; }
    public required int Channels { get; init; }
    public required double Bpm { get; init; }
}

/// <summary>Immutable schedule snapshot consumed by <see cref="AudioEngine"/>.</summary>
public sealed class PlaybackSchedule
{
    public ScheduledNoteEvent[] Notes { get; init; } = Array.Empty<ScheduledNoteEvent>();
    public ScheduledControlChangeEvent[] ControlChanges { get; init; } = Array.Empty<ScheduledControlChangeEvent>();
    public ScheduledAudioClip[] AudioClips { get; init; } = Array.Empty<ScheduledAudioClip>();
    public double ArrangementEndBeat { get; init; }
}

/// <summary>A MIDI note event scheduled on the timeline.</summary>
public sealed record ScheduledNoteEvent(
    Guid TrackId,
    double OnBeat,
    double OffBeat,
    InstrumentSlot[]? Slots,
    Effects.IMidiAwareEffect[] MidiEffects,
    int Note,
    float Velocity,
    float Gain = 1f,
    float Pan = 0f);

/// <summary>A MIDI CC event scheduled on the timeline.</summary>
public sealed record ScheduledControlChangeEvent(
    Guid TrackId,
    double Beat,
    InstrumentSlot[]? Slots,
    int Controller,
    int Value);

/// <summary>An audio clip scheduled for streaming playback.</summary>
public sealed class ScheduledAudioClip
{
    public required Track Track { get; init; }
    public required double StartBeat { get; init; }
    public required double LengthBeats { get; init; }
    public required Files.AudioSampleBuffer Samples { get; init; }
    public required bool StretchToTempo { get; init; }
    public required double SourceDurSeconds { get; init; }
    public required double SourceOffsetSeconds { get; init; }
    public required double FadeInBeats { get; init; }
    public required double FadeOutBeats { get; init; }
    public Dsp.PitchShifter[]? PitchShifters { get; init; }
    public WarpMap? Warp { get; init; }
    public WarpMode WarpMode { get; init; } = WarpMode.Beats;
    public bool PitchCorrected { get; init; }
    public double AraPitchOffsetSemitones { get; init; }
    public IReadOnlyList<PitchNoteSegment> PitchSegments { get; init; } = Array.Empty<PitchNoteSegment>();
    public float Gain { get; init; } = 1f;
}
