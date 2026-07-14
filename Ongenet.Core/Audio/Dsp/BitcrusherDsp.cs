using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// Bitcrusher DSP: sample-and-hold decimation, bit-depth reduction, and optional gate/drive/shape.
/// Shared by <see cref="Effects.BitcrusherEffect"/> and Field bitcrusher nodes.
/// </summary>
public sealed class BitcrusherDsp
{
    private int _holdCounter;
    private float _held;

    public double Bits { get; set; } = 8;
    public double Downsample { get; set; } = 1;
    public double Gate { get; set; } = 1.0;
    public double Shape { get; set; }
    public double Drive { get; set; }
    public bool AntiAlias { get; set; }
    public double Mix { get; set; } = 1.0;

    public void Reset()
    {
        _holdCounter = 0;
        _held = 0;
    }

    public float Process(float input)
    {
        var dry = input;
        var x = input;

        var gate = Math.Clamp(Gate, 0, 1);
        if (gate < 0.999) x *= (float)gate;

        if (Drive > 0.001 || Shape > 0.001)
        {
            var d = 1.0 + Math.Clamp(Drive, 0, 1) * 8.0;
            x = WaveShaper.Shape(x * (float)d, ShaperType.Tanh, 1f + (float)Math.Clamp(Shape, 0, 1));
        }

        var hold = Math.Max(1, (int)Math.Round(Math.Clamp(Downsample, 1, 64)));
        if (_holdCounter <= 0)
        {
            var levels = Math.Pow(2.0, Math.Clamp(Bits, 1, 16));
            var step = (float)(2.0 / levels);
            _held = (float)(Math.Round(x / step) * step);
            _holdCounter = hold;
        }

        _holdCounter--;
        x = _held;

        if (AntiAlias) x = x * 0.92f + dry * 0.08f;

        var mix = (float)Math.Clamp(Mix, 0, 1);
        return dry + mix * (x - dry);
    }
}
