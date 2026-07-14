using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Midi;
using Ongenet.Core.Audio.MidiFx;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Audio.Containers;

/// <summary>Parallel sum of nested FX branches.</summary>
public sealed class FxLayerEffect : ContainerEffectBase, IAudioRouter, IContextualEffect
{
    private EffectContext? _ctx;

    public FxLayerEffect()
    {
        MutableBranches.Add(CreateBranch(new UtilityEffect()));
        MutableBranches.Add(CreateBranch(new UtilityEffect()));
    }

    public override string Name => "FX Layer";
    public const string TypeId = "container.fx_layer";
    protected override string GetTypeId() => TypeId;
    public int BranchCount => Branches.Count;

    public void SetContext(EffectContext context) => _ctx = context;

    public override void Process(Span<float> buffer)
    {
        if (Branches.Count == 0) return;
        var dry = Scratch.AsSpan(0, buffer.Length);
        buffer.CopyTo(dry);
        buffer.Clear();
        foreach (var branch in Branches)
        {
            dry.CopyTo(Scratch.AsSpan(0, buffer.Length));
            ContainerRenderer.ProcessBranch(branch, Scratch.AsSpan(0, buffer.Length), _ctx);
            for (var i = 0; i < buffer.Length; i++) buffer[i] += Scratch[i];
        }
    }

    private AudioFormat _format = AudioFormat.Default;
    public override void Prepare(AudioFormat format) { _format = format; base.Prepare(format); }

    public override IAudioEffect Clone()
    {
        var c = new FxLayerEffect();
        CloneBranchesInto(c);
        return c;
    }

    /// <summary>Factory preset helper for <see cref="FactoryContainerPresets"/>.</summary>
    public static FxLayerEffect FromChains(params IAudioEffect[][] chains)
    {
        var fx = new FxLayerEffect();
        fx.MutableBranches.Clear();
        foreach (var chain in chains)
            fx.MutableBranches.Add(CreateBranch(chain));
        return fx;
    }
}

/// <summary>One active parallel FX branch.</summary>
public sealed class FxSelectorEffect : ContainerEffectBase, IAudioRouter, IContextualEffect
{
    private int _selected;
    private EffectContext? _ctx;
    private Parameter[]? _parameters;

    public FxSelectorEffect()
    {
        MutableBranches.Add(CreateBranch(new UtilityEffect()));
        MutableBranches.Add(CreateBranch(new FilterEffect()));
    }

    public override string Name => "FX Selector";
    public const string TypeId = "container.fx_selector";
    protected override string GetTypeId() => TypeId;
    public int BranchCount => Branches.Count;

    public int SelectedIndex
    {
        get => _selected;
        set => _selected = Math.Clamp(value, 0, Math.Max(0, Branches.Count - 1));
    }

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Layer", BuildNames(), () => SelectedIndex, v => SelectedIndex = v)
    };

    public void SetContext(EffectContext context) => _ctx = context;

    public override void Process(Span<float> buffer)
    {
        if (SelectedIndex < 0 || SelectedIndex >= Branches.Count) return;
        ContainerRenderer.ProcessBranch(Branches[SelectedIndex], buffer, _ctx);
    }

    private string[] BuildNames()
    {
        var names = new string[Branches.Count];
        for (var i = 0; i < names.Length; i++) names[i] = $"FX {i + 1}";
        return names;
    }

    public override IAudioEffect Clone()
    {
        var c = new FxSelectorEffect { SelectedIndex = SelectedIndex };
        CloneBranchesInto(c);
        return c;
    }
}

/// <summary>Crossover multiband processor with per-band FX chains.</summary>
public sealed class MultibandFxEffect : ContainerEffectBase, IAudioRouter, IContextualEffect
{
    public const string TypeId2 = "container.multiband_fx_2";
    public const string TypeId3 = "container.multiband_fx_3";

    private readonly int _bands;
    private readonly double[] _crossovers;
    private EffectContext? _ctx;
    private BiquadCoefficients _lp = BiquadCoefficients.Identity;
    private BiquadCoefficients _hp = BiquadCoefficients.Identity;
    private Biquad[] _lpState = Array.Empty<Biquad>();
    private Biquad[] _hpState = Array.Empty<Biquad>();
    private float[] _bandBuf = Array.Empty<float>();
    private IReadOnlyList<Parameter>? _parameters;
    private AudioFormat _format = AudioFormat.Default;

