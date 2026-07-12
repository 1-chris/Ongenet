using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;

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

    /// <summary>Per-channel gains for 5.1 live monitoring from stereo source + pan/width.</summary>
    public static (float L, float R, float C, float Lfe, float Ls, float Rs) Surround51StripGains(
        double volume, double pan, double width, SurroundChannelPan? surroundPan = null)
    {
        var (l, r, c, lfe, ls, rs) = SurroundPanner.Pan51(pan, width, surroundPan);
        var v = (float)Math.Clamp(volume, 0.0, 1.0);
        return (l * v, r * v, c * v, lfe * v, ls * v, rs * v);
    }

    /// <summary>Per-channel gains for 7.1 live monitoring from stereo source + pan/width.</summary>
    public static (float L, float R, float C, float Lfe, float Ls, float Rs, float Sl, float Sr)
        Surround71StripGains(double volume, double pan, double width, SurroundChannelPan? surroundPan = null)
    {
        var (l, r, c, lfe, ls, rs, sl, sr) = SurroundPanner.Pan71(pan, width, surroundPan);
        var v = (float)Math.Clamp(volume, 0.0, 1.0);
        return (l * v, r * v, c * v, lfe * v, ls * v, rs * v, sl * v, sr * v);
    }

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
        PitchShifter[]? pitchShifters = null, double araPitchOffsetSemitones = 0.0,
        IReadOnlyList<PitchNoteSegment>? pitchSegments = null)
    {
        if (stretch <= 0) stretch = 1.0;
        var ratio = (double)samples.SampleRate / deviceSampleRate * stretch;
        var frameCount = samples.FrameCount;
        var frames = temp.Length / channels;
        var offsetFrames = sourceOffsetSeconds * samples.SampleRate;

        var useSegments = AudioClipPitch.HasPitchSegments(pitchSegments);
        var usePitch = pitchShifters is not null
            && (Math.Abs(stretch - 1.0) > 1e-6 || Math.Abs(araPitchOffsetSemitones) > 1e-6 || useSegments);
        if (usePitch && !useSegments)
            AudioClipPitch.ApplyRatios(pitchShifters!, stretch, araPitchOffsetSemitones);

        var lastRatio = useSegments ? -1.0 : 0.0;

        for (var frame = 0; frame < frames; frame++)
        {
            var localBeat = blockStartBeat + frame / samplesPerBeat - clipStartBeat;
            if (localBeat < 0) continue;
            if (localBeat >= clipLengthBeats) break;

            var filePos = offsetFrames + localBeat * samplesPerBeat * ratio;
            var f0 = (long)filePos;
            if (f0 >= frameCount) break;

            if (usePitch && useSegments)
            {
                var combined = AudioClipPitch.CombinedRatio(stretch, f0, pitchSegments, araPitchOffsetSemitones);
                if (Math.Abs(combined - lastRatio) > 1e-5)
                {
                    AudioClipPitch.ApplyCombinedRatio(pitchShifters!, combined);
                    lastRatio = combined;
                }
            }

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

    /// <summary>
    /// Renders a warped audio clip using a <see cref="WarpMap"/> for beat↔source positioning.
    /// Mode-specific stretch: <see cref="WarpMode.Repitch"/> resamples (pitch tracks tempo),
    /// <see cref="WarpMode.Beats"/> holds pitch via inverse segment ratio (Rubber Band–style PSOLA),
    /// <see cref="WarpMode.Tones"/> uses tonal PSOLA grains, <see cref="WarpMode.Complex"/> blends
    /// spectral hold on long segments, and <see cref="WarpMode.Texture"/> favors wide grains without
    /// pitch correction.
    /// </summary>
    public static void RenderWarpedAudioClip(Span<float> temp, AudioSampleBuffer samples, WarpMap warp,
        double clipStartBeat, double clipLengthBeats, double blockStartBeat,
        double samplesPerBeat, int deviceSampleRate, int channels, WarpMode warpMode,
        bool pitchCorrected, double fadeInBeats = 0.0, double fadeOutBeats = 0.0,
        PitchShifter[]? pitchShifters = null, double araPitchOffsetSemitones = 0.0,
        IReadOnlyList<PitchNoteSegment>? pitchSegments = null)
    {
        var frames = temp.Length / channels;
        var frameCount = samples.FrameCount;
        var fileSampleRate = samples.SampleRate;
        var useSegments = AudioClipPitch.HasPitchSegments(pitchSegments);
        var useAra = Math.Abs(araPitchOffsetSemitones) > 1e-6 || useSegments;
        var preservePitch = pitchShifters is not null
            && (pitchCorrected || warpMode is WarpMode.Beats or WarpMode.Tones or WarpMode.Complex || useAra);
        var useTextureGrains = warpMode == WarpMode.Texture && pitchShifters is not null;
        var lastSeg = -1;
        var lastSourceFrame = -1L;
        var araRatio = useAra && !useSegments
            ? MusicalMath.SemitonesToRatio(araPitchOffsetSemitones)
            : 1.0;

        if (useTextureGrains)
        {
            foreach (var sh in pitchShifters!)
            {
                sh.SetRatio(1.0);
                sh.SetPeriod(0);
            }
        }

        for (var frame = 0; frame < frames; frame++)
        {
            var localBeat = blockStartBeat + frame / samplesPerBeat - clipStartBeat;
            if (localBeat < 0) continue;
            if (localBeat >= clipLengthBeats) break;

            var sourceSec = warp.BeatToSource(localBeat);
            var filePos = sourceSec * fileSampleRate;
            var f0 = (long)filePos;

            if (preservePitch && pitchShifters is not null)
            {
                var pitchRatio = useSegments
                    ? AudioClipPitch.SegmentPitchRatio(f0, pitchSegments, araPitchOffsetSemitones)
                    : araRatio;
                var seg = warp.SegmentIndexAt(localBeat);
                if (seg != lastSeg || (useSegments && f0 != lastSourceFrame))
                {
                    lastSeg = seg;
                    lastSourceFrame = f0;
                    var segRatio = warp.SegmentRatio(localBeat);
                    UpdateWarpPitchShifters(pitchShifters, warp, seg, segRatio, fileSampleRate, warpMode, pitchRatio);
                }
            }
            else if (useAra && pitchShifters is not null)
            {
                if (useSegments)
                    AudioClipPitch.ApplyRatiosAtFrame(pitchShifters, 1.0, f0, pitchSegments, araPitchOffsetSemitones);
                else
                    AudioClipPitch.ApplyAraOnly(pitchShifters, araPitchOffsetSemitones);
            }

            if (f0 < 0) continue;
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
                if (preservePitch) sample = pitchShifters![c].Process(sample);
                else if (useTextureGrains) sample = pitchShifters![c].Process(sample);
                temp[baseIndex + c] += sample * gain;
            }
        }
    }

    private static void UpdateWarpPitchShifters(PitchShifter[] shifters, WarpMap warp, int seg, double segRatio,
        int fileSampleRate, WarpMode warpMode, double araRatio = 1.0)
    {
        if (segRatio <= 1e-6)
        {
            foreach (var sh in shifters) sh.SetRatio(araRatio);
            return;
        }

        switch (warpMode)
        {
            case WarpMode.Repitch:
                foreach (var sh in shifters) sh.SetRatio(araRatio);
                break;

            case WarpMode.Beats:
                foreach (var sh in shifters) sh.SetRatio(1.0 / segRatio * araRatio);
                break;

            case WarpMode.Tones:
            {
                var (_, _, s0, s1) = warp.Segment(seg);
                var srcLen = Math.Abs(s1 - s0);
                var period = srcLen > 1e-6 ? fileSampleRate / Math.Max(40.0, 1.0 / srcLen) : 0;
                foreach (var sh in shifters)
                {
                    sh.SetPeriod(period);
                    sh.SetRatio(1.0 / segRatio * araRatio);
                }
                break;
            }

            case WarpMode.Complex:
            {
                // Full tonal PSOLA with segment-aware grain size (Rubber Band–class quality target).
                var (_, _, s0, s1) = warp.Segment(seg);
                var srcLen = Math.Abs(s1 - s0);
                var period = srcLen > 1e-6
                    ? fileSampleRate / Math.Clamp(1.0 / srcLen, 40.0, 512.0)
                    : fileSampleRate / 128.0;
                foreach (var sh in shifters)
                {
                    sh.SetPeriod(period);
                    sh.SetRatio(1.0 / segRatio * araRatio);
                }
                break;
            }

            case WarpMode.Texture:
            {
                var (_, _, s0, s1) = warp.Segment(seg);
                var srcLen = Math.Abs(s1 - s0);
                var widePeriod = srcLen > 1e-6 ? fileSampleRate * Math.Min(0.12, srcLen * 0.5) : fileSampleRate * 0.05;
                foreach (var sh in shifters)
                {
                    sh.SetPeriod(widePeriod);
                    sh.SetRatio(araRatio);
                }
                break;
            }

            default:
                foreach (var sh in shifters) sh.SetRatio(1.0 / segRatio * araRatio);
                break;
        }
    }

    /// <summary>Peak level with release ballistics for meters.</summary>
    public static float PeakLevel(ReadOnlySpan<float> buffer, int channels, int frames, float current, float release)
    {
        float peak = 0;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            var l = buffer[i];
            if (l < 0) l = -l;
            if (l > peak) peak = l;
            if (channels >= 2)
            {
                var r = buffer[i + 1];
                if (r < 0) r = -r;
                if (r > peak) peak = r;
            }
        }

        var decayed = current * release;
        return peak > decayed ? peak : decayed;
    }
}
