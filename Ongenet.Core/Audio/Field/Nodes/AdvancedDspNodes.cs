using System;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Field.Nodes;

/// <summary>Ring modulator: multiplies the input by a sine carrier at Frequency.</summary>
public sealed class RingModNode : FieldNode
{
    public const string Type = "shape.ringmod";
    public override string TypeId => Type;
    public override string DisplayName => "Ring Mod";
    public override string Category => FieldNodeCategories.Shapers;

    public double Frequency { get; set; } = 440;
    public double Mix { get; set; } = 1.0;

    private RingModulator[] _rm = Array.Empty<RingModulator>();

    public RingModNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Frequency", 1, 8000, () => Frequency, v => Frequency = v, "0", "Hz", 2.0));
        AddParam(new FloatParameter("Mix", 0, 1, () => Mix, v => Mix = v, "0.00"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _rm = new RingModulator[VoiceCount];
        for (var i = 0; i < VoiceCount; i++) _rm[i] = new RingModulator();
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _rm.Length) _rm[voice].Reset();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var rm = _rm[ctx.Voice];
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate;
        for (var i = 0; i < ctx.Frames; i++)
        {
            rm.Configure(ModValue(ctx, 0, Frequency, i), sr);
            rm.Mix = (float)ModValue(ctx, 1, Mix, i);
            outBuf[i] = rm.Process(input[i]);
        }
    }
}

/// <summary>Single-sideband frequency shifter — slides the spectrum by ShiftHz.</summary>
public sealed class FreqShiftNode : FieldNode
{
    public const string Type = "time.freqshift";
    public override string TypeId => Type;
    public override string DisplayName => "Freq Shift";
    public override string Category => FieldNodeCategories.Time;

    public double ShiftHz { get; set; } = 100;
    public double Mix { get; set; } = 1.0;

    private FreqShifter[] _fs = Array.Empty<FreqShifter>();

    public FreqShiftNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Shift", -2000, 2000, () => ShiftHz, v => ShiftHz = v, "0", "Hz"));
        AddParam(new FloatParameter("Mix", 0, 1, () => Mix, v => Mix = v, "0.00"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _fs = new FreqShifter[VoiceCount];
        for (var i = 0; i < VoiceCount; i++) _fs[i] = new FreqShifter();
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _fs.Length) _fs[voice].Reset();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var fs = _fs[ctx.Voice];
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate;
        for (var i = 0; i < ctx.Frames; i++)
        {
            fs.Configure(ModValue(ctx, 0, ShiftHz, i), sr);
            var dry = input[i];
            var wet = fs.Process(dry);
            var mix = (float)ModValue(ctx, 1, Mix, i);
            outBuf[i] = dry + (wet - dry) * mix;
        }
    }
}

/// <summary>Leslie rotary-speaker emulation with Doppler wobble and amplitude tremolo.</summary>
public sealed class RotaryNode : FieldNode
{
    public const string Type = "time.rotary";
    public override string TypeId => Type;
    public override string DisplayName => "Rotary";
    public override string Category => FieldNodeCategories.Time;

    public double Speed { get; set; } = 0.5;
    public double Drive { get; set; }
    public double Mix { get; set; } = 1.0;

    private RotarySpeaker[] _rotary = Array.Empty<RotarySpeaker>();

    public RotaryNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Speed", 0, 1, () => Speed, v => Speed = v, "0.00"));
        AddParam(new FloatParameter("Drive", 0, 24, () => Drive, v => Drive = v, "0.0", "dB"));
        AddParam(new FloatParameter("Mix", 0, 1, () => Mix, v => Mix = v, "0.00"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _rotary = new RotarySpeaker[VoiceCount];
        for (var i = 0; i < VoiceCount; i++)
        {
            _rotary[i] = new RotarySpeaker();
            _rotary[i].Configure(format.SampleRate);
        }
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _rotary.Length) _rotary[voice].Reset();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var rotary = _rotary[ctx.Voice];
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++)
        {
            rotary.SetSpeed(ModValue(ctx, 0, Speed, i));
            rotary.SetDrive(ModValue(ctx, 1, Drive, i));
            rotary.Mix = (float)ModValue(ctx, 2, Mix, i);
            var x = input[i];
            rotary.Process(x, x, out var outL, out _);
            outBuf[i] = outL;
        }
    }
}