    public MultibandFxEffect(int bands)
    {
        _bands = bands;
        _crossovers = bands switch
        {
            2 => new[] { 1000.0 },
            _ => new[] { 250.0, 2500.0 }
        };
        EnsureBranchCount(bands);
        for (var i = 0; i < bands; i++)
            if (Branches[i].Effects.Count == 0) Branches[i].Effects.Add(new UtilityEffect());
    }

    public override string Name => _bands == 2 ? "Multiband FX-2" : "Multiband FX-3";
    protected override string GetTypeId() => _bands == 2 ? TypeId2 : TypeId3;
    public int BranchCount => _bands;

    public double Crossover1Hz { get; set; } = 250;
    public double Crossover2Hz { get; set; } = 2500;

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= BuildParameters();

    private IReadOnlyList<Parameter> BuildParameters()
    {
        if (_bands == 2)
        {
            return new Parameter[]
            {
                new FloatParameter("Crossover", 80, 8000, () => Crossover1Hz, v => Crossover1Hz = v, "0", "Hz", 2.0)
            };
        }

        return new Parameter[]
        {
            new FloatParameter("Low/Mid", 80, 2000, () => Crossover1Hz, v => Crossover1Hz = v, "0", "Hz", 2.0),
            new FloatParameter("Mid/High", 500, 12000, () => Crossover2Hz, v => Crossover2Hz = v, "0", "Hz", 2.0)
        };
    }

    public void SetContext(EffectContext context) => _ctx = context;

    public override void Prepare(AudioFormat format)
    {
        _format = format;
        base.Prepare(format);
        var channels = format.Channels < 1 ? 1 : format.Channels;
        var sr = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _lpState = new Biquad[channels];
        _hpState = new Biquad[channels];
        _lp = BiquadCoefficients.Compute(FilterMode.LowPass, Crossover1Hz, 0.707, sr);
        if (_bands == 2)
            _hp = BiquadCoefficients.Compute(FilterMode.HighPass, Crossover1Hz, 0.707, sr);
        var frames = 4096;
        if (_bandBuf.Length < frames * channels) _bandBuf = new float[frames * channels];
    }

    public override void Process(Span<float> buffer)
    {
        if (_bands == 2) ProcessTwoBand(buffer);
        else ProcessThreeBand(buffer);
    }

    private void ProcessTwoBand(Span<float> buffer)
    {
        var channels = _format.Channels < 1 ? 1 : _format.Channels;
        var frames = buffer.Length / channels;
        EnsureScratch(_format);
        buffer.CopyTo(Scratch.AsSpan(0, buffer.Length));
        buffer.Clear();

        for (var band = 0; band < 2; band++)
        {
            var bandSpan = _bandBuf.AsSpan(0, buffer.Length);
            for (var frame = 0; frame < frames; frame++)
            {
                var i = frame * channels;
                for (var c = 0; c < channels && c < _lpState.Length; c++)
                {
                    var s = Scratch[i + c];
                    var v = band == 0
                        ? (float)_lpState[c].Process(in _lp, s)
                        : (float)_hpState[c].Process(in _hp, s);
                    bandSpan[i + c] = v;
                }
            }

            ContainerRenderer.ProcessBranch(Branches[band], bandSpan, _ctx);
            for (var j = 0; j < buffer.Length; j++) buffer[j] += bandSpan[j];
        }
    }

