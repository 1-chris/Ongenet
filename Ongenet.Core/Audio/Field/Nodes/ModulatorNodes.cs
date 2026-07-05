using System;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Field.Nodes;

/// <summary>
/// Low-frequency oscillator modulation source. Free-running by default; patch a gate into the retrigger
/// inlet to restart its phase per note (which also makes it per-voice).
/// </summary>
public sealed class LfoNode : FieldNode
{
    public const string Type = "mod.lfo";
    public override string TypeId => Type;
    public override string DisplayName => "LFO";
    public override string Category => FieldNodeCategories.Modulators;

    public double Rate { get; set; } = 2.0;
    public double Depth { get; set; } = 1.0;
    public int WaveIndex { get; set; }
    public double Phase { get; set; }
    public bool Unipolar { get; set; }

    private Lfo[] _lfo = Array.Empty<Lfo>();
    private float[] _prevGate = Array.Empty<float>();

    public LfoNode()
    {
        AddInput("retrig", "Retrig", FieldSignalKind.Note);
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Rate", 0.01, 40, () => Rate, v => Rate = v, "0.00", "Hz", 2.0));
        AddParam(new FloatParameter("Depth", 0, 1, () => Depth, v => Depth = v));
        AddParam(new ChoiceParameter("Wave", new[] { "Sine", "Triangle", "Saw", "Square" }, () => WaveIndex, i => WaveIndex = i), modulatable: false);
        AddParam(new FloatParameter("Phase", 0, 1, () => Phase, v => Phase = v));
        AddParam(new BoolParameter("Unipolar", () => Unipolar, v => Unipolar = v));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _lfo = new Lfo[VoiceCount];
        _prevGate = new float[VoiceCount];
        for (var i = 0; i < VoiceCount; i++) { _lfo[i] = new Lfo(); _lfo[i].Reset(Phase); }
    }

    public override void ResetVoice(int voice)
    {
        if (voice >= _lfo.Length) return;
        _prevGate[voice] = 0f;
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var lfo = _lfo[v];
        lfo.Wave = (LfoWave)Math.Clamp(WaveIndex, 0, 3);
        var retrig = ctx.Input(0);
        var g0 = retrig.Length > 0 ? retrig[0] : 0f;
        if (_prevGate[v] <= 0.5f && g0 > 0.5f) lfo.Reset(Phase);
        _prevGate[v] = g0;

        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate;
        for (var i = 0; i < ctx.Frames; i++)
        {
            lfo.SetRate(ModValue(ctx, 0, Rate, i), sr);
            var raw = lfo.Next();
            if (Unipolar) raw = raw * 0.5 + 0.5;
            outBuf[i] = (float)(raw * ModValue(ctx, 1, Depth, i));
        }
    }
}

/// <summary>Slow smooth random "analog drift" source in [-depth, depth].</summary>
public sealed class DriftNode : FieldNode
{
    public const string Type = "mod.drift";
    public override string TypeId => Type;
    public override string DisplayName => "Drift";
    public override string Category => FieldNodeCategories.Modulators;

    public double Rate { get; set; } = 0.3;
    public double Depth { get; set; } = 1.0;

    private DriftGenerator[] _drift = Array.Empty<DriftGenerator>();

    public DriftNode()
    {
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Rate", 0.01, 10, () => Rate, v => Rate = v, "0.00", "Hz", 2.0));
        AddParam(new FloatParameter("Depth", 0, 1, () => Depth, v => Depth = v));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _drift = new DriftGenerator[VoiceCount];
        for (var i = 0; i < VoiceCount; i++)
        {
            _drift[i] = new DriftGenerator();
            _drift[i].Configure(Rate, format.SampleRate, (uint)(0x2200 + i * 2654435761u));
        }
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _drift.Length) _drift[voice].Configure(Rate, Format.SampleRate, (uint)(0x2200 + voice * 2654435761u));
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var d = _drift[ctx.Voice];
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++) outBuf[i] = (float)(d.Next() * ModValue(ctx, 1, Depth, i));
    }
}

