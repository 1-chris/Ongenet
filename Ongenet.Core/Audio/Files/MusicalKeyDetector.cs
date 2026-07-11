using System;

namespace Ongenet.Core.Audio.Files;

/// <summary>
/// Estimates the musical key of an audio buffer using Mixxx's default Queen Mary
/// <c>GetKeyMode</c> pipeline (Constant-Q chromagram, profile correlation, median filter).
/// </summary>
public static class MusicalKeyDetector
{
    /// <summary>Returns e.g. <c>"A min"</c>, or empty when the material is too short or silent.</summary>
    public static string Detect(AudioSampleBuffer buffer)
        => QueenMaryKeyDetector.Detect(buffer);
}