    private void ProcessThreeBand(Span<float> buffer)
    {
        var channels = _format.Channels < 1 ? 1 : _format.Channels;
        var frames = buffer.Length / channels;
        var sr = _format.SampleRate > 0 ? _format.SampleRate : 44100.0;
        var lpMid = BiquadCoefficients.Compute(FilterMode.LowPass, Crossover1Hz, 0.707, sr);
        var hpMid = BiquadCoefficients.Compute(FilterMode.HighPass, Crossover1Hz, 0.707, sr);
        var lpHigh = BiquadCoefficients.Compute(FilterMode.LowPass, Crossover2Hz, 0.707, sr);
        var hpHigh = BiquadCoefficients.Compute(FilterMode.HighPass, Crossover2Hz, 0.707, sr);
        var midLp = new Biquad[channels];
        var midHp = new Biquad[channels];
        var highLp = new Biquad[channels];
        var highHp = new Biquad[channels];

        EnsureScratch(_format);
        buffer.CopyTo(Scratch.AsSpan(0, buffer.Length));
        buffer.Clear();

        for (var band = 0; band < 3; band++)
        {
            var bandSpan = _bandBuf.AsSpan(0, buffer.Length);
            for (var frame = 0; frame < frames; frame++)
            {
                var i = frame * channels;
                for (var c = 0; c < channels && c < _lpState.Length; c++)
                {
                    var s = Scratch[i + c];
                    float v = band switch
                    {
                        0 => (float)_lpState[c].Process(in _lp, s),
                        1 => (float)midHp[c].Process(in hpMid, midLp[c].Process(in lpMid, s)),
                        _ => (float)highHp[c].Process(in hpHigh, highLp[c].Process(in lpHigh, s))
                    };
                    bandSpan[i + c] = v;
                }
            }

            ContainerRenderer.ProcessBranch(Branches[band], bandSpan, _ctx);
            for (var j = 0; j < buffer.Length; j++) buffer[j] += bandSpan[j];
        }
    }

    public override IAudioEffect Clone()
    {
        var c = new MultibandFxEffect(_bands) { Crossover1Hz = Crossover1Hz, Crossover2Hz = Crossover2Hz };
        CloneBranchesInto(c);
        return c;
    }

    /// <summary>Factory preset helper for <see cref="FactoryContainerPresets"/>.</summary>
    public static MultibandFxEffect FromBands(int bands, double crossover1Hz, double crossover2Hz,
        params IAudioEffect[][] bandChains)
    {
        var fx = new MultibandFxEffect(bands)
        {
            Crossover1Hz = crossover1Hz,
            Crossover2Hz = crossover2Hz
        };
        for (var i = 0; i < bandChains.Length && i < fx.MutableBranches.Count; i++)
        {
            var branch = fx.MutableBranches[i];
            branch.Effects.Clear();
            foreach (var effect in bandChains[i])
                branch.Effects.Add(effect);
        }

        return fx;
    }
}

/// <summary>Mid/side encode → process → decode around two FX branches.</summary>
public sealed class MidSideSplitEffect : ContainerEffectBase, IAudioRouter, IContextualEffect
{

    private EffectContext? _ctx;

    public MidSideSplitEffect()
    {
        EnsureBranchCount(2);
        Branches[0].Effects.Add(new UtilityEffect());
        Branches[1].Effects.Add(new UtilityEffect());
    }

    public override string Name => "Mid-Side Split";
    public const string TypeId = "container.mid_side_split";
    protected override string GetTypeId() => TypeId;
    public int BranchCount => 2;

    public void SetContext(EffectContext context) => _ctx = context;

    public override void Process(Span<float> buffer)
    {
        if (buffer.Length < 2) return;
        var channels = _format.Channels < 1 ? 2 : _format.Channels;
        if (channels < 2) return;
        var frames = buffer.Length / channels;
        EnsureScratch(_format);
        var mid = Scratch.AsSpan(0, buffer.Length);
        var side = _bandBuf.AsSpan(0, buffer.Length);

        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            var l = buffer[i];
            var r = buffer[i + 1];
            mid[i] = (l + r) * 0.5f;
            mid[i + 1] = mid[i];
            side[i] = (l - r) * 0.5f;
            side[i + 1] = side[i];
        }

        ContainerRenderer.ProcessBranch(Branches[0], mid, _ctx);
        ContainerRenderer.ProcessBranch(Branches[1], side, _ctx);

        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            var m = mid[i];
            var s = side[i];
            buffer[i] = m + s;
            buffer[i + 1] = m - s;
        }
    }

    private AudioFormat _format = AudioFormat.Default;
    private float[] _bandBuf = Array.Empty<float>();
    public override void Prepare(AudioFormat format)
    {
        _format = format;
        base.Prepare(format);
        var len = Math.Max(8192, (format.Channels < 1 ? 2 : format.Channels) * 4096);
        if (_bandBuf.Length < len) _bandBuf = new float[len];
    }

    public override IAudioEffect Clone()
    {
        var c = new MidSideSplitEffect();
        CloneBranchesInto(c);
        return c;
    }
}

