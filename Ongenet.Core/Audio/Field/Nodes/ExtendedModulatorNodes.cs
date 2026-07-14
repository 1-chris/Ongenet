using System;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Modulation;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Field.Nodes;

/// <summary>MSEG-style multi-segment CV source (mirrors <see cref="SegmentsModulator"/>).</summary>
public sealed class SegmentsNode : FieldNode
{
    public const string Type = "mod.segments";
    public override string TypeId => Type;
    public override string DisplayName => "Segments";
    public override string Category => FieldNodeCategories.Modulators;

    public double Rate { get; set; } = 1.0;
    public double S0 { get; set; }
    public double S1 { get; set; } = 0.5;
    public double S2 { get; set; } = 1.0;
    public double S3 { get; set; } = 0.5;
    public double S4 { get; set; }

    private double[] _phase = Array.Empty<double>();

    public SegmentsNode()
    {
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Rate", 0.01, 20, () => Rate, v => Rate = v, "0.00", "Hz", 2.0));
        AddParam(new FloatParameter("S0", 0, 1, () => S0, v => S0 = v, "0.00"));
        AddParam(new FloatParameter("S1", 0, 1, () => S1, v => S1 = v, "0.00"));
        AddParam(new FloatParameter("S2", 0, 1, () => S2, v => S2 = v, "0.00"));
        AddParam(new FloatParameter("S3", 0, 1, () => S3, v => S3 = v, "0.00"));
        AddParam(new FloatParameter("S4", 0, 1, () => S4, v => S4 = v, "0.00"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _phase = new double[VoiceCount];
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate <= 0 ? 44100 : Format.SampleRate;
        var phase = _phase[v];
        var levels = new[] { S0, S1, S2, S3, S4 };
        for (var i = 0; i < ctx.Frames; i++)
        {
            var rate = ModValue(ctx, 0, Rate, i);
            phase += Math.Max(0.001, rate) / sr;
            if (phase >= 1.0) phase -= Math.Floor(phase);
            var idx = (int)(phase * levels.Length) % levels.Length;
            outBuf[i] = (float)levels[idx];
        }

        _phase[v] = phase;
    }
}

/// <summary>Blended multi-wave LFO (mirrors <see cref="WavetableLfoModulator"/>).</summary>
public sealed class WavetableLfoNode : FieldNode
{
    public const string Type = "mod.wavetable_lfo";
    public override string TypeId => Type;
    public override string DisplayName => "Wavetable LFO";
    public override string Category => FieldNodeCategories.Modulators;

    public double Rate { get; set; } = 0.5;
    public int Shape { get; set; }

    private double[] _phase = Array.Empty<double>();

    public WavetableLfoNode()
    {
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Rate", 0.01, 20, () => Rate, v => Rate = v, "0.00", "Hz", 2.0));
        AddParam(new ChoiceParameter("Shape", new[] { "Sine", "Triangle", "Saw", "Square" }, () => Shape, i => Shape = i), modulatable: false);
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _phase = new double[VoiceCount];
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate <= 0 ? 44100 : Format.SampleRate;
        var phase = _phase[v];
        var wave = (LfoWave)(Shape % 4);
        for (var i = 0; i < ctx.Frames; i++)
        {
            var wt = ModulatorEval.LfoUnipolar(wave, phase);
            wt = wt * 0.7 + ModulatorEval.LfoUnipolar(LfoWave.Square, phase * 2) * 0.3;
            outBuf[i] = (float)wt;
            phase += Math.Max(0.001, ModValue(ctx, 0, Rate, i)) / sr;
            if (phase >= 1.0) phase -= Math.Floor(phase);
        }

        _phase[v] = phase;
    }
}

/// <summary>Tempo-synced beat LFO (mirrors <see cref="BeatLfoModulator"/>).</summary>
public sealed class BeatLfoNode : FieldNode
{
    public const string Type = "mod.beat_lfo";
    public override string TypeId => Type;
    public override string DisplayName => "Beat LFO";
    public override string Category => FieldNodeCategories.Modulators;

    public double RateBeats { get; set; } = 1.0;
    public int WaveIndex { get; set; }
    public double Bpm { get; set; } = 120;

    private double[] _beat = Array.Empty<double>();

    public BeatLfoNode()
    {
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Beats", 0.0625, 16, () => RateBeats, v => RateBeats = v, "0.###"));
        AddParam(new ChoiceParameter("Wave", new[] { "Sine", "Triangle", "Saw", "Square" }, () => WaveIndex, i => WaveIndex = i), modulatable: false);
        AddParam(new FloatParameter("BPM", 20, 300, () => Bpm, v => Bpm = v, "0"), modulatable: false);
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _beat = new double[VoiceCount];
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate <= 0 ? 44100 : Format.SampleRate;
        var beat = _beat[v];
        var wave = (LfoWave)Math.Clamp(WaveIndex, 0, 3);
        var dt = 60.0 / (Bpm > 0 ? Bpm : 120) / sr;
        for (var i = 0; i < ctx.Frames; i++)
        {
            var phase = beat / Math.Max(1e-4, RateBeats);
            phase -= Math.Floor(phase);
            outBuf[i] = (float)ModulatorEval.LfoUnipolar(wave, phase);
            beat += dt;
        }

        _beat[v] = beat;
    }
}

/// <summary>Step sequencer CV (mirrors <see cref="StepsModulator"/>).</summary>
public sealed class StepsNode : FieldNode
{
    public const string Type = "mod.steps";
    public override string TypeId => Type;
    public override string DisplayName => "Steps";
    public override string Category => FieldNodeCategories.Modulators;

    public int StepCount { get; set; } = 8;
    public double RateBeats { get; set; } = 0.25;
    public double Bpm { get; set; } = 120;

    private double[] _beat = Array.Empty<double>();

    public StepsNode()
    {
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Steps", 2, 16, () => StepCount, v => StepCount = (int)v, "0"), modulatable: false);
        AddParam(new FloatParameter("Beat", 0.0625, 4, () => RateBeats, v => RateBeats = v, "0.###"));
        AddParam(new FloatParameter("BPM", 20, 300, () => Bpm, v => Bpm = v, "0"), modulatable: false);
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _beat = new double[VoiceCount];
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate <= 0 ? 44100 : Format.SampleRate;
        var beat = _beat[v];
        var dt = 60.0 / (Bpm > 0 ? Bpm : 120) / sr;
        var steps = Math.Clamp(StepCount, 2, 16);
        for (var i = 0; i < ctx.Frames; i++)
        {
            outBuf[i] = (float)ModulatorEval.StepIndex(beat, 1.0 / Math.Max(1e-4, RateBeats), steps);
            beat += dt;
        }

        _beat[v] = beat;
    }
}

/// <summary>Cyclic four-stage envelope CV (mirrors <see cref="FourStageModulator"/>).</summary>
public sealed class FourStageNode : FieldNode
{
    public const string Type = "mod.4stage";
    public override string TypeId => Type;
    public override string DisplayName => "4-Stage";
    public override string Category => FieldNodeCategories.Modulators;

    public double Attack { get; set; } = 0.01;
    public double Hold { get; set; } = 0.05;
    public double Decay { get; set; } = 0.3;
    public double Rate { get; set; } = 0.25;

    private double[] _t = Array.Empty<double>();

    public FourStageNode()
    {
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Attack", 0.001, 2, () => Attack, v => Attack = v, "0.000", "s"));
        AddParam(new FloatParameter("Hold", 0, 2, () => Hold, v => Hold = v, "0.000", "s"));
        AddParam(new FloatParameter("Decay", 0.001, 4, () => Decay, v => Decay = v, "0.000", "s"));
        AddParam(new FloatParameter("Rate", 0.01, 4, () => Rate, v => Rate = v, "0.00", "Hz", 2.0));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _t = new double[VoiceCount];
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate <= 0 ? 44100 : Format.SampleRate;
        var t = _t[v];
        for (var i = 0; i < ctx.Frames; i++)
        {
            var rate = ModValue(ctx, 3, Rate, i);
            var period = rate > 0 ? 1.0 / rate : 1.0;
            var env = new CurveEnvelope(0, ModValue(ctx, 0, Attack, i), ModValue(ctx, 1, Hold, i),
                ModValue(ctx, 2, Decay, i), 0.5);
            outBuf[i] = (float)Math.Clamp(env.Evaluate(t % period), 0, 1);
            t += 1.0 / sr;
        }

        _t[v] = t;
    }
}

/// <summary>Linear ramp over a bar period (mirrors <see cref="RampModulator"/>).</summary>
public sealed class RampNode : FieldNode
{
    public const string Type = "mod.ramp";
    public override string TypeId => Type;
    public override string DisplayName => "Ramp";
    public override string Category => FieldNodeCategories.Modulators;

    public double PeriodBeats { get; set; } = 4;
    public bool Reverse { get; set; }
    public double Bpm { get; set; } = 120;

    private double[] _beat = Array.Empty<double>();

    public RampNode()
    {
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Beats", 0.25, 32, () => PeriodBeats, v => PeriodBeats = v, "0.##"));
        AddParam(new BoolParameter("Reverse", () => Reverse, v => Reverse = v));
        AddParam(new FloatParameter("BPM", 20, 300, () => Bpm, v => Bpm = v, "0"), modulatable: false);
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _beat = new double[VoiceCount];
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate <= 0 ? 44100 : Format.SampleRate;
        var beat = _beat[v];
        var dt = 60.0 / (Bpm > 0 ? Bpm : 120) / sr;
        for (var i = 0; i < ctx.Frames; i++)
        {
            var phase = beat / Math.Max(1e-4, PeriodBeats);
            phase -= Math.Floor(phase);
            if (Reverse) phase = 1.0 - phase;
            outBuf[i] = (float)phase;
            beat += dt;
        }

        _beat[v] = beat;
    }
}

/// <summary>Toggle button CV source (mirrors <see cref="ButtonModulator"/>).</summary>
public sealed class ButtonNode : FieldNode
{
    public const string Type = "mod.button";
    public override string TypeId => Type;
    public override string DisplayName => "Button";
    public override string Category => FieldNodeCategories.Modulators;

    public bool Pressed { get; set; }

    public ButtonNode()
    {
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new BoolParameter("Pressed", () => Pressed, v => Pressed = v));
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var outBuf = ctx.Output(0);
        var val = Pressed ? 1f : 0f;
        for (var i = 0; i < ctx.Frames; i++) outBuf[i] = val;
    }
}

/// <summary>Four-value macro selector (mirrors <see cref="Macro4Modulator"/>).</summary>
public sealed class Macro4Node : FieldNode
{
    public const string Type = "mod.macro_4";
    public override string TypeId => Type;
    public override string DisplayName => "Macro-4";
    public override string Category => FieldNodeCategories.Modulators;

