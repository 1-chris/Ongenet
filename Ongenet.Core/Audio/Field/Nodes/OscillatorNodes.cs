using System;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Field.Nodes;

/// <summary>
/// A single naive oscillator (sine/triangle/saw/square/noise). Tracks a pitch (Hz) inlet, transposed by
/// coarse/fine tuning, and accepts a phase-modulation inlet (in cycles) for FM/PM patches.
/// </summary>
public sealed class WaveOscNode : FieldNode
{
    public const string Type = "osc.wave";
    public override string TypeId => Type;
    public override string DisplayName => "Wave Osc";
    public override string Category => FieldNodeCategories.Oscillators;

    public int WaveIndex { get; set; }         // OscWave
    public double Coarse { get; set; }          // semitones
    public double Fine { get; set; }            // cents
    public double PhaseOffset { get; set; }
    public double Level { get; set; } = 1.0;

    private WaveOscillator[] _osc = Array.Empty<WaveOscillator>();

    public WaveOscNode()
    {
        AddInput("pitch", "Pitch", FieldSignalKind.Note);
        AddInput("fm", "Phase Mod", FieldSignalKind.Cv);
        AddOutput("out", "Out");
        AddParam(new ChoiceParameter("Wave", new[] { "Sine", "Triangle", "Saw", "Square", "Noise" },
            () => WaveIndex, i => WaveIndex = i), modulatable: false);
        AddParam(new FloatParameter("Coarse", -48, 48, () => Coarse, v => Coarse = v, "0.#", "st"));
        AddParam(new FloatParameter("Fine", -100, 100, () => Fine, v => Fine = v, "0.#", "ct"));
        AddParam(new FloatParameter("Phase", 0, 1, () => PhaseOffset, v => PhaseOffset = v));
        AddParam(new FloatParameter("Level", 0, 1, () => Level, v => Level = v));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _osc = new WaveOscillator[VoiceCount];
        for (var i = 0; i < VoiceCount; i++)
        {
            _osc[i] = new WaveOscillator();
            _osc[i].SetSampleRate(format.SampleRate);
            _osc[i].SeedNoise((uint)(0x1000 + i * 2654435761u));
        }
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _osc.Length) _osc[voice].ResetPhase(0);
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var osc = _osc[ctx.Voice];
        osc.Wave = (OscWave)Math.Clamp(WaveIndex, 0, 4);
        var pitch = ctx.Input(0);
        var fm = ctx.Input(1);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++)
        {
            var coarse = ModValue(ctx, 1, Coarse, i);
            var fine = ModValue(ctx, 2, Fine, i);
            var freq = pitch[i] * Math.Pow(2.0, (coarse + fine / 100.0) / 12.0);
            osc.SetFrequency(freq);
            osc.PhaseOffset = ModValue(ctx, 3, PhaseOffset, i) + fm[i];
            outBuf[i] = osc.Next() * (float)ModValue(ctx, 4, Level, i);
        }
    }
}

/// <summary>White-noise source with its own per-voice generator (decorrelated across voices).</summary>
public sealed class NoiseNode : FieldNode
{
    public const string Type = "osc.noise";
    public override string TypeId => Type;
    public override string DisplayName => "Noise";
    public override string Category => FieldNodeCategories.Oscillators;

    public double Level { get; set; } = 1.0;
    private FastRandom[] _rng = Array.Empty<FastRandom>();

    public NoiseNode()
    {
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Level", 0, 1, () => Level, v => Level = v));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _rng = new FastRandom[VoiceCount];
        for (var i = 0; i < VoiceCount; i++) _rng[i] = new FastRandom((uint)(0x9111 + i * 40503u));
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var outBuf = ctx.Output(0);
        var rng = _rng[ctx.Voice];
        for (var i = 0; i < ctx.Frames; i++)
            outBuf[i] = rng.NextBipolar() * (float)ModValue(ctx, 0, Level, i);
        _rng[ctx.Voice] = rng;
    }
}

/// <summary>
/// A faithful 2-operator FM cell (sine carrier phase-modulated by a sine modulator at Ratio × the note
/// frequency, depth Mod Index) — the core of the built-in FM Synth, exposed as a reusable node.
/// </summary>
public sealed class FmOperatorNode : FieldNode
{
    public const string Type = "osc.fm2op";
    public override string TypeId => Type;
    public override string DisplayName => "FM Operator";
    public override string Category => FieldNodeCategories.Oscillators;
    private const double TwoPi = 2.0 * Math.PI;

    public double Ratio { get; set; } = 2.0;
    public double ModIndex { get; set; } = 2.0;

