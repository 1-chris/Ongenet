using System;
using System.Collections.Generic;
using System.Linq;

namespace Ongenet.Core.Audio.Field;

/// <summary>
/// Default registry of Field node types. Built-in primitives are registered from
/// <see cref="FieldNodeCatalog"/>; module-wrapper and plugin bridges add more at runtime via
/// <see cref="Register"/>. Idempotent by id, mirroring the instrument/effect registries.
/// </summary>
public sealed class FieldNodeRegistry : IFieldNodeRegistry
{
    private readonly object _lock = new();
    private readonly List<FieldNodeInfo> _infos = new();

    public FieldNodeRegistry()
    {
        foreach (var info in FieldNodeCatalog.BuiltIns()) _infos.Add(info);
    }

    public event Action? Changed;

    public IReadOnlyList<FieldNodeInfo> Available
    {
        get { lock (_lock) return _infos.ToList(); }
    }

    public FieldNode Create(string id)
    {
        var node = TryCreate(id);
        if (node is null) throw new ArgumentException($"Unknown Field node type '{id}'.", nameof(id));
        return node;
    }

    public FieldNode? TryCreate(string id)
    {
        FieldNodeInfo? info;
        lock (_lock) info = _infos.FirstOrDefault(i => i.Id == id);
        return info?.Create();
    }

    public void Register(FieldNodeInfo info)
    {
        lock (_lock)
        {
            if (_infos.Any(i => i.Id == info.Id)) return;
            _infos.Add(info);
        }

        Changed?.Invoke();
    }
}