    public double M1 { get; set; } = 0.25;
    public double M2 { get; set; } = 0.5;
    public double M3 { get; set; } = 0.75;
    public double M4 { get; set; } = 1;
    public int Select { get; set; }

    public Macro4Node()
    {
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("M1", 0, 1, () => M1, v => M1 = v, "0.00"));
        AddParam(new FloatParameter("M2", 0, 1, () => M2, v => M2 = v, "0.00"));
        AddParam(new FloatParameter("M3", 0, 1, () => M3, v => M3 = v, "0.00"));
        AddParam(new FloatParameter("M4", 0, 1, () => M4, v => M4 = v, "0.00"));
        AddParam(new ChoiceParameter("Select", new[] { "M1", "M2", "M3", "M4" }, () => Select, i => Select = i), modulatable: false);
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var vals = new[] { M1, M2, M3, M4 };
        var val = (float)Math.Clamp(vals[Math.Clamp(Select, 0, 3)], 0, 1);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++) outBuf[i] = val;
    }
}

/// <summary>2D XY macro blender (mirrors <see cref="XyModulator"/>).</summary>
public sealed class XyCvNode : FieldNode
{
    public const string Type = "mod.xy";
    public override string TypeId => Type;
    public override string DisplayName => "XY";
    public override string Category => FieldNodeCategories.Modulators;

