using System;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Field.Nodes;

/// <summary>
/// A feedback delay line with a modulatable time — the building block for echoes, chorus and flanger
/// (a short, LFO-modulated time gives chorus/flange). Mono; use two for stereo.
/// </summary>
public sealed class DelayNode : FieldNode
{
    public const string Type = "time.delay";
    public override string TypeId => Type;
    public override string DisplayName => "Delay";
    public override string Category => FieldNodeCategories.Time;

    public double TimeMs { get; set; } = 250;
    public double Feedback { get; set; } = 0.35;
    public double Mix { get; set; } = 0.3;

    private const double MaxMs = 4000;
    private DelayLine[] _dl = Array.Empty<DelayLine>();

    public DelayNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Time", 0.5, MaxMs, () => TimeMs, v => TimeMs = v, "0.0", "ms", 2.0));
        AddParam(new FloatParameter("Feedback", 0, 0.99, () => Feedback, v => Feedback = v, "0.00"));
        AddParam(new FloatParameter("Mix", 0, 1, () => Mix, v => Mix = v, "0.00"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        var size = (int)(MaxMs / 1000.0 * format.SampleRate) + 8;
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
        var sr = Format.SampleRate;
        for (var i = 0; i < ctx.Frames; i++)
        {
            var delaySamp = Math.Max(1.0, ModValue(ctx, 0, TimeMs, i) / 1000.0 * sr);
            var fb = (float)ModValue(ctx, 1, Feedback, i);
            var mix = (float)ModValue(ctx, 2, Mix, i);
            var delayed = dl.ReadFrac(delaySamp);
            dl.Write(input[i] + delayed * fb);
            outBuf[i] = input[i] * (1f - mix) + delayed * mix;
        }
    }
}

/// <summary>Varispeed "tape stop" slowdown driven by a 0..1 amount (1 = fully stopped).</summary>
public sealed class TapeStopNode : FieldNode
{
    public const string Type = "time.tapestop";
    public override string TypeId => Type;
    public override string DisplayName => "Tape Stop";
    public override string Category => FieldNodeCategories.Time;

    public double Amount { get; set; }

    private TapeStopProcessor[] _tape = Array.Empty<TapeStopProcessor>();

    public TapeStopNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Stop", 0, 1, () => Amount, v => Amount = v, "0.00"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _tape = new TapeStopProcessor[VoiceCount];
        for (var i = 0; i < VoiceCount; i++) { _tape[i] = new TapeStopProcessor(); _tape[i].Prepare(format.SampleRate); }
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _tape.Length) _tape[voice].Reset();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var tape = _tape[ctx.Voice];
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++) outBuf[i] = tape.Process(input[i], ModValue(ctx, 0, Amount, i));
    }
}

/// <summary>Time-domain pitch shifter (grain crossfade). Pitch offset in semitones.</summary>
public sealed class PitchShiftNode : FieldNode
{
    public const string Type = "time.pitchshift";
    public override string TypeId => Type;
    public override string DisplayName => "Pitch Shift";
    public override string Category => FieldNodeCategories.Time;

    public double Semitones { get; set; }

    private PitchShifter[] _ps = Array.Empty<PitchShifter>();

    public PitchShiftNode()
    {
        AddInput("in", "In");
        AddOutput("out", "Out");
        AddParam(new FloatParameter("Pitch", -24, 24, () => Semitones, v => Semitones = v, "0.0", "st"));
        Build();
    }

    public override void Prepare(AudioFormat format, int maxBlock, int voiceCount)
    {
        base.Prepare(format, maxBlock, voiceCount);
        _ps = new PitchShifter[VoiceCount];
        for (var i = 0; i < VoiceCount; i++) { _ps[i] = new PitchShifter(); _ps[i].Configure(format.SampleRate); }
    }

    public override void ResetVoice(int voice)
    {
        if (voice < _ps.Length) _ps[voice].Reset();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var ps = _ps[ctx.Voice];
        var input = ctx.Input(0);
        var outBuf = ctx.Output(0);
        for (var i = 0; i < ctx.Frames; i++)
        {
            ps.SetRatio(MusicalMath.SemitonesToRatio(ModValue(ctx, 0, Semitones, i)));
            outBuf[i] = ps.Process(input[i]);
        }
    }
}
