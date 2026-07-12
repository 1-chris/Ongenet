using System;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio;

/// <summary>Offline bake of a single audio clip (warp/stretch/fades) without track FX.</summary>
public static class ClipBake
{
    private const int BlockFrames = 512;

    /// <summary>
    /// Renders <paramref name="clip"/> through the same clip maths as live playback, returning
    /// interleaved PCM at <paramref name="sampleRate"/> for the clip's beat length.
    /// </summary>
    public static AudioSampleBuffer Bake(Clip clip, double bpm, int sampleRate, int channels = 2)
    {
        if (clip.Samples is null || clip.LengthBeats <= 0 || sampleRate <= 0)
            return new AudioSampleBuffer(Array.Empty<float>(), channels < 1 ? 1 : channels, sampleRate);

        channels = Math.Max(1, channels);
        var prepared = ClipPlaybackSource.Prepare(clip, bpm);
        var samples = prepared.Samples;
        var warp = prepared.Warp;
        var stretch = prepared.StretchRatio;

        PitchShifter[]? shifters = AudioClipPitch.CreateShiftersIfNeeded(clip, warp, channels, sampleRate);

        var samplesPerBeat = bpm > 0 ? sampleRate * 60.0 / bpm : sampleRate;
        var totalFrames = (int)Math.Ceiling(clip.LengthBeats * samplesPerBeat);
        var output = new float[totalFrames * channels];
        var temp = new float[BlockFrames * channels];
        var fadeIn = clip.UserFadeInBeats;
        var fadeOut = clip.UserFadeOutBeats;
        var useWarp = warp is not null && (warp.HasExplicitMarkers || clip.WarpMode != WarpMode.Beats);

        AudioSampleBuffer sourceSamples = samples;

        for (var written = 0; written < totalFrames;)
        {
            var blockFrames = Math.Min(BlockFrames, totalFrames - written);
            var blockStartBeat = clip.StartBeat + written / samplesPerBeat;
            var tempSpan = temp.AsSpan(0, blockFrames * channels);
            tempSpan.Clear();

            if (useWarp)
            {
                Mixing.RenderWarpedAudioClip(tempSpan, sourceSamples, warp!, clip.StartBeat, clip.LengthBeats,
                    blockStartBeat, samplesPerBeat, sampleRate, channels, clip.WarpMode,
                    clip.PitchCorrected, fadeIn, fadeOut, shifters, clip.AraPitchOffsetSemitones,
                    clip.PitchSegments);
            }
            else
            {
                Mixing.RenderAudioClip(tempSpan, sourceSamples, clip.StartBeat, clip.LengthBeats, blockStartBeat,
                    samplesPerBeat, sampleRate, channels, stretch, clip.SourceOffsetSeconds,
                    fadeIn, fadeOut, shifters, clip.AraPitchOffsetSemitones, clip.PitchSegments);
            }

            tempSpan.CopyTo(output.AsSpan(written * channels, blockFrames * channels));
            written += blockFrames;
        }

        return new AudioSampleBuffer(output, channels, sampleRate);
    }
}
