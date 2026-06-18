using System;

namespace Ongenet.Core.Audio.Parameters;

/// <summary>A continuous numeric parameter with a range, rendered as a slider.</summary>
public sealed class FloatParameter : Parameter
{
    private readonly Func<double> _get;
    private readonly Action<double> _set;

    public FloatParameter(string name, double min, double max, Func<double> get, Action<double> set,
        string format = "0.##", string unit = "", double skew = 1.0)
        : base(name)
    {
        Min = min;
        Max = max;
        _get = get;
        _set = set;
        Format = format;
        Unit = unit;
        Skew = skew <= 0 ? 1.0 : skew;
    }

    public double Min { get; }
    public double Max { get; }

    /// <summary>.NET format string for displaying the value.</summary>
    public string Format { get; }

    /// <summary>Unit suffix shown after the value (e.g. "Hz", "dB", "ms", "%"); may be empty.</summary>
    public string Unit { get; }

    /// <summary>
    /// Knob curve exponent: <c>value = min + (max−min)·t^skew</c> (t is the 0..1 knob position).
    /// 1 = linear; &gt;1 gives finer control near the minimum (good for frequency/time).
    /// </summary>
    public double Skew { get; }

    public double Value
    {
        get => _get();
        set => _set(value < Min ? Min : value > Max ? Max : value);
    }
}
