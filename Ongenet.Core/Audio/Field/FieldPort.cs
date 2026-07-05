namespace Ongenet.Core.Audio.Field;

/// <summary>Whether a <see cref="FieldPort"/> is an inlet (consumes a signal) or an outlet (produces one).</summary>
public enum FieldPortDirection
{
    Input,
    Output
}

/// <summary>
/// A single inlet or outlet on a <see cref="FieldNode"/>. Ports are declared once (in the node's
/// constructor) and are stable for the node's lifetime. A parameter-modulation inlet is an ordinary
/// input port with <see cref="IsModulation"/> set and <see cref="ModParamIndex"/> pointing at the
/// <see cref="Ongenet.Core.Audio.Parameters.FloatParameter"/> it modulates.
/// </summary>
public sealed class FieldPort
{
    public FieldPort(string id, string displayName, FieldSignalKind kind, FieldPortDirection direction)
    {
        Id = id;
        DisplayName = displayName;
        Kind = kind;
        Direction = direction;
    }

    /// <summary>Stable id, unique within the owning node. Used by persistence to re-link connections.</summary>
    public string Id { get; }

    /// <summary>Human-readable label shown next to the port in the editor.</summary>
    public string DisplayName { get; }

    public FieldSignalKind Kind { get; }

    public FieldPortDirection Direction { get; }

    /// <summary>True when this input is an auto-generated modulation inlet for a float parameter.</summary>
    public bool IsModulation { get; init; }

    /// <summary>Index into the owning node's <c>Parameters</c> list when <see cref="IsModulation"/> is true; else -1.</summary>
    public int ModParamIndex { get; init; } = -1;
}