/// <summary>Independent L/R processing chains.</summary>
public sealed class StereoSplitEffect : ContainerEffectBase, IAudioRouter, IContextualEffect
{

    private EffectContext? _ctx;

    public StereoSplitEffect()
    {
        EnsureBranchCount(2);
        Branches[0].Effects.Add(new UtilityEffect());
        Branches[1].Effects.Add(new UtilityEffect());
    }

    public override string Name => "Stereo Split";
    public const string TypeId = "container.stereo_split";
    protected override string GetTypeId() => TypeId;
    public int BranchCount => 2;

    public void SetContext(EffectContext context) => _ctx = context;

    public override void Process(Span<float> buffer)
    {
        if (buffer.Length < 2) return;
        var channels = _format.Channels < 1 ? 2 : _format.Channels;
        if (channels < 2) return;
        var frames = buffer.Length / channels;
        EnsureScratch(_format);
        var left = Scratch.AsSpan(0, buffer.Length);
        var right = _bandBuf.AsSpan(0, buffer.Length);

        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            left[i] = buffer[i];
            left[i + 1] = buffer[i];
            right[i] = buffer[i + 1];
            right[i + 1] = buffer[i + 1];
        }

        ContainerRenderer.ProcessBranch(Branches[0], left, _ctx);
        ContainerRenderer.ProcessBranch(Branches[1], right, _ctx);

        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            buffer[i] = left[i];
            buffer[i + 1] = right[i];
        }
    }

    private AudioFormat _format = AudioFormat.Default;
    private float[] _bandBuf = Array.Empty<float>();
    public override void Prepare(AudioFormat format)
    {
        _format = format;
        base.Prepare(format);
        var len = Math.Max(8192, (format.Channels < 1 ? 2 : format.Channels) * 4096);
        if (_bandBuf.Length < len) _bandBuf = new float[len];
    }

    public override IAudioEffect Clone()
    {
        var c = new StereoSplitEffect();
        CloneBranchesInto(c);
        return c;
    }
}

/// <summary>2-axis morph between two FX branches.</summary>
public sealed class XyFxEffect : ContainerEffectBase, IAudioRouter, IContextualEffect
{

    private double _x = 0.5;
    private double _y = 0.5;
    private EffectContext? _ctx;
    private Parameter[]? _parameters;

    public XyFxEffect()
    {
        MutableBranches.Add(CreateBranch(new UtilityEffect()));
        MutableBranches.Add(CreateBranch(new FilterEffect()));
    }

    public override string Name => "XY FX";
    public const string TypeId = "container.xy_fx";
    protected override string GetTypeId() => TypeId;
    
    public int BranchCount => 2;

    public double X { get => _x; set => _x = Math.Clamp(value, 0, 1); }
    public double Y { get => _y; set => _y = Math.Clamp(value, 0, 1); }

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("X", 0, 1, () => X, v => X = v, "0.00"),
        new FloatParameter("Y", 0, 1, () => Y, v => Y = v, "0.00")
    };

    public void SetContext(EffectContext context) => _ctx = context;

    public override void Process(Span<float> buffer)
    {
        EnsureScratch(_format);
        buffer.CopyTo(Scratch.AsSpan(0, buffer.Length));
        var b = _bandBuf.AsSpan(0, buffer.Length);
        Scratch.AsSpan(0, buffer.Length).CopyTo(b);
        ContainerRenderer.ProcessBranch(Branches[0], Scratch.AsSpan(0, buffer.Length), _ctx);
        ContainerRenderer.ProcessBranch(Branches[1], b, _ctx);
        var mix = (float)(X * Y);
        for (var i = 0; i < buffer.Length; i++)
            buffer[i] = Scratch[i] * (1f - mix) + b[i] * mix;
    }

    private AudioFormat _format = AudioFormat.Default;
    private float[] _bandBuf = Array.Empty<float>();
    public override void Prepare(AudioFormat format)
    {
        _format = format;
        base.Prepare(format);
        var len = Math.Max(8192, (format.Channels < 1 ? 1 : format.Channels) * 4096);
        if (_bandBuf.Length < len) _bandBuf = new float[len];
    }

    public override IAudioEffect Clone()
    {
        var c = new XyFxEffect { X = X, Y = Y };
        CloneBranchesInto(c);
        return c;
    }
}

