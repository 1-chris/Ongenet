using System;
using System.Linq;
using Ongenet.Core.Audio;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Field;
using Ongenet.Core.Audio.Field.Nodes;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;

namespace Ongenet.Core.Tests.Audio;

public sealed class KarplusAdditiveSmokeTests
{
    private static readonly AudioFormat Fmt = new(44100, 2);

    private static float Peak(ReadOnlySpan<float> buffer)
    {
        var peak = 0f;
        foreach (var s in buffer) peak = Math.Max(peak, Math.Abs(s));
        return peak;
    }

    private static float Rms(ReadOnlySpan<float> buffer)
    {
        double sum = 0;
        foreach (var s in buffer) sum += s * (double)s;
        return (float)Math.Sqrt(sum / Math.Max(1, buffer.Length));
    }

    [Fact]
    public void FieldNodeCatalog_IncludesKarplusAndAdditiveNodes()
    {
        var nodes = new FieldNodeRegistry();
        Assert.NotNull(nodes.TryCreate(KarplusNode.Type));
        Assert.NotNull(nodes.TryCreate(PartialBankNode.Type));
        Assert.NotNull(nodes.TryCreate(SpectralImportNode.Type));
    }

    [Fact]
    public void KarplusNode_RendersFiniteAudio()
    {
        var graph = new FieldGraph();
        var note = new NoteInNode();
        var ks = new KarplusNode();
        var outN = new AudioOutNode();
        graph.AddNode(note);
        graph.AddNode(ks);
        graph.AddNode(outN);
        graph.Connect(note.Id, "pitch", ks.Id, "pitch");
        graph.Connect(note.Id, "gate", ks.Id, "gate");
        graph.Connect(ks.Id, "out", outN.Id, "l");
        graph.Connect(ks.Id, "out", outN.Id, "r");

        var compiled = FieldGraphCompiler.Compile(graph, Fmt, 512, isInstrument: true);
        compiled.NoteOn(60, 1f);
        var buffer = new float[512 * 2];
        compiled.Process(buffer, 120, 0, false, ReadOnlySpan<float>.Empty, 0);
        Assert.True(Rms(buffer) > 1e-4f);
        foreach (var s in buffer) Assert.True(float.IsFinite(s));
    }

    [Fact]
    public void PartialBankNode_RendersFiniteAudio()
    {
        var graph = new FieldGraph();
        var note = new NoteInNode();
        var partials = new PartialBankNode();
        var outN = new AudioOutNode();
        graph.AddNode(note);
        graph.AddNode(partials);
        graph.AddNode(outN);
        graph.Connect(note.Id, "pitch", partials.Id, "pitch");
        graph.Connect(partials.Id, "out", outN.Id, "l");
        graph.Connect(partials.Id, "out", outN.Id, "r");

        var compiled = FieldGraphCompiler.Compile(graph, Fmt, 512, isInstrument: true);
        compiled.NoteOn(69, 1f);
        var buffer = new float[512 * 2];
        compiled.Process(buffer, 120, 0, false, ReadOnlySpan<float>.Empty, 0);
        Assert.True(Rms(buffer) > 1e-4f);
        foreach (var s in buffer) Assert.True(float.IsFinite(s));
    }

    [Fact]
    public void SpectralImport_FeedsPartialBank()
    {
        var graph = new FieldGraph();
        var note = new NoteInNode();
        var osc = new WaveOscNode { WaveIndex = 2, Level = 0.8 };
        var import = new SpectralImportNode { Sensitivity = 0.001 };
        var partials = new PartialBankNode();
        var outN = new AudioOutNode();
        graph.AddNode(note);
        graph.AddNode(osc);
        graph.AddNode(import);
        graph.AddNode(partials);
        graph.AddNode(outN);
        graph.Connect(note.Id, "pitch", osc.Id, "pitch");
        graph.Connect(osc.Id, "out", import.Id, "in");
        graph.Connect(note.Id, "gate", import.Id, "gate");
        graph.Connect(import.Id, "spectrum", partials.Id, "spectrum");
        graph.Connect(note.Id, "pitch", partials.Id, "pitch");
        graph.Connect(partials.Id, "out", outN.Id, "l");
        graph.Connect(partials.Id, "out", outN.Id, "r");

        var compiled = FieldGraphCompiler.Compile(graph, Fmt, 512, isInstrument: true);
        compiled.NoteOn(60, 1f);
        var buffer = new float[512 * 2];
        for (var b = 0; b < 4; b++)
        {
            Array.Clear(buffer);
            compiled.Process(buffer, 120, 0, false, ReadOnlySpan<float>.Empty, 0);
        }

        Assert.True(Rms(buffer) > 1e-4f);
        foreach (var s in buffer) Assert.True(float.IsFinite(s));
    }

    [Fact]
    public void BasicSampler_KarplusFactoryPreset_RendersWithoutSample()
    {
        var inst = FactoryPresets.Definitions.First(d => d.PresetName == "Plucked String").Create();
        Assert.Equal(1, ((BasicSamplerInstrument)inst).VoiceMode);
        inst.Prepare(Fmt);
        inst.NoteOn(60, 0.9f);
        var buf = new float[512];
        for (var b = 0; b < 4; b++)
        {
            Array.Clear(buf);
            inst.Render(buf);
        }

        Assert.True(Peak(buf) > 1e-4f);
    }

    [Fact]
    public void Wavetable_ResynthFromSample_RendersFiniteAudio()
    {
        const int fftSize = 2048;
        var samples = new float[fftSize];
        for (var i = 0; i < fftSize; i++)
            samples[i] = (float)Math.Sin(2.0 * Math.PI * 3.0 * i / fftSize);
        var sample = new AudioSampleBuffer(samples, 1, 44100);

        var inst = new WavetableInstrument { ResynthFromSample = true };
        inst.LoadSample(sample, "sine3");
        inst.Prepare(Fmt);
        inst.NoteOn(60, 0.9f);
        var buf = new float[512 * 2];
        for (var b = 0; b < 8; b++)
        {
            Array.Clear(buf);
            inst.Render(buf);
        }

        Assert.True(Peak(buf) > 1e-4f);
        foreach (var s in buf) Assert.True(float.IsFinite(s));
    }

    [Fact]
    public void AdditivePartialEngine_ImportSpectrum_ProducesAudio()
    {
        var engine = new AdditivePartialEngine();
        engine.SetSampleRate(44100);
        engine.PartialCount = 8;
        var mags = new float[64];
        for (var i = 0; i < mags.Length; i++) mags[i] = 1f / (i + 1);
        engine.ImportSpectrum(mags, mags.Length);
        engine.SetFundamental(440);
        var s = engine.Process();
        Assert.True(Math.Abs(s) <= 1.5f);
        Assert.True(float.IsFinite(s));
    }
}
