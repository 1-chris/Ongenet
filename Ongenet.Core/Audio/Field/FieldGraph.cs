using System;
using System.Collections.Generic;
using System.Linq;

namespace Ongenet.Core.Audio.Field;

/// <summary>
/// The editable Field document: a set of <see cref="FieldNode"/>s, the <see cref="FieldConnection"/>s
/// between their ports, cosmetic <see cref="FieldGroup"/>s, and the canvas view state (zoom/pan). This is
/// the UI-thread model; the audio thread never touches it directly — it runs an immutable
/// <see cref="CompiledGraph"/> produced by <see cref="FieldGraphCompiler"/>.
/// </summary>
public sealed class FieldGraph
{
    private readonly List<FieldNode> _nodes = new();
    private readonly List<FieldConnection> _connections = new();
    private readonly List<FieldGroup> _groups = new();

    public IReadOnlyList<FieldNode> Nodes => _nodes;
    public IReadOnlyList<FieldConnection> Connections => _connections;
    public IReadOnlyList<FieldGroup> Groups => _groups;

    /// <summary>Canvas view: horizontal pan offset in canvas units.</summary>
    public double ViewX { get; set; }

    /// <summary>Canvas view: vertical pan offset in canvas units.</summary>
    public double ViewY { get; set; }

    /// <summary>Canvas view: zoom factor (1 = 100%).</summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>Bumped on every structural change so consumers (compiler, UI) can detect edits cheaply.</summary>
    public int Revision { get; private set; }

    public FieldNode? FindNode(Guid id) => _nodes.FirstOrDefault(n => n.Id == id);

    public void AddNode(FieldNode node)
    {
        _nodes.Add(node);
        Revision++;
    }

    public void RemoveNode(Guid id)
    {
        _connections.RemoveAll(c => c.SourceNode == id || c.DestNode == id);
        foreach (var g in _groups) g.NodeIds.Remove(id);
        _nodes.RemoveAll(n => n.Id == id);
        Revision++;
    }

    /// <summary>
    /// Connects <paramref name="srcPort"/> on <paramref name="srcNode"/> to <paramref name="dstPort"/> on
    /// <paramref name="dstNode"/>. An input port accepts multiple connections (they are summed); a duplicate
    /// connection is ignored. Returns the connection (existing or new), or null if the ports are invalid.
    /// </summary>
    public FieldConnection? Connect(Guid srcNode, string srcPort, Guid dstNode, string dstPort)
    {
        var s = FindNode(srcNode);
        var d = FindNode(dstNode);
        if (s is null || d is null) return null;
        if (s.Outputs.All(p => p.Id != srcPort)) return null;
        if (d.Inputs.All(p => p.Id != dstPort)) return null;

        var existing = _connections.FirstOrDefault(c => c.Matches(srcNode, srcPort, dstNode, dstPort));
        if (existing is not null) return existing;

        var conn = new FieldConnection(srcNode, srcPort, dstNode, dstPort);
        _connections.Add(conn);
        Revision++;
        return conn;
    }

    public void Disconnect(FieldConnection connection)
    {
        if (_connections.Remove(connection)) Revision++;
    }

    public void DisconnectPort(Guid node, string port)
    {
        var n = _connections.RemoveAll(c =>
            (c.SourceNode == node && c.SourcePort == port) || (c.DestNode == node && c.DestPort == port));
        if (n > 0) Revision++;
    }

    public void AddGroup(FieldGroup group)
    {
        _groups.Add(group);
        Revision++;
    }

    public void RemoveGroup(Guid id)
    {
        _groups.RemoveAll(g => g.Id == id);
        Revision++;
    }

    public void Clear()
    {
        _nodes.Clear();
        _connections.Clear();
        _groups.Clear();
        Revision++;
    }

    /// <summary>Signals that node parameters/positions changed without a structural edit (bumps <see cref="Revision"/>).</summary>
    public void Touch() => Revision++;
}