/// <summary>FFT convolution reverb with a synthesised exponential-decay impulse.</summary>
public sealed class ConvolutionNode : FieldNode
{
    public const string Type = "time.convolution";
    public override string TypeId => Type;
    public override string DisplayName => "Convolution";
    public override string Category => FieldNodeCategories.Time;

    public double Decay { get; set; } = 1.5;
    public double Mix { get; set; } = 0.3;

    private ConvolutionReverb[] _rev = Array.Empty<ConvolutionReverb>();
    private float[][] _scratch = Array.Empty<float[]>();
    private double[] _lastDecay = Array.Empty<double>();

    public ConvolutionNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Decay", 0.1, 4, () => Decay, v => Decay = v, "0.##", "s"));
        AddParam(new FloatParameter("Mix", 0, 1, () => Mix, v => Mix = v, "0.00"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _rev = new ConvolutionReverb[VoiceCount];
        _scratch = new float[VoiceCount][];
        _lastDecay = new double[VoiceCount];
        for (var i = 0; i < VoiceCount; i++)
        {
            _rev[i] = new ConvolutionReverb();
            _rev[i].Configure(format.SampleRate, Decay);
            _scratch[i] = new float[maxBlock * 2];
            _lastDecay[i] = double.NaN;
        }
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _rev.Length) _rev[voice].Reset();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var rev = _rev[v];
        var scratch = _scratch[v];
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var decay = ModValue(ctx, 0, Decay, 0);
        if (decay != _lastDecay[v])
        {
            rev.Configure(Format.SampleRate, decay);
            _lastDecay[v] = decay;
        }

        rev.Mix = (float)ModValue(ctx, 1, Mix, 0);
        var frames = ctx.Frames;
        for (var i = 0; i < frames; i++)
        {
            scratch[i * 2] = input[i];
            scratch[i * 2 + 1] = input[i];
        }

        rev.Process(scratch, frames);
        for (var i = 0; i < frames; i++) outBuf[i] = scratch[i * 2];
    }
}

/// <summary>Hammond-style drawbar organ oscillator with simplified registration macros.</summary>
public sealed class DrawbarOrganNode : FieldNode
{
    public const string Type = "osc.organ";
    public override string TypeId => Type;
    public override string DisplayName => "Drawbar Organ";
    public override string Category => FieldNodeCategories.Oscillators;

    public double Fundamental { get; set; } = 1.0;
    public double Odd { get; set; } = 0.8;
    public double Even { get; set; } = 0.8;
    public double Vibrato { get; set; } = 0.35;

    private DrawbarOrgan[] _organ = Array.Empty<DrawbarOrgan>();

