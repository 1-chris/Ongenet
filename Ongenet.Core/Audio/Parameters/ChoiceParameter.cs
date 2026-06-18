using System;
using System.Collections.Generic;

namespace Ongenet.Core.Audio.Parameters;

/// <summary>A discrete parameter chosen from a fixed list of options, rendered as a combo box.</summary>
public sealed class ChoiceParameter : Parameter
{
    private readonly Func<int> _get;
    private readonly Action<int> _set;

    public ChoiceParameter(string name, IReadOnlyList<string> options, Func<int> get, Action<int> set)
        : base(name)
    {
        Options = options;
        _get = get;
        _set = set;
    }

    public IReadOnlyList<string> Options { get; }

    public int SelectedIndex
    {
        get => _get();
        set
        {
            if (value < 0 || value >= Options.Count) return;
            _set(value);
        }
    }
}
