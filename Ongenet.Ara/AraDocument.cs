namespace Ongenet.Ara;

using Ongenet.Core.Models.Audio;

/// <summary>ARA2 extension hosting seam (Melodyne-style editing). Desktop-only.</summary>
public interface IAraDocument
{
    string PluginId { get; }
    bool IsActive { get; }
    void OpenEditor();
}

/// <summary>Implemented by plugin instances that expose Celemony ARA editing.</summary>
public interface IAraCapable
{
    bool SupportsAra { get; }
    IAraDocument? AraDocument { get; }
    void BindAraDocument(IAraDocument document);
    void OpenAraEditor();
}

/// <summary>Host-side ARA document controller and VST3 factory discovery.</summary>
public interface IAraHost
{
    /// <summary>True when built with ENABLE_ARA and the Celemony SDK is present.</summary>
    bool IsSdkAvailable { get; }

    /// <summary>Scans a VST3 module for ARA Main Factory classes matching <paramref name="audioClassName"/>.</summary>
    bool TryDiscoverAraPlugin(string modulePath, string audioClassName);

    /// <summary>Creates (or reuses) an ARA document binding for a plugin instance.</summary>
    IAraDocument BindPlugin(string pluginId, IAraCapable plugin);

    /// <summary>Creates (or reuses) a clip-bound ARA document for a plugin instance.</summary>
    IAraDocument BindClip(Clip clip, string pluginId, IAraCapable plugin);

    /// <summary>Opens the ARA editor UI for a bound document.</summary>
    void OpenEditor(IAraDocument document);
}

public sealed class NullAraDocument : IAraDocument
{
    public string PluginId => "";
    public bool IsActive => false;
    public void OpenEditor() { }
}

public sealed class StubAraDocument : IAraDocument
{
    public StubAraDocument(string pluginId) => PluginId = pluginId;
    public string PluginId { get; }
    public bool IsActive => true;
    public void OpenEditor() { }
}