    public DrawbarOrganNode()
    {
        AddInput("pitch", "Pitch", FieldSignalKind.Note);
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Fundamental", 0, 1, () => Fundamental, v => Fundamental = v, "0.00"));
        AddParam(new FloatParameter("Odd", 0, 1, () => Odd, v => Odd = v, "0.00"));
        AddParam(new FloatParameter("Even", 0, 1, () => Even, v => Even = v, "0.00"));
        AddParam(new FloatParameter("Vibrato", 0, 1, () => Vibrato, v => Vibrato = v, "0.00"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _organ = new DrawbarOrgan[VoiceCount];
        for (var i = 0; i < VoiceCount; i++) _organ[i] = new DrawbarOrgan();
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _organ.Length) _organ[voice].Reset();
    }

    private static void ApplyDrawbars(DrawbarOrgan organ, double fundamental, double odd, double even)
    {
        // Indices: 0=16′, 1=5⅓′, 2=8′, 3=4′, 4=2⅔′, 5=2′, 6=1⅗′, 7=1⅓′, 8=1′
        organ.SetDrawbar(0, fundamental * odd * 0.8);   // sub
        organ.SetDrawbar(1, even * 0.8);                // fifth
        organ.SetDrawbar(2, fundamental);               // 8′ fundamental
        organ.SetDrawbar(3, even);                      // 4′
        organ.SetDrawbar(4, odd * 0.7);                 // 2⅔′
        organ.SetDrawbar(5, even * 0.9);                // 2′
        organ.SetDrawbar(6, odd * 0.5);                 // 1⅗′
        organ.SetDrawbar(7, even * 0.6);                // 1⅓′
        organ.SetDrawbar(8, odd * 0.4);                 // 1′
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var organ = _organ[ctx.Voice];
        var pitch = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate;
        for (var i = 0; i < ctx.Frames; i++)
        {
            var fund = ModValue(ctx, 0, Fundamental, i);
            var odd = ModValue(ctx, 1, Odd, i);
            var even = ModValue(ctx, 2, Even, i);
            var vib = ModValue(ctx, 3, Vibrato, i);
            organ.Configure(pitch[i], sr);
            ApplyDrawbars(organ, fund, odd, even);
            organ.SetVibrato(5.5, vib * 35.0);
            outBuf[i] = organ.Process();
        }
    }
}

/// <summary>Casio CZ–style phase-distortion oscillator.</summary>
public sealed class PhaseDistortionNode : FieldNode
{
    public const string Type = "osc.phasedist";
    public override string TypeId => Type;
    public override string DisplayName => "Phase Distortion";
    public override string Category => FieldNodeCategories.Oscillators;

    public double Frequency { get; set; } = 440;
    public double Distort { get; set; } = 0.5;

    private PhaseDistortionOsc[] _osc = Array.Empty<PhaseDistortionOsc>();

    public PhaseDistortionNode()
    {
        AddInput("pitch", "Pitch", FieldSignalKind.Note);
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Frequency", 20, 8000, () => Frequency, v => Frequency = v, "0", "Hz", 2.0));
        AddParam(new FloatParameter("Distort", 0, 1, () => Distort, v => Distort = v, "0.00"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _osc = new PhaseDistortionOsc[VoiceCount];
        for (var i = 0; i < VoiceCount; i++) _osc[i] = new PhaseDistortionOsc();
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _osc.Length) _osc[voice].Reset();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var osc = _osc[ctx.Voice];
        var pitch = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate;
        for (var i = 0; i < ctx.Frames; i++)
        {
            var freq = pitch[i] > 1e-6 ? pitch[i] : ModValue(ctx, 0, Frequency, i);
            outBuf[i] = osc.Process(freq, ModValue(ctx, 1, Distort, i), sr);
        }
    }
}

/// <summary>Serial all-pass diffusion chain for smearing transients without spectral colour.</summary>
public sealed class AllpassDiffuserNode : FieldNode
{
    public const string Type = "filter.diffuser";
    public override string TypeId => Type;
    public override string DisplayName => "Diffuser";
    public override string Category => FieldNodeCategories.Filters;

    public double Size { get; set; } = 0.6;
    public double Feedback { get; set; } = 0.5;

    private AllpassDiffuser[] _diff = Array.Empty<AllpassDiffuser>();

    public AllpassDiffuserNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Size", 0.05, 1, () => Size, v => Size = v, "0.00"));
        AddParam(new FloatParameter("Feedback", 0, 0.9, () => Feedback, v => Feedback = v, "0.00"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _diff = new AllpassDiffuser[VoiceCount];
        for (var i = 0; i < VoiceCount; i++)
        {
            _diff[i] = new AllpassDiffuser();
            _diff[i].Configure(Size, Feedback, format.SampleRate);
        }
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _diff.Length) _diff[voice].Reset();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var diff = _diff[ctx.Voice];
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate;
        for (var i = 0; i < ctx.Frames; i++)
        {
            diff.Configure(ModValue(ctx, 0, Size, i), ModValue(ctx, 1, Feedback, i), sr);
            outBuf[i] = diff.Process(input[i]);
        }
    }
}

/// <summary>Multi-band spectral shaper with macro tilt and Low/Mid/High trims.</summary>
public sealed class HarmonicSculptNode : FieldNode
{
    public const string Type = "shape.sculpt";
    public override string TypeId => Type;
    public override string DisplayName => "Harmonic Sculpt";
    public override string Category => FieldNodeCategories.Shapers;

