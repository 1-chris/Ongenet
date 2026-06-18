using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A noise gate / downward expander: when the signal level falls below the threshold the gain is
/// pulled down toward the floor (Range), with attack/hold/release ballistics so it doesn't chatter.
/// </summary>
public sealed class GateEffect : IAudioEffect
{
    public const string TypeId = "gate";

    public bool Enabled { get; set; } = true;

    public double ThresholdDb { get; set; } = -40.0;
    public double AttackMs { get; set; } = 2.0;
    public double HoldMs { get; set; } = 50.0;
    public double ReleaseMs { get; set; } = 150.0;
    public double RangeDb { get; set; } = -60.0; // floor gain when fully closed

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private readonly EnvelopeFollower _detector = new();
    private readonly EnvelopeFollower _gainEnv = new();
    private int _holdRemaining;

    public string Name => "Gate";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Threshold", -80.0, 0.0, () => ThresholdDb, v => ThresholdDb = v, "0.#", "dB"),
        new FloatParameter("Attack", 0.1, 100.0, () => AttackMs, v => AttackMs = v, "0.#", "ms", 2.0),
        new FloatParameter("Hold", 0.0, 500.0, () => HoldMs, v => HoldMs = v, "0", "ms", 2.0),
        new FloatParameter("Release", 5.0, 2000.0, () => ReleaseMs, v => ReleaseMs = v, "0", "ms", 2.0),
        new FloatParameter("Range", -80.0, 0.0, () => RangeDb, v => RangeDb = v, "0.#", "dB")
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _detector.Reset();
        _gainEnv.Reset();
        _holdRemaining = 0;
    }

    public IAudioEffect Clone() => new GateEffect
    {
        Enabled = Enabled, ThresholdDb = ThresholdDb, AttackMs = AttackMs,
        HoldMs = HoldMs, ReleaseMs = ReleaseMs, RangeDb = RangeDb
    };

    public void Process(Span<float> buffer)
    {
        var channels = _channels < 1 ? 1 : _channels;
        _detector.SetTimes(0.5, 10.0, _sampleRate);          // fast level detector
        _gainEnv.SetTimes(AttackMs, ReleaseMs, _sampleRate); // gain ballistics
        var floor = (float)AudioMath.Db2Lin(RangeDb);
        var hold = (int)(HoldMs / 1000.0 * _sampleRate);

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

            var open = AudioMath.Lin2Db(_detector.Process(detect)) >= ThresholdDb;
            if (open) _holdRemaining = hold;
            else if (_holdRemaining > 0) _holdRemaining--;

            var target = open || _holdRemaining > 0 ? 1.0 : floor;
            var gain = (float)_gainEnv.Process(target);

            for (var c = 0; c < channels; c++) buffer[i + c] *= gain;
        }
    }
}
