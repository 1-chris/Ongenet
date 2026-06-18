using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A brickwall peak limiter: lowering the threshold drives the signal harder into the limiter; a
/// fast peak follower pulls the gain down so the output never exceeds the ceiling, with a final
/// hard clamp as a safety net.
/// </summary>
public sealed class LimiterEffect : IAudioEffect
{
    public const string TypeId = "limiter";

    public bool Enabled { get; set; } = true;

    public double ThresholdDb { get; set; } = 0.0;   // input gain = -Threshold
    public double CeilingDb { get; set; } = -0.3;
    public double ReleaseMs { get; set; } = 80.0;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private readonly EnvelopeFollower _follower = new();

    public string Name => "Limiter";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Threshold", -24.0, 0.0, () => ThresholdDb, v => ThresholdDb = v, "0.#", "dB"),
        new FloatParameter("Ceiling", -24.0, 0.0, () => CeilingDb, v => CeilingDb = v, "0.#", "dB"),
        new FloatParameter("Release", 1.0, 500.0, () => ReleaseMs, v => ReleaseMs = v, "0", "ms", 2.0)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _follower.Reset();
    }

    public IAudioEffect Clone() => new LimiterEffect
    {
        Enabled = Enabled, ThresholdDb = ThresholdDb, CeilingDb = CeilingDb, ReleaseMs = ReleaseMs
    };

    public void Process(Span<float> buffer)
    {
        var channels = _channels < 1 ? 1 : _channels;
        _follower.SetTimes(0.5, ReleaseMs, _sampleRate); // near-instant attack, smooth release
        var inGain = (float)AudioMath.Db2Lin(-ThresholdDb);
        var ceiling = (float)AudioMath.Db2Lin(CeilingDb);

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            float detect = 0;
            for (var c = 0; c < channels; c++)
            {
                var a = buffer[i + c] * inGain;
                if (a < 0) a = -a;
                if (a > detect) detect = a;
            }

            var env = (float)_follower.Process(detect);
            var gain = env > ceiling ? ceiling / env : 1f;

            for (var c = 0; c < channels; c++)
            {
                var v = buffer[i + c] * inGain * gain;
                buffer[i + c] = AudioMath.Clamp(v, -ceiling, ceiling);
            }
        }
    }
}
