using System;

namespace Ongenet.Core.Audio.Field;

/// <summary>
/// A directed patch cord from one node's output port to another node's input port. Ports are referenced
/// by their stable string ids so connections survive save/load and node re-creation.
/// </summary>
public sealed class FieldConnection
{
    public FieldConnection(Guid sourceNode, string sourcePort, Guid destNode, string destPort)
    {
        SourceNode = sourceNode;
        SourcePort = sourcePort;
        DestNode = destNode;
        DestPort = destPort;
    }

    public Guid SourceNode { get; }
    public string SourcePort { get; }
    public Guid DestNode { get; }
    public string DestPort { get; }

    public bool Matches(Guid srcNode, string srcPort, Guid dstNode, string dstPort)
        => SourceNode == srcNode && SourcePort == srcPort && DestNode == dstNode && DestPort == dstPort;
}
