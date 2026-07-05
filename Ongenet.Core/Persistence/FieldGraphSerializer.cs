using System;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Core.Persistence;

/// <summary>
/// Reads/writes a <see cref="FieldGraph"/> to the chunked <see cref="OngenWriter"/>/<see cref="OngenReader"/>
/// format used by projects and presets. Each node is a self-describing chunk (type id, instance id, canvas
/// position, parameters, an optional inline hosted sample, and an optional custom-state blob), so an unknown
/// node type is skipped without corrupting the rest. Connections and groups reference nodes by their stable
/// instance ids. Used by <see cref="Audio.Field.FieldInstrument"/>/<see cref="Audio.Field.FieldEffect"/> as
/// their <see cref="IProjectStatefulComponent"/> payload.
/// </summary>
public static class FieldGraphSerializer
{
    private const int Version = 2; // v2 adds per-node Width/VisualHeight

    public static void Write(OngenWriter w, FieldGraph graph)
    {
        w.WriteInt(Version);

        w.WriteInt(graph.Nodes.Count);
        foreach (var node in graph.Nodes)
        {
            w.WriteChunk(c =>
            {
                c.WriteString(node.TypeId);
                c.WriteGuid(node.Id);
                c.WriteDouble(node.X);
                c.WriteDouble(node.Y);
                c.WriteDouble(node.Width);
                c.WriteDouble(node.VisualHeight);
                ComponentSerializer.WriteParameters(c, node.Parameters);

                // Inline hosted sample (multiple sample nodes per graph, so not the single-host project path).
                if (node is ISampleHost { CurrentSample: { } sample } host)
                {
                    c.WriteBool(true);
                    c.WriteString(host.SampleName ?? "");
                    c.WriteInt(sample.Channels);
                    c.WriteInt(sample.SampleRate);
                    c.WriteInt(sample.Samples.Length);
                    foreach (var s in sample.Samples) c.WriteFloat(s);
                }
                else
                {
                    c.WriteBool(false);
                }

                if (node is IProjectStatefulComponent stateful)
                {
                    c.WriteBool(true);
                    c.WriteChunk(stateful.WriteProjectState);
                }
                else
                {
                    c.WriteBool(false);
                }
            });
        }

        w.WriteInt(graph.Connections.Count);
        foreach (var conn in graph.Connections)
        {
            w.WriteChunk(c =>
            {
                c.WriteGuid(conn.SourceNode);
                c.WriteString(conn.SourcePort);
                c.WriteGuid(conn.DestNode);
                c.WriteString(conn.DestPort);
            });
        }

        w.WriteInt(graph.Groups.Count);
        foreach (var group in graph.Groups)
        {
            w.WriteChunk(c =>
            {
                c.WriteGuid(group.Id);
                c.WriteString(group.Name);
                c.WriteBool(group.Collapsed);
                c.WriteDouble(group.X);
                c.WriteDouble(group.Y);
                c.WriteInt(group.NodeIds.Count);
                foreach (var id in group.NodeIds) c.WriteGuid(id);
            });
        }

        w.WriteDouble(graph.ViewX);
        w.WriteDouble(graph.ViewY);
        w.WriteDouble(graph.Zoom);
    }

    public static void Read(OngenReader r, FieldGraph graph, IFieldNodeRegistry registry)
    {
        graph.Clear();
        var version = r.ReadInt();

        var nodeCount = r.ReadInt();
        for (var i = 0; i < nodeCount; i++)
        {
            r.ReadChunk(c =>
            {
                var typeId = c.ReadString();
                var id = c.ReadGuid();
                var x = c.ReadDouble();
                var y = c.ReadDouble();
                var width = 0.0;
                var visualHeight = 0.0;
                if (version >= 2)
                {
                    width = c.ReadDouble();
                    visualHeight = c.ReadDouble();
                }

                var persisted = ComponentSerializer.ReadParameters(c);

                FieldNode? node = null;
                try { node = registry.TryCreate(typeId); }
                catch { node = null; }

                if (node is not null)
                {
                    node.Id = id;
                    node.X = x;
                    node.Y = y;
                    node.Width = width;
                    node.VisualHeight = visualHeight;
                    ComponentSerializer.ApplyParameters(node.Parameters, persisted);
                }

                if (c.ReadBool())
                {
                    var name = c.ReadString();
                    var channels = c.ReadInt();
                    var rate = c.ReadInt();
                    var len = c.ReadInt();
                    var samples = new float[len];
                    for (var s = 0; s < len; s++) samples[s] = c.ReadFloat();
                    if (node is ISampleHost host) host.LoadSample(new AudioSampleBuffer(samples, channels, rate), name);
                }

                if (c.ReadBool())
                    c.ReadChunk(cc => (node as IProjectStatefulComponent)?.ReadProjectState(cc));

                if (node is not null) graph.AddNode(node);
            });
        }

        var connCount = r.ReadInt();
        for (var i = 0; i < connCount; i++)
        {
            r.ReadChunk(c =>
            {
                var sn = c.ReadGuid();
                var sp = c.ReadString();
                var dn = c.ReadGuid();
                var dp = c.ReadString();
                graph.Connect(sn, sp, dn, dp);
            });
        }

        if (!r.ChunkHasMore) return;
        var groupCount = r.ReadInt();
        for (var i = 0; i < groupCount; i++)
        {
            r.ReadChunk(c =>
            {
                var group = new FieldGroup
                {
                    Id = c.ReadGuid(),
                    Name = c.ReadString(),
                    Collapsed = c.ReadBool(),
                    X = c.ReadDouble(),
                    Y = c.ReadDouble()
                };
                var n = c.ReadInt();
                for (var g = 0; g < n; g++) group.NodeIds.Add(c.ReadGuid());
                graph.AddGroup(group);
            });
        }

        if (!r.ChunkHasMore) return;
        graph.ViewX = r.ReadDouble();
        graph.ViewY = r.ReadDouble();
        graph.Zoom = r.ReadDouble();
    }
}