    public double X { get; set; } = 0.5;
    public double Y { get; set; } = 0.5;
    public int Axis { get; set; }

    public XyCvNode()
    {
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("X", 0, 1, () => X, v => X = v, "0.00"));
        AddParam(new FloatParameter("Y", 0, 1, () => Y, v => Y = v, "0.00"));
        AddParam(new ChoiceParameter("Axis", new[] { "X", "Y", "X+Y" }, () => Axis, i => Axis = i), modulatable: false);
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++)
        {
            var x = ModValue(ctx, 0, X, i);
            var y = ModValue(ctx, 1, Y, i);
            outBuf[i] = (float)(Axis switch
            {
                1 => y,
                2 => Math.Clamp((x + y) * 0.5, 0, 1),
                _ => x
            });
        }
    }
}

/// <summary>Keyboard tracking CV (mirrors <see cref="KeytrackPlusModulator"/>).</summary>
public sealed class KeytrackNode : FieldNode
{
    public const string Type = "mod.keytrack";
    public override string TypeId => Type;
    public override string DisplayName => "Keytrack+";
    public override string Category => FieldNodeCategories.Modulators;

    public int Root { get; set; } = 60;
    public int Range { get; set; } = 24;

    public KeytrackNode()
    {
        AddInput("pitch", "Pitch", FieldSignalKind.Note);
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Root", 0, 127, () => Root, v => Root = (int)v, "0"), modulatable: false);
        AddParam(new FloatParameter("Range", 1, 48, () => Range, v => Range = (int)v, "0"), modulatable: false);
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var pitch = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var r = Math.Max(1, Range);
        for (var i = 0; i < ctx.Frames; i++)
        {
            var note = pitch[i] > 1 ? 69 + 12 * Math.Log(pitch[i] / 440.0, 2) : Root;
            outBuf[i] = (float)Math.Clamp((note - Root + r * 0.5) / r, 0, 1);
        }
    }
}

