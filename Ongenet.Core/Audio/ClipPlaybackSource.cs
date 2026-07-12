using System;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio;

/// <summary>
/// Shared clip playback preparation so live rendering and offline bakes use the same warp/stretch
/// path (including Complex-mode Rubber Band pre-stretch).
/// </summary>
public static class ClipPlaybackSource
{
    public sealed record Prepared(
        AudioSampleBuffer Samples,
        WarpMap? Warp,
        bool StretchToTempo,
        double SourceDurSeconds,
        double StretchRatio);

    /// <summary>Prepares PCM and warp metadata for scheduling or offline bake.</summary>
    public static Prepared Prepare(Clip clip, double bpm)
    {
        if (clip.Samples is not { } samples || clip.LengthBeats <= 0)
        {
            return new Prepared(
                new AudioSampleBuffer(Array.Empty<float>(), 1, 44100),
                null, false, 0, 1.0);
        }

        var sourceDur = clip.SourceLengthSeconds
            ?? Math.Max(0.0, samples.FrameCount / (double)samples.SampleRate - clip.SourceOffsetSeconds);
        var sourceEnd = clip.SourceOffsetSeconds + sourceDur;
        var warp = clip.WarpMarkers.Count > 0 || clip.StretchToTempo
            ? WarpMap.FromClip(clip, sourceEnd)
            : null;

        var stretch = clip.StretchToTempo && warp is null
            ? TempoSync.Stretch(sourceDur, bpm, clip.LengthBeats)
            : 1.0;

        var playbackSamples = samples;
        WarpMap? playbackWarp = warp;

        if (clip.WarpMode is WarpMode.Complex && clip.StretchToTempo && Math.Abs(stretch - 1.0) > 1e-4)
        {
            var stretched = RubberBandStretcher.TimeStretch(
                samples.Samples, samples.Channels, samples.SampleRate, stretch);
            playbackSamples = new AudioSampleBuffer(stretched, samples.Channels, samples.SampleRate);
            stretch = 1.0;
            playbackWarp = clip.WarpMarkers.Count > 0 ? WarpMap.FromClip(clip, sourceEnd) : null;
        }

        return new Prepared(playbackSamples, playbackWarp, clip.StretchToTempo, sourceDur, stretch);
    }
}
