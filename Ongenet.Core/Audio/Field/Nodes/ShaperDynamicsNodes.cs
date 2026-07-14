using System;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Field.Nodes;

/// <summary>Waveshaper / distortion: tanh, hard clip, foldback or sine-fold with drive and asymmetry bias.</summary>
public sealed class WaveShaperNode : FieldNode
{
    public const string Type = "shape.waveshaper";
    public override string TypeId => Type;
    public override string DisplayName => "Waveshaper";
    public override string Category => FieldNodeCategories.Shapers;

    public int ShapeIndex { get; set; }
    public double Drive { get; set; } = 1.0;
    public double Bias { get; set; }

    public WaveShaperNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out");
        AddParam(new ChoiceParameter("Shape", new[] { "Tanh", "Hard Clip", "Foldback", "Sine Fold" }, () => ShapeIndex, i => ShapeIndex = i), modulatable: false);
        AddParam(new FloatParameter("Drive", 0.1, 40, () => Drive, v => Drive = v, "0.00", "", 2.0));
        AddParam(new FloatParameter("Bias", -0.5, 0.5, () => Bias, v => Bias = v, "0.00"));
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var type = (ShaperType)Math.Clamp(ShapeIndex, 0, 3);
        for (var i = 0; i < ctx.Frames; i++)
            outBuf[i] = WaveShaper.Shape(input[i], type, (float)ModValue(ctx, 0, Drive, i), (float)ModValue(ctx, 1, Bias, i));
    }
}

/// <summary>Simple tanh soft clipper with drive.</summary>
public sealed class SoftClipNode : FieldNode
{
    public const string Type = "shape.softclip";
    public override string TypeId => Type;
    public override string DisplayName => "Soft Clip";
    public override string Category => FieldNodeCategories.Shapers;

    public double Drive { get; set; } = 1.0;

    public SoftClipNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Drive", 0.1, 20, () => Drive, v => Drive = v, "0.00", "", 2.0));
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++)
            outBuf[i] = AudioMath.SoftClip(input[i] * (float)ModValue(ctx, 0, Drive, i));
    }
}

/// <summary>Bit-depth reduction and sample-rate decimation.</summary>
public sealed class BitcrusherNode : FieldNode
{
    public const string Type = "shape.bitcrush";
    public override string TypeId => Type;
    public override string DisplayName => "Bitcrusher";
    public override string Category => FieldNodeCategories.Shapers;

    public double Bits { get; set; } = 8;
    public double Downsample { get; set; } = 1;

    private BitcrusherDsp[] _dsp = Array.Empty<BitcrusherDsp>();

    public BitcrusherNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Bits", 1, 16, () => Bits, v => Bits = v, "0.0"));
        AddParam(new FloatParameter("Downsample", 1, 64, () => Downsample, v => Downsample = v, "0"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _dsp = new BitcrusherDsp[VoiceCount];
        for (var i = 0; i < VoiceCount; i++) _dsp[i] = new BitcrusherDsp();
    }

    public override void ResetVoice(int voice)
    {
        if (voice >= _dsp.Length) return;
        _dsp[voice].Reset();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var d = _dsp[ctx.Voice];
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++)
        {
            d.Bits = ModValue(ctx, 0, Bits, i);
            d.Downsample = ModValue(ctx, 1, Downsample, i);
            d.Mix = 1;
            outBuf[i] = d.Process(input[i]);
        }
    }
}

/// <summary>Multi-stage "screaming" distortion (EQ-boost → distort → clean-up), the hardstyle tail core.</summary>
public sealed class DistortionStackNode : FieldNode
{
    public const string Type = "shape.diststack";
    public override string TypeId => Type;
    public override string DisplayName => "Distortion Stack";
    public override string Category => FieldNodeCategories.Shapers;

    public double Stages { get; set; } = 4;
    public double Scream { get; set; } = 1200;
    public double Drive { get; set; } = 6;
    public double Tone { get; set; } = 8000;
    public double Asym { get; set; } = 0.2;
    public int ShapeIndex { get; set; }

