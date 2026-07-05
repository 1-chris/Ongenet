using System;
using System.IO;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Field.Nodes;
using Ongenet.Core.Audio.Field.Patches;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Tests.Field;

public class FieldEngineTests
{
    private static readonly AudioFormat Fmt = new(44100, 2);

    private static float Rms(ReadOnlySpan<float> buffer)
    {
        double sum = 0;
        foreach (var s in buffer) sum += s * (double)s;
        return (float)Math.Sqrt(sum / Math.Max(1, buffer.Length));
    }

    [Fact]
    public void BeginnerInstrumentProducesSoundOnNote()
    {
        var reg = new FieldNodeRegistry();
        var inst = new FieldInstrument(reg);
        inst.Prepare(Fmt);

        var buffer = new float[512 * 2];

        // No note: silent.
        Array.Clear(buffer);
        inst.Render(buffer);
        Assert.True(Rms(buffer) < 1e-6f);

        // Note on: after a couple of blocks (queued events applied on render) there should be signal.
        inst.NoteOn(69, 1.0f); // A4
        Array.Clear(buffer);
        inst.Render(buffer);
        Array.Clear(buffer);
        inst.Render(buffer);
        Assert.True(Rms(buffer) > 1e-3f, "expected audible output while a note is held");
        foreach (var s in buffer) Assert.True(float.IsFinite(s));
    }

    [Fact]
    public void ReleasedNoteDecaysToSilenceAndFreesVoice()
    {
        var reg = new FieldNodeRegistry();
        var inst = new FieldInstrument(reg);
        inst.Prepare(Fmt);
        var buffer = new float[512 * 2];

        inst.NoteOn(60, 1.0f);
        for (var i = 0; i < 4; i++) { Array.Clear(buffer); inst.Render(buffer); }
        inst.NoteOff(60);

        // The beginner ADSR release is 0.3 s (~26 blocks of 512). Render well past it.
        float last = 1f;
        for (var i = 0; i < 200; i++) { Array.Clear(buffer); inst.Render(buffer); last = Rms(buffer); }
        Assert.True(last < 1e-5f, "released note should decay to silence");
    }

    [Fact]
    public void EffectPassesAudioThrough()
    {
        var reg = new FieldNodeRegistry();
        var fx = new FieldEffect(reg);
        fx.Prepare(Fmt);

        var buffer = new float[256 * 2];
        for (var i = 0; i < buffer.Length; i++) buffer[i] = 0.5f;
        fx.Process(buffer);
        // The beginner effect is a pass-through; output should stay near the input level and be finite.
        Assert.True(Rms(buffer) > 0.1f);
        foreach (var s in buffer) Assert.True(float.IsFinite(s));
    }

    [Fact]
    public void GraphSerializationRoundTrips()
    {
        var reg = new FieldNodeRegistry();
        var graph = new FieldGraph();
        FieldPatches.BuildBeginnerInstrument(graph);
        var nodeCount = graph.Nodes.Count;
        var connCount = graph.Connections.Count;

        using var ms = new MemoryStream();
        using (var w = new OngenWriter(ms)) FieldGraphSerializer.Write(w, graph);
        ms.Position = 0;

        var restored = new FieldGraph();
        using (var r = new OngenReader(ms)) FieldGraphSerializer.Read(r, restored, reg);

        Assert.Equal(nodeCount, restored.Nodes.Count);
        Assert.Equal(connCount, restored.Connections.Count);
    }

    [Fact]
    public void CompilerHandlesFeedbackWithoutHanging()
    {
        // A feedback loop: delay output feeds an add back into itself. Must compile and run finitely.
        var reg = new FieldNodeRegistry();
        var graph = new FieldGraph();
        var inNode = new AudioInNode { X = 0, Y = 0 };
        var add = new AddNode { X = 100, Y = 0 };
        var delay = new DelayNode { X = 200, Y = 0, Feedback = 0.0, Mix = 1.0, TimeMs = 5 };
        var outNode = new AudioOutNode { X = 300, Y = 0 };
        graph.AddNode(inNode);
        graph.AddNode(add);
        graph.AddNode(delay);
        graph.AddNode(outNode);
        graph.Connect(inNode.Id, "l", add.Id, "a");
        graph.Connect(delay.Id, "out", add.Id, "b"); // feedback edge
        graph.Connect(add.Id, "out", delay.Id, "in");
        graph.Connect(delay.Id, "out", outNode.Id, "l");
        graph.Connect(delay.Id, "out", outNode.Id, "r");

        var compiled = FieldGraphCompiler.Compile(graph, Fmt, 256, isInstrument: false);
        var buffer = new float[256 * 2];
        for (var i = 0; i < buffer.Length; i++) buffer[i] = 0.25f;
        compiled.Process(buffer, 120, 0, false, ReadOnlySpan<float>.Empty, 0);
        foreach (var s in buffer) Assert.True(float.IsFinite(s));
    }

    [Fact]
    public void ModulationInletDrivesParameter()
    {
        // A macro at value 1 modulating an oscillator's Level (via its mod inlet) should change output.
        var reg = new FieldNodeRegistry();
        var graph = new FieldGraph();
        var note = new NoteInNode();
        var osc = new WaveOscNode { WaveIndex = 0, Level = 0.0 }; // silent unless modulated
        var macro = new MacroNode { Value = 1.0 };
        var outNode = new AudioOutNode();
        graph.AddNode(note);
        graph.AddNode(osc);
        graph.AddNode(macro);
        graph.AddNode(outNode);
        graph.Connect(note.Id, "pitch", osc.Id, "pitch");
        graph.Connect(macro.Id, "out", osc.Id, "mod:4"); // Level is parameter index 4 on WaveOscNode
        graph.Connect(osc.Id, "out", outNode.Id, "l");
        graph.Connect(osc.Id, "out", outNode.Id, "r");

        var compiled = FieldGraphCompiler.Compile(graph, Fmt, 256, isInstrument: true);
        compiled.NoteOn(69, 1.0f);
        var buffer = new float[256 * 2];
        compiled.Process(buffer, 120, 0, false, ReadOnlySpan<float>.Empty, 0);
        Assert.True(Rms(buffer) > 1e-3f, "modulation should raise the oscillator level above zero");
    }
}
