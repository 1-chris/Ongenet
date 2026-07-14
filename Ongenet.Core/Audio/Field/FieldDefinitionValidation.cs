using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Field.Nodes;

namespace Ongenet.Core.Audio.Field;

/// <summary>Save-time validation for promoting a Field graph to a library instrument or effect.</summary>
public static class FieldDefinitionValidation
{
    public sealed record Result(bool Ok, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
    {
        public static Result Success(params string[] warnings) => new(true, System.Array.Empty<string>(), warnings);
        public static Result Fail(params string[] errors) => new(false, errors, System.Array.Empty<string>());
    }

    public static Result Validate(FieldGraph graph, FieldGraphRole role, FieldSurfaceDefinition surface)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (graph.Nodes.Count == 0)
            errors.Add("The graph is empty.");

        if (role == FieldGraphRole.Instrument)
        {
            if (!graph.Nodes.Any(n => n is AudioOutNode))
                errors.Add("Instruments need an Audio Out node.");
        }
        else
        {
            if (!graph.Nodes.Any(n => n is AudioInNode))
                errors.Add("Effects need an Audio In node.");
            if (!graph.Nodes.Any(n => n is AudioOutNode))
                errors.Add("Effects need an Audio Out node.");
            if (graph.Nodes.Any(n => n is SidechainInNode))
                warnings.Add("This effect uses a Sidechain In node; set the source track on the effect card after loading.");
        }

        foreach (var exposed in surface.ExposedControls)
        {
            var binding = new FieldParameterBinding
            {
                NodeId = exposed.NodeId,
                ParamIndex = exposed.ParamIndex,
                ExpectedKind = exposed.ExpectedKind
            };
            if (!FieldExposedParameters.TryResolve(graph, binding, out _))
                warnings.Add($"Exposed control '{exposed.DisplayName}' is not currently resolvable.");
        }

        foreach (var widget in surface.Widgets)
        {
            if (widget.BindingKind == FieldWidgetBindingKind.Parameter && widget.ParameterBinding is { } b
                && !FieldExposedParameters.TryResolve(graph, b, out _))
                warnings.Add($"Widget '{widget.Label}' parameter binding is unresolved.");
        }

        return errors.Count > 0 ? new Result(false, errors, warnings) : Result.Success(warnings.ToArray());
    }
}
