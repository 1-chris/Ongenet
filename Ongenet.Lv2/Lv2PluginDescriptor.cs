using System.Collections.Generic;

namespace Ongenet.Lv2;

/// <summary>
/// A plugin's UI, parsed from the bundle. v1 hosts <c>ui:X11UI</c> (Linux) only; other kinds are
/// recorded but reported as not hostable.
/// </summary>
public sealed class Lv2UiInfo
{
    public required string Uri { get; init; }
    public required string BinaryPath { get; init; }
    public required string BundlePath { get; init; }
    public required bool IsX11 { get; init; }
    public required IReadOnlyList<string> RequiredFeatures { get; init; }
}

/// <summary>
/// Everything needed to host one LV2 plugin, parsed from its bundle's Turtle: the resolved binary
/// path, the plugin URI (globally-unique, stable id), display name, instrument/effect classification,
/// the full port list, and the set of features the plugin requires (used to skip plugins needing
/// features this host doesn't yet provide). The binary is not loaded until the plugin is instantiated.
/// </summary>
public sealed class Lv2PluginDescriptor
{
    public required string BundlePath { get; init; }
    public required string BinaryPath { get; init; }
    public required string Uri { get; init; }
    public required string Name { get; init; }
    public required bool IsInstrument { get; init; }
    public required bool IsEffect { get; init; }
    public required IReadOnlyList<PortDescriptor> Ports { get; init; }
    public required IReadOnlyList<string> RequiredFeatures { get; init; }

    /// <summary>The plugin's UI, if it declares one (else null).</summary>
    public Lv2UiInfo? Ui { get; init; }
}
