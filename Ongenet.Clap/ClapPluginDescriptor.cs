namespace Ongenet.Clap;

/// <summary>Metadata for one CLAP plugin found in a module on disk.</summary>
public sealed record ClapPluginDescriptor(
    string ModulePath,
    string PluginId,
    string Name,
    string Vendor,
    bool IsInstrument,
    bool IsEffect);
