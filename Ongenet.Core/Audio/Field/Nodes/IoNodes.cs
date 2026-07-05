using System;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Field.Nodes;

/// <summary>
/// Note source: emits the current voice's pitch (Hz), gate (1 while held) and velocity (0..1). Being a note
/// source makes it — and everything downstream of it — per-voice.
/// </summary>
public sealed class NoteInNode : FieldNode
{
    public const string Type = "io.note";
    public override string TypeId => Type;
    public override string DisplayName => "Note In";
    public override string Category => FieldNodeCategories.Io;
    public override bool IsNoteSource => true;

    public NoteInNode()
    {
        AddOutput("pitch", "Pitch", FieldSignalKind.Note);
        AddOutput("gate", "Gate", FieldSignalKind.Note);
        AddOutput("vel", "Velocity", FieldSignalKind.Note);
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var v = ctx.Voices.Length > ctx.Voice ? ctx.Voices[ctx.Voice] : null;
        var hz = (float)(v?.Frequency ?? 0.0);
        var gate = v is { Gate: true } ? 1f : 0f;
        var vel = v?.Velocity ?? 0f;
        var pitch = ctx.Output(0);
        var g = ctx.Output(1);
        var vv = ctx.Output(2);
        for (var i = 0; i < ctx.Frames; i++) { pitch[i] = hz; g[i] = gate; vv[i] = vel; }
    }
}

/// <summary>Feeds the graph's incoming audio (effect mode) as a stereo pair.</summary>
public sealed class AudioInNode : FieldNode
{
    public const string Type = "io.audio_in";
    public override string TypeId => Type;
    public override string DisplayName => "Audio In";
    public override string Category => FieldNodeCategories.Io;

    public AudioInNode()
    {
        AddOutput("l", "L");
        AddOutput("r", "R");
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        Array.Copy(ctx.HostInLeft, ctx.Output(0), ctx.Frames);
        Array.Copy(ctx.HostInRight, ctx.Output(1), ctx.Frames);
    }
}

/// <summary>Feeds the sidechain / source-track audio as a stereo pair (zero when no source is connected).</summary>
public sealed class SidechainInNode : FieldNode
{
    public const string Type = "io.sidechain_in";
    public override string TypeId => Type;
    public override string DisplayName => "Sidechain In";
    public override string Category => FieldNodeCategories.Io;

    public SidechainInNode()
    {
        AddOutput("l", "L");
        AddOutput("r", "R");
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        Array.Copy(ctx.SidechainLeft, ctx.Output(0), ctx.Frames);
        Array.Copy(ctx.SidechainRight, ctx.Output(1), ctx.Frames);
    }
}

/// <summary>
/// The graph's final output. Always global, so its per-voice inputs are summed across active voices (the
/// voice-collecting boundary). Applies a level with optional modulation.
/// </summary>
public sealed class AudioOutNode : FieldNode
{
    public const string Type = "io.audio_out";
    public override string TypeId => Type;
    public override string DisplayName => "Audio Out";
    public override string Category => FieldNodeCategories.Io;
    public override bool ForceGlobal => true;

    public double Level { get; set; } = 1.0;

    public AudioOutNode()
    {
        AddInput("l", "L");
        AddInput("r", "R");
        AddParam(new FloatParameter("Level", 0.0, 2.0, () => Level, v => Level = v));
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var l = ctx.Input(0);
        var r = ctx.Input(1);
        var outL = ctx.HostOutLeft;
        var outR = ctx.HostOutRight;
        for (var i = 0; i < ctx.Frames; i++)
        {
            var lvl = (float)ModValue(ctx, 0, Level, i);
            outL[i] += l[i] * lvl;
            outR[i] += r[i] * lvl;
        }
    }
}

/// <summary>
/// A stereo voice collector: passes audio through but is always global, so per-voice inputs are summed to a
/// single global stream. Place before post-mix effects (reverb/delay) to run them once, like Padda.
/// </summary>
public sealed class VoiceSumNode : FieldNode
{
    public const string Type = "io.voice_sum";
    public override string TypeId => Type;
    public override string DisplayName => "Voice Sum";
    public override string Category => FieldNodeCategories.Io;
    public override bool ForceGlobal => true;

    public VoiceSumNode()
    {
        AddInput("l", "L");
        AddInput("r", "R");
        AddOutput("l", "L");
        AddOutput("r", "R");
        Build();
    }

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        Array.Copy(ctx.Input(0), ctx.Output(0), ctx.Frames);
        Array.Copy(ctx.Input(1), ctx.Output(1), ctx.Frames);
    }
}

/// <summary>
/// An oscilloscope tap: sums its (per-voice) input to a global mono stream, captures it for the UI, and
/// passes it through unchanged.
/// </summary>
public sealed class ScopeNode : FieldNode, IWaveformSource
{
    public const string Type = "io.scope";
    public override string TypeId => Type;
    public override string DisplayName => "Scope";
    public override string Category => FieldNodeCategories.Io;
    public override bool ForceGlobal => true;
    public override bool HasVisual => true;

    private readonly SpectrumScope _scope = new();

    public ScopeNode()
    {
        AddInput("in", "In");
        AddOutput("thru", "Thru");
        Build();
    }

    /// <summary>Sample rate the captured audio is at (for <see cref="IWaveformSource"/>).</summary>
    public int SampleRate => Format.SampleRate <= 0 ? 44100 : Format.SampleRate;

    /// <summary>Copies the most recent captured samples for the UI. Returns the number written.</summary>
    public int CaptureLatest(float[] dest) => _scope.CaptureLatest(dest);

    public override void ProcessBlock(FieldRenderContext ctx)
    {
        var input = ctx.Input(0);
        var thru = ctx.Output(0);
        Array.Copy(input, thru, ctx.Frames);
        _scope.Tap(input.AsSpan(0, ctx.Frames), 1);
    }
}