    private const int Bands = 8;

    public double Shape { get; set; }
    public double Low { get; set; } = 1.0;
    public double Mid { get; set; } = 1.0;
    public double High { get; set; } = 1.0;

    private HarmonicSculptor[] _sculpt = Array.Empty<HarmonicSculptor>();
    private double[] _lastShape = Array.Empty<double>();
    private double[] _lastLow = Array.Empty<double>();
    private double[] _lastMid = Array.Empty<double>();
    private double[] _lastHigh = Array.Empty<double>();

    public HarmonicSculptNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Shape", -1, 1, () => Shape, v => Shape = v, "0.00"));
        AddParam(new FloatParameter("Low", 0, 2, () => Low, v => Low = v, "0.00"));
        AddParam(new FloatParameter("Mid", 0, 2, () => Mid, v => Mid = v, "0.00"));
        AddParam(new FloatParameter("High", 0, 2, () => High, v => High = v, "0.00"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _sculpt = new HarmonicSculptor[VoiceCount];
        _lastShape = new double[VoiceCount];
        _lastLow = new double[VoiceCount];
        _lastMid = new double[VoiceCount];
        _lastHigh = new double[VoiceCount];
        for (var i = 0; i < VoiceCount; i++)
        {
            _sculpt[i] = new HarmonicSculptor();
            _sculpt[i].Configure(Bands, format.SampleRate);
            _lastShape[i] = double.NaN;
        }
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _sculpt.Length) _sculpt[voice].Reset();
    }

    private void EnsureGains(int voice, double shape, double low, double mid, double high)
    {
        if (shape == _lastShape[voice] && low == _lastLow[voice] &&
            mid == _lastMid[voice] && high == _lastHigh[voice])
            return;

        var sc = _sculpt[voice];
        for (var b = 0; b < Bands; b++)
        {
            var t = Bands > 1 ? (double)b / (Bands - 1) : 0.5;
            var tilt = 1.0 + shape * (0.5 - t) * 1.5;
            var zone = t < 0.33 ? low : t < 0.66 ? mid : high;
            sc.SetBandGain(b, tilt * zone);
        }

        _lastShape[voice] = shape;
        _lastLow[voice] = low;
        _lastMid[voice] = mid;
        _lastHigh[voice] = high;
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var sc = _sculpt[v];
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var shape = ModValue(ctx, 0, Shape, 0);
        var low = ModValue(ctx, 1, Low, 0);
        var mid = ModValue(ctx, 2, Mid, 0);
        var high = ModValue(ctx, 3, High, 0);
        EnsureGains(v, shape, low, mid, high);
        for (var i = 0; i < ctx.Frames; i++) outBuf[i] = sc.Process(input[i]);
    }
}

/// <summary>One-shot drum voice: pitch-swept sine plus noise, gated by a rising edge.</summary>
public sealed class DrumTriggerNode : FieldNode
{
    public const string Type = "smp.drum_trigger";
    public override string TypeId => Type;
    public override string DisplayName => "Drum Trigger";
    public override string Category => FieldNodeCategories.Sampler;

    public double Pitch { get; set; } = 80;
    public double Decay { get; set; } = 0.35;
    public double Noise { get; set; } = 0.25;

    private double[] _t = Array.Empty<double>();
    private double[] _phase = Array.Empty<double>();
    private float[] _prevGate = Array.Empty<float>();
    private FastRandom[] _rng = Array.Empty<FastRandom>();

