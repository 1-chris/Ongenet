using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A stereo-linked feed-forward compressor: a peak envelope follower drives gain reduction above
/// the threshold by the given ratio, with attack/release ballistics and makeup gain.
/// </summary>
public sealed class CompressorEffect : IAudioEffect
{
    public const string TypeId = "compressor";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double ThresholdDb { get; set; } = -18.0;
    public double Ratio { get; set; } = 4.0;
    public double AttackMs { get; set; } = 10.0;
    public double ReleaseMs { get; set; } = 120.0;
    public double MakeupDb { get; set; }

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private readonly EnvelopeFollower _follower = new();

    public string Name => "Compressor";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Threshold", -60.0, 0.0, () => ThresholdDb, v => ThresholdDb = v, "0.#", "dB"),
        new FloatParameter("Ratio", 1.0, 20.0, () => Ratio, v => Ratio = v, "0.#", ":1"),
        new FloatParameter("Attack", 0.1, 200.0, () => AttackMs, v => AttackMs = v, "0.#", "ms", 2.0),
        new FloatParameter("Release", 5.0, 1000.0, () => ReleaseMs, v => ReleaseMs = v, "0", "ms", 2.0),
        new FloatParameter("Makeup", 0.0, 24.0, () => MakeupDb, v => MakeupDb = v, "0.#", "dB")
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _follower.Reset();
    }

    public IAudioEffect Clone() => new CompressorEffect
    {
        Enabled = Enabled, ThresholdDb = ThresholdDb, Ratio = Ratio,
        AttackMs = AttackMs, ReleaseMs = ReleaseMs, MakeupDb = MakeupDb
    };

    public void Process(Span<float> buffer)
    {
        var channels = _channels < 1 ? 1 : _channels;
        _follower.SetTimes(AttackMs, ReleaseMs, _sampleRate);
        var threshold = ThresholdDb;
        var slope = 1.0 - 1.0 / Math.Max(1.0, Ratio);
        var makeup = AudioMath.Db2Lin(MakeupDb);

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            float detect = 0;
            for (var c = 0; c < channels; c++)
            {
                var a = buffer[i + c];
                if (a < 0) a = -a;
                if (a > detect) detect = a;
            }

            var env = _follower.Process(detect);
            var over = AudioMath.Lin2Db(env) - threshold;
            var grDb = over > 0 ? over * slope : 0;
            var gain = (float)(makeup * AudioMath.Db2Lin(-grDb));

            for (var c = 0; c < channels; c++) buffer[i + c] *= gain;
        }
    }
}
