using System;

namespace Ongenet.Core.Audio;

/// <summary>
/// Maths for fitting a sample of a known natural tempo onto the project's beat grid.
///
/// A sample of length <c>duration</c> at <c>naturalBpm</c> contains <c>duration·naturalBpm/60</c>
/// musical beats. To lock it to the grid we place it over a whole number of project beats and let the
/// engine resample (time-stretch) the audio to span exactly that many beats. To avoid extreme
/// stretches when the sample is near double/half the project tempo, the target length is snapped by
/// octaves so the residual stretch always stays within √2 of 1× (e.g. a 75 BPM loop in a 140 BPM
/// project becomes a 2× length region played ~0.93×, not a 1× region played 1.87×).
/// </summary>
public static class TempoSync
{
    /// <summary>
    /// The clip length, in project beats, that a sample should occupy to sit on the grid — the sample's
    /// natural beat count snapped up/down by octaves toward the project tempo, rounded to a whole beat.
    /// Returns 0 when inputs are invalid.
    /// </summary>
    public static double MusicalBeats(double durationSeconds, double naturalBpm, double projectBpm)
    {
        if (durationSeconds <= 0 || naturalBpm <= 0 || projectBpm <= 0) return 0;

        var naturalBeats = durationSeconds * naturalBpm / 60.0;
        var octave = Math.Round(Math.Log2(projectBpm / naturalBpm)); // 0 normally, ±1 near double/half
        var beats = naturalBeats * Math.Pow(2.0, octave);

        var rounded = Math.Round(beats);
        // Don't round a sub-beat sample up to a full beat (that would over-stretch a short hit).
        return rounded < 1 ? beats : rounded;
    }

    /// <summary>
    /// Playback rate multiplier so a sample of <paramref name="durationSeconds"/> spans
    /// <paramref name="lengthBeats"/> project beats at <paramref name="projectBpm"/>. 1.0 = native speed.
    /// </summary>
    public static double Stretch(double durationSeconds, double projectBpm, double lengthBeats)
    {
        if (durationSeconds <= 0 || projectBpm <= 0 || lengthBeats <= 0) return 1.0;
        return durationSeconds * projectBpm / (60.0 * lengthBeats);
    }
}
