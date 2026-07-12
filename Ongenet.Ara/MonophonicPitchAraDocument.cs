namespace Ongenet.Ara;

using System;

/// <summary>
/// Built-in monophonic pitch editor fallback when the Celemony ARA SDK is unavailable.
/// Hosts can open this instead of a plugin-native ARA UI.
/// </summary>
public sealed class MonophonicPitchAraDocument : IAraDocument
{
    public MonophonicPitchAraDocument(string pluginId, double sourceSemitoneOffset = 0, Guid? clipId = null)
    {
        PluginId = pluginId;
        SourceSemitoneOffset = sourceSemitoneOffset;
        ClipId = clipId ?? Guid.Empty;
    }

    public string PluginId { get; }
    public Guid ClipId { get; }
    public bool IsActive => true;

    /// <summary>Timeline start of the bound clip region, in beats.</summary>
    public double RegionStartBeat { get; private set; }

    /// <summary>Length of the bound clip region, in beats.</summary>
    public double RegionLengthBeats { get; private set; }

    /// <summary>Global pitch shift applied to the ARA region, in semitones.</summary>
    public double SourceSemitoneOffset { get; set; }

    public event Action? Changed;

    public void BindRegion(double startBeat, double lengthBeats)
    {
        RegionStartBeat = startBeat;
        RegionLengthBeats = lengthBeats;
        Changed?.Invoke();
    }

    public void SetSemitoneOffset(double semitones)
    {
        SourceSemitoneOffset = semitones;
        Changed?.Invoke();
    }

    public void OpenEditor()
    {
        // UI host opens MonophonicPitchEditorWindow when wired; no-op at Core seam level.
    }
}
