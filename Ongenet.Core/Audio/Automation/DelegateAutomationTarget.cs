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

    /// <summary>
    /// Hints used to build a serializable <see cref="AutomationBinding"/> when a lane is created. For
    /// Volume/Pan/EffectEnabled the kind is set directly; for an instrument/effect parameter the kind is
    /// left null and <see cref="BindSource"/> holds the <c>Parameter</c> to locate by reference.
    /// </summary>
    public AutomationTargetKind? BindKind { get; init; }
    public object? BindSource { get; init; }

    public double Read() => _get();
    public void Write(double value) => _set(value);
}