    private DistortionStack[] _stack = Array.Empty<DistortionStack>();

    public DistortionStackNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Stages", 0, DistortionStack.MaxStages, () => Stages, v => Stages = Math.Round(v), "0"), modulatable: false);
        AddParam(new FloatParameter("Scream", 120, 9000, () => Scream, v => Scream = v, "0", "Hz", 2.0));
        AddParam(new FloatParameter("Drive", -12, 36, () => Drive, v => Drive = v, "0.0", "dB"));
        AddParam(new FloatParameter("Tone", 500, 18000, () => Tone, v => Tone = v, "0", "Hz", 2.0));
        AddParam(new FloatParameter("Asym", 0, 1, () => Asym, v => Asym = v, "0.00"));
        AddParam(new ChoiceParameter("Shape", new[] { "Tanh", "Hard Clip", "Foldback", "Sine Fold" }, () => ShapeIndex, i => ShapeIndex = i), modulatable: false);
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _stack = new DistortionStack[VoiceCount];
        for (var i = 0; i < VoiceCount; i++) _stack[i] = new DistortionStack();
        Configure();
    }

    public override void ResetVoice(int voice)
    {
        if (voice >= _stack.Length) return;
        ConfigureOne(_stack[voice]);
    }

    private void Configure()
    {
        foreach (var s in _stack) ConfigureOne(s);
    }

    private void ConfigureOne(DistortionStack s)
        => s.Configure((int)Stages, Scream, 0.5, 6.0, 1.0, Drive, Asym, Tone,
            (ShaperType)Math.Clamp(ShapeIndex, 0, 3), Format.SampleRate);

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var s = _stack[ctx.Voice];
        ConfigureOne(s);
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++) outBuf[i] = s.Process(input[i]);
    }
}

/// <summary>A feed-forward compressor with peak detection, soft knee-less ratio and makeup gain.</summary>
public sealed class CompressorNode : FieldNode
{
    public const string Type = "dyn.compressor";
    public override string TypeId => Type;
    public override string DisplayName => "Compressor";
    public override string Category => FieldNodeCategories.Dynamics;

    public double ThresholdDb { get; set; } = -18;
    public double Ratio { get; set; } = 4;
    public double Attack { get; set; } = 10;
    public double ReleaseMs { get; set; } = 120;
    public double MakeupDb { get; set; }

    private EnvelopeFollower[] _env = Array.Empty<EnvelopeFollower>();

    public CompressorNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Threshold", -60, 0, () => ThresholdDb, v => ThresholdDb = v, "0.0", "dB"));
        AddParam(new FloatParameter("Ratio", 1, 20, () => Ratio, v => Ratio = v, "0.0"));
        AddParam(new FloatParameter("Attack", 0.1, 200, () => Attack, v => Attack = v, "0.0", "ms"));
        AddParam(new FloatParameter("Release", 5, 1000, () => ReleaseMs, v => ReleaseMs = v, "0", "ms"));
        AddParam(new FloatParameter("Makeup", 0, 24, () => MakeupDb, v => MakeupDb = v, "0.0", "dB"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _env = new EnvelopeFollower[VoiceCount];
        for (var i = 0; i < VoiceCount; i++) _env[i] = new EnvelopeFollower();
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _env.Length) _env[voice].Reset();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var env = _env[ctx.Voice];
        env.SetTimes(Attack, ReleaseMs, Format.SampleRate);
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var threshold = ThresholdDb;
        var ratio = Math.Max(1.0, Ratio);
        var makeup = (float)AudioMath.Db2Lin(MakeupDb);
        for (var i = 0; i < ctx.Frames; i++)
        {
            var x = input[i];
            var level = env.Process(x < 0 ? -x : x);
            var db = AudioMath.Lin2Db(level);
            var gainDb = 0.0;
            if (db > threshold) gainDb = (threshold - db) * (1.0 - 1.0 / ratio);
            outBuf[i] = x * (float)AudioMath.Db2Lin(gainDb) * makeup;
        }
    }
}
