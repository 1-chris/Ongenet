using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Models.Audio;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Audio.Containers;

/// <summary>Drum machine: note→pad routing with nested instrument slots.</summary>
public sealed class DrumMachineInstrument : ContainerInstrumentBase, INoteRouter
{
    public const string TypeId = "container.drum_machine";
    protected override string GetTypeId() => TypeId;

    private readonly int[] _padNotes = new int[16];
    private Parameter[]? _parameters;

    public DrumMachineInstrument()
    {
        // Default pads at C4–G4 so the inspector keyboard and piano roll (middle octave) trigger pads.
        for (var i = 0; i < 8; i++)
        {
            _padNotes[i] = 60 + i;
            var drum = new DrumModelInstrument();
            drum.ApplyModel(i switch
            {
                0 => 27, // Kick
                1 => 25, // Snare
                2 => 21, // Closed hat
                3 => 17, // Tom
                4 => 28, // Perc
                5 => 16, // Rim
                6 => 14, // Hat
                7 => 18, // Tom high
                _ => 27
            });
            MutableChildren.Add(CreateSlot(drum, enabled: true));
        }
    }

    public override string Name => "Drum Machine";

    public override int? MaxChildren => 16;
    public override int MinChildren => 1;

    public int GetPadMidiNote(int index)
        => index >= 0 && index < _padNotes.Length ? _padNotes[index] : 60 + index;

    public double MasterGain { get; set; } = 0.85;

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Gain", 0, 1, () => MasterGain, v => MasterGain = v, "0.00")
    };

    public int[]? RouteNote(int midiNote, float velocity)
    {
        for (var i = 0; i < Children.Count; i++)
        {
            var padNote = i < _padNotes.Length ? _padNotes[i] : 60 + i;
            if (padNote == midiNote) return new[] { i };
        }

        // GM drum lane (36+) and chromatic fallback from the first pad note.
        var baseNote = _padNotes.Length > 0 ? _padNotes[0] : 60;
        if (midiNote >= baseNote && midiNote < baseNote + Children.Count)
            return new[] { midiNote - baseNote };
        if (midiNote >= 36 && midiNote < 36 + Children.Count)
            return new[] { midiNote - 36 };

        return Array.Empty<int>();
    }

    public override void Render(Span<float> buffer)
    {
        base.Render(buffer);
        if (Math.Abs(MasterGain - 1.0) > 1e-6)
        {
            for (var i = 0; i < buffer.Length; i++) buffer[i] *= (float)MasterGain;
        }
    }

    public override void WriteProjectState(OngenWriter writer)
    {
        base.WriteProjectState(writer);
        writer.WriteInt(_padNotes.Length);
        foreach (var n in _padNotes) writer.WriteInt(n);
    }

    public override void ReadProjectState(OngenReader reader)
    {
        base.ReadProjectState(reader);
        if (!reader.ChunkHasMore) return;
        var count = reader.ReadInt();
        for (var i = 0; i < count && i < _padNotes.Length; i++) _padNotes[i] = reader.ReadInt();
    }

    public override IInstrument Clone()
    {
        var c = new DrumMachineInstrument { MasterGain = MasterGain };
        CloneChildrenInto(c);
        for (var i = 0; i < _padNotes.Length; i++) c._padNotes[i] = _padNotes[i];
        return c;
    }
}

/// <summary>Parallel sum of nested instruments with per-layer gain.</summary>
public sealed class InstrumentLayerInstrument : ContainerInstrumentBase, IAudioRouter
{
    public const string TypeId = "container.instrument_layer";
    protected override string GetTypeId() => TypeId;

    public InstrumentLayerInstrument()
    {
        MutableChildren.Add(CreateSlot(new OscillatorInstrument { Waveform = Waveform.Sawtooth }));
        MutableChildren.Add(CreateSlot(new OscillatorInstrument { Waveform = Waveform.Square }));
    }

    public override string Name => "Instrument Layer";
    public int BranchCount => Children.Count;

    public override bool CanAddChildren => true;
    public override bool CanRemoveChildren => true;
    public override int MinChildren => 1;

