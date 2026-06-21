using System;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Estimates the musical key of an audio buffer. Builds a 12-bin chromagram (energy per pitch class,
/// summed across octaves via the Goertzel algorithm) over a downmixed mono window, then correlates it
/// against the Krumhansl–Schmuckler major and minor key profiles rotated to all 12 tonics; the best
/// correlation wins. Returns e.g. <c>"A min"</c>, or an empty string when the material is too short/flat
/// to call. Intended for off-thread use on a one-off preview, not the audio thread.
/// </summary>
public static class MusicalKeyDetector
{
    private static readonly string[] PitchClasses = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    // Krumhansl–Schmuckler tonal hierarchy profiles (relative weights of each scale degree).
    private static readonly double[] Major = { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };
    private static readonly double[] Minor = { 6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 };

    private const int LowMidi = 36;   // C2
    private const int HighMidi = 96;  // C7 (exclusive upper bound at 95)
    private const double MaxSeconds = 12.0;

    public static string Detect(AudioSampleBuffer buffer)
    {
        var chroma = Chromagram(buffer);
        if (chroma is null) return string.Empty;

        var bestScore = double.NegativeInfinity;
        var bestTonic = 0;
        var bestMinor = false;
        for (var tonic = 0; tonic < 12; tonic++)
        {
            var maj = Correlate(chroma, Major, tonic);
            if (maj > bestScore) { bestScore = maj; bestTonic = tonic; bestMinor = false; }
            var min = Correlate(chroma, Minor, tonic);
            if (min > bestScore) { bestScore = min; bestTonic = tonic; bestMinor = true; }
        }

        if (double.IsNaN(bestScore) || bestScore <= 0) return string.Empty;
        return $"{PitchClasses[bestTonic]} {(bestMinor ? "min" : "maj")}";
    }

    // Returns a normalized 12-bin chroma vector, or null if the buffer is too short / silent.
    private static double[]? Chromagram(AudioSampleBuffer buffer)
    {
        var sr = buffer.SampleRate;
        var total = buffer.FrameCount;
        if (total < sr / 4) return null; // < 0.25s — not enough to analyse

        var count = (int)Math.Min(total, (long)(MaxSeconds * sr));
        var mono = new float[count];
        var channels = buffer.Channels;
        var energy = 0.0;
        for (var f = 0; f < count; f++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++) sum += buffer.Sample(f, c);
            var v = sum / channels;
            mono[f] = v;
            energy += v * (double)v;
        }

        if (energy <= 1e-6) return null; // silent

        var chroma = new double[12];
        for (var midi = LowMidi; midi < HighMidi; midi++)
        {
            var freq = 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);
            if (freq >= sr * 0.5) break;
            chroma[midi % 12] += Goertzel(mono, freq, sr);
        }

        // Normalize to zero mean unit-ish scale (Pearson correlation handles the rest).
        var max = 0.0;
        foreach (var v in chroma) if (v > max) max = v;
        if (max <= 0) return null;
        for (var i = 0; i < 12; i++) chroma[i] /= max;
        return chroma;
    }

    // Goertzel magnitude of a single frequency over the window (cheap single-bin DFT).
    private static double Goertzel(float[] x, double freq, int sampleRate)
    {
        var w = 2.0 * Math.PI * freq / sampleRate;
        var coeff = 2.0 * Math.Cos(w);
        double s1 = 0, s2 = 0;
        for (var i = 0; i < x.Length; i++)
        {
            var s0 = x[i] + coeff * s1 - s2;
            s2 = s1;
            s1 = s0;
        }

        var power = s1 * s1 + s2 * s2 - coeff * s1 * s2;
        return power > 0 ? Math.Sqrt(power) / x.Length : 0;
    }

    // Pearson correlation between the chroma (rotated so `tonic` aligns to profile degree 0) and a profile.
    private static double Correlate(double[] chroma, double[] profile, int tonic)
    {
        double meanC = 0, meanP = 0;
        for (var i = 0; i < 12; i++) { meanC += chroma[(tonic + i) % 12]; meanP += profile[i]; }
        meanC /= 12; meanP /= 12;

        double num = 0, denC = 0, denP = 0;
        for (var i = 0; i < 12; i++)
        {
            var c = chroma[(tonic + i) % 12] - meanC;
            var p = profile[i] - meanP;
            num += c * p;
            denC += c * c;
            denP += p * p;
        }

        var den = Math.Sqrt(denC * denP);
        return den > 0 ? num / den : 0;
    }
}
