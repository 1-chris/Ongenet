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

    /// <summary>Raised when playback reaches the end of the buffer (may fire on the audio thread —
    /// handlers must marshal to the UI thread themselves).</summary>
    event Action? Finished;

    /// <summary>Starts auditioning <paramref name="buffer"/> from the beginning, replacing any current one.</summary>
    void Play(AudioSampleBuffer buffer);

    /// <summary>Stops any current audition immediately.</summary>
    void Stop();

    /// <summary>Audio-thread hook: sums the current audition (resampled to <paramref name="format"/>) into
    /// <paramref name="buffer"/>. Called by the engine each block.</summary>
    void Mix(Span<float> buffer, AudioFormat format);
}
