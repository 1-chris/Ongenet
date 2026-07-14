using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Persistence;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A stereo-linked feed-forward compressor: a peak envelope follower drives gain reduction above
/// the threshold by the given ratio, with attack/release ballistics and makeup gain. Optional
/// external sidechain input ducks from another track's output.
/// </summary>
public sealed class CompressorEffect : IAudioEffect, IContextualEffect, ISourceTrackEffect, IProjectStatefulComponent
{
    public const string TypeId = "compressor";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double ThresholdDb { get; set; } = -18.0;
    public double Ratio { get; set; } = 4.0;
    public double AttackMs { get; set; } = 10.0;
    public double ReleaseMs { get; set; } = 120.0;
    public double MakeupDb { get; set; }
    public bool Enhanced { get; set; }

    /// <summary>Source track whose output drives the detector; null = use this track's input.</summary>
    public Guid? SidechainSourceTrackId { get; set; }

    Guid? ISourceTrackEffect.SourceTrackId
    {
        get => SidechainSourceTrackId;
        set => SidechainSourceTrackId = value;
    }

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private readonly EnvelopeFollower _follower = new();
    private EffectContext? _ctx;

    public string Name => "Compressor";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Threshold", -60.0, 0.0, () => ThresholdDb, v => ThresholdDb = v, "0.#", "dB"),
        new FloatParameter("Ratio", 1.0, 20.0, () => Ratio, v => Ratio = v, "0.#", ":1"),
        new FloatParameter("Attack", 0.1, 200.0, () => AttackMs, v => AttackMs = v, "0.#", "ms", 2.0),
        new FloatParameter("Release", 5.0, 1000.0, () => ReleaseMs, v => ReleaseMs = v, "0", "ms", 2.0),
        new FloatParameter("Makeup", 0.0, 24.0, () => MakeupDb, v => MakeupDb = v, "0.#", "dB"),
        new BoolParameter("Enhanced", () => Enhanced, v => Enhanced = v)
    };

    public void SetContext(EffectContext context) => _ctx = context;

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _follower.Reset();
    }

    public IAudioEffect Clone() => new CompressorEffect
    {
        Enabled = Enabled, ThresholdDb = ThresholdDb, Ratio = Ratio,
        AttackMs = AttackMs, ReleaseMs = ReleaseMs, MakeupDb = MakeupDb,
        Enhanced = Enhanced, SidechainSourceTrackId = SidechainSourceTrackId
    };

    public void Process(Span<float> buffer)
    {
        var channels = _channels < 1 ? 1 : _channels;
        _follower.SetTimes(AttackMs, ReleaseMs, _sampleRate);
        var threshold = ThresholdDb;
        var slope = 1.0 - 1.0 / Math.Max(1.0, Ratio);
        var makeup = AudioMath.Db2Lin(MakeupDb);

        ReadOnlySpan<float> sidechain = ReadOnlySpan<float>.Empty;
        var sidechainChannels = 1;
        if (_ctx is not null && SidechainSourceTrackId is { } srcId)
        {
            _ctx.Sidechain.Request(srcId);
            sidechain = _ctx.Sidechain.Read(srcId, out sidechainChannels);
        }

        var useSidechain = SidechainSourceTrackId.HasValue;
        var sidechainFrames = sidechainChannels > 0 ? sidechain.Length / sidechainChannels : 0;
        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            float detect = 0;

            if (useSidechain && frame < sidechainFrames)
            {
                var si = frame * sidechainChannels;
                for (var c = 0; c < sidechainChannels; c++)
                {
                    var a = sidechain[si + c];
                    if (a < 0) a = -a;
                    if (a > detect) detect = a;
                }
            }
            else
            {
                for (var c = 0; c < channels; c++)
                {
                    var a = buffer[i + c];
                    if (a < 0) a = -a;
                    if (a > detect) detect = a;
                }
            }

            var env = _follower.Process(detect);
            var levelDb = AudioMath.Lin2Db(env);
            var over = levelDb - threshold;
            double grDb;
            if (Enhanced && over > -6.0 && over < 0)
                grDb = over * over / 12.0 * slope; // soft 6 dB knee
            else
                grDb = over > 0 ? over * slope : 0;
            var gain = (float)(makeup * AudioMath.Db2Lin(-grDb));

            for (var c = 0; c < channels; c++) buffer[i + c] *= gain;
        }
    }

    public void WriteProjectState(OngenWriter writer)
    {
        writer.WriteBool(SidechainSourceTrackId.HasValue);
        writer.WriteGuid(SidechainSourceTrackId ?? Guid.Empty);
    }

    public void ReadProjectState(OngenReader reader)
    {
        var has = reader.ReadBool();
        var id = reader.ReadGuid();
        SidechainSourceTrackId = has ? id : null;
    }
}
