using System;
using System.Collections.Generic;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Field;

/// <summary>
/// Builds delegate-backed top-level <see cref="Parameter"/> proxies that read/write node parameters
/// referenced by a surface's exposed-control list. Unresolved bindings become inert placeholders so
/// parameter indices stay stable for presets and automation.
/// </summary>
public static class FieldExposedParameters
{
    public static List<Parameter> Build(FieldGraph graph, IReadOnlyList<FieldExposedControl> exposed)
    {
        var list = new List<Parameter>(exposed.Count);
        foreach (var control in exposed)
            list.Add(BuildOne(graph, control));
        return list;
    }

    public static FieldBoundParamKind KindOf(Parameter parameter) => parameter switch
    {
        FloatParameter => FieldBoundParamKind.Float,
        BoolParameter => FieldBoundParamKind.Bool,
        ChoiceParameter => FieldBoundParamKind.Choice,
        _ => FieldBoundParamKind.Float
    };

    public static bool TryResolve(FieldGraph graph, FieldParameterBinding binding, out Parameter? parameter)
    {
        parameter = null;
        var node = FindNode(graph, binding.NodeId);
        if (node is null) return false;
        if (binding.ParamIndex < 0 || binding.ParamIndex >= node.Parameters.Count) return false;
        var p = node.Parameters[binding.ParamIndex];
        if (KindOf(p) != binding.ExpectedKind) return false;
        parameter = p;
        return true;
    }

    private static Parameter BuildOne(FieldGraph graph, FieldExposedControl control)
    {
        var name = string.IsNullOrWhiteSpace(control.DisplayName) ? "Control" : control.DisplayName;
        var node = FindNode(graph, control.NodeId);
        Parameter? live = null;
        if (node is not null && control.ParamIndex >= 0 && control.ParamIndex < node.Parameters.Count)
        {
            var candidate = node.Parameters[control.ParamIndex];
            if (KindOf(candidate) == control.ExpectedKind) live = candidate;
        }

        return control.ExpectedKind switch
        {
            FieldBoundParamKind.Bool => BuildBool(name, control.Group, live as BoolParameter),
            FieldBoundParamKind.Choice => BuildChoice(name, control.Group, live as ChoiceParameter),
            _ => BuildFloat(name, control.Group, live as FloatParameter)
        };
    }

    private static FloatParameter BuildFloat(string name, string? group, FloatParameter? live)
    {
        if (live is not null)
        {
            return new FloatParameter(name, live.Min, live.Max, () => live.Value, v => live.Value = v,
                live.Format, live.Unit, live.Skew) { Group = group };
        }

        var stored = 0.0;
        return new FloatParameter(name, 0, 1, () => stored, v => stored = v) { Group = group };
    }

    private static BoolParameter BuildBool(string name, string? group, BoolParameter? live)
    {
        if (live is not null)
            return new BoolParameter(name, () => live.Value, v => live.Value = v) { Group = group };

        var stored = false;
        return new BoolParameter(name, () => stored, v => stored = v) { Group = group };
    }

    private static ChoiceParameter BuildChoice(string name, string? group, ChoiceParameter? live)
    {
        if (live is not null)
        {
            return new ChoiceParameter(name, live.Options, () => live.SelectedIndex, i => live.SelectedIndex = i)
                { Group = group };
        }

        var options = new[] { "(missing)" };
        var stored = 0;
        return new ChoiceParameter(name, options, () => stored, i => stored = i) { Group = group };
    }

    private static FieldNode? FindNode(FieldGraph graph, Guid id)
    {
        foreach (var node in graph.Nodes)
            if (node.Id == id) return node;
        return null;
    }
}
