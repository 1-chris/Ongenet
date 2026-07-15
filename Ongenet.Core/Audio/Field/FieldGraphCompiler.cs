using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Field;

/// <summary>
/// Turns an editable <see cref="FieldGraph"/> into an immutable, allocation-free <see cref="CompiledGraph"/>.
/// It resolves port connections, computes the per-voice/global partition (as a monotonic fixpoint), finds a
/// topological processing order (dropping feedback edges from the ordering while keeping their buffers for a
/// one-block-delay read), and lays out the preallocated buffer pool.
/// </summary>
public static class FieldGraphCompiler
{
    public const int DefaultMaxVoices = 16;

    /// <summary>
    /// Compiles <paramref name="graph"/> for the given format. <paramref name="isInstrument"/> selects
    /// polyphony (per-voice nodes get <paramref name="maxVoices"/> state slots) versus effect mode (a single
    /// always-on voice, additive vs. in-place output).
    /// </summary>
    public static CompiledGraph Compile(FieldGraph graph, AudioFormat format, int maxBlock,
        bool isInstrument, int maxVoices = DefaultMaxVoices)
    {
        if (maxBlock < 1) maxBlock = 1;
        var voiceCount = isInstrument ? Math.Max(1, maxVoices) : 1;

        var nodes = new List<FieldNode>(graph.Nodes);
        var count = nodes.Count;
        var indexOf = new Dictionary<Guid, int>(count);
        for (var i = 0; i < count; i++) indexOf[nodes[i].Id] = i;

        // Prepare only nodes not already prepared for this exact config. Skipping already-prepared nodes on a
        // structural recompile avoids reallocating DSP state that the previously compiled graph — still live
        // on the audio thread until the new snapshot is swapped in — is concurrently using.
        foreach (var node in nodes)
            if (!node.IsPreparedFor(format, maxBlock, voiceCount))
                node.Prepare(format, maxBlock, voiceCount);

        // Resolve connections into per-node, per-input-port edge lists.
        var inEdgesRaw = new List<(int srcNode, int srcPort, bool audio)>[count][];
        var inPortIndex = new Dictionary<string, int>[count];
        var outPortIndex = new Dictionary<string, int>[count];
        for (var i = 0; i < count; i++)
        {
            var n = nodes[i];
            inPortIndex[i] = new Dictionary<string, int>(n.Inputs.Count);
            for (var p = 0; p < n.Inputs.Count; p++) inPortIndex[i][n.Inputs[p].Id] = p;
            outPortIndex[i] = new Dictionary<string, int>(n.Outputs.Count);
            for (var p = 0; p < n.Outputs.Count; p++) outPortIndex[i][n.Outputs[p].Id] = p;

            inEdgesRaw[i] = new List<(int, int, bool)>[n.Inputs.Count];
            for (var p = 0; p < n.Inputs.Count; p++) inEdgesRaw[i][p] = new List<(int, int, bool)>();
        }

        // Asset connections (soundfont/wavetable/…) are resolved once here, not on the audio thread; they
        // are kept out of the buffer graph so they never allocate buffers or affect ordering/polyphony.
        var assetConnections = new List<(int srcNode, string srcPort, int dstNode, string dstPort)>();

        foreach (var conn in graph.Connections)
        {
            if (!indexOf.TryGetValue(conn.SourceNode, out var si)) continue;
            if (!indexOf.TryGetValue(conn.DestNode, out var di)) continue;
            if (!outPortIndex[si].TryGetValue(conn.SourcePort, out var sp)) continue;
            if (!inPortIndex[di].TryGetValue(conn.DestPort, out var dp)) continue;

            if (nodes[di].Inputs[dp].Kind == FieldSignalKind.Asset)
            {
                assetConnections.Add((si, conn.SourcePort, di, conn.DestPort));
                continue;
            }

            var audio = nodes[si].Outputs[sp].Kind == FieldSignalKind.Audio;
            inEdgesRaw[di][dp].Add((si, sp, audio));
        }

        // Resolve asset connections: clear every consumer's asset inlets, then push connected providers'.
        for (var i = 0; i < count; i++)
            if (nodes[i] is IFieldAssetConsumer consumer)
                foreach (var port in nodes[i].Inputs)
                    if (port.Kind == FieldSignalKind.Asset)
                        consumer.SetAsset(port.Id, null);

        foreach (var (srcNode, srcPort, dstNode, dstPort) in assetConnections)
            if (nodes[srcNode] is IFieldAssetProvider provider && nodes[dstNode] is IFieldAssetConsumer consumer)
                consumer.SetAsset(dstPort, provider.GetAsset(srcPort));

        // Dependency set per node (distinct source node indices feeding any of its inputs).
        var deps = new HashSet<int>[count];
        for (var i = 0; i < count; i++)
        {
            deps[i] = new HashSet<int>();
            foreach (var portEdges in inEdgesRaw[i])
                foreach (var e in portEdges)
                    deps[i].Add(e.srcNode);
        }

        // Per-voice partition: monotonic fixpoint. A node is per-voice unless it is forced global; it becomes
        // per-voice if it is a note source or any of its inputs comes from a per-voice source.
        var perVoice = new bool[count];
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < count; i++)
            {
                if (nodes[i].ForceGlobal) { if (perVoice[i]) { perVoice[i] = false; } continue; }
                if (perVoice[i]) continue;
                var pv = nodes[i].IsNoteSource;
                if (!pv)
                {
                    foreach (var src in deps[i])
                        if (perVoice[src]) { pv = true; break; }
                }

                if (pv && isInstrument) { perVoice[i] = true; changed = true; }
            }
        }

        for (var i = 0; i < count; i++) nodes[i].PerVoice = perVoice[i];

        // Topological order (deps before node), dropping back-edges from the ordering only.
        var order = new List<int>(count);
        var state = new byte[count]; // 0 unvisited, 1 visiting, 2 done
        for (var i = 0; i < count; i++)
            if (state[i] == 0) TopoVisit(i, deps, state, order);

        // Allocate output buffer slots.
        var outSlotBase = new int[count][];
        var totalSlots = 0;
        for (var i = 0; i < count; i++)
        {
            var n = nodes[i];
            outSlotBase[i] = new int[n.Outputs.Count];
            var per = perVoice[i] ? voiceCount : 1;
            for (var p = 0; p < n.Outputs.Count; p++)
            {
                outSlotBase[i][p] = totalSlots;
                totalSlots += per;
            }
        }

        var outBuf = AllocateOutputBuffers(
            nodes, order, inEdgesRaw, perVoice, outSlotBase, totalSlots, voiceCount, maxBlock);

        // Build compiled nodes in topological order.
        var compiled = new CompiledGraph.CompiledNode[order.Count];
        var scratchPorts = 1;
        for (var oi = 0; oi < order.Count; oi++)
        {
            var i = order[oi];
            var n = nodes[i];
            if (n.Inputs.Count > scratchPorts) scratchPorts = n.Inputs.Count;

            var inEdges = new CompiledGraph.InEdge[n.Inputs.Count][];
            for (var p = 0; p < n.Inputs.Count; p++)
            {
                var raw = inEdgesRaw[i][p];
                var arr = new CompiledGraph.InEdge[raw.Count];
                for (var e = 0; e < raw.Count; e++)
                {
                    var (srcNode, srcPort, audio) = raw[e];
                    arr[e] = new CompiledGraph.InEdge(srcNode, outSlotBase[srcNode][srcPort], perVoice[srcNode], audio);
                }

                inEdges[p] = arr;
            }

            compiled[oi] = new CompiledGraph.CompiledNode
            {
                Node = n,
                PerVoice = perVoice[i],
                InEdges = inEdges,
                OutSlotBase = outSlotBase[i],
                InputBuffers = new float[n.Inputs.Count][],
                OutputBuffers = new float[n.Outputs.Count][],
                ModByParam = new float[n.Parameters.Count][]
            };
        }

        var voices = new FieldVoiceManager(voiceCount);
        return new CompiledGraph(format, maxBlock, isInstrument, voices, compiled, outBuf, scratchPorts);
    }

    private static float[][] AllocateOutputBuffers(
        List<FieldNode> nodes,
        List<int> order,
        List<(int srcNode, int srcPort, bool audio)>[][] inEdgesRaw,
        bool[] perVoice,
        int[][] outSlotBase,
        int totalSlots,
        int voiceCount,
        int maxBlock)
    {
        var position = new int[nodes.Count];
        for (var oi = 0; oi < order.Count; oi++) position[order[oi]] = oi;

        var lastUse = new int[nodes.Count][];
        var feedback = new bool[nodes.Count][];
        for (var i = 0; i < nodes.Count; i++)
        {
            lastUse[i] = new int[nodes[i].Outputs.Count];
            feedback[i] = new bool[nodes[i].Outputs.Count];
            Array.Fill(lastUse[i], position[i]);
        }

        for (var dst = 0; dst < nodes.Count; dst++)
        {
            foreach (var input in inEdgesRaw[dst])
            foreach (var edge in input)
            {
                var srcPosition = position[edge.srcNode];
                var dstPosition = position[dst];
                if (dstPosition <= srcPosition)
                    feedback[edge.srcNode][edge.srcPort] = true;
                else if (dstPosition > lastUse[edge.srcNode][edge.srcPort])
                    lastUse[edge.srcNode][edge.srcPort] = dstPosition;
            }
        }

        // Logical slots remain stable for the compiled edge table, but non-overlapping lifetimes point
        // at the same physical arrays. Feedback outputs stay dedicated because their previous-block
        // contents are read before the source runs in the next block.
        var result = new float[totalSlots][];
        var available = new Stack<float[]>();
        var active = new List<(int End, float[] Buffer)>();
        for (var oi = 0; oi < order.Count; oi++)
        {
            for (var a = active.Count - 1; a >= 0; a--)
            {
                if (active[a].End >= oi) continue;
                available.Push(active[a].Buffer);
                active.RemoveAt(a);
            }

            var nodeIndex = order[oi];
            var slotsPerPort = perVoice[nodeIndex] ? voiceCount : 1;
            for (var port = 0; port < nodes[nodeIndex].Outputs.Count; port++)
            {
                for (var voice = 0; voice < slotsPerPort; voice++)
                {
                    var buffer = feedback[nodeIndex][port] || available.Count == 0
                        ? new float[maxBlock]
                        : available.Pop();
                    result[outSlotBase[nodeIndex][port] + voice] = buffer;
                    if (!feedback[nodeIndex][port])
                        active.Add((lastUse[nodeIndex][port], buffer));
                }
            }
        }

        return result;
    }

    private static void TopoVisit(int u, HashSet<int>[] deps, byte[] state, List<int> order)
    {
        state[u] = 1;
        foreach (var dep in deps[u])
        {
            if (state[dep] == 0) TopoVisit(dep, deps, state, order);
            // state[dep] == 1 is a back-edge (feedback); skip it in the ordering.
        }

        state[u] = 2;
        order.Add(u);
    }
}
