using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// Preset tuning for algorithmic reverbs: room, hall, plate, shimmer-lite.
/// </summary>
public readonly record struct ReverbAlgorithmPreset(
    string Name,
    double RoomSize,
    double Damping,
    double Width,
    double PreDelayMs,
    double ModDepth);

public static class ReverbAlgorithmBank
{
    public static readonly ReverbAlgorithmPreset[] Presets =
    {
        new("Room", 0.45, 0.55, 0.9, 8, 0.0),
        new("Hall", 0.78, 0.42, 1.0, 18, 0.15),
        new("Plate", 0.62, 0.68, 0.75, 5, 0.08),
        new("Chamber", 0.55, 0.5, 0.85, 12, 0.05),
        new("Large Hall", 0.92, 0.38, 1.0, 28, 0.22),
    };

    public static ReverbAlgorithmPreset Get(int index) =>
        Presets[Math.Clamp(index, 0, Presets.Length - 1)];
}
