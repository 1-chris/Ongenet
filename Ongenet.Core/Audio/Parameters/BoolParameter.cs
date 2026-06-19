using System;

namespace Ongenet.Core.Audio.Parameters;

/// <summary>A two-state parameter (rendered as a checkbox) — e.g. phase invert, mono, on/off.</summary>
public sealed class BoolParameter : Parameter
{
    private readonly Func<bool> _get;
    private readonly Action<bool> _set;

    public BoolParameter(string name, Func<bool> get, Action<bool> set) : base(name)
    {
        _get = get;
        _set = set;
        DefaultValue = get(); // the owner's initial state (its code default) — used by "Reset to default"
    }

    /// <summary>The state captured at construction (the owner's code default), restored by "Reset to default".</summary>
    public bool DefaultValue { get; }

    public bool Value
    {
        get => _get();
        set => _set(value);
    }
}
