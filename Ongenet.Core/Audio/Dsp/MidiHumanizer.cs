using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// Shared helpers for MIDI humanization / timing jitter (used by note FX and clip edit).
/// Alloc-free for the sample path; note FX call these from the UI/scheduler thread.
/// </summary>
public static class MidiHumanizer
{
    /// <summary>Applies bidirectional velocity jitter in 0..1 domain. Returns clamped result.</summary>
    public static float Velocity(float velocity01, float amount01, Random rng)
    {
        if (amount01 <= 0) return velocity01;
        var delta = (float)((rng.NextDouble() * 2 - 1) * amount01);
        return Math.Clamp(velocity01 + delta, 0f, 1f);
    }

    /// <summary>Returns a timing offset in beats for humanized onset.</summary>
    public static double TimingBeats(double maxOffsetBeats, Random rng)
    {
        if (maxOffsetBeats <= 0) return 0;
        return (rng.NextDouble() * 2 - 1) * maxOffsetBeats;
    }

    /// <summary>Quantizes a beat time toward the nearest grid with adjustable strength 0..1.</summary>
    public static double Quantize(double beat, double gridBeats, float strength)
    {
        if (gridBeats <= 1e-9 || strength <= 0) return beat;
        var snapped = Math.Round(beat / gridBeats) * gridBeats;
        return beat + (snapped - beat) * Math.Clamp(strength, 0f, 1f);
    }
}

/// <summary>Scale-aware note snapping and strum onset spreads for chord humanization.</summary>
public static class MidiStrummer
{
    /// <summary>
    /// Spreads simultaneous notes across <paramref name="spreadBeats"/> by sorted pitch order.
    /// </summary>
    public static double OnsetForIndex(double baseOnBeat, int index, int count, double spreadBeats, bool up)
    {
        if (count <= 1 || spreadBeats <= 0) return baseOnBeat;
        var t = index / (double)(count - 1);
        if (!up) t = 1.0 - t;
        return baseOnBeat + t * spreadBeats;
    }
}