    private double[] _cPhase = Array.Empty<double>();
    private double[] _mPhase = Array.Empty<double>();

    public FmOperatorNode()
    {
        AddInput("pitch", "Pitch", FieldSignalKind.Note);
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Ratio", 0.5, 8.0, () => Ratio, v => Ratio = v, "0.0"));
        AddParam(new FloatParameter("Mod Index", 0.0, 12.0, () => ModIndex, v => ModIndex = v, "0.0"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _cPhase = new double[VoiceCount];
        _mPhase = new double[VoiceCount];
    }

    public override void ResetVoice(int voice)
    {
        if (voice >= _cPhase.Length) return;
        _cPhase[voice] = 0;
        _mPhase[voice] = 0;
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var pitch = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var sr = Format.SampleRate <= 0 ? 44100 : Format.SampleRate;
        var cp = _cPhase[v];
        var mp = _mPhase[v];
        for (var i = 0; i < ctx.Frames; i++)
        {
            var freq = pitch[i];
            var ratio = ModValue(ctx, 0, Ratio, i);
            var index = ModValue(ctx, 1, ModIndex, i);
            var cInc = freq / sr;
            var mInc = freq * ratio / sr;
            var modulator = Math.Sin(mp * TwoPi);
            outBuf[i] = (float)Math.Sin((cp + index * modulator) * TwoPi);
            cp += cInc;
            if (cp >= 1.0) cp -= 1.0;
            mp += mInc;
            if (mp >= 1.0) mp -= 1.0;
        }

        _cPhase[v] = cp;
        _mPhase[v] = mp;
    }
}

/// <summary>A detuned unison stack (supersaw-style) producing a wide stereo tone — the Padda layer building block.</summary>
public sealed class UnisonOscNode : FieldNode
{
    public const string Type = "osc.unison";
    public override string TypeId => Type;
    public override string DisplayName => "Unison Osc";
    public override string Category => FieldNodeCategories.Oscillators;
    private const int MaxUnison = 9;

    public int WaveIndex { get; set; } = 2; // saw
    public int Voices { get; set; } = 7;
    public double DetuneCents { get; set; } = 20;
    public double StereoWidth { get; set; } = 0.7;
    public double Blend { get; set; } = 0.8;
    public double Coarse { get; set; }
    public double Level { get; set; } = 1.0;

    private UnisonOscillator[] _osc = Array.Empty<UnisonOscillator>();

    public UnisonOscNode()
    {
        AddInput("pitch", "Pitch", FieldSignalKind.Note);
        AddOutput("l", "L");
        AddOutput("r", "R");
        AddParam(new ChoiceParameter("Wave", new[] { "Sine", "Triangle", "Saw", "Square" },
            () => WaveIndex, i => WaveIndex = i), modulatable: false);
        AddParam(new FloatParameter("Voices", 1, MaxUnison, () => Voices, v => Voices = (int)Math.Round(v), "0"), modulatable: false);
        AddParam(new FloatParameter("Detune", 0, 100, () => DetuneCents, v => DetuneCents = v, "0.#", "ct"));
        AddParam(new FloatParameter("Width", 0, 1, () => StereoWidth, v => StereoWidth = v));
        AddParam(new FloatParameter("Blend", 0, 1, () => Blend, v => Blend = v));
        AddParam(new FloatParameter("Coarse", -48, 48, () => Coarse, v => Coarse = v, "0.#", "st"));
        AddParam(new FloatParameter("Level", 0, 1, () => Level, v => Level = v));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _osc = new UnisonOscillator[VoiceCount];
        for (var i = 0; i < VoiceCount; i++)
        {
            _osc[i] = new UnisonOscillator(MaxUnison);
            _osc[i].SetSampleRate(format.SampleRate);
            _osc[i].Seed((uint)(0x5000 + i * 2246822519u));
        }
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _osc.Length) _osc[voice].Seed((uint)(0x5000 + voice * 2246822519u));
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var osc = _osc[ctx.Voice];
        osc.Wave = (OscWave)Math.Clamp(WaveIndex, 0, 3);
        osc.Configure(Math.Clamp(Voices, 1, MaxUnison), DetuneCents, StereoWidth, Blend);
        var pitch = ctx.Input(0);
        var outL = ctx.Output(0);
        var outR = ctx.Output(1);
        var semi = Math.Pow(2.0, Coarse / 12.0);
        var lvl = (float)Level;
        for (var i = 0; i < ctx.Frames; i++)
        {
            osc.SetBaseFrequency(pitch[i] * semi);
            osc.Render(out var l, out var r);
            outL[i] = l * lvl;
            outR[i] = r * lvl;
        }
    }
}
