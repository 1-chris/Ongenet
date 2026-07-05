using System;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Field.Nodes;

/// <summary>Resonant biquad filter (low/band/high-pass, notch) with modulatable cutoff and resonance.</summary>
public sealed class BiquadFilterNode : FieldNode
{
    public const string Type = "filter.biquad";
    public override string TypeId => Type;
    public override string DisplayName => "Filter";
    public override string Category => FieldNodeCategories.Filters;

    public int ModeIndex { get; set; } // 0 LP,1 BP,2 HP,3 Notch
    public double Cutoff { get; set; } = 2000;
    public double Resonance { get; set; } = 0.7;

    private Biquad[] _bq = Array.Empty<Biquad>();

    public BiquadFilterNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out");
        AddParam(new ChoiceParameter("Mode", new[] { "Low-pass", "Band-pass", "High-pass", "Notch" },
            () => ModeIndex, i => ModeIndex = i), modulatable: false);
        AddParam(new FloatParameter("Cutoff", 20, 20000, () => Cutoff, v => Cutoff = v, "0", "Hz", 3.0));
        AddParam(new FloatParameter("Resonance", 0.1, 20, () => Resonance, v => Resonance = v, "0.00"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _bq = new Biquad[VoiceCount];
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _bq.Length) _bq[voice].Reset();
    }

    private static FilterMode Mode(int i) => i switch
    {
        1 => FilterMode.BandPass,
        2 => FilterMode.HighPass,
        3 => FilterMode.Notch,
        _ => FilterMode.LowPass
    };

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        ref var bq = ref _bq[ctx.Voice];
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var mode = Mode(ModeIndex);
        var sr = Format.SampleRate;
        var perSample = IsModulated(ctx, 1) || IsModulated(ctx, 2);
        if (!perSample)
        {
            var c = BiquadCoefficients.Compute(mode, Cutoff, Resonance, sr);
            for (var i = 0; i < ctx.Frames; i++) outBuf[i] = (float)bq.Process(c, input[i]);
        }
        else
        {
            for (var i = 0; i < ctx.Frames; i++)
            {
                var c = BiquadCoefficients.Compute(mode, ModValue(ctx, 1, Cutoff, i), ModValue(ctx, 2, Resonance, i), sr);
                outBuf[i] = (float)bq.Process(c, input[i]);
            }
        }
    }
}

/// <summary>Gentle one-pole low/high-pass — cheap tone shaping and CV smoothing.</summary>
public sealed class OnePoleNode : FieldNode
{
    public const string Type = "filter.onepole";
    public override string TypeId => Type;
    public override string DisplayName => "One-Pole";
    public override string Category => FieldNodeCategories.Filters;

    public int ModeIndex { get; set; } // 0 LP, 1 HP
    public double Cutoff { get; set; } = 1000;

    private OnePole[] _f = Array.Empty<OnePole>();

    public OnePoleNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out");
        AddParam(new ChoiceParameter("Mode", new[] { "Low-pass", "High-pass" }, () => ModeIndex, i => ModeIndex = i), modulatable: false);
        AddParam(new FloatParameter("Cutoff", 20, 20000, () => Cutoff, v => Cutoff = v, "0", "Hz", 3.0));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _f = new OnePole[VoiceCount];
        for (var i = 0; i < VoiceCount; i++) _f[i] = new OnePole();
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _f.Length) _f[voice].Reset();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var f = _f[ctx.Voice];
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var hp = ModeIndex == 1;
        var perSample = IsModulated(ctx, 0);
        if (!perSample) f.SetLowpass(Cutoff, Format.SampleRate);
        for (var i = 0; i < ctx.Frames; i++)
        {
            if (perSample) f.SetLowpass(ModValue(ctx, 0, Cutoff, i), Format.SampleRate);
            outBuf[i] = (float)(hp ? f.ProcessHP(input[i]) : f.ProcessLP(input[i]));
        }
    }
}

/// <summary>Single parametric EQ band (bell / shelf / pass / notch).</summary>
public sealed class EqBandNode : FieldNode
{
    public const string Type = "filter.eqband";
    public override string TypeId => Type;
    public override string DisplayName => "EQ Band";
    public override string Category => FieldNodeCategories.Filters;

    public int TypeIndex { get; set; } // EqBandType
    public double Frequency { get; set; } = 1000;
    public double Gain { get; set; }
    public double Q { get; set; } = 1.0;