/// <summary>Mixes audio from another track via the sidechain bus.</summary>
public sealed class AudioReceiverEffect : ContainerEffectBase, IContextualEffect, ISourceTrackEffect
{

    private EffectContext? _ctx;
    private double _mix = 1.0;
    private Parameter[]? _parameters;

    public AudioReceiverEffect()
    {
        EnsureBranchCount(1);
        Branches[0].Effects.Add(new UtilityEffect());
    }

    public override string Name => "Audio Receiver";
    public const string TypeId = "container.audio_receiver";
    protected override string GetTypeId() => TypeId;
    

    public Guid? SourceTrackId { get; set; }
    public double Mix { get => _mix; set => _mix = Math.Clamp(value, 0, 1); }

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Mix", 0, 1, () => Mix, v => Mix = v, "0.00")
    };

    public void SetContext(EffectContext context) => _ctx = context;

    public override void Process(Span<float> buffer)
    {
        EnsureScratch(_format);
        var received = false;
        if (_ctx?.Sidechain is { } bus && SourceTrackId is { } id)
        {
            bus.Request(id);
            var src = bus.Read(id, out _);
            if (src.Length >= buffer.Length)
            {
                src.Slice(0, buffer.Length).CopyTo(Scratch);
                ContainerRenderer.ProcessBranch(Branches[0], Scratch.AsSpan(0, buffer.Length), _ctx);
                received = true;
            }
        }

        if (!received) return;
        var mix = (float)Mix;
        for (var i = 0; i < buffer.Length; i++)
            buffer[i] = buffer[i] * (1f - mix) + Scratch[i] * mix;
    }

    private AudioFormat _format = AudioFormat.Default;
    public override void Prepare(AudioFormat format) { _format = format; base.Prepare(format); }

    public override void WriteProjectState(OngenWriter writer)
    {
        base.WriteProjectState(writer);
        writer.WriteNullableGuid(SourceTrackId);
    }

    public override void ReadProjectState(OngenReader reader)
    {
        base.ReadProjectState(reader);
        if (reader.ChunkHasMore) SourceTrackId = reader.ReadNullableGuid();
    }

    public override IAudioEffect Clone()
    {
        var c = new AudioReceiverEffect { SourceTrackId = SourceTrackId, Mix = Mix };
        CloneBranchesInto(c);
        return c;
    }
}

/// <summary>Base for note-FX container effects with parallel or selected MIDI-FX chains.</summary>
public abstract class NoteFxContainerBase : IAudioEffect, IContainerEffect, IProjectStatefulComponent,
    IMidiAwareEffect
{
    protected readonly List<List<IMidiEffect>> Chains = new();
    private readonly List<IAudioEffect> _childFx = new();

    public abstract string Name { get; }

    string IAudioEffect.TypeId => GetTypeId();
    protected abstract string GetTypeId();

    public bool Enabled { get; set; } = true;
    public virtual IReadOnlyList<Parameter> Parameters { get; } = Array.Empty<Parameter>();
    public IReadOnlyList<IAudioEffect> Children => _childFx;

    public void Prepare(AudioFormat format) { }

    public void Process(Span<float> buffer) { }

    public abstract IAudioEffect Clone();

    public void HandleMidi(in MidiMessage message)
    {
        if (!Enabled) return;
        foreach (var chain in ActiveChains())
        foreach (var fx in chain)
        {
            if (!fx.Enabled) continue;
            foreach (var __ in fx.Process(message)) { }
        }
    }

    public void AllNotesOff()
    {
        foreach (var chain in Chains)
        foreach (var fx in chain) fx.Reset();
    }

    protected abstract IEnumerable<List<IMidiEffect>> ActiveChains();

    public virtual void WriteProjectState(OngenWriter writer)
    {
        writer.WriteInt(Chains.Count);
        foreach (var chain in Chains) ContainerPersistence.WriteMidiEffectChain(writer, chain);
    }

    public virtual void ReadProjectState(OngenReader reader)
    {
        if (ContainerReadContext.Current?.MidiEffects is not { } registry)
        {
            reader.ReadInt();
            return;
        }

        Chains.Clear();
        var count = reader.ReadInt();
        for (var i = 0; i < count; i++)
        {
            var chain = new List<IMidiEffect>();
            ContainerPersistence.ReadMidiEffectChain(reader, chain, registry, ContainerReadContext.Current.Warnings);
            Chains.Add(chain);
        }
    }
}

