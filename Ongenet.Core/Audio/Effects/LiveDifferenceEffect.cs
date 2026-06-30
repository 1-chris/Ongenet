using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Subtracts a source track's signal from the audio this effect is applied to: <c>out = in − Amount·source</c>.
/// The classic use is <b>vocal isolation</b>: put the full song on one track and the instrumental on another,
/// drop this effect on the song track, and point it at the instrumental — what's left is (mostly) the vocal.
/// It reads the source through the engine's shared <see cref="ISidechainBus"/> (the same mechanism the
/// Sidechain and Vocoder effects use), so it needs no new engine plumbing.
///
/// <para><b>Track order matters.</b> The sidechain bus delivers the source's <i>current</i> block only when
/// the source track is rendered before this one, so for sample-accurate cancellation the subtracted track
/// must sit <i>above</i> the track carrying this effect. Cancellation is also only as good as the two
/// signals' time/level alignment — align the clips on the timeline and trim <see cref="Amount"/> to taste.</para>
/// </summary>
public sealed class LiveDifferenceEffect : IAudioEffect, IContextualEffect, ISourceTrackEffect, IProjectStatefulComponent
{
    public const string TypeId = "live-difference";

    string IAudioEffect.TypeId => TypeId;
    public string Name => "Live Difference";
    public bool Enabled { get; set; } = true;

    /// <summary>How much of the source is subtracted (1 = full subtraction; &gt;1 over-subtracts).</summary>
    public double Amount { get; set; } = 1.0;

    /// <summary>Make-up gain (dB) applied to the residual, since the difference is usually quieter.</summary>
    public double OutputDb { get; set; }

    /// <summary>
    /// Fine time alignment of the source relative to the dry signal, in milliseconds (fractional-sample,
    /// bipolar). Phase cancellation is extremely sensitive to timing — tune this by ear until the
    /// subtracted material nulls out. Positive delays the source; negative delays the dry signal.
    /// </summary>
    public double AlignMs { get; set; }

    /// <summary>The source track/group whose output is subtracted; null = pass-through.</summary>
    public Guid? SourceTrackId { get; set; }

    private const double MaxAlignMs = 50.0;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private DelayLine[] _dryLines = Array.Empty<DelayLine>();
    private DelayLine[] _srcLines = Array.Empty<DelayLine>();
    private EffectContext? _ctx;
    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Amount", 0.0, 2.0, () => Amount, v => Amount = v, "0.00", "x", 1.0),
        new FloatParameter("Align", -MaxAlignMs, MaxAlignMs, () => AlignMs, v => AlignMs = v, "0.00", "ms"),
        new FloatParameter("Output", -24.0, 24.0, () => OutputDb, v => OutputDb = v, "0.#", "dB")
    };

    public void SetContext(EffectContext context) => _ctx = context;

    public void Prepare(AudioFormat format)
    {
        var channels = format.Channels < 1 ? 1 : format.Channels;
        _channels = channels;
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;

        // Delay lines size for the max alignment plus a 1-sample base latency and interpolation headroom.
        var size = (int)Math.Ceiling(MaxAlignMs * _sampleRate / 1000.0) + 4;
        var dry = new DelayLine[channels];
        var srcL = new DelayLine[channels];
        for (var c = 0; c < channels; c++)
        {
            dry[c] = new DelayLine(); dry[c].Resize(size);
            srcL[c] = new DelayLine(); srcL[c].Resize(size);
        }

        // Publish the fully-built arrays with single reference assignments. Prepare runs on the UI thread
        // while Process runs on the audio thread (and the engine can activate the effect before Prepare),
        // so the audio thread must only ever see a complete, internally-consistent line array — never a
        // half-resized one (which previously crashed ReadFrac with an out-of-range index).
        _dryLines = dry;
        _srcLines = srcL;
    }

    public void Process(Span<float> buffer)
    {
        var channels = _channels < 1 ? 1 : _channels;
        var frames = buffer.Length / channels;
        var amount = (float)Math.Max(0.0, Amount);
        var outGain = (float)AudioMath.Db2Lin(OutputDb);

        // Read the source track's output from the shared sidechain bus (empty until the engine publishes it).
        var src = ReadOnlySpan<float>.Empty;
        var srcChannels = 1;
        if (_ctx is { } ctx && SourceTrackId is { } id)
        {
            ctx.Sidechain.Request(id);
            src = ctx.Sidechain.Read(id, out srcChannels);
        }

        if (src.IsEmpty || amount <= 0f)
        {
            // Nothing to subtract — just the make-up gain (true pass-through at 0 dB).
            if (outGain != 1f)
                for (var i = 0; i < buffer.Length; i++) buffer[i] *= outGain;
            return;
        }

        // Snapshot the delay-line arrays once — Prepare may swap them from another thread (and the effect can
        // be active before its first Prepare). If they aren't ready/sized for this block, fall back to a
        // direct, zero-latency subtraction so we never index a missing/half-built line.
        var dryLines = _dryLines;
        var srcLines = _srcLines;
        var canAlign = dryLines.Length >= channels && srcLines.Length >= channels;

        // Bipolar fine alignment: delay the source by +align, or the dry by −align, plus a shared 1-sample
        // base so both reads are ≥1 (lets the delay line interpolate a fractional-sample offset). Align is
        // clamped to the range the lines were sized for. Output latency is ~1 sample at align ≥ 0.
        var alignSamples = Math.Clamp(AlignMs, -MaxAlignMs, MaxAlignMs) * _sampleRate / 1000.0;
        var srcDelay = 1.0 + Math.Max(0.0, alignSamples);
        var dryDelay = 1.0 + Math.Max(0.0, -alignSamples);

        var srcFrames = srcChannels > 0 ? src.Length / srcChannels : 0;
        for (var f = 0; f < frames; f++)
        {
            var bi = f * channels;
            for (var c = 0; c < channels; c++)
            {
                var s = 0f;
                if (f < srcFrames)
                {
                    var sc = c < srcChannels ? c : srcChannels - 1; // mono source → broadcast to all channels
                    s = src[f * srcChannels + sc];
                }

                if (canAlign)
                {
                    // Read the delayed dry + source (read-before-write), then feed this frame in.
                    var dry = dryLines[c].ReadFrac(dryDelay);
                    var srcDelayed = srcLines[c].ReadFrac(srcDelay);
                    dryLines[c].Write(buffer[bi + c]);
                    srcLines[c].Write(s);
                    buffer[bi + c] = (dry - amount * srcDelayed) * outGain;
                }
                else
                {
                    buffer[bi + c] = (buffer[bi + c] - amount * s) * outGain;
                }
            }
        }
    }

    public IAudioEffect Clone() => new LiveDifferenceEffect
    {
        Enabled = Enabled,
        Amount = Amount,
        AlignMs = AlignMs,
        OutputDb = OutputDb,
        SourceTrackId = SourceTrackId
    };

    // The source-track reference isn't a generic Parameter, so persist it as custom state (like Sidechain).
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
