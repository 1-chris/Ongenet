using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Instruments;
using Ongenet.Core.Audio.Parameters;
using Ongenet.Core.Services;

namespace Ongenet.Scripting;

internal static class ScriptingParameterHelper
{
    public static ScriptParameterValue ToValue(Parameter p) => p switch
    {
        FloatParameter f => new ScriptParameterValue(f.Name, ScriptParameterKind.Float, FloatValue: f.Value),
        BoolParameter b => new ScriptParameterValue(b.Name, ScriptParameterKind.Bool, BoolValue: b.Value),
        ChoiceParameter c => new ScriptParameterValue(c.Name, ScriptParameterKind.Choice, ChoiceIndex: c.SelectedIndex),
        _ => new ScriptParameterValue(p.Name, ScriptParameterKind.Float)
    };

    public static IReadOnlyList<ScriptParameterValue> Snapshot(IReadOnlyList<Parameter> parameters)
        => parameters.Select(ToValue).ToArray();

    public static void SetByName(IReadOnlyList<Parameter> parameters, string name, double value)
    {
        var p = Find(parameters, name);
        switch (p)
        {
            case FloatParameter f: f.Value = value; break;
            case ChoiceParameter c: c.SelectedIndex = (int)Math.Round(value); break;
            case BoolParameter b: b.Value = value >= 0.5; break;
        }
    }

    public static void SetBoolByName(IReadOnlyList<Parameter> parameters, string name, bool value)
    {
        if (Find(parameters, name) is BoolParameter b) b.Value = value;
    }

    public static void SetChoiceByName(IReadOnlyList<Parameter> parameters, string name, int index)
    {
        if (Find(parameters, name) is ChoiceParameter c) c.SelectedIndex = index;
    }

    public static int? TryDetectPresetIndex(IReadOnlyList<Parameter> parameters, IPresetProvider provider)
    {
        var current = Snapshot(parameters);
        for (var i = 0; i < provider.PresetNames.Count; i++)
        {
            provider.LoadPreset(i);
            if (Snapshot(parameters).SequenceEqual(current, ScriptParameterValueComparer.Instance))
                return i;
        }

        // Restore first preset then re-apply current by reloading from saved snapshot
        for (var i = 0; i < provider.PresetNames.Count; i++)
        {
            provider.LoadPreset(i);
            if (Snapshot(parameters).SequenceEqual(current, ScriptParameterValueComparer.Instance))
                return i;
        }

        return null;
    }

    private static Parameter? Find(IReadOnlyList<Parameter> parameters, string name)
        => parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private sealed class ScriptParameterValueComparer : IEqualityComparer<ScriptParameterValue>
    {
        public static readonly ScriptParameterValueComparer Instance = new();

        public bool Equals(ScriptParameterValue? x, ScriptParameterValue? y)
        {
            if (x is null || y is null) return x == y;
            if (x.Kind != y.Kind || x.Name != y.Name) return false;
            return x.Kind switch
            {
                ScriptParameterKind.Float => Math.Abs(x.FloatValue - y.FloatValue) < 1e-6,
                ScriptParameterKind.Bool => x.BoolValue == y.BoolValue,
                ScriptParameterKind.Choice => x.ChoiceIndex == y.ChoiceIndex,
                _ => false
            };
        }

        public int GetHashCode(ScriptParameterValue obj) => HashCode.Combine(obj.Name, obj.Kind, obj.FloatValue);
    }
}