/// <summary>Math combiner on two CV inputs (mirrors <see cref="MathModulator"/>).</summary>
public sealed class MathCvNode : FieldNode
{
    public const string Type = "mod.math";
    public override string TypeId => Type;
    public override string DisplayName => "Math";
    public override string Category => FieldNodeCategories.Modulators;

    public int Op { get; set; }

    public MathCvNode()
    {
        AddInput("a", "A", FieldSignalKind.Cv);
        AddInput("b", "B", FieldSignalKind.Cv);
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new ChoiceParameter("Op", new[] { "Add", "Sub", "Mul", "Div" }, () => Op, i => Op = i), modulatable: false);
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var a = ctx.Input(0);
        var b = ctx.Input(1);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++)
        {
            outBuf[i] = Op switch
            {
                1 => (float)Math.Clamp(a[i] - b[i], 0, 1),
                2 => (float)Math.Clamp(a[i] * b[i], 0, 1),
                3 => (float)Math.Clamp(a[i] / Math.Max(1e-6f, b[i]), 0, 1),
                _ => (float)Math.Clamp(a[i] + b[i], 0, 1)
            };
        }
    }
}

/// <summary>CV crossfade (mirrors <see cref="MixModulator"/>).</summary>
public sealed class MixCvNode : FieldNode
{
    public const string Type = "mod.mix";
    public override string TypeId => Type;
    public override string DisplayName => "Mix";
    public override string Category => FieldNodeCategories.Modulators;

    public double Mix { get; set; } = 0.5;

    public MixCvNode()
    {
        AddInput("a", "A", FieldSignalKind.Cv);
        AddInput("b", "B", FieldSignalKind.Cv);
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Mix", 0, 1, () => Mix, v => Mix = v, "0.00"));
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var a = ctx.Input(0);
        var b = ctx.Input(1);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++)
        {
            var mix = ModValue(ctx, 0, Mix, i);
            outBuf[i] = (float)(a[i] * (1.0 - mix) + b[i] * mix);
        }
    }
}

/// <summary>Vibrato-style sine wobble (mirrors <see cref="VibratoModulator"/>).</summary>
public sealed class VibratoCvNode : FieldNode
{
    public const string Type = "mod.vibrato";
    public override string TypeId => Type;
    public override string DisplayName => "Vibrato";
    public override string Category => FieldNodeCategories.Modulators;

    public double Rate { get; set; } = 5;
    public double Depth { get; set; } = 0.5;

    private double[] _phase = Array.Empty<double>();

