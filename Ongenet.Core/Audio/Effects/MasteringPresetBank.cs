using System;

namespace Ongenet.Core.Audio.Effects;

public readonly record struct MultibandMasteringPreset(
    string Name, double Depth, double HighBoostDb, string Description);

public readonly record struct LimiterMasteringPreset(
    string Name, double ThresholdDb, double CeilingDb, double ReleaseMs, bool Spectral, string Description);

public static class MasteringPresetBank
{
    public static readonly MultibandMasteringPreset[] MultibandPresets =
    {
        new("Transparent", 0.15, 0.0, "Light upward compression — preserves dynamics."),
        new("Glue", 0.35, 1.5, "Gentle bus glue with a touch of air."),
        new("OTT", 0.55, 3.0, "Classic trance wall-of-sound inflate."),
        new("Aggressive", 0.75, 5.0, "Heavy OTT for drops and lead buses."),
        new("Max", 1.0, 7.0, "Full-depth multiband crush — use sparingly."),
    };

    public static readonly LimiterMasteringPreset[] LimiterPresets =
    {
        new("Transparent", -3.0, -0.3, 120.0, false,
            "Light limiting with −0.3 dBFS ceiling — archival / CD."),
        new("Streaming", -4.0, -1.0, 80.0, false,
            "−1.0 dBFS ceiling for lossy streaming encode headroom."),
        new("Loud", -6.0, -0.5, 60.0, true,
            "Competitive loudness with spectral follower; −0.5 dBFS ceiling."),
        new("Master", -8.0, -0.3, 100.0, true,
            "Deep limiting for club masters; spectral mode on."),
        new("Safety", -12.0, -1.5, 200.0, false,
            "Conservative safety net (−1.5 dBFS) for broadcast / podcast."),
    };

    public static MultibandMasteringPreset GetMultiband(int index) =>
        MultibandPresets[Math.Clamp(index, 0, MultibandPresets.Length - 1)];

    public static LimiterMasteringPreset GetLimiter(int index) =>
        LimiterPresets[Math.Clamp(index, 0, LimiterPresets.Length - 1)];
}
