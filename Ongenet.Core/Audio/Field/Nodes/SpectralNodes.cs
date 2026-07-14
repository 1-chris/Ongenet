using System;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Field.Nodes;

/// <summary>Frequency split macro: low-pass and high-pass outputs at a crossover.</summary>
public sealed class SpectralSplitNode : FieldNode
{
    public const string Type = "spectral.split";
    public override string TypeId => Type;
    public override string DisplayName => "Freq Split";
    public override string Category => FieldNodeCategories.Spectral;

    public double Crossover { get; set; } = 1000;

    private Biquad[] _low = Array.Empty<Biquad>();
    private Biquad[] _high = Array.Empty<Biquad>();

    public SpectralSplitNode()
    {
        AddInput("in", "In");
        AddOutput("low", "Low");
        AddOutput("high", "High");
        AddParam(new FloatParameter("Crossover", 40, 16000, () => Crossover, v => Crossover = v, "0", "Hz", 2.0));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _low = new Biquad[VoiceCount];
        _high = new Biquad[VoiceCount];
        for (var i = 0; i < VoiceCount; i++)
        {
            _low[i] = new Biquad();
            _high[i] = new Biquad();
        }
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _low.Length)
        {
            _low[voice].Reset();
            _high[voice].Reset();
        }
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var input = ctx.Input(0);
        var lowOut = ctx.Output(0);
        var highOut = ctx.Output(1);
        var sr = Format.SampleRate;
        ref var lp = ref _low[v];
        ref var hp = ref _high[v];

        for (var i = 0; i < ctx.Frames; i++)
        {
            var hz = ModValue(ctx, 0, Crossover, i);
            var lpCoeffs = BiquadCoefficients.Compute(FilterMode.LowPass, hz, 0.707, sr);
            var hpCoeffs = BiquadCoefficients.Compute(FilterMode.HighPass, hz, 0.707, sr);
            var x = input[i];
            lowOut[i] = (float)lp.Process(in lpCoeffs, x);
            highOut[i] = (float)hp.Process(in hpCoeffs, x);
        }
    }
}

/// <summary>Transient split macro: separates attack transient from sustain body.</summary>
public sealed class SpectralTransientNode : FieldNode
{
    public const string Type = "spectral.transient";
    public override string TypeId => Type;
    public override string DisplayName => "Transient Split";
    public override string Category => FieldNodeCategories.Spectral;

    public double Attack { get; set; }
    public double Sustain { get; set; }

    private EnvelopeFollower[] _fast = Array.Empty<EnvelopeFollower>();
    private EnvelopeFollower[] _slow = Array.Empty<EnvelopeFollower>();

    public SpectralTransientNode()
    {
        AddInput("in", "In");
        AddOutput("trans", "Trans");
        AddOutput("body", "Body");
        AddParam(new FloatParameter("Attack", -1, 1, () => Attack, v => Attack = v, "0.00"));
        AddParam(new FloatParameter("Sustain", -1, 1, () => Sustain, v => Sustain = v, "0.00"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _fast = new EnvelopeFollower[VoiceCount];
        _slow = new EnvelopeFollower[VoiceCount];
        for (var i = 0; i < VoiceCount; i++)
        {
            _fast[i] = new EnvelopeFollower();
            _slow[i] = new EnvelopeFollower();
        }
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _fast.Length)
        {
            _fast[voice].Reset();
            _slow[voice].Reset();
        }
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var input = ctx.Input(0);
        var transOut = ctx.Output(0);
        var bodyOut = ctx.Output(1);
        var sr = Format.SampleRate;
        var fast = _fast[v];
        var slow = _slow[v];
        fast.SetTimes(0.1, 20.0, sr);
        slow.SetTimes(10.0, 200.0, sr);

        for (var i = 0; i < ctx.Frames; i++)
        {
            var detect = Math.Abs(input[i]);
            var f = fast.Process(detect);
            var s = slow.Process(detect);
            var transient = Math.Max(0.0, f - s);
            var body = s;
            var attackGain = 1.0 + ModValue(ctx, 0, Attack, i);
            var sustainGain = 1.0 + ModValue(ctx, 1, Sustain, i);
            transOut[i] = (float)(input[i] * transient * attackGain);
            bodyOut[i] = (float)(input[i] * body * sustainGain);
        }
    }
}