    public VibratoCvNode()
    {
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Rate", 0.1, 20, () => Rate, v => Rate = v, "0.0", "Hz"));
        AddParam(new FloatParameter("Depth", 0, 1, () => Depth, v => Depth = v, "0.00"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _phase = new double[VoiceCount];
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate <= 0 ? 44100 : Format.SampleRate;
        var phase = _phase[v];
        for (var i = 0; i < ctx.Frames; i++)
        {
            var wt = ModulatorEval.LfoUnipolar(LfoWave.Sine, phase);
            var depth = ModValue(ctx, 1, Depth, i);
            outBuf[i] = (float)Math.Clamp(0.5 + (wt - 0.5) * depth, 0, 1);
            phase += Math.Max(0.001, ModValue(ctx, 0, Rate, i)) / sr;
            if (phase >= 1.0) phase -= Math.Floor(phase);
        }

        _phase[v] = phase;
    }
}

/// <summary>Sample-and-hold CV (mirrors <see cref="SampleHoldModulator"/>).</summary>
public sealed class SampleHoldCvNode : FieldNode
{
    public const string Type = "mod.sample_hold";
    public override string TypeId => Type;
    public override string DisplayName => "Sample & Hold";
    public override string Category => FieldNodeCategories.Modulators;

    public double Rate { get; set; } = 4;

    private FastRandom[] _rng = Array.Empty<FastRandom>();
    private double[] _phase = Array.Empty<double>();
    private float[] _held = Array.Empty<float>();

    public SampleHoldCvNode()
    {
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Rate", 0.1, 40, () => Rate, v => Rate = v, "0.0", "Hz", 2.0));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _rng = new FastRandom[VoiceCount];
        _phase = new double[VoiceCount];
        _held = new float[VoiceCount];
        for (var i = 0; i < VoiceCount; i++)
        {
            _rng[i] = new FastRandom((uint)(0x5151 + i * 9973u));
            _held[i] = _rng[i].NextBipolar() * 0.5f + 0.5f;
        }
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate <= 0 ? 44100 : Format.SampleRate;
        var phase = _phase[v];
        var held = _held[v];
        var rng = _rng[v];
        for (var i = 0; i < ctx.Frames; i++)
        {
            phase += Math.Max(0.001, ModValue(ctx, 0, Rate, i)) / sr;
            if (phase >= 1.0)
            {
                phase -= Math.Floor(phase);
                held = rng.NextBipolar() * 0.5f + 0.5f;
            }

            outBuf[i] = held;
        }

        _phase[v] = phase;
        _held[v] = held;
        _rng[v] = rng;
    }
}

/// <summary>Quantize incoming CV to steps (mirrors <see cref="QuantizeModulator"/>).</summary>
public sealed class QuantizeCvNode : FieldNode
{
    public const string Type = "mod.quantize_cv";
    public override string TypeId => Type;
    public override string DisplayName => "Quantize CV";
    public override string Category => FieldNodeCategories.Modulators;

    public int Steps { get; set; } = 8;

    public QuantizeCvNode()
    {
        AddInput("in", "In", FieldSignalKind.Cv);
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Steps", 2, 32, () => Steps, v => Steps = (int)v, "0"), modulatable: false);
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var steps = Math.Clamp(Steps, 2, 32);
        for (var i = 0; i < ctx.Frames; i++)
        {
            var q = Math.Round(Math.Clamp(input[i], 0f, 1f) * (steps - 1)) / (steps - 1);
            outBuf[i] = (float)q;
        }
    }
}

/// <summary>Chromatic pitch to CV (mirrors <see cref="Pitch12Modulator"/>).</summary>
public sealed class Pitch12Node : FieldNode
{
    public const string Type = "mod.pitch_12";
    public override string TypeId => Type;
    public override string DisplayName => "Pitch-12";
    public override string Category => FieldNodeCategories.Modulators;

    public Pitch12Node()
    {
        AddInput("pitch", "Pitch", FieldSignalKind.Note);
        AddOutput("out", "Out", FieldSignalKind.Cv);
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var pitch = ctx.Input(0);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++)
        {
            var note = pitch[i] > 1 ? 69 + 12 * Math.Log(pitch[i] / 440.0, 2) : 60;
            outBuf[i] = (float)Math.Clamp(note / 127.0, 0, 1);
        }
    }
}

/// <summary>Four-input selector (mirrors <see cref="Select4Modulator"/>).</summary>
public sealed class Select4Node : FieldNode
{
    public const string Type = "mod.select_4";
    public override string TypeId => Type;
    public override string DisplayName => "Select-4";
    public override string Category => FieldNodeCategories.Modulators;

