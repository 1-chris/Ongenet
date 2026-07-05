namespace Ongenet.Au;

/// <summary>
/// Metadata for one Audio Unit discovered via the Component Manager. The
/// (<see cref="Type"/>, <see cref="SubType"/>, <see cref="Manufacturer"/>) four-char-code triple
/// uniquely identifies the component and is enough to re-find and instantiate it later.
/// </summary>
public sealed record AuPluginDescriptor(
    uint Type,
    uint SubType,
    uint Manufacturer,
    string Name,
    bool IsInstrument,
    bool IsEffect);