/// <summary>Parallel note-FX chains (all active).</summary>
public sealed class NoteFxLayerEffect : NoteFxContainerBase
{

    public NoteFxLayerEffect()
    {
        Chains.Add(new List<IMidiEffect> { new ScaleMidiEffect() });
        Chains.Add(new List<IMidiEffect> { new HumanizeMidiEffect() });
    }

    public override string Name => "Note FX Layer";
    public const string TypeId = "container.note_fx_layer";
    protected override string GetTypeId() => TypeId;

    protected override IEnumerable<List<IMidiEffect>> ActiveChains() => Chains;

    public override IAudioEffect Clone()
    {
        var c = new NoteFxLayerEffect();
        foreach (var chain in Chains)
        {
            var copy = new List<IMidiEffect>();
            foreach (var fx in chain) copy.Add(fx.Clone());
            c.Chains.Add(copy);
        }

        return c;
    }
}

/// <summary>One active note-FX chain.</summary>
public sealed class NoteFxSelectorEffect : NoteFxContainerBase
{

    private int _selected;
    private Parameter[]? _parameters;

    public NoteFxSelectorEffect()
    {
        Chains.Add(new List<IMidiEffect> { new ScaleMidiEffect() });
        Chains.Add(new List<IMidiEffect> { new ArpMidiEffect() });
    }

    public override string Name => "Note FX Selector";
    public const string TypeId = "container.note_fx_selector";
    protected override string GetTypeId() => TypeId;

    public int SelectedIndex
    {
        get => _selected;
        set => _selected = Math.Clamp(value, 0, Math.Max(0, Chains.Count - 1));
    }

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Chain", BuildNames(), () => SelectedIndex, v => SelectedIndex = v)
    };

    private string[] BuildNames()
    {
        var names = new string[Chains.Count];
        for (var i = 0; i < names.Length; i++) names[i] = $"Chain {i + 1}";
        return names;
    }

    protected override IEnumerable<List<IMidiEffect>> ActiveChains()
    {
        if (SelectedIndex >= 0 && SelectedIndex < Chains.Count) yield return Chains[SelectedIndex];
    }

    public override IAudioEffect Clone()
    {
        var c = new NoteFxSelectorEffect { SelectedIndex = SelectedIndex };
        foreach (var chain in Chains)
        {
            var copy = new List<IMidiEffect>();
            foreach (var fx in chain) copy.Add(fx.Clone());
            c.Chains.Add(copy);
        }

        return c;
    }
}

/// <summary>Receives/forwards MIDI from a configured source track id (stored for routing UI).</summary>
public sealed class NoteReceiverEffect : NoteFxContainerBase, ISourceTrackEffect
{

    public NoteReceiverEffect() => Chains.Add(new List<IMidiEffect> { new QuantizeMidiEffect() });

    public override string Name => "Note Receiver";
    public const string TypeId = "container.note_receiver";
    protected override string GetTypeId() => TypeId;

    public Guid? SourceTrackId { get; set; }

    protected override IEnumerable<List<IMidiEffect>> ActiveChains() => Chains;

    public override void WriteProjectState(OngenWriter writer)
    {
        base.WriteProjectState(writer);
        writer.WriteNullableGuid(SourceTrackId);
    }

    public override void ReadProjectState(OngenReader reader)
    {
        base.ReadProjectState(reader);
        if (reader.ChunkHasMore) SourceTrackId = reader.ReadNullableGuid();
    }

    public override IAudioEffect Clone()
    {
        var c = new NoteReceiverEffect { SourceTrackId = SourceTrackId };
        foreach (var chain in Chains)
        {
            var copy = new List<IMidiEffect>();
            foreach (var fx in chain) copy.Add(fx.Clone());
            c.Chains.Add(copy);
        }

        return c;
    }
}