    public int Select { get; set; }

    public Select4Node()
    {
        AddInput("a", "A", FieldSignalKind.Cv);
        AddInput("b", "B", FieldSignalKind.Cv);
        AddInput("c", "C", FieldSignalKind.Cv);
        AddInput("d", "D", FieldSignalKind.Cv);
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new ChoiceParameter("Select", new[] { "A", "B", "C", "D" }, () => Select, i => Select = i), modulatable: false);
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var idx = Math.Clamp(Select, 0, 3);
        var src = ctx.Input(idx);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++) outBuf[i] = src[i];
    }
}

/// <summary>Four-value vector blender (mirrors <see cref="Vector4Modulator"/>).</summary>
public sealed class Vector4Node : FieldNode
{
    public const string Type = "mod.vector_4";
    public override string TypeId => Type;
    public override string DisplayName => "Vector-4";
    public override string Category => FieldNodeCategories.Modulators;

    public double W0 { get; set; } = 0.25;
    public double W1 { get; set; } = 0.25;
    public double W2 { get; set; } = 0.25;
    public double W3 { get; set; } = 0.25;

    public Vector4Node()
    {
        AddInput("a", "A", FieldSignalKind.Cv);
        AddInput("b", "B", FieldSignalKind.Cv);
        AddInput("c", "C", FieldSignalKind.Cv);
        AddInput("d", "D", FieldSignalKind.Cv);
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("W0", 0, 1, () => W0, v => W0 = v, "0.00"));
        AddParam(new FloatParameter("W1", 0, 1, () => W1, v => W1 = v, "0.00"));
        AddParam(new FloatParameter("W2", 0, 1, () => W2, v => W2 = v, "0.00"));
        AddParam(new FloatParameter("W3", 0, 1, () => W3, v => W3 = v, "0.00"));
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var a = ctx.Input(0);
        var b = ctx.Input(1);
        var c = ctx.Input(2);
        var d = ctx.Input(3);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++)
        {
            var sum = W0 + W1 + W2 + W3 + 1e-9;
            outBuf[i] = (float)((a[i] * W0 + b[i] * W1 + c[i] * W2 + d[i] * W3) / sum);
        }
    }
}

/// <summary>Beat phase 0..1 CV (mirrors note-counter style timing sources).</summary>
public sealed class BeatPhaseNode : FieldNode
{
    public const string Type = "mod.beat_phase";
    public override string TypeId => Type;
    public override string DisplayName => "Beat Phase";
    public override string Category => FieldNodeCategories.Modulators;

    public double Bpm { get; set; } = 120;
    public double BeatsPerBar { get; set; } = 4;

    private double[] _beat = Array.Empty<double>();

    public BeatPhaseNode()
    {
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("BPM", 20, 300, () => Bpm, v => Bpm = v, "0"), modulatable: false);
        AddParam(new FloatParameter("Bar", 1, 16, () => BeatsPerBar, v => BeatsPerBar = v, "0"), modulatable: false);
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _beat = new double[VoiceCount];
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate <= 0 ? 44100 : Format.SampleRate;
        var beat = _beat[v];
        var dt = 60.0 / (Bpm > 0 ? Bpm : 120) / sr;
        var bar = Math.Max(1e-4, BeatsPerBar);
        for (var i = 0; i < ctx.Frames; i++)
        {
            var phase = beat / bar;
            phase -= Math.Floor(phase);
            outBuf[i] = (float)phase;
            beat += dt;
        }

        _beat[v] = beat;
    }
}

/// <summary>Voice-index spread CV (mirrors <see cref="StackSpreadModulator"/>).</summary>
public sealed class StackSpreadNode : FieldNode
{
    public const string Type = "mod.stack_spread";
    public override string TypeId => Type;
    public override string DisplayName => "Stack Spread";
    public override string Category => FieldNodeCategories.Modulators;

    public int VoiceCountParam { get; set; } = 8;