    public DrumTriggerNode()
    {
        AddInput("gate", "Gate", FieldSignalKind.Note);
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Pitch", 20, 400, () => Pitch, v => Pitch = v, "0", "Hz", 2.0));
        AddParam(new FloatParameter("Decay", 0.01, 2, () => Decay, v => Decay = v, "0.00", "s"));
        AddParam(new FloatParameter("Noise", 0, 1, () => Noise, v => Noise = v, "0.00"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _t = new double[VoiceCount];
        _phase = new double[VoiceCount];
        _prevGate = new float[VoiceCount];
        _rng = new FastRandom[VoiceCount];
        for (var i = 0; i < VoiceCount; i++) _rng[i] = new FastRandom((uint)(0xD00D + i * 7919u));
    }

    public override void ResetVoice(int voice)
    {
        if (voice >= _t.Length) return;
        _t[voice] = 0;
        _phase[voice] = 0;
        _prevGate[voice] = 0;
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var gate = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var g0 = gate.Length > 0 ? gate[0] : 0f;
        if (_prevGate[v] <= 0.5f && g0 > 0.5f)
        {
            _t[v] = 0;
            _phase[v] = 0;
        }

        _prevGate[v] = g0;

        var decay = ModValue(ctx, 1, Decay, 0);
        var noiseAmt = ModValue(ctx, 2, Noise, 0);
        var baseHz = ModValue(ctx, 0, Pitch, 0);
        var ampEnv = new CurveEnvelope(0, 0.001, 0, decay, 0.7);
        var pitchEnv = new CurveEnvelope(0, 0, 0, decay * 0.6, 0.65);
        var sr = Format.SampleRate <= 0 ? 44100 : Format.SampleRate;
        var dt = 1.0 / sr;
        var t = _t[v];
        var phase = _phase[v];
        var rng = _rng[v];

        for (var i = 0; i < ctx.Frames; i++)
        {
            var amp = ampEnv.Evaluate(t);
            var pe = pitchEnv.Evaluate(t);
            var freq = baseHz * MusicalMath.SemitonesToRatio(24.0 * pe);
            var inc = freq / sr;
            var tone = (float)Math.Sin(phase * 2.0 * Math.PI) * (float)amp;
            var n = rng.NextBipolar() * (float)noiseAmt * (float)amp;
            outBuf[i] = tone + n;
            phase += inc;
            if (phase >= 1.0) phase -= 1.0;
            t += dt;
        }

        _t[v] = t;
        _phase[v] = phase;
        _rng[v] = rng;
    }
}

/// <summary>Filtered noise burst with a curved decay envelope, retriggered on gate.</summary>
public sealed class DrumNoiseNode : FieldNode
{
    public const string Type = "smp.drum_noise";
    public override string TypeId => Type;
    public override string DisplayName => "Drum Noise";
    public override string Category => FieldNodeCategories.Sampler;

    public double Cutoff { get; set; } = 4000;
    public double Decay { get; set; } = 0.2;
    public double Resonance { get; set; } = 0.7;

    private double[] _t = Array.Empty<double>();
    private float[] _prevGate = Array.Empty<float>();
    private FastRandom[] _rng = Array.Empty<FastRandom>();
    private Biquad[] _bq = Array.Empty<Biquad>();

    public DrumNoiseNode()
    {
        AddInput("gate", "Gate", FieldSignalKind.Note);
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Cutoff", 100, 16000, () => Cutoff, v => Cutoff = v, "0", "Hz", 2.0));
        AddParam(new FloatParameter("Decay", 0.01, 2, () => Decay, v => Decay = v, "0.00", "s"));
        AddParam(new FloatParameter("Resonance", 0.1, 12, () => Resonance, v => Resonance = v, "0.00"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _t = new double[VoiceCount];
        _prevGate = new float[VoiceCount];
        _rng = new FastRandom[VoiceCount];
        _bq = new Biquad[VoiceCount];
        for (var i = 0; i < VoiceCount; i++) _rng[i] = new FastRandom((uint)(0x6015E + i * 104729u));
    }

    public override void ResetVoice(int voice)
    {
        if (voice >= _t.Length) return;
        _t[voice] = 0;
        _prevGate[voice] = 0;
        if (voice < _bq.Length) _bq[voice].Reset();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var gate = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var g0 = gate.Length > 0 ? gate[0] : 0f;
        if (_prevGate[v] <= 0.5f && g0 > 0.5f) _t[v] = 0;
        _prevGate[v] = g0;

        ref var bq = ref _bq[v];
        var decay = ModValue(ctx, 1, Decay, 0);
        var env = new CurveEnvelope(0, 0.0005, 0, decay, 0.75);
        var sr = Format.SampleRate;
        var coeffs = BiquadCoefficients.Compute(FilterMode.BandPass,
            ModValue(ctx, 0, Cutoff, 0), ModValue(ctx, 2, Resonance, 0), sr);
        var t = _t[v];
        var dt = 1.0 / (sr <= 0 ? 44100 : sr);
        var rng = _rng[v];

        for (var i = 0; i < ctx.Frames; i++)
        {
            if (IsModulated(ctx, 0) || IsModulated(ctx, 2))
            {
                coeffs = BiquadCoefficients.Compute(FilterMode.BandPass,
                    ModValue(ctx, 0, Cutoff, i), ModValue(ctx, 2, Resonance, i), sr);
            }

            var amp = env.Evaluate(t);
            var n = rng.NextBipolar() * (float)amp;
            outBuf[i] = (float)bq.Process(coeffs, n);
            t += dt;
        }

        _t[v] = t;
        _rng[v] = rng;
    }
}

/// <summary>Pitch CV envelope for drum design — sweeps from Start down by Sweep semitones.</summary>
public sealed class DrumPitchEnvNode : FieldNode
{
    public const string Type = "env.drum_pitch";
    public override string TypeId => Type;
    public override string DisplayName => "Drum Pitch Env";
    public override string Category => FieldNodeCategories.Envelopes;

    public double Start { get; set; } = 200;
    public double Sweep { get; set; } = -24;
    public double Decay { get; set; } = 0.25;
    public double Curve { get; set; } = 0.65;

    private double[] _t = Array.Empty<double>();
    private float[] _prevGate = Array.Empty<float>();

    public DrumPitchEnvNode()
    {
        AddInput("gate", "Gate", FieldSignalKind.Note);
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Start", 20, 2000, () => Start, v => Start = v, "0", "Hz", 2.0));
        AddParam(new FloatParameter("Sweep", -48, 0, () => Sweep, v => Sweep = v, "0.0", "st"));
        AddParam(new FloatParameter("Decay", 0.01, 2, () => Decay, v => Decay = v, "0.00", "s"));
        AddParam(new FloatParameter("Curve", 0, 1, () => Curve, v => Curve = v, "0.00"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _t = new double[VoiceCount];
        _prevGate = new float[VoiceCount];
    }

    public override void ResetVoice(int voice)
    {
        if (voice >= _t.Length) return;
        _t[voice] = 0;
        _prevGate[voice] = 0;
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var gate = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var g0 = gate.Length > 0 ? gate[0] : 0f;
        if (_prevGate[v] <= 0.5f && g0 > 0.5f) _t[v] = 0;
        _prevGate[v] = g0;

        var env = new CurveEnvelope(0, 0, 0,
            ModValue(ctx, 2, Decay, 0), ModValue(ctx, 3, Curve, 0));
        var startHz = ModValue(ctx, 0, Start, 0);
        var sweepSt = ModValue(ctx, 1, Sweep, 0);
        var sr = Format.SampleRate <= 0 ? 44100 : Format.SampleRate;
        var dt = 1.0 / sr;
        var t = _t[v];

        for (var i = 0; i < ctx.Frames; i++)
        {
            var e = env.Evaluate(t);
            outBuf[i] = (float)(startHz * MusicalMath.SemitonesToRatio(sweepSt * e));
            t += dt;
        }

        _t[v] = t;
    }
}
