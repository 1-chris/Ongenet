using System;

namespace Ongenet.Core.Audio.Automation;

/// <summary>An <see cref="IAutomationTarget"/> backed by get/set delegates over a live control.</summary>
public sealed class DelegateAutomationTarget : IAutomationTarget
{
    private readonly Func<double> _get;
    private readonly Action<double> _set;

    public DelegateAutomationTarget(string name, double minimum, double maximum,
        Func<double> get, Action<double> set, bool stepped = false)
    {
        Name = name;
        Minimum = minimum;
        Maximum = maximum;
        _get = get;
        _set = set;
        Stepped = stepped;
    }

    public string Name { get; }
    public double Minimum { get; }
    public double Maximum { get; }
    public bool Stepped { get; }

    public double Read() => _get();
    public void Write(double value) => _set(value);
}
