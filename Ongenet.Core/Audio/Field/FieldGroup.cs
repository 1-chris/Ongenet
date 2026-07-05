using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Field;

/// <summary>
/// A cosmetic container that bundles a set of nodes so the editor can show them as one collapsible unit
/// (an "instrument" or "FX chain" block that can be entered and navigated). Groups do not change audio
/// processing — the compiler flattens them — so grouping is purely an organisational/navigation aid.
/// </summary>
public sealed class FieldGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Group";
    public List<Guid> NodeIds { get; } = new();
    public bool Collapsed { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}
