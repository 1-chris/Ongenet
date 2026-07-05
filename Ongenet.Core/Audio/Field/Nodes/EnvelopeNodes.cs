using System;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Field.Nodes;

/// <summary>Gated ADSR amplitude/modulation envelope. Retriggers on a rising edge of the gate inlet.</summary>
public sealed class AdsrNode : FieldNode
{
    public const string Type = "env.adsr";
    public override string TypeId => Type;
    public override string DisplayName => "ADSR";
    public override string Category => FieldNodeCategories.Envelopes;

    public double Attack { get; set; } = 0.005;
    public double Decay { get; set; } = 0.08;
    public double Sustain { get; set; } = 0.7;
    public double Release { get; set; } = 0.2;

    private AdsrEnvelope[] _env = Array.Empty<AdsrEnvelope>();
    private float[] _prevGate = Array.Empty<float>();

    public AdsrNode()
    {
        AddInput("gate", "Gate", FieldSignalKind.Note);
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Attack", 0.001, 4.0, () => Attack, v => Attack = v, "0.000", "s"));
        AddParam(new FloatParameter("Decay", 0.001, 4.0, () => Decay, v => Decay = v, "0.000", "s"));
        AddParam(new FloatParameter("Sustain", 0.0, 1.0, () => Sustain, v => Sustain = v));
        AddParam(new FloatParameter("Release", 0.001, 6.0, () => Release, v => Release = v, "0.000", "s"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _env = new AdsrEnvelope[VoiceCount];
        _prevGate = new float[VoiceCount];
        for (var i = 0; i < VoiceCount; i++)
        {
            _env[i] = new AdsrEnvelope();
            _env[i].SetSampleRate(format.SampleRate);
        }
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _prevGate.Length) _prevGate[voice] = 0f;
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var env = _env[v];
        env.AttackSeconds = ModValue(ctx, 0, Attack, 0);
        env.DecaySeconds = ModValue(ctx, 1, Decay, 0);
        env.SustainLevel = ModValue(ctx, 2, Sustain, 0);
        env.ReleaseSeconds = ModValue(ctx, 3, Release, 0);

        var gate = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var g0 = gate.Length > 0 ? gate[0] : 0f;
        if (_prevGate[v] <= 0.5f && g0 > 0.5f) env.Gate();
        else if (_prevGate[v] > 0.5f && g0 <= 0.5f) env.Release();
        _prevGate[v] = g0;

        for (var i = 0; i < ctx.Frames; i++) outBuf[i] = env.Process();
    }
}

/// <summary>Gated DAHDSR envelope (delay, hold added to ADSR) — the SFZ-style envelope.</summary>
public sealed class DahdsrNode : FieldNode
{
    public const string Type = "env.dahdsr";
    public override string TypeId => Type;
    public override string DisplayName => "DAHDSR";
    public override string Category => FieldNodeCategories.Envelopes;

    public double Delay { get; set; }
    public double Attack { get; set; } = 0.005;
    public double Hold { get; set; }
    public double Decay { get; set; } = 0.1;
    public double Sustain { get; set; } = 0.8;
    public double Release { get; set; } = 0.2;

    private DahdsrEnvelope[] _env = Array.Empty<DahdsrEnvelope>();
    private float[] _prevGate = Array.Empty<float>();

    public DahdsrNode()
    {
        AddInput("gate", "Gate", FieldSignalKind.Note);
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Delay", 0, 2, () => Delay, v => Delay = v, "0.000", "s"));
        AddParam(new FloatParameter("Attack", 0.001, 4, () => Attack, v => Attack = v, "0.000", "s"));
        AddParam(new FloatParameter("Hold", 0, 2, () => Hold, v => Hold = v, "0.000", "s"));
        AddParam(new FloatParameter("Decay", 0.001, 4, () => Decay, v => Decay = v, "0.000", "s"));
        AddParam(new FloatParameter("Sustain", 0, 1, () => Sustain, v => Sustain = v));
        AddParam(new FloatParameter("Release", 0.001, 6, () => Release, v => Release = v, "0.000", "s"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _env = new DahdsrEnvelope[VoiceCount];
        _prevGate = new float[VoiceCount];
        for (var i = 0; i < VoiceCount; i++)
        {
            _env[i] = new DahdsrEnvelope();
            _env[i].SetSampleRate(format.SampleRate);
        }
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _prevGate.Length) _prevGate[voice] = 0f;
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var env = _env[v];
        env.DelaySeconds = ModValue(ctx, 0, Delay, 0);
        env.AttackSeconds = ModValue(ctx, 1, Attack, 0);
        env.HoldSeconds = ModValue(ctx, 2, Hold, 0);
        env.DecaySeconds = ModValue(ctx, 3, Decay, 0);
        env.SustainLevel = ModValue(ctx, 4, Sustain, 0);
        env.ReleaseSeconds = ModValue(ctx, 5, Release, 0);

        var gate = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var g0 = gate.Length > 0 ? gate[0] : 0f;
        if (_prevGate[v] <= 0.5f && g0 > 0.5f) env.Gate();
        else if (_prevGate[v] > 0.5f && g0 <= 0.5f) env.Release();
        _prevGate[v] = g0;

        for (var i = 0; i < ctx.Frames; i++) outBuf[i] = env.Process();
    }
}

/// <summary>
/// A one-shot, time-based curve envelope (delay → attack → hold → curved decay → 0), retriggered on the gate
/// rising edge. The deterministic shape used by percussion (Kicka) for amp/pitch sweeps.
/// </summary>
public sealed class CurveEnvNode : FieldNode
{
    public const string Type = "env.curve";
    public override string TypeId => Type;
    public override string DisplayName => "Curve Env";
    public override string Category => FieldNodeCategories.Envelopes;

