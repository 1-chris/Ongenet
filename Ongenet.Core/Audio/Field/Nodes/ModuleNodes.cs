using System;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Files;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Audio.Field.Nodes;

/// <summary>
/// Wraps a whole built-in or plugin <see cref="IInstrument"/> as one Field node. It runs globally with its
/// own polyphony, fed the raw note stream, and exposes the instrument's parameters (each with a modulation
/// inlet) — so any instrument can be combined with Field primitives and modulated. The node type id encodes
/// the wrapped instrument's id (<c>module.inst.&lt;id&gt;</c>) so it round-trips through save/load.
/// </summary>
public sealed class InstrumentModuleNode : FieldNode, IFieldNoteReceiver, IProjectStatefulComponent, ISampleHost
{
    public const string Prefix = "module.inst.";

    private readonly IInstrument _inst;
    private float[] _temp = Array.Empty<float>();

    public InstrumentModuleNode(IInstrument instrument)
    {
        _inst = instrument;
        AddOutput("l", "L");
        AddOutput("r", "R");
        foreach (var p in _inst.Parameters) AddParam(p);
        Build();
    }

    public override string TypeId => Prefix + _inst.TypeId;
    public override string DisplayName => _inst.Name;
    public override string Category => FieldNodeCategories.Modules;
    public override bool ForceGlobal => true;

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _inst.Prepare(format);
        _temp = new float[maxBlock * Math.Max(1, format.Channels)];
    }

    public void NoteOn(int midiNote, float velocity) => _inst.NoteOn(midiNote, velocity);
    public void NoteOff(int midiNote) => _inst.NoteOff(midiNote);
    public void AllNotesOff() => _inst.AllNotesOff();

    public void PitchBend(double semitones)
    {
        var value14 = (int)Math.Clamp(8192 + semitones / 2.0 * 8192.0, 0, 16383);
        _inst.PitchBend(value14);
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var channels = Math.Max(1, Format.Channels);
        var len = ctx.Frames * channels;
        Array.Clear(_temp, 0, len);
        _inst.Render(_temp.AsSpan(0, len));

        var outL = ctx.Output(0);
        var outR = ctx.Output(1);
        for (var f = 0; f < ctx.Frames; f++)
        {
            var b = f * channels;
            outL[f] = _temp[b];
            outR[f] = channels > 1 ? _temp[b + 1] : _temp[b];
        }
    }

    // Delegate hosted-sample and custom-state persistence to the wrapped instrument.
    public string? SampleName => (_inst as ISampleHost)?.SampleName;
    public AudioSampleBuffer? CurrentSample => (_inst as ISampleHost)?.CurrentSample;
    public void LoadSample(AudioSampleBuffer sample, string name) => (_inst as ISampleHost)?.LoadSample(sample, name);

    public void WriteProjectState(OngenWriter writer)
    {
        var has = _inst is IProjectStatefulComponent;
        writer.WriteBool(has);
        if (has) writer.WriteChunk(((IProjectStatefulComponent)_inst).WriteProjectState);
    }

    public void ReadProjectState(OngenReader reader)
    {
        if (!reader.ReadBool()) return;
        reader.ReadChunk(c => (_inst as IProjectStatefulComponent)?.ReadProjectState(c));
    }
}

/// <summary>
/// Wraps a whole built-in or plugin <see cref="IAudioEffect"/> as one global Field node (stereo in/out),
/// exposing its parameters with modulation inlets. Type id: <c>module.fx.&lt;id&gt;</c>.
/// </summary>
public sealed class EffectModuleNode : FieldNode, IProjectStatefulComponent, ISampleHost, IWaveformSource
{
    public const string Prefix = "module.fx.";

    private readonly IAudioEffect _fx;
    private readonly EffectContext _effCtx = new();
    private float[] _temp = Array.Empty<float>();

    public EffectModuleNode(IAudioEffect effect)
    {
        _fx = effect;
        AddInput("l", "L");
        AddInput("r", "R");
        AddOutput("l", "L");
        AddOutput("r", "R");
        foreach (var p in _fx.Parameters) AddParam(p);
        Build();
    }

    public override string TypeId => Prefix + _fx.TypeId;
    public override string DisplayName => _fx.Name;
    public override string Category => FieldNodeCategories.Modules;
    public override bool ForceGlobal => true;

    // Surface a wrapped analyser effect (e.g. the 3D Scope) as an on-graph visualization.
    public override bool HasVisual => _fx is IWaveformSource;
    public int SampleRate => (_fx as IWaveformSource)?.SampleRate ?? (Format.SampleRate <= 0 ? 44100 : Format.SampleRate);
    public int CaptureLatest(float[] dest) => (_fx as IWaveformSource)?.CaptureLatest(dest) ?? 0;

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _fx.Prepare(format);
        _temp = new float[maxBlock * Math.Max(1, format.Channels)];
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var channels = Math.Max(1, Format.Channels);
        var l = ctx.Input(0);
        var r = ctx.Input(1);
        for (var f = 0; f < ctx.Frames; f++)
        {
            var b = f * channels;
            _temp[b] = l[f];
            if (channels > 1) _temp[b + 1] = r[f];
        }

        if (_fx is IContextualEffect contextual)
        {
            _effCtx.Format = Format;
            _effCtx.Bpm = ctx.Bpm;
            _effCtx.PlayheadBeats = ctx.PlayheadBeats;
            _effCtx.Playing = ctx.Playing;
            _effCtx.Sidechain = SidechainBus.Empty;
            contextual.SetContext(_effCtx);
        }

        if (_fx.Enabled) _fx.Process(_temp.AsSpan(0, ctx.Frames * channels));

        var outL = ctx.Output(0);
        var outR = ctx.Output(1);
        for (var f = 0; f < ctx.Frames; f++)
        {
            var b = f * channels;
            outL[f] = _temp[b];
            outR[f] = channels > 1 ? _temp[b + 1] : _temp[b];
        }
    }

    public string? SampleName => (_fx as ISampleHost)?.SampleName;
    public AudioSampleBuffer? CurrentSample => (_fx as ISampleHost)?.CurrentSample;
    public void LoadSample(AudioSampleBuffer sample, string name) => (_fx as ISampleHost)?.LoadSample(sample, name);

    public void WriteProjectState(OngenWriter writer)
    {
        var has = _fx is IProjectStatefulComponent;
        writer.WriteBool(has);
        if (has) writer.WriteChunk(((IProjectStatefulComponent)_fx).WriteProjectState);
    }

    public void ReadProjectState(OngenReader reader)
    {
        if (!reader.ReadBool()) return;
        reader.ReadChunk(c => (_fx as IProjectStatefulComponent)?.ReadProjectState(c));
    }
}
