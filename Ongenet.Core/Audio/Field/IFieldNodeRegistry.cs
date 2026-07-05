using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Field;

/// <summary>
/// The catalogue of Field node types available in the component palette and used to recreate nodes when
/// loading a graph. Built-ins are registered by <see cref="FieldNodeRegistry"/>; the module-wrapper and
/// plugin bridges register additional types at runtime.
/// </summary>
public interface IFieldNodeRegistry
{
    IReadOnlyList<FieldNodeInfo> Available { get; }

    /// <summary>Creates a node by type id. Throws if unknown.</summary>
    FieldNode Create(string id);

    /// <summary>Creates a node by type id, or null if the type is unavailable (used by the loader).</summary>
    FieldNode? TryCreate(string id);

    void Register(FieldNodeInfo info);

    event Action? Changed;
}
