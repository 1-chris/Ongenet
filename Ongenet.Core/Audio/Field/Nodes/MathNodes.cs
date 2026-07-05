using System;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Field.Nodes;

/// <summary>A constant value source.</summary>
public sealed class ConstantNode : FieldNode
{
    public const string Type = "math.const";
    public override string TypeId => Type;
    public override string DisplayName => "Constant";
    public override string Category => FieldNodeCategories.Math;

    public double Value { get; set; } = 1.0;

    public ConstantNode()
    {
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Value", -20000, 20000, () => Value, v => Value = v, "0.###"));
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var outBuf = ctx.Output(0);
        var val = (float)Value;
        for (var i = 0; i < ctx.Frames; i++) outBuf[i] = val;
    }
}

/// <summary>Multiplies the input by a (modulatable) linear gain — the core VCA.</summary>
public sealed class GainNode : FieldNode
{
    public const string Type = "math.gain";
    public override string TypeId => Type;
    public override string DisplayName => "Gain";
    public override string Category => FieldNodeCategories.Math;

    public double Amount { get; set; } = 1.0;

    public GainNode()
    {
        AddInput("in", "In");
        AddInput("cv", "CV", FieldSignalKind.Cv);
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Gain", 0, 4, () => Amount, v => Amount = v, "0.00"));
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var input = ctx.Input(0);
        var cv = ctx.Input(1);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++)
            outBuf[i] = input[i] * (float)ModValue(ctx, 0, Amount, i) * cv[i];
    }
}

/// <summary>Adds two signals.</summary>
public sealed class AddNode : FieldNode
{
    public const string Type = "math.add";
    public override string TypeId => Type;
    public override string DisplayName => "Add";
    public override string Category => FieldNodeCategories.Math;

    public AddNode()
    {
        AddInput("a", "A");
        AddInput("b", "B");
        AddOutput("out", "Out");
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var a = ctx.Input(0);
        var b = ctx.Input(1);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++) outBuf[i] = a[i] + b[i];
    }
}

/// <summary>Multiplies two signals (ring modulation / amplitude modulation).</summary>
public sealed class MultiplyNode : FieldNode
{
    public const string Type = "math.mul";
    public override string TypeId => Type;
    public override string DisplayName => "Multiply";
    public override string Category => FieldNodeCategories.Math;

    public MultiplyNode()
    {
        AddInput("a", "A");
        AddInput("b", "B");
        AddOutput("out", "Out");
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var a = ctx.Input(0);
        var b = ctx.Input(1);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++) outBuf[i] = a[i] * b[i];
    }
}

/// <summary>Crossfades between A and B by the Mix amount (0 = A, 1 = B).</summary>
public sealed class MixNode : FieldNode
{
    public const string Type = "math.mix";
    public override string TypeId => Type;
    public override string DisplayName => "Mix";
    public override string Category => FieldNodeCategories.Math;

    public double Mix { get; set; } = 0.5;

    public MixNode()
    {
        AddInput("a", "A");
        AddInput("b", "B");
        AddOutput("out", "Out");
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
            var m = (float)ModValue(ctx, 0, Mix, i);
            outBuf[i] = a[i] * (1f - m) + b[i] * m;
        }
    }
}

/// <summary>Attenuverter + offset: <c>out = in × Scale + Offset</c>. Maps and biases modulation signals.</summary>
public sealed class ScaleOffsetNode : FieldNode
{
    public const string Type = "math.scale";
    public override string TypeId => Type;
    public override string DisplayName => "Scale/Offset";
    public override string Category => FieldNodeCategories.Math;

    public double Scale { get; set; } = 1.0;
    public double Offset { get; set; }

    public ScaleOffsetNode()
    {
        AddInput("in", "In", FieldSignalKind.Cv);
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Scale", -4, 4, () => Scale, v => Scale = v, "0.00"));
        AddParam(new FloatParameter("Offset", -4, 4, () => Offset, v => Offset = v, "0.00"));
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++)
            outBuf[i] = input[i] * (float)ModValue(ctx, 0, Scale, i) + (float)ModValue(ctx, 1, Offset, i);
    }
}

/// <summary>Clamps a signal to [Min, Max].</summary>
public sealed class ClampNode : FieldNode
{
    public const string Type = "math.clamp";
    public override string TypeId => Type;
    public override string DisplayName => "Clamp";
    public override string Category => FieldNodeCategories.Math;

    public double Min { get; set; } = -1;
    public double Max { get; set; } = 1;

