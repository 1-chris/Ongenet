using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Dynamics processor: above threshold compresses, below expands (hybrid), with attack/release
/// ballistics and makeup gain.
/// </summary>
public sealed class DynamicsEffect : IAudioEffect
{
    public const string TypeId = "dynamics";

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

    public string Name => "Dynamics";

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

    public IAudioEffect Clone() => new DynamicsEffect
    {
        Enabled = Enabled, ThresholdDb = ThresholdDb, Ratio = Ratio,
        AttackMs = AttackMs, ReleaseMs = ReleaseMs, MakeupDb = MakeupDb
    };

    public void Process(Span<float> buffer)
    {
        var channels = _channels < 1 ? 1 : _channels;
        _follower.SetTimes(AttackMs, ReleaseMs, _sampleRate);
        var threshold = ThresholdDb;
        var ratio = Math.Max(1.0, Ratio);
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
            var levelDb = AudioMath.Lin2Db(env);
            double gainDb;
            if (levelDb > threshold)
            {
                // Compress above threshold.
                gainDb = -(levelDb - threshold) * (1.0 - 1.0 / ratio);
            }
            else
            {
                // Expand below threshold (pull quieter material further down).
                gainDb = (levelDb - threshold) * (1.0 - 1.0 / ratio);
                if (gainDb < -24.0) gainDb = -24.0;
            }

            var gain = (float)(makeup * AudioMath.Db2Lin(gainDb));
            for (var c = 0; c < channels; c++) buffer[i + c] *= gain;
        }
    }
}