    public override IInstrument Clone()
    {
        var c = new InstrumentLayerInstrument();
        CloneChildrenInto(c);
        return c;
    }
}

/// <summary>One active nested instrument selected by a choice parameter.</summary>
public sealed class InstrumentSelectorInstrument : ContainerInstrumentBase, INoteRouter
{
    public const string TypeId = "container.instrument_selector";
    protected override string GetTypeId() => TypeId;

    private int _selected;
    private Parameter[]? _parameters;

    public InstrumentSelectorInstrument()
    {
        MutableChildren.Add(CreateSlot(new OscillatorInstrument()));
        MutableChildren.Add(CreateSlot(new BassSynthInstrument()));
    }

    public override string Name => "Instrument Selector";

    public override int? MaxChildren => 2;
    public override int MinChildren => 1;

    public int SelectedIndex
    {
        get => _selected;
        set => _selected = Math.Clamp(value, 0, Math.Max(0, Children.Count - 1));
    }

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new ChoiceParameter("Instrument", BuildNames(), () => SelectedIndex, v => SelectedIndex = v)
    };

    public int[]? RouteNote(int midiNote, float velocity) => new[] { SelectedIndex };

    public override void Render(Span<float> buffer)
        => ContainerRenderer.RenderChild(this, SelectedIndex, buffer);

    private string[] BuildNames()
    {
        var names = new string[Children.Count];
        for (var i = 0; i < names.Length; i++) names[i] = $"Layer {i + 1}";
        return names;
    }

    public override IInstrument Clone()
    {
        var c = new InstrumentSelectorInstrument { SelectedIndex = SelectedIndex };
        CloneChildrenInto(c);
        return c;
    }
}

/// <summary>Serial instrument + post insert-FX chain.</summary>
public sealed class ChainInstrument : ContainerInstrumentBase
{
    public const string TypeId = "container.chain";
    protected override string GetTypeId() => TypeId;

    private readonly List<IAudioEffect> _postEffects = new();
    private EffectContext? _ctx;
    private AudioFormat _format = AudioFormat.Default;

    public ChainInstrument()
    {
        MutableChildren.Add(CreateSlot(new OscillatorInstrument()));
        _postEffects.Add(new UtilityEffect());
    }

    public override string Name => "Chain";

    public List<IAudioEffect> EditablePostEffects => _postEffects;

    public IReadOnlyList<IAudioEffect> PostEffects => _postEffects;

    public void SetContext(EffectContext ctx) => _ctx = ctx;

    public override void Prepare(AudioFormat format)
    {
        _format = format;
        base.Prepare(format);
        foreach (var fx in _postEffects) fx.Prepare(format);
    }

    public override void Render(Span<float> buffer)
    {
        if (Children.Count == 0) { buffer.Clear(); return; }
        EnsureScratch(_format);
        ContainerRenderer.RenderChild(this, 0, Scratch, _ctx);
        Scratch.AsSpan(0, buffer.Length).CopyTo(buffer);
        ContainerRenderer.ProcessEffectChain(_postEffects, buffer, _ctx);
    }

    public override void WriteProjectState(OngenWriter writer)
    {
        base.WriteProjectState(writer);
        var store = ContainerWriteContext.Current?.Store ?? new SampleStore();
        ContainerPersistence.WriteEffectChain(writer, _postEffects, store);
    }

    public override void ReadProjectState(OngenReader reader)
    {
        base.ReadProjectState(reader);
        if (ContainerReadContext.Current is not { } ctx || !reader.ChunkHasMore) return;
        ContainerPersistence.ReadEffectChain(reader, _postEffects, ctx.Instruments, ctx.Effects,
            ctx.MidiEffects, ctx.SampleLookup, ctx.Warnings);
    }

    public override IInstrument Clone()
    {
        var c = new ChainInstrument();
        CloneChildrenInto(c);
        foreach (var fx in _postEffects) c._postEffects.Add(fx.Clone());
        return c;
    }
}

/// <summary>4-corner XY morph between nested instruments (bilinear crossfade).</summary>
public sealed class XyInstrument : ContainerInstrumentBase, IAudioRouter
{
    public const string TypeId = "container.xy_instrument";
    protected override string GetTypeId() => TypeId;

