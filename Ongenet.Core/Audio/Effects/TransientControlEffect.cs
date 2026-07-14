using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// Transient shaper: a fast envelope follower isolates the attack portion and a slow follower
/// tracks sustain; Attack and Sustain scale those components independently.
/// </summary>
public sealed class TransientControlEffect : IAudioEffect
{
    public const string TypeId = "transient_control";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double Attack { get; set; } = 0.0;
    public double Sustain { get; set; } = 0.0;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private readonly EnvelopeFollower _fast = new();
    private readonly EnvelopeFollower _slow = new();

    public string Name => "Transient Control";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Attack", -1.0, 1.0, () => Attack, v => Attack = v, "0.##"),
        new FloatParameter("Sustain", -1.0, 1.0, () => Sustain, v => Sustain = v, "0.##")
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _fast.Reset();
        _slow.Reset();
    }

    public IAudioEffect Clone() => new TransientControlEffect
    {
        Enabled = Enabled, Attack = Attack, Sustain = Sustain
    };

    public void Process(Span<float> buffer)
    {
        var channels = _channels < 1 ? 1 : _channels;
        _fast.SetTimes(0.1, 20.0, _sampleRate);
        _slow.SetTimes(10.0, 200.0, _sampleRate);
        var attackAmt = Math.Clamp(Attack, -1, 1);
        var sustainAmt = Math.Clamp(Sustain, -1, 1);

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

            var fast = _fast.Process(detect);
            var slow = _slow.Process(detect);
            var transient = Math.Max(0.0, fast - slow);
            var body = slow;

            // Map -1..+1 → gain multipliers around unity for each component.
            var attackGain = 1.0 + attackAmt;
            var sustainGain = 1.0 + sustainAmt;
            var envSum = transient + body + 1e-9;
            var gain = (float)((transient * attackGain + body * sustainGain) / envSum);

            for (var c = 0; c < channels; c++) buffer[i + c] *= gain;
        }
    }
}
