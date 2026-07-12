using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio;

/// <summary>Shared pitch-shifter allocation for clip playback (stretch, warp, ARA offset, poly segments).</summary>
public static class AudioClipPitch
{
    /// <summary>Crossfade length at segment edges when blending overlapping blobs.</summary>
    public const int SegmentCrossfadeSamples = 256;

    public static bool HasPitchSegments(IReadOnlyList<PitchNoteSegment>? segments)
        => segments is { Count: > 0 };

    public static bool NeedsShifters(Clip clip, WarpMap? warp) =>
        clip is { PitchCorrected: true, StretchToTempo: true }
        || clip.WarpMode is WarpMode.Beats or WarpMode.Tones or WarpMode.Complex or WarpMode.Texture
        || warp is not null && (warp.HasExplicitMarkers || clip.WarpMode != WarpMode.Beats)
        || Math.Abs(clip.AraPitchOffsetSemitones) > 1e-6
        || HasPitchSegments(clip.PitchSegments);

    public static PitchShifter[]? CreateShiftersIfNeeded(Clip clip, WarpMap? warp, int channels, int sampleRate)
    {
        if (!NeedsShifters(clip, warp)) return null;
        return Build(channels, sampleRate);
    }

    public static PitchShifter[] Build(int channels, int sampleRate)
    {
        var shifters = new PitchShifter[channels];
        for (var i = 0; i < channels; i++)
        {
            shifters[i] = new PitchShifter();
            shifters[i].Configure(sampleRate);
        }

        return shifters;
    }

    /// <summary>Combines tempo-stretch pitch compensation with segment or fallback ARA pitch.</summary>
    public static double CombinedRatio(double stretch, long sourceFrame,
        IReadOnlyList<PitchNoteSegment>? segments, double araFallbackSemitones)
    {
        var stretchPitch = Math.Abs(stretch - 1.0) > 1e-6 ? 1.0 / stretch : 1.0;
        return stretchPitch * SegmentPitchRatio(sourceFrame, segments, araFallbackSemitones);
    }

    /// <summary>
    /// Pitch ratio from active segments at <paramref name="sourceFrame"/>, amplitude-weighted with
    /// edge crossfades. Falls back to <paramref name="araFallbackSemitones"/> when no segment covers
    /// the frame.
    /// </summary>
    public static double SegmentPitchRatio(long sourceFrame, IReadOnlyList<PitchNoteSegment>? segments,
        double araFallbackSemitones)
    {
        if (segments is null or { Count: 0 })
            return Math.Abs(araFallbackSemitones) > 1e-6
                ? MusicalMath.SemitonesToRatio(araFallbackSemitones)
                : 1.0;

        double weightedLog = 0;
        double totalWeight = 0;
        foreach (var seg in segments)
        {
            var w = SegmentWeight(sourceFrame, seg);
            if (w <= 0) continue;
            weightedLog += w * Math.Log(MusicalMath.CentsToRatio(seg.PitchCents), 2.0);
            totalWeight += w;
        }

        if (totalWeight > 1e-6)
            return Math.Pow(2.0, weightedLog / totalWeight);

        return Math.Abs(araFallbackSemitones) > 1e-6
            ? MusicalMath.SemitonesToRatio(araFallbackSemitones)
            : 1.0;
    }

    /// <summary>Amplitude-weighted contribution of a segment at a source frame, with edge crossfade.</summary>
    public static double SegmentWeight(long frame, PitchNoteSegment seg)
    {
        if (frame < seg.StartSample || frame >= seg.EndSample) return 0;
        var len = seg.EndSample - seg.StartSample;
        if (len <= 0) return 0;

        var pos = frame - seg.StartSample;
        var fade = 1.0;
        var crossfade = SegmentCrossfadeSamples;
        if (crossfade > 0 && len > crossfade * 2)
        {
            if (pos < crossfade)
                fade = pos / (double)crossfade;
            else if (pos >= len - crossfade)
                fade = (len - pos) / (double)crossfade;
        }

        return seg.Amplitude * fade;
    }

    /// <summary>Combines tempo-stretch pitch compensation with optional ARA semitone offset.</summary>
    public static void ApplyRatios(PitchShifter[] shifters, double stretch, double araPitchSemitones)
    {
        ApplyCombinedRatio(shifters, CombinedRatio(stretch, 0, null, araPitchSemitones));
    }

    public static void ApplyRatiosAtFrame(PitchShifter[] shifters, double stretch, long sourceFrame,
        IReadOnlyList<PitchNoteSegment>? segments, double araFallbackSemitones)
    {
        ApplyCombinedRatio(shifters, CombinedRatio(stretch, sourceFrame, segments, araFallbackSemitones));
    }

    public static void ApplyCombinedRatio(PitchShifter[] shifters, double combinedRatio)
    {
        if (Math.Abs(combinedRatio - 1.0) < 1e-6)
        {
            foreach (var sh in shifters) sh.SetRatio(1.0);
            return;
        }

        foreach (var sh in shifters) sh.SetRatio(combinedRatio);
    }

    public static void ApplyAraOnly(PitchShifter[] shifters, double araPitchSemitones)
    {
        if (Math.Abs(araPitchSemitones) < 1e-6) return;
        var ratio = MusicalMath.SemitonesToRatio(araPitchSemitones);
        foreach (var sh in shifters) sh.SetRatio(ratio);
    }
}
