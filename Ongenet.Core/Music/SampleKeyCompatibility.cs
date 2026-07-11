using System;

namespace Ongenet.Core.Music;

/// <summary>
/// Rates how well a target key fits when pitch-shifting a sample for use in a song. Pitch-shift only
/// moves by semitones — it does not rewrite major/minor harmony — so "fine" keys are ones that need a
/// small shift and/or sit in a common relationship (relative, parallel, IV, V) to the source.
/// </summary>
public static class SampleKeyCompatibility
{
    public enum Fit { Same, Parallel, Relative, Subdominant, Dominant, CloseShift, Other }

    /// <summary>All 24 root/mode combinations in chromatic order.</summary>
    public static void EnumerateTargets(Span<(int Root, bool IsMinor)> buffer)
    {
        var i = 0;
        for (var root = 0; root < 12; root++)
        {
            buffer[i++] = (root, false);
            buffer[i++] = (root, true);
        }
    }

    public static Fit Classify(int fromRoot, bool fromMinor, int toRoot, bool toMinor)
    {
        fromRoot = Mod12(fromRoot);
        toRoot = Mod12(toRoot);

        if (fromRoot == toRoot && fromMinor == toMinor) return Fit.Same;
        if (fromRoot == toRoot) return Fit.Parallel;

        var semi = MusicalKeyFormat.ShortestSemitoneDelta(fromRoot, toRoot);

        // Relative major/minor share the same key signature (tonics a minor third apart).
        if (fromMinor && !toMinor && Mod12(toRoot - fromRoot) == 3) return Fit.Relative;
        if (!fromMinor && toMinor && Mod12(toRoot - fromRoot) == 9) return Fit.Relative;

        // iv / v in the source mode (same mode targets only — cross-mode IV/V is ambiguous).
        if (fromMinor == toMinor)
        {
            if (semi == 5 || semi == -7) return Fit.Subdominant;
            if (semi == 7 || semi == -5) return Fit.Dominant;
        }

        if (Math.Abs(semi) <= 2) return Fit.CloseShift;
        return Fit.Other;
    }

    public static bool IsRecommended(Fit fit) => fit is Fit.Same or Fit.Parallel or Fit.Relative
        or Fit.Subdominant or Fit.Dominant or Fit.CloseShift;

    public static string FitLabel(Fit fit) => fit switch
    {
        Fit.Same => "same key",
        Fit.Parallel => "parallel",
        Fit.Relative => "relative",
        Fit.Subdominant => "subdominant",
        Fit.Dominant => "dominant",
        Fit.CloseShift => "low shift",
        _ => string.Empty
    };

    public static string FitMarker(Fit fit) => IsRecommended(fit) ? "★" : string.Empty;

    private static int Mod12(int pitchClass) => ((pitchClass % 12) + 12) % 12;
}
