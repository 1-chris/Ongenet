using System;

namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A stereo feedback comb filter: two short delay lines (slightly different L/R times) with a damped
/// feedback loop. Its resonant tuning (~1/delay) reinforces a band of harmonics, and the L/R offset
/// decorrelates the channels — together this produces the metallic, saw-like "zaag" texture used on
/// modern hardstyle kicks. Feedback is clamped and low-passed in the loop so it can't run away.
/// Reusable by any instrument/effect.
/// </summary>
public sealed class CombFilter
{
    private const double MaxDelayMs = 30.0;

    private readonly DelayLine _l = new();
    private readonly DelayLine _r = new();
    private readonly OnePole _dampL = new();
    private readonly OnePole _dampR = new();

    private double _delayL = 64;
    private double _delayR = 64;
    private float _fb;
    private float _mix;

    public void Configure(double delayMs, double stereoPct, double feedback, double mix, int sampleRate)
    {
        var size = (int)(MaxDelayMs / 1000.0 * sampleRate) + 8;
        if (_l.Size < size) { _l.Resize(size); _r.Resize(size); }

        _fb = (float)Math.Clamp(feedback, 0.0, 0.9);
        _mix = (float)Math.Clamp(mix, 0.0, 1.0);

        var d = Math.Clamp(delayMs, 0.1, MaxDelayMs);
        var off = d * Math.Clamp(stereoPct, 0.0, 0.5);
        _delayL = Math.Max(1.0, (d - off * 0.5) / 1000.0 * sampleRate);
        _delayR = Math.Max(1.0, (d + off * 0.5) / 1000.0 * sampleRate);
        _dampL.SetLowpass(6000.0, sampleRate);
        _dampR.SetLowpass(6000.0, sampleRate);
    }

    public void Reset()
    {
        _l.Clear();
        _r.Clear();
        _dampL.Reset();
        _dampR.Reset();
    }

    public void Process(float l, float r, out float outL, out float outR)
    {
        var dl = _l.ReadFrac(_delayL);
        var dr = _r.ReadFrac(_delayR);
        _l.Write(l + (float)_dampL.ProcessLP(dl) * _fb);
        _r.Write(r + (float)_dampR.ProcessLP(dr) * _fb);
        outL = l * (1f - _mix) + dl * _mix;
        outR = r * (1f - _mix) + dr * _mix;
    }
}
