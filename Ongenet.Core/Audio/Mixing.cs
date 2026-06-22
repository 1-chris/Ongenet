using System;
using Ongenet.Core.Audio.Dsp;
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
    /// Volume + balance pan → per-channel gains for a bus (group/master). Unlike <see cref="StripGains"/>'s
    /// constant-power law (−3 dB at centre), a bus is unity at centre so stacking groups/master doesn't
    /// compound attenuation: pan just trims the opposite channel.
    /// </summary>
    public static (float Left, float Right) BusGains(double volume, double pan)
    {
        var v = (float)Math.Clamp(volume, 0.0, 1.0);
        var p = (float)Math.Clamp(pan, -1.0, 1.0);
        var left = v * (p <= 0 ? 1f : 1f - p);
        var right = v * (p >= 0 ? 1f : 1f + p);
        return (left, right);
    }

    /// <summary>
    /// Renders an audio clip's samples (no strip) additively into a scratch buffer, resampling
    /// from the file rate to the device/render rate and positioning by the playhead beat.
    /// <paramref name="stretch"/> is an extra playback-rate multiplier (1.0 = native): the engine sets
    /// it so a tempo-synced clip's whole sample spans its beat-length at the project tempo.
    /// <paramref name="sourceOffsetSeconds"/> shifts the read position into the buffer (non-zero for a
    /// sliced clip's right-hand piece, which starts partway through the source).
    /// <paramref name="fadeInBeats"/>/<paramref name="fadeOutBeats"/> apply a per-clip crossfade gain
    /// (independent of any track strip volume). When <paramref name="pitchShifters"/> is supplied (one
    /// per channel) and the clip is stretched, the resampled stream is shifted by <c>1/stretch</c> so the
    /// time-stretch preserves pitch; pass null for the plain (pitch-tracks-tempo) resample.
    /// </summary>
    public static void RenderAudioClip(Span<float> temp, AudioSampleBuffer samples,
        double clipStartBeat, double clipLengthBeats, double blockStartBeat,
        double samplesPerBeat, int deviceSampleRate, int channels, double stretch = 1.0,
        double sourceOffsetSeconds = 0.0, double fadeInBeats = 0.0, double fadeOutBeats = 0.0,
        PitchShifter[]? pitchShifters = null)
    {
        if (stretch <= 0) stretch = 1.0;
        var ratio = (double)samples.SampleRate / deviceSampleRate * stretch;
        var frameCount = samples.FrameCount;
        var frames = temp.Length / channels;
        var offsetFrames = sourceOffsetSeconds * samples.SampleRate;

        var usePitch = pitchShifters is not null && Math.Abs(stretch - 1.0) > 1e-6;
        if (usePitch)
        {
            var pitchRatio = 1.0 / stretch; // undo only the stretch's pitch shift, not the SR conversion
            foreach (var sh in pitchShifters!) sh.SetRatio(pitchRatio);
        }

        for (var frame = 0; frame < frames; frame++)
        {
            var localBeat = blockStartBeat + frame / samplesPerBeat - clipStartBeat;
            if (localBeat < 0) continue;
            if (localBeat >= clipLengthBeats) break;

            var filePos = offsetFrames + localBeat * samplesPerBeat * ratio;
            var f0 = (long)filePos;
            if (f0 >= frameCount) break;

            var frac = (float)(filePos - f0);
            var gain = Crossfade.Gain(localBeat, clipLengthBeats, fadeInBeats, fadeOutBeats);
            var baseIndex = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var fileChannel = c < samples.Channels ? c : samples.Channels - 1;
                var s0 = samples.Sample(f0, fileChannel);
                var s1 = samples.Sample(f0 + 1, fileChannel);
                var sample = s0 + (s1 - s0) * frac;
                if (usePitch) sample = pitchShifters![c].Process(sample);
                temp[baseIndex + c] += sample * gain;
            }
        }
    }
}
