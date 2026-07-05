using System;

namespace Ongenet.Core.Audio.Field;

/// <summary>
/// Palette/registry descriptor for a Field node type: a stable id, a display name, a category for grouping
/// in the component palette, and a factory. Mirrors <see cref="Instruments.InstrumentInfo"/>.
/// </summary>
public sealed record FieldNodeInfo(string Id, string DisplayName, string Category, Func<FieldNode> Create)
{
    /// <summary>Optional one-line description shown in the palette.</summary>
    public string Description { get; init; } = "";
}