    public double Delay { get; set; }
    public double Attack { get; set; } = 0.001;
    public double Hold { get; set; }
    public double Decay { get; set; } = 0.3;
    public double Curve { get; set; } = 0.7;

    private double[] _t = Array.Empty<double>();
    private float[] _prevGate = Array.Empty<float>();

    public CurveEnvNode()
    {
        AddInput("gate", "Gate", FieldSignalKind.Note);
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Delay", 0, 2, () => Delay, v => Delay = v, "0.000", "s"));
        AddParam(new FloatParameter("Attack", 0.0, 1, () => Attack, v => Attack = v, "0.000", "s"));
        AddParam(new FloatParameter("Hold", 0, 2, () => Hold, v => Hold = v, "0.000", "s"));
        AddParam(new FloatParameter("Decay", 0.001, 8, () => Decay, v => Decay = v, "0.000", "s"));
        AddParam(new FloatParameter("Curve", 0, 1, () => Curve, v => Curve = v));
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
        _prevGate[voice] = 0f;
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voice;
        var env = new CurveEnvelope(ModValue(ctx, 0, Delay, 0), ModValue(ctx, 1, Attack, 0),
            ModValue(ctx, 2, Hold, 0), ModValue(ctx, 3, Decay, 0), ModValue(ctx, 4, Curve, 0));
        var gate = ctx.Input(0);
        var outBuf = ctx.Output(0);
        var g0 = gate.Length > 0 ? gate[0] : 0f;
        if (_prevGate[v] <= 0.5f && g0 > 0.5f) _t[v] = 0;
        _prevGate[v] = g0;

        var sr = Format.SampleRate <= 0 ? 44100 : Format.SampleRate;
        var dt = 1.0 / sr;
        var t = _t[v];
        for (var i = 0; i < ctx.Frames; i++)
        {
            outBuf[i] = (float)env.Evaluate(t);
            t += dt;
        }

        _t[v] = t;
    }
}

/// <summary>Peak envelope follower: rectifies its input and smooths with attack/release times.</summary>
public sealed class EnvFollowerNode : FieldNode
{
    public const string Type = "env.follower";
    public override string TypeId => Type;
    public override string DisplayName => "Env Follower";
    public override string Category => FieldNodeCategories.Envelopes;

    public double Attack { get; set; } = 5;
    public double ReleaseMs { get; set; } = 100;

    private EnvelopeFollower[] _f = Array.Empty<EnvelopeFollower>();

    public EnvFollowerNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out", FieldSignalKind.Cv);
        AddParam(new FloatParameter("Attack", 0.1, 200, () => Attack, v => Attack = v, "0.#", "ms"));
        AddParam(new FloatParameter("Release", 1, 1000, () => ReleaseMs, v => ReleaseMs = v, "0.#", "ms"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _f = new EnvelopeFollower[VoiceCount];
        for (var i = 0; i < VoiceCount; i++) _f[i] = new EnvelopeFollower();
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _f.Length) _f[voice].Reset();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var f = _f[ctx.Voice];
        f.SetTimes(ModValue(ctx, 0, Attack, 0), ModValue(ctx, 1, ReleaseMs, 0), Format.SampleRate);
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++)
        {
            var x = input[i];
            outBuf[i] = (float)f.Process(x < 0 ? -x : x);
        }
    }
}
