using System;

namespace Ongenet.Core.Audio.Effects;

public readonly record struct MultibandMasteringPreset(string Name, double Depth, double HighBoostDb);

public readonly record struct LimiterMasteringPreset(
    string Name, double ThresholdDb, double CeilingDb, double ReleaseMs, bool Spectral);

public static class MasteringPresetBank
{
    public static readonly MultibandMasteringPreset[] MultibandPresets =
    {
        new("Transparent", 0.15, 0.0),
        new("Glue", 0.35, 1.5),
        new("OTT", 0.55, 3.0),
        new("Aggressive", 0.75, 5.0),
        new("Max", 1.0, 7.0),
    };

    public static readonly LimiterMasteringPreset[] LimiterPresets =
    {
        new("Transparent", -3.0, -0.3, 120.0, false),
        new("Streaming", -4.0, -1.0, 80.0, false),
        new("Loud", -6.0, -0.5, 60.0, true),
        new("Master", -8.0, -0.3, 100.0, true),
        new("Safety", -12.0, -1.5, 200.0, false),
    };

    public static MultibandMasteringPreset GetMultiband(int index) =>
        MultibandPresets[Math.Clamp(index, 0, MultibandPresets.Length - 1)];

    public static LimiterMasteringPreset GetLimiter(int index) =>
        LimiterPresets[Math.Clamp(index, 0, LimiterPresets.Length - 1)];
}
