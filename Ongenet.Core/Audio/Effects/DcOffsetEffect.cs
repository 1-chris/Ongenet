using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Dsp;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// DC offset utility: optional one-pole high-pass DC blocker plus a controllable DC bias amount.
/// </summary>
public sealed class DcOffsetEffect : IAudioEffect
{
    public const string TypeId = "dc_offset";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double Amount { get; set; }
    public bool Block { get; set; } = true;

    private int _channels = 2;
    private double _sampleRate = 44100.0;
    private OnePole[] _hp = Array.Empty<OnePole>();

    public string Name => "DC Offset";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Amount", -1.0, 1.0, () => Amount, v => Amount = v, "0.###"),
        new BoolParameter("Block", () => Block, v => Block = v)
    };

    public void Prepare(AudioFormat format)
    {
        _sampleRate = format.SampleRate > 0 ? format.SampleRate : 44100.0;
        _channels = format.Channels < 1 ? 1 : format.Channels;
        var hp = new OnePole[_channels];
        for (var c = 0; c < _channels; c++)
        {
            hp[c] = new OnePole();
            hp[c].SetLowpass(10.0, _sampleRate);
            hp[c].Reset();
        }

        _hp = hp;
    }

    public IAudioEffect Clone() => new DcOffsetEffect
    {
        Enabled = Enabled, Amount = Amount, Block = Block
    };

    public void Process(Span<float> buffer)
    {
        var channels = Math.Min(_channels < 1 ? 1 : _channels, _hp.Length);
        var offset = (float)Math.Clamp(Amount, -1, 1);
        var hp = _hp;
        var frames = buffer.Length / channels;

        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                var x = buffer[i + c] + offset;
                buffer[i + c] = Block ? (float)hp[c].ProcessHP(x) : x;
            }
        }
    }
}
