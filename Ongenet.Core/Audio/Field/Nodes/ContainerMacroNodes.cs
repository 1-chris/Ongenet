using System;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Field.Nodes;

/// <summary>Parallel layer macro: mixes two audio branches with a crossfade.</summary>
public sealed class ContainerLayerNode : FieldNode
{
    public const string Type = "container.layer";
    public override string TypeId => Type;
    public override string DisplayName => "Layer";
    public override string Category => FieldNodeCategories.Containers;

    public double Mix { get; set; } = 0.5;
    public double GainA { get; set; } = 1.0;
    public double GainB { get; set; } = 1.0;

    public ContainerLayerNode()
    {
        AddInput("a", "A");
        AddInput("b", "B");
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Mix", 0, 1, () => Mix, v => Mix = v, "0.00"));
        AddParam(new FloatParameter("Gain A", 0, 2, () => GainA, v => GainA = v, "0.00"));
        AddParam(new FloatParameter("Gain B", 0, 2, () => GainB, v => GainB = v, "0.00"));
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
            var ga = ModValue(ctx, 1, GainA, i);
            var gb = ModValue(ctx, 2, GainB, i);
            outBuf[i] = (float)(a[i] * ga * (1.0 - mix) + b[i] * gb * mix);
        }
    }
}

/// <summary>Selector macro: crossfades between two branches (FX/Instrument Selector routing).</summary>
public sealed class ContainerSelectorNode : FieldNode
{
    public const string Type = "container.selector";
    public override string TypeId => Type;
    public override string DisplayName => "Selector";
    public override string Category => FieldNodeCategories.Containers;

    public double Select { get; set; }

    public ContainerSelectorNode()
    {
        AddInput("a", "A");
        AddInput("b", "B");
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Select", 0, 1, () => Select, v => Select = v, "0.00"));
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var a = ctx.Input(0);
        var b = ctx.Input(1);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++)
        {
            var sel = ModValue(ctx, 0, Select, i);
            outBuf[i] = (float)(a[i] * (1.0 - sel) + b[i] * sel);
        }
    }
}

/// <summary>Three-band multiband macro with independent Low/Mid/High trims.</summary>
public sealed class ContainerMultibandNode : FieldNode
{
    public const string Type = "container.multiband";
    public override string TypeId => Type;
    public override string DisplayName => "Multiband";
    public override string Category => FieldNodeCategories.Containers;

    public double LowCross { get; set; } = 250;
    public double HighCross { get; set; } = 4000;
    public double LowGain { get; set; } = 1.0;
    public double MidGain { get; set; } = 1.0;
    public double HighGain { get; set; } = 1.0;

    private Biquad[] _lowPass = Array.Empty<Biquad>();
    private Biquad[] _highPass = Array.Empty<Biquad>();
    private double[] _lastLow = Array.Empty<double>();
    private double[] _lastHigh = Array.Empty<double>();

    public ContainerMultibandNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Low Cross", 40, 2000, () => LowCross, v => LowCross = v, "0", "Hz", 2.0));
        AddParam(new FloatParameter("High Cross", 500, 16000, () => HighCross, v => HighCross = v, "0", "Hz", 2.0));
        AddParam(new FloatParameter("Low", 0, 2, () => LowGain, v => LowGain = v, "0.00"));
        AddParam(new FloatParameter("Mid", 0, 2, () => MidGain, v => MidGain = v, "0.00"));
        AddParam(new FloatParameter("High", 0, 2, () => HighGain, v => HighGain = v, "0.00"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _lowPass = new Biquad[VoiceCount];
        _highPass = new Biquad[VoiceCount];
        _lastLow = new double[VoiceCount];
        _lastHigh = new double[VoiceCount];
        for (var i = 0; i < VoiceCount; i++)
        {
            _lowPass[i] = new Biquad();
            _highPass[i] = new Biquad();
            _lastLow[i] = double.NaN;
            _lastHigh[i] = double.NaN;
        }
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _lowPass.Length)
        {
            _lowPass[voice].Reset();
            _highPass[voice].Reset();
        }
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate;
        ref var lp = ref _lowPass[v];
        ref var hp = ref _highPass[v];

        BiquadCoefficients lpCoeffs = default;
        BiquadCoefficients hpCoeffs = default;
        for (var i = 0; i < ctx.Frames; i++)
        {
            var lowHz = ModValue(ctx, 0, LowCross, i);
            var highHz = ModValue(ctx, 1, HighCross, i);
            if (lowHz != _lastLow[v] || highHz != _lastHigh[v])
            {
                lpCoeffs = BiquadCoefficients.Compute(FilterMode.LowPass, lowHz, 0.707, sr);
                hpCoeffs = BiquadCoefficients.Compute(FilterMode.HighPass, highHz, 0.707, sr);
                _lastLow[v] = lowHz;
                _lastHigh[v] = highHz;
            }

            var x = input[i];
            var low = (float)lp.Process(in lpCoeffs, x);
            var high = (float)hp.Process(in hpCoeffs, x);
            var mid = x - low - high;
            outBuf[i] = low * (float)ModValue(ctx, 2, LowGain, i)
                        + mid * (float)ModValue(ctx, 3, MidGain, i)
                        + high * (float)ModValue(ctx, 4, HighGain, i);
        }
    }
}
