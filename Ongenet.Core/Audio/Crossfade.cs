using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Models.Audio;

namespace Ongenet.Core.Audio;

/// <summary>
/// Derives crossfades from overlapping audio clips on a single track and provides the shared fade-gain
/// curve. When two clips overlap, the earlier one fades out and the later one fades in across the overlap
/// so the pair sums to a constant amplitude (a linear crossfade). The same <see cref="Gain"/> function is
/// used by the mixer (<see cref="Mixing.RenderAudioClip"/>) and the timeline's fade visual, so what you
/// see matches what you hear. Per-clip and therefore independent of the track/lane strip volume.
/// </summary>
public static class Crossfade
{
    /// <summary>
    /// Computes the fade-in / fade-out length (in beats) for each audio clip from its overlaps with the
    /// neighbours on the same track. Clips are paired in start-beat order; a pair that overlaps gets a
    /// crossfade as long as the overlap (clamped to the shorter clip). Non-audio clips are ignored.
    /// </summary>
    public static Dictionary<Clip, (double FadeInBeats, double FadeOutBeats)> Compute(IEnumerable<Clip> clips)
    {
        var fades = new Dictionary<Clip, (double FadeInBeats, double FadeOutBeats)>();
        var ordered = new List<Clip>();
        foreach (var clip in clips)
        {
            if (!clip.IsAudio) continue;
            ordered.Add(clip);
            fades[clip] = (0.0, 0.0);
        }

        ordered.Sort((a, b) => a.StartBeat.CompareTo(b.StartBeat));

        for (var i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1];
            var cur = ordered[i];
            var overlap = prev.EndBeat - cur.StartBeat;
            if (overlap <= 0) continue;

            var len = Math.Min(overlap, Math.Min(prev.LengthBeats, cur.LengthBeats));
            if (len <= 0) continue;

            var p = fades[prev];
            var c = fades[cur];
            fades[prev] = (p.FadeInBeats, Math.Max(p.FadeOutBeats, len));
            fades[cur] = (Math.Max(c.FadeInBeats, len), c.FadeOutBeats);
        }

        return fades;
    }

    /// <summary>
    /// Linear fade gain (0..1) at <paramref name="localBeat"/> within a clip of <paramref name="lengthBeats"/>,
    /// given its fade-in / fade-out lengths in beats. The fade-in ramps up over the first
    /// <paramref name="fadeInBeats"/> and the fade-out ramps down over the last <paramref name="fadeOutBeats"/>;
    /// both are applied so a clip can fade in and out at once.
    /// </summary>
    public static float Gain(double localBeat, double lengthBeats, double fadeInBeats, double fadeOutBeats)
    {
        var g = 1.0;
        if (fadeInBeats > 0 && localBeat < fadeInBeats)
            g *= localBeat / fadeInBeats;
        if (fadeOutBeats > 0 && localBeat > lengthBeats - fadeOutBeats)
            g *= (lengthBeats - localBeat) / fadeOutBeats;
        return (float)Math.Clamp(g, 0.0, 1.0);
    }

    // Bounds the preview render so a very long overlap can't allocate an oversized buffer.
    private const int MaxPreviewFrames = 96_000;

    /// <summary>
    /// Renders the actual crossfaded (summed, fade-weighted) signal for the overlap region of two clips into
    /// a mono <see cref="AudioWaveform"/>, so the timeline can show what really plays there instead of two
    /// stacked raw waveforms. <paramref name="crossfadeBeats"/> is the overlap/fade length from
    /// <see cref="Compute"/>. Returns null when the clips don't overlap or have no samples. Mirrors the engine's
    /// mix (via <see cref="Mixing.RenderAudioClip"/>), minus pitch correction (irrelevant to the envelope).
    /// </summary>
    public static AudioWaveform? OverlapWaveform(Clip earlier, Clip later, double crossfadeBeats, double projectBpm)
    {
        if (earlier.Samples is not { } es || later.Samples is null) return null;

        var ovStart = later.StartBeat;
        var ovEnd = Math.Min(earlier.EndBeat, later.EndBeat);
        var ovBeats = ovEnd - ovStart;
        if (ovBeats <= 0 || crossfadeBeats <= 0) return null;

        var sampleRate = es.SampleRate > 0 ? es.SampleRate : 44100;
        var samplesPerBeat = projectBpm > 0 ? sampleRate * 60.0 / projectBpm : sampleRate;
        var frames = (int)Math.Clamp(Math.Round(ovBeats * samplesPerBeat), 1, MaxPreviewFrames);
        var temp = new float[frames];

        // The earlier clip is fading out across the overlap; the later is fading in. blockStartBeat = ovStart
        // positions both reads at the overlap, and RenderAudioClip sums them with their fade gains.
        RenderInto(temp, earlier, ovStart, samplesPerBeat, sampleRate, projectBpm, 0, crossfadeBeats);
        RenderInto(temp, later, ovStart, samplesPerBeat, sampleRate, projectBpm, crossfadeBeats, 0);

        return AudioWaveform.Build(new AudioSampleBuffer(temp, 1, sampleRate));
    }

    private static void RenderInto(float[] temp, Clip clip, double blockStartBeat, double samplesPerBeat,
        int sampleRate, double projectBpm, double fadeInBeats, double fadeOutBeats)
    {
        if (clip.Samples is not { } s) return;
        var sourceDur = clip.SourceLengthSeconds
            ?? Math.Max(0.0, s.FrameCount / (double)s.SampleRate - clip.SourceOffsetSeconds);
        var stretch = clip.StretchToTempo ? TempoSync.Stretch(sourceDur, projectBpm, clip.LengthBeats) : 1.0;
        Mixing.RenderAudioClip(temp, s, clip.StartBeat, clip.LengthBeats, blockStartBeat, samplesPerBeat,
            sampleRate, 1, stretch, clip.SourceOffsetSeconds, fadeInBeats, fadeOutBeats);
    }
}