    private Biquad[] _bq = Array.Empty<Biquad>();

    public EqBandNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out");
        AddParam(new ChoiceParameter("Type", new[] { "Bell", "Low Shelf", "High Shelf", "High-pass", "Low-pass", "Notch" },
            () => TypeIndex, i => TypeIndex = i), modulatable: false);
        AddParam(new FloatParameter("Freq", 20, 20000, () => Frequency, v => Frequency = v, "0", "Hz", 3.0));
        AddParam(new FloatParameter("Gain", -24, 24, () => Gain, v => Gain = v, "0.0", "dB"));
        AddParam(new FloatParameter("Q", 0.1, 18, () => Q, v => Q = v, "0.00"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _bq = new Biquad[VoiceCount];
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _bq.Length) _bq[voice].Reset();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        ref var bq = ref _bq[ctx.Voice];
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var type = (EqBandType)Math.Clamp(TypeIndex, 0, 5);
        var c = BiquadCoefficients.ComputeEq(type, Frequency, Q, Gain, Format.SampleRate);
        for (var i = 0; i < ctx.Frames; i++) outBuf[i] = (float)bq.Process(c, input[i]);
    }
}

/// <summary>Stereo feedback comb filter (metallic "zaag" resonance).</summary>
public sealed class CombNode : FieldNode
{
    public const string Type = "filter.comb";
    public override string TypeId => Type;
    public override string DisplayName => "Comb";
    public override string Category => FieldNodeCategories.Filters;
    public override bool ForceGlobal => true;

    public double DelayMs { get; set; } = 5;
    public double Stereo { get; set; } = 0.2;
    public double Feedback { get; set; } = 0.6;
    public double Mix { get; set; } = 1.0;

    private readonly CombFilter _comb = new();

    public CombNode()
    {
        AddInput("l", "L");
        AddInput("r", "R");
        AddOutput("l", "L");
        AddOutput("r", "R");
        AddParam(new FloatParameter("Delay", 0.1, 30, () => DelayMs, v => DelayMs = v, "0.00", "ms"));
        AddParam(new FloatParameter("Stereo", 0, 0.5, () => Stereo, v => Stereo = v));
        AddParam(new FloatParameter("Feedback", 0, 0.9, () => Feedback, v => Feedback = v));
        AddParam(new FloatParameter("Mix", 0, 1, () => Mix, v => Mix = v));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _comb.Configure(DelayMs, Stereo, Feedback, Mix, format.SampleRate);
        _comb.Reset();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        _comb.Configure(DelayMs, Stereo, Feedback, Mix, Format.SampleRate);
        var l = ctx.Input(0);
        var r = ctx.Input(1);
        var outL = ctx.Output(0);
        var outR = ctx.Output(1);
        for (var i = 0; i < ctx.Frames; i++)
        {
            _comb.Process(l[i], r[i], out var ol, out var or);
            outL[i] = ol;
            outR[i] = or;
        }
    }
}

/// <summary>A Schroeder all-pass section (delay + feedback) — a reverb/phaser building block.</summary>
public sealed class AllpassNode : FieldNode
{
    public const string Type = "filter.allpass";
    public override string TypeId => Type;
    public override string DisplayName => "All-Pass";
    public override string Category => FieldNodeCategories.Filters;

    public double DelayMs { get; set; } = 5;
    public double Feedback { get; set; } = 0.5;

    private DelayLine[] _dl = Array.Empty<DelayLine>();

    public AllpassNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Delay", 0.1, 100, () => DelayMs, v => DelayMs = v, "0.00", "ms"));
        AddParam(new FloatParameter("Feedback", 0, 0.95, () => Feedback, v => Feedback = v));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        var size = (int)(0.1 * format.SampleRate) + 8;
        _dl = new DelayLine[VoiceCount];
        for (var i = 0; i < VoiceCount; i++) { _dl[i] = new DelayLine(); _dl[i].Resize(size); }
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _dl.Length) _dl[voice].Clear();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var dl = _dl[ctx.Voice];
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var g = (float)Math.Clamp(Feedback, 0, 0.95);
        var delay = Math.Max(1.0, DelayMs / 1000.0 * Format.SampleRate);
        for (var i = 0; i < ctx.Frames; i++)
        {
            var delayed = dl.ReadFrac(delay);
            var x = input[i];
            var y = -g * x + delayed;
            dl.Write(x + g * y);
            outBuf[i] = y;
        }
    }
}
