using System;
using Ongenet.Core.Audio.Files;

namespace Ongenet.Core.Audio;

/// <summary>
/// Shared per-track mixing maths used by both the live <see cref="AudioEngine"/> and the
/// <see cref="OfflineRenderer"/>, so real-time playback and rendered files sound identical.
/// </summary>
public static class Mixing
{
    /// <summary>Constant-power pan + volume → per-channel gains.</summary>
    public static (float Left, float Right) StripGains(double volume, double pan)
    {
        var v = (float)Math.Clamp(volume, 0.0, 1.0);
        var p = (float)Math.Clamp(pan, -1.0, 1.0);
        var angle = (p + 1f) * 0.25f * MathF.PI;
        return (v * MathF.Cos(angle), v * MathF.Sin(angle));
    }

    /// <summary>Gain for a channel: 0=left, 1=right, others=average.</summary>
    public static float ChannelGain(int channel, float leftGain, float rightGain)
        => channel == 0 ? leftGain : channel == 1 ? rightGain : (leftGain + rightGain) * 0.5f;

    /// <summary>
    /// Renders an audio clip's samples (no strip) additively into a scratch buffer, resampling
    /// from the file rate to the device/render rate and positioning by the playhead beat.
    /// </summary>
    public static void RenderAudioClip(Span<float> temp, AudioSampleBuffer samples,
        double clipStartBeat, double clipLengthBeats, double blockStartBeat,
        double samplesPerBeat, int deviceSampleRate, int channels)
    {
        var ratio = (double)samples.SampleRate / deviceSampleRate;
        var frameCount = samples.FrameCount;
        var frames = temp.Length / channels;

        for (var frame = 0; frame < frames; frame++)
        {
            var localBeat = blockStartBeat + frame / samplesPerBeat - clipStartBeat;
            if (localBeat < 0) continue;
            if (localBeat >= clipLengthBeats) break;

            var filePos = localBeat * samplesPerBeat * ratio;
            var f0 = (long)filePos;
            if (f0 >= frameCount) break;

            var frac = (float)(filePos - f0);
            var baseIndex = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var fileChannel = c < samples.Channels ? c : samples.Channels - 1;
                var s0 = samples.Sample(f0, fileChannel);
                var s1 = samples.Sample(f0 + 1, fileChannel);
                temp[baseIndex + c] += s0 + (s1 - s0) * frac;
            }
        }
    }
}
