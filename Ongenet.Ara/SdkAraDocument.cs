using System;

namespace Ongenet.Ara;

/// <summary>
/// ARA document wrapper used when ENABLE_ARA is defined and ARA_SDK_PATH is configured.
/// Delegates editing to <see cref="MonophonicPitchAraDocument"/> until real SDK controllers are wired.
/// </summary>
public sealed class SdkAraDocument : IAraDocument
{
    private readonly MonophonicPitchAraDocument _inner;

    public SdkAraDocument(string pluginId, double sourceSemitoneOffset = 0, Guid? clipId = null)
        => _inner = new MonophonicPitchAraDocument(pluginId, sourceSemitoneOffset, clipId);

    public MonophonicPitchAraDocument Inner => _inner;

    public string PluginId => _inner.PluginId;
    public Guid ClipId => _inner.ClipId;
    public bool IsActive => _inner.IsActive;

    public double RegionStartBeat => _inner.RegionStartBeat;
    public double RegionLengthBeats => _inner.RegionLengthBeats;

    public double SourceSemitoneOffset
    {
        get => _inner.SourceSemitoneOffset;
        set => _inner.SourceSemitoneOffset = value;
    }

    public event Action? Changed
    {
        add => _inner.Changed += value;
        remove => _inner.Changed -= value;
    }

    /// <summary>True when built with ENABLE_ARA and the Celemony SDK path is set.</summary>
    public static bool IsSdkConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ARA_SDK_PATH"));

    public void BindRegion(double startBeat, double lengthBeats) => _inner.BindRegion(startBeat, lengthBeats);

    public void SetSemitoneOffset(double semitones) => _inner.SetSemitoneOffset(semitones);

    public void OpenEditor() => _inner.OpenEditor();
}
