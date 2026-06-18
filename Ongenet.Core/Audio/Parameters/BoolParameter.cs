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
    }

    public bool Value
    {
        get => _get();
        set => _set(value);
    }
}
