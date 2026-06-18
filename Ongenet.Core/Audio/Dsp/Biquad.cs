using Ongenet.Core.Audio.Effects;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// Direct-form-I biquad state (per channel). Pair with <see cref="BiquadCoefficients"/> (computed by
/// the owning effect): <c>y = b0·x + b1·x1 + b2·x2 − a1·y1 − a2·y2</c>. A mutable struct so an array
/// of these gives cheap per-channel state.
/// </summary>
public struct Biquad
{
    private double _x1, _x2, _y1, _y2;

    public void Reset()
    {
        _x1 = _x2 = _y1 = _y2 = 0;
    }

    public double Process(in BiquadCoefficients c, double x)
    {
        var y = c.B0 * x + c.B1 * _x1 + c.B2 * _x2 - c.A1 * _y1 - c.A2 * _y2;
        _x2 = _x1; _x1 = x;
        _y2 = _y1; _y1 = y;
        return y;
    }
}
