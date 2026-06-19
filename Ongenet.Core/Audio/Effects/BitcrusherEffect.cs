using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Effects;

/// <summary>
/// A lo-fi bitcrusher: reduces bit depth (quantisation) and sample rate (sample-and-hold decimation),
/// mixed with the dry signal.
/// </summary>
public sealed class BitcrusherEffect : IAudioEffect
{
    public const string TypeId = "bitcrusher";

    string IAudioEffect.TypeId => TypeId;

    public bool Enabled { get; set; } = true;

    public double Bits { get; set; } = 8.0;
    public double Downsample { get; set; } = 4.0;
    public double Mix { get; set; } = 1.0;

    private int _channels = 2;
    private float[] _held = new float[2];
    private int[] _counter = new int[2];

    public string Name => "Bitcrusher";

    private IReadOnlyList<Parameter>? _parameters;

    public IReadOnlyList<Parameter> Parameters => _parameters ??= new Parameter[]
    {
        new FloatParameter("Bits", 1.0, 16.0, () => Bits, v => Bits = v, "0"),
        new FloatParameter("Downsample", 1.0, 50.0, () => Downsample, v => Downsample = v, "0"),
        new FloatParameter("Mix", 0.0, 1.0, () => Mix, v => Mix = v)
    };

    public void Prepare(AudioFormat format)
    {
        _channels = format.Channels < 1 ? 1 : format.Channels;
        _held = new float[_channels];
        _counter = new int[_channels];
    }

    public IAudioEffect Clone() => new BitcrusherEffect
    {
        Enabled = Enabled, Bits = Bits, Downsample = Downsample, Mix = Mix
    };

    public void Process(Span<float> buffer)
    {
        if (_held.Length == 0) return;
        var channels = _channels;
        var mix = (float)Math.Clamp(Mix, 0, 1);
        var levels = Math.Pow(2.0, Math.Clamp(Bits, 1, 16));
        var step = (float)(2.0 / levels);
        var hold = (int)Math.Clamp(Downsample, 1, 50);

        var frames = buffer.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var i = frame * channels;
            for (var c = 0; c < channels; c++)
            {
                if (_counter[c] <= 0)
                {
                    // Quantise to the bit grid and hold.
                    _held[c] = (float)(Math.Round(buffer[i + c] / step) * step);
                    _counter[c] = hold;
                }

                _counter[c]--;
                buffer[i + c] = buffer[i + c] * (1 - mix) + _held[c] * mix;
            }
        }
    }
}
