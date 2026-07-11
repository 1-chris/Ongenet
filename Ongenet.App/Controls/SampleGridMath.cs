using System;
using Ongenet.Core.Audio.Midi;

namespace Ongenet.App.Controls;

/// <summary>Beat-grid snap helpers for the sample waveform editor (seconds-based X axis).</summary>
public static class SampleGridMath
{
    /// <summary>
    /// Snaps <paramref name="seconds"/> to the nearest musical grid line at the current zoom.
    /// </summary>
    public static double SnapSeconds(double seconds, double contentWidth, double durationSeconds,
        double secondsPerBeat, int beatsPerBar)
    {
        if (durationSeconds <= 0 || contentWidth <= 0 || secondsPerBeat <= 0) return seconds;

        seconds = Math.Clamp(seconds, 0, durationSeconds);
        var beat = seconds / secondsPerBeat;
        var pixelsPerBeat = secondsPerBeat * (contentWidth / durationSeconds);
        var stepBeats = GridMath.SnapBeats(pixelsPerBeat, beatsPerBar);
        var snappedBeat = MidiQuantize.Snap(beat, stepBeats);
        var snapped = snappedBeat * secondsPerBeat;
        return Math.Clamp(snapped, 0, durationSeconds);
    }

    /// <summary>Sub-beat grid step in beats for drawing, at the current zoom.</summary>
    public static double GridStepBeats(double contentWidth, double durationSeconds, double secondsPerBeat,
        int beatsPerBar)
    {
        if (durationSeconds <= 0 || contentWidth <= 0 || secondsPerBeat <= 0) return 1.0;
        var pixelsPerBeat = secondsPerBeat * (contentWidth / durationSeconds);
        return GridMath.SnapBeats(pixelsPerBeat, beatsPerBar);
    }

    public static bool IsMultiple(double value, double of)
    {
        if (of <= 0) return false;
        var ratio = value / of;
        return Math.Abs(ratio - Math.Round(ratio)) < 1e-6;
    }
}