/// <summary>Stepped random (sample &amp; hold) source: holds a new random value at the given rate.</summary>
public sealed class RandomShNode : FieldNode
{
    public const string Type = "mod.random";
    public override string TypeId => Type;
    public override string DisplayName => "Random S&H";
    public override string Category => FieldNodeCategories.Modulators;

    public double Rate { get; set; } = 8.0;
    public double Depth { get; set; } = 1.0;
    public bool Unipolar { get; set; }

    private FastRandom[] _rng = Array.Empty<FastRandom>();
    private double[] _phase = Array.Empty<double>();
    private float[] _held = Array.Empty<float>();

    public RandomShNode()
    {
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Rate", 0.1, 100, () => Rate, v => Rate = v, "0.0", "Hz", 2.0));
        AddParam(new FloatParameter("Depth", 0, 1, () => Depth, v => Depth = v));
        AddParam(new BoolParameter("Unipolar", () => Unipolar, v => Unipolar = v));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _rng = new FastRandom[VoiceCount];
        _phase = new double[VoiceCount];
        _held = new float[VoiceCount];
        for (var i = 0; i < VoiceCount; i++) { _rng[i] = new FastRandom((uint)(0x7700 + i * 40503u)); _held[i] = _rng[i].NextBipolar(); }
    }

    public override void ResetVoice(int voice)
    {
        if (voice >= _phase.Length) return;
        _phase[voice] = 1.0; // force a new sample on the first block
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var rng = _rng[v];
        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate <= 0 ? 44100 : Format.SampleRate;
        var phase = _phase[v];
        var held = _held[v];
        for (var i = 0; i < ctx.Frames; i++)
        {
            phase += Math.Max(0.001, ModValue(ctx, 0, Rate, i)) / sr;
            if (phase >= 1.0)
            {
                phase -= Math.Floor(phase);
                held = rng.NextBipolar();
            }

            var val = Unipolar ? held * 0.5f + 0.5f : held;
            outBuf[i] = val * (float)ModValue(ctx, 1, Depth, i);
        }

        _phase[v] = phase;
        _held[v] = held;
        _rng[v] = rng;
    }
}

/// <summary>A free-running 0..1 phasor (sawtooth ramp) — a phase source for custom modulation.</summary>
public sealed class PhasorNode : FieldNode
{
    public const string Type = "mod.phasor";
    public override string TypeId => Type;
    public override string DisplayName => "Phasor";
    public override string Category => FieldNodeCategories.Modulators;

    public double Rate { get; set; } = 1.0;
    private double[] _phase = Array.Empty<double>();
    private float[] _prevGate = Array.Empty<float>();

    public PhasorNode()
    {
        AddInput("retrig", "Retrig", FieldSignalKind.Note);
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Rate", 0.01, 40, () => Rate, v => Rate = v, "0.00", "Hz", 2.0));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _phase = new double[VoiceCount];
        _prevGate = new float[VoiceCount];
    }

    public override void ResetVoice(int voice)
    {
        if (voice >= _phase.Length) return;
        _phase[voice] = 0;
        _prevGate[voice] = 0f;
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var retrig = ctx.Input(0);
        var g0 = retrig.Length > 0 ? retrig[0] : 0f;
        if (_prevGate[v] <= 0.5f && g0 > 0.5f) _phase[v] = 0;
        _prevGate[v] = g0;

        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate <= 0 ? 44100 : Format.SampleRate;
        var phase = _phase[v];
        for (var i = 0; i < ctx.Frames; i++)
        {
            outBuf[i] = (float)phase;
            phase += Math.Max(0.0, ModValue(ctx, 0, Rate, i)) / sr;
            if (phase >= 1.0) phase -= Math.Floor(phase);
        }

        _phase[v] = phase;
    }
}

/// <summary>A macro knob: outputs a constant control value (with its own modulation inlet, so macros can chain).</summary>
public sealed class MacroNode : FieldNode
{
    public const string Type = "mod.macro";
    public override string TypeId => Type;
    public override string DisplayName => "Macro";
    public override string Category => FieldNodeCategories.Modulators;

    public double Value { get; set; } = 0.0;

    public MacroNode()
    {
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Value", -1, 1, () => Value, v => Value = v, "0.00"));
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++) outBuf[i] = (float)ModValue(ctx, 0, Value, i);
    }
}