    public ClampNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Min", -4, 4, () => Min, v => Min = v, "0.00"));
        AddParam(new FloatParameter("Max", -4, 4, () => Max, v => Max = v, "0.00"));
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var lo = (float)Math.Min(Min, Max);
        var hi = (float)Math.Max(Min, Max);
        for (var i = 0; i < ctx.Frames; i++)
        {
            var x = input[i];
            outBuf[i] = x < lo ? lo : x > hi ? hi : x;
        }
    }
}

/// <summary>Inverts a signal's polarity.</summary>
public sealed class InvertNode : FieldNode
{
    public const string Type = "math.invert";
    public override string TypeId => Type;
    public override string DisplayName => "Invert";
    public override string Category => FieldNodeCategories.Math;

    public InvertNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out");
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++) outBuf[i] = -input[i];
    }
}

/// <summary>Equal-power pan of a mono signal into a stereo pair.</summary>
public sealed class PanNode : FieldNode
{
    public const string Type = "math.pan";
    public override string TypeId => Type;
    public override string DisplayName => "Pan";
    public override string Category => FieldNodeCategories.Math;

    public double Pan { get; set; }

    public PanNode()
    {
        AddInput("in", "In");
        AddOutput("l", "L");
        AddOutput("r", "R");
        AddParam(new FloatParameter("Pan", -1, 1, () => Pan, v => Pan = v, "0.00"));
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var input = ctx.Input(0);
        var outL = ctx.Output(0);
        var outR = ctx.Output(1);
        var perSample = IsModulated(ctx, 0);
        AudioMath.PanGains(Pan, out var gl, out var gr);
        for (var i = 0; i < ctx.Frames; i++)
        {
            if (perSample) AudioMath.PanGains(ModValue(ctx, 0, Pan, i), out gl, out gr);
            outL[i] = input[i] * gl;
            outR[i] = input[i] * gr;
        }
    }
}

/// <summary>Comparator: outputs 1 when the input exceeds the threshold, else 0 (a gate/trigger extractor).</summary>
public sealed class ComparatorNode : FieldNode
{
    public const string Type = "logic.compare";
    public override string TypeId => Type;
    public override string DisplayName => "Comparator";
    public override string Category => FieldNodeCategories.Logic;

    public double Threshold { get; set; }

    public ComparatorNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Threshold", -1, 1, () => Threshold, v => Threshold = v, "0.00"));
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++) outBuf[i] = input[i] > ModValue(ctx, 0, Threshold, i) ? 1f : 0f;
    }
}

/// <summary>Quantises a signal in [-1, 1] to a number of discrete steps.</summary>
public sealed class QuantizeNode : FieldNode
{
    public const string Type = "logic.quantize";
    public override string TypeId => Type;
    public override string DisplayName => "Quantize";
    public override string Category => FieldNodeCategories.Logic;

    public double Steps { get; set; } = 8;

    public QuantizeNode()
    {
        AddInput("in", "In", FieldSignalKind.Cv);
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Steps", 2, 64, () => Steps, v => Steps = Math.Round(v), "0"), modulatable: false);
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var steps = Math.Max(2, (int)Steps);
        var half = (steps - 1) / 2.0;
        for (var i = 0; i < ctx.Frames; i++)
        {
            var n = Math.Round(input[i] * half);
            outBuf[i] = (float)(n / half);
        }
    }
}

/// <summary>Samples its input when the trigger rises and holds it until the next trigger.</summary>
public sealed class SampleHoldNode : FieldNode
{
    public const string Type = "logic.samplehold";
    public override string TypeId => Type;
    public override string DisplayName => "Sample &amp; Hold";
    public override string Category => FieldNodeCategories.Logic;

    private float[] _held = Array.Empty<float>();
    private float[] _prevTrig = Array.Empty<float>();

    public SampleHoldNode()
    {
        AddInput("in", "In");
        AddInput("trig", "Trig", FieldSignalKind.Cv);
        AddOutput("out", "Out");
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _held = new float[VoiceCount];
        _prevTrig = new float[VoiceCount];
    }

    public override void ResetVoice(int voice)
    {
        if (voice >= _held.Length) return;
        _held[voice] = 0;
        _prevTrig[voice] = 0;
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var input = ctx.Input(0);
        var trig = ctx.Input(1);
        var outBuf = ctx.Output(0);
        var held = _held[v];
        var prev = _prevTrig[v];
        for (var i = 0; i < ctx.Frames; i++)
        {
            var t = trig[i];
            if (prev <= 0.5f && t > 0.5f) held = input[i];
            prev = t;
            outBuf[i] = held;
        }

        _held[v] = held;
        _prevTrig[v] = prev;
    }
}
