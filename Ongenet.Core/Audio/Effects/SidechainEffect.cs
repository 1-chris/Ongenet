using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A ducking sidechain. With no source picked it runs a tempo-synced volume "pump" (the classic
/// EDM ghost-kick effect) retriggered on a chosen note division — zero setup, just works. Pick a source
/// track/group (e.g. the kick) and it instead ducks whenever that track is loud, like a real sidechain
/// compressor. The source signal is read through the engine's <see cref="ISidechainBus"/>.
/// </summary>
public sealed class SidechainEffect : IAudioEffect, IContextualEffect, IProjectStatefulComponent
{
    public const string TypeId = "sidechain";

    string IAudioEffect.TypeId => TypeId;
    public string Name => "Sidechain";
    public bool Enabled { get; set; } = true;

    /// <summary>Tempo-mode note divisions (quarter-note beats) and their labels, index-aligned.</summary>
    private static readonly double[] DivisionBeats = { 4.0, 2.0, 1.0, 0.5, 0.25, 1.0 / 3.0 };
    private static readonly string[] DivisionNames = { "1 bar", "1/2", "1/4", "1/8", "1/16", "1/8T" };

    /// <summary>How much the level is ducked at the peak of each pump (0 = none, 1 = full silence).</summary>
    public double Amount { get; set; } = 0.85;

    /// <summary>Tempo-mode pump rate (index into <see cref="DivisionNames"/>); default 1/4 note.</summary>
    public int RateIndex { get; set; } = 2;

    /// <summary>Track-mode envelope attack (ms) — how fast the duck clamps down when the source hits.</summary>
    public double AttackMs { get; set; } = 5.0;

    /// <summary>Track-mode envelope release (ms) — how fast the level recovers after the source.</summary>
    public double ReleaseMs { get; set; } = 200.0;

    /// <summary>Source track/group whose output triggers the duck; null = tempo-synced pump mode.</summary>
    public Guid? SourceTrackId { get; set; }

    /// <summary>True when ducking from an external track rather than the tempo-synced pump.</summary>
    public bool IsTrackMode => SourceTrackId.HasValue;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private readonly EnvelopeFollower _follower = new();
    private readonly OnePole _gainSmooth = new();
    private EffectContext? _ctx;

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Amount", 0.0, 1.0, () => Amount, v => Amount = v, "0%", "", 1.0),
        new ChoiceParameter("Rate", DivisionNames, () => RateIndex, v => RateIndex = v),
        new FloatParameter("Attack", 0.1, 100.0, () => AttackMs, v => AttackMs = v, "0.#", "ms", 2.0),
        new FloatParameter("Release", 5.0, 1000.0, () => ReleaseMs, v => ReleaseMs = v, "0", "ms", 2.0)
    };

    public void SetContext(EffectContext context) => _ctx = context;

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _follower.Reset();
        _gainSmooth.SetSmoothTime(3.0, _sampleRate); // de-zipper / de-click the gain envelope
        _gainSmooth.Reset(1.0);
    }

    public void Process(Span<float> buffer)
    {
        var channels = _channels < 1 ? 1 : _channels;
        var frames = buffer.Length / channels;
        var amount = AudioMath.Clamp(Amount, 0.0, 1.0);

        if (IsTrackMode) ProcessTrack(buffer, frames, channels, amount);
        else ProcessTempo(buffer, frames, channels, amount);
    }

    // --- Tempo-synced pump: a duck retriggered every note division, shaped to recover over the division. ---
    private void ProcessTempo(Span<float> buffer, int frames, int channels, double amount)
    {
        var ctx = _ctx;
        if (ctx is null || !ctx.Playing)
        {
            // Stopped: glide back to unity so there's no residual duck.
            ApplyFlat(buffer, frames, channels);
            return;
        }

        var division = DivisionBeats[Math.Clamp(RateIndex, 0, DivisionBeats.Length - 1)];
        var beatsPerSample = ctx.Bpm / 60.0 / _sampleRate;
        var beat = ctx.PlayheadBeats;

        for (var f = 0; f < frames; f++)
        {
            var phase = Frac((beat + f * beatsPerSample) / division); // 0 at the hit, → 1 over the division
            var dip = (1.0 - phase) * (1.0 - phase);                  // full duck at the hit, eases back up
            var gain = (float)_gainSmooth.ProcessLP(1.0 - amount * dip);
            ScaleFrame(buffer, f * channels, channels, gain);
        }
    }

    // --- Track-trigger: duck by the smoothed level of the source track read from the sidechain bus. ---
    private void ProcessTrack(Span<float> buffer, int frames, int channels, double amount)
    {
        var ctx = _ctx;
        var src = ReadOnlySpan<float>.Empty;
        var srcChannels = 1;
        if (ctx is not null && SourceTrackId is { } id)
        {
            ctx.Sidechain.Request(id);                 // ask the engine to publish this source
            src = ctx.Sidechain.Read(id, out srcChannels);
        }

        _follower.SetTimes(AttackMs, ReleaseMs, _sampleRate);
        var srcFrames = srcChannels > 0 ? src.Length / srcChannels : 0;

        for (var f = 0; f < frames; f++)
        {
            // Peak across the source's channels for this frame (0 when the source is silent/absent).
            float detect = 0;
            if (f < srcFrames)
            {
                var si = f * srcChannels;
                for (var c = 0; c < srcChannels; c++)
                {
                    var a = src[si + c];
                    if (a < 0) a = -a;
                    if (a > detect) detect = a;
                }
            }

            var env = _follower.Process(detect);
            var gain = (float)_gainSmooth.ProcessLP(1.0 - amount * AudioMath.Clamp(env, 0.0, 1.0));
            ScaleFrame(buffer, f * channels, channels, gain);
        }
    }

    private void ApplyFlat(Span<float> buffer, int frames, int channels)
    {
        for (var f = 0; f < frames; f++)
        {
            var gain = (float)_gainSmooth.ProcessLP(1.0);
            if (gain < 0.9999f) ScaleFrame(buffer, f * channels, channels, gain); // only touch while gliding up
        }
    }

    private static void ScaleFrame(Span<float> buffer, int i, int channels, float gain)
    {
        for (var c = 0; c < channels; c++) buffer[i + c] *= gain;
    }

    private static double Frac(double x) => x - Math.Floor(x);

    public IAudioEffect Clone() => new SidechainEffect
    {
        Enabled = Enabled,
        Amount = Amount,
        RateIndex = RateIndex,
        AttackMs = AttackMs,
        ReleaseMs = ReleaseMs,
        SourceTrackId = SourceTrackId
    };

    // The source-track reference isn't a generic Parameter, so persist it as custom state.
    public void WriteProjectState(OngenWriter writer)
    {
        writer.WriteBool(SourceTrackId.HasValue);
        writer.WriteGuid(SourceTrackId ?? Guid.Empty);
    }

    public void ReadProjectState(OngenReader reader)
    {
        var has = reader.ReadBool();
        var id = reader.ReadGuid();
        SourceTrackId = has ? id : null;
    }
}