    public StackSpreadNode()
    {
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Voices", 1, 16, () => VoiceCountParam, v => VoiceCountParam = (int)v, "0"), modulatable: false);
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var count = Math.Max(1, VoiceCountParam);
        var val = (float)Math.Clamp(ctx.Voice / (double)(count - 1 <= 0 ? 1 : count - 1), 0, 1);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++) outBuf[i] = val;
    }
}

/// <summary>Parallel sequence of eight steps (mirrors <see cref="ParSeq8Modulator"/>).</summary>
public sealed class ParSeq8Node : FieldNode
{
    public const string Type = "mod.pareq_8";
    public override string TypeId => Type;
    public override string DisplayName => "ParSeq-8";
    public override string Category => FieldNodeCategories.Modulators;

    public double RateBeats { get; set; } = 0.25;
    public double Bpm { get; set; } = 120;
    public int Lane { get; set; }

    private double[] _beat = Array.Empty<double>();

    public ParSeq8Node()
    {
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Beat", 0.0625, 4, () => RateBeats, v => RateBeats = v, "0.###"));
        AddParam(new FloatParameter("BPM", 20, 300, () => Bpm, v => Bpm = v, "0"), modulatable: false);
        AddParam(new FloatParameter("Lane", 0, 7, () => Lane, v => Lane = (int)v, "0"), modulatable: false);
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _beat = new double[VoiceCount];
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate <= 0 ? 44100 : Format.SampleRate;
        var beat = _beat[v];
        var dt = 60.0 / (Bpm > 0 ? Bpm : 120) / sr;
        var lane = Math.Clamp(Lane, 0, 7);
        for (var i = 0; i < ctx.Frames; i++)
        {
            var idx = (int)Math.Floor(beat / Math.Max(1e-4, RateBeats)) % 8;
            outBuf[i] = idx == lane ? 1f : 0f;
            beat += dt;
        }

        _beat[v] = beat;
    }
}

/// <summary>Classic bipolar LFO mapped to unipolar CV (mirrors <see cref="ClassicLfoModulator"/>).</summary>
public sealed class ClassicLfoNode : FieldNode
{
    public const string Type = "mod.classic_lfo";
    public override string TypeId => Type;
    public override string DisplayName => "Classic LFO";
    public override string Category => FieldNodeCategories.Modulators;

    public double Rate { get; set; } = 1;
    public int WaveIndex { get; set; } = 1;

    private double[] _phase = Array.Empty<double>();

    public ClassicLfoNode()
    {
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Rate", 0.01, 20, () => Rate, v => Rate = v, "0.00", "Hz", 2.0));
        AddParam(new ChoiceParameter("Wave", new[] { "Sine", "Triangle", "Saw", "Square" }, () => WaveIndex, i => WaveIndex = i), modulatable: false);
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _phase = new double[VoiceCount];
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate <= 0 ? 44100 : Format.SampleRate;
        var phase = _phase[v];
        var wave = (LfoWave)Math.Clamp(WaveIndex, 0, 3);
        for (var i = 0; i < ctx.Frames; i++)
        {
            outBuf[i] = (float)ModulatorEval.LfoUnipolar(wave, phase);
            phase += Math.Max(0.001, ModValue(ctx, 0, Rate, i)) / sr;
            if (phase >= 1.0) phase -= Math.Floor(phase);
        }

        _phase[v] = phase;
    }
}

/// <summary>Globals/tempo helper CV (mirrors <see cref="GlobalsModulator"/>).</summary>
public sealed class GlobalsNode : FieldNode
{
    public const string Type = "mod.globals";
    public override string TypeId => Type;
    public override string DisplayName => "Globals";
    public override string Category => FieldNodeCategories.Modulators;

    public double Bpm { get; set; } = 120;
    public int Source { get; set; }

    public GlobalsNode()
    {
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("BPM", 20, 300, () => Bpm, v => Bpm = v, "0"), modulatable: false);
        AddParam(new ChoiceParameter("Source", new[] { "Tempo", "Swing", "Groove" }, () => Source, i => Source = i), modulatable: false);
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var val = Source switch
        {
            1 => 0.5,
            2 => 0.33,
            _ => Math.Clamp(Bpm / 200.0, 0, 1)
        };
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++) outBuf[i] = (float)val;
    }
}