    private double _x = 0.5;
    private double _y = 0.5;
    private AudioFormat _format = AudioFormat.Default;
    private float[] _scratchB = Array.Empty<float>();
    private float[] _scratchC = Array.Empty<float>();
    private float[] _scratchD = Array.Empty<float>();
    private Parameter[]? _parameters;

    public XyInstrument()
    {
        MutableChildren.Add(CreateSlot(new OscillatorInstrument { Waveform = Waveform.Sawtooth }));
        MutableChildren.Add(CreateSlot(new BassSynthInstrument()));
        MutableChildren.Add(CreateSlot(new TripleOscInstrument()));
        MutableChildren.Add(CreateSlot(new OscillatorInstrument { Waveform = Waveform.Square }));
    }

    public override string Name => "XY Instrument";
    public int BranchCount => 4;

    public override int? MaxChildren => 4;
    public override int MinChildren => 4;

    public double X { get => _x; set => _x = Math.Clamp(value, 0, 1); }
    public double Y { get => _y; set => _y = Math.Clamp(value, 0, 1); }

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("X", 0, 1, () => X, v => X = v, "0.00"),
        new FloatParameter("Y", 0, 1, () => Y, v => Y = v, "0.00")
    };

    public override void Prepare(AudioFormat format)
    {
        _format = format;
        base.Prepare(format);
        var len = Math.Max(8192, (format.Channels < 1 ? 1 : format.Channels) * 4096);
        if (_scratchB.Length < len) _scratchB = new float[len];
        if (_scratchC.Length < len) _scratchC = new float[len];
        if (_scratchD.Length < len) _scratchD = new float[len];
    }

    public override void Render(Span<float> buffer)
    {
        if (Children.Count < 4) { buffer.Clear(); return; }

        EnsureScratch(_format);
        Span<float> scratch = Scratch;
        var b = _scratchB.AsSpan(0, buffer.Length);
        var c = _scratchC.AsSpan(0, buffer.Length);
        var d = _scratchD.AsSpan(0, buffer.Length);

        ContainerRenderer.RenderChild(this, 0, scratch);
        ContainerRenderer.RenderChild(this, 1, b);
        ContainerRenderer.RenderChild(this, 2, c);
        ContainerRenderer.RenderChild(this, 3, d);

        Span<float> w = stackalloc float[4];
        XyMorphMath.CornerWeights(X, Y, w);

        for (var i = 0; i < buffer.Length; i++)
            buffer[i] = scratch[i] * w[0] + b[i] * w[1] + c[i] * w[2] + d[i] * w[3];
    }

    public override IInstrument Clone()
    {
        var c = new XyInstrument { X = X, Y = Y };
        CloneChildrenInto(c);
        return c;
    }
}

/// <summary>Routes notes to a child instrument with optional velocity shaping (replacement trigger).</summary>
public sealed class ReplacerInstrument : ContainerInstrumentBase
{
    public const string TypeId = "container.replacer";
    protected override string GetTypeId() => TypeId;

    private double _velocityScale = 1.0;
    private Parameter[]? _parameters;

    public ReplacerInstrument() => MutableChildren.Add(CreateSlot(new BasicSamplerInstrument()));

    public override string Name => "Replacer";

    public override int? MaxChildren => 1;
    public override int MinChildren => 1;

    public double VelocityScale
    {
        get => _velocityScale;
        set => _velocityScale = Math.Clamp(value, 0, 2);
    }

    public override IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Velocity", 0, 2, () => VelocityScale, v => VelocityScale = v, "0.00")
    };

    public override void NoteOn(int midiNote, float velocity)
    {
        if (Children.Count == 0) return;
        var slot = Children[0];
        if (!slot.Enabled) return;
        slot.Instrument.AllNotesOff();
        slot.Instrument.NoteOn(midiNote, velocity * (float)VelocityScale);
    }

    public override void Render(Span<float> buffer)
        => ContainerRenderer.RenderChild(this, 0, buffer);

    public override IInstrument Clone()
    {
        var c = new ReplacerInstrument { VelocityScale = VelocityScale };
        CloneChildrenInto(c);
        return c;
    }
}
