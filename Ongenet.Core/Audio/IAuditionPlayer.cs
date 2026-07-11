using System;
using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Audio;

/// <summary>
/// Plays a one-off audio buffer (a library/file preview) through the main output by mixing into the
/// engine's master block. Independent of the transport — auditioning works whether or not the song is
/// playing. Starting a new audition replaces any currently sounding one.
/// </summary>
public interface IAuditionPlayer
{
    /// <summary>True while a buffer is sounding.</summary>
    bool IsPlaying { get; }

    /// <summary>Current read position within the audition buffer, in seconds (0 when stopped).</summary>
    double PositionSeconds { get; }

    /// <summary>Duration of the buffer currently auditioning, in seconds (0 when stopped).</summary>
    double DurationSeconds { get; }

    /// <summary>Raised when playback reaches the end of the buffer (may fire on the audio thread —
    /// handlers must marshal to the UI thread themselves).</summary>
    event Action? Finished;

    /// <summary>Starts auditioning <paramref name="buffer"/> from <paramref name="startSeconds"/>,
    /// replacing any current one.</summary>
    void Play(AudioSampleBuffer buffer, double startSeconds = 0);

    /// <summary>Stops any current audition immediately.</summary>
    void Stop();

    /// <summary>Audio-thread hook: sums the current audition (resampled to <paramref name="format"/>) into
    /// <paramref name="buffer"/>. Called by the engine each block.</summary>
    void Mix(Span<float> buffer, AudioFormat format);
}
