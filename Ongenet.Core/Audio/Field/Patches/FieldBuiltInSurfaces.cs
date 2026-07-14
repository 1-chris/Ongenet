using System;
using System.Collections.Generic;
using System.Linq;
using Ongenet.Core.Audio.Effects;
using Ongenet.Core.Audio.Field.Nodes;
using Ongenet.Core.Audio.Parameters;

namespace Ongenet.Core.Audio.Field.Patches;

/// <summary>
/// Creates editable, performance-oriented control surfaces for the built-in Field patches.
/// The surfaces bind to stable node ids from the freshly built graph, so users can immediately
/// play a patch and then freely move, rename, add, remove, or rebind its widgets in Interface mode.
/// </summary>
public static class FieldBuiltInSurfaces
{
    private const double Margin = 16;
    private const double GroupWidth = 176;
    private const double GroupGap = 12;
    private const double HeaderHeight = 28;
    private const double CellWidth = 76;
    private const double CellHeight = 76;
    private const int Columns = 3;
    private const int MaxControls = 24;

    public static FieldSurfaceDefinition BuildInstrument(int index, FieldGraph graph)
    {
        var patchName = FieldBuiltInPatches.InstrumentPatchNames[
            Math.Clamp(index, 0, FieldBuiltInPatches.InstrumentPatchNames.Count - 1)];
        return patchName switch
        {
            "Polymer" => BuildPolymerSurface(graph),
            "Organ" => BuildOrganSurface(graph),
            "Drum Model" => BuildDrumModelSurface(graph),
            "Polysynth" => BuildPolysynthSurface(graph),
            _ => Build(graph, patchName)
        };
    }

    public static FieldSurfaceDefinition BuildEffect(int index, FieldGraph graph)
        => Build(graph, FieldBuiltInPatches.EffectPatchNames[
            Math.Clamp(index, 0, FieldBuiltInPatches.EffectPatchNames.Count - 1)]);

    private static FieldSurfaceDefinition Build(FieldGraph graph, string patchName)
    {
        var surface = new FieldSurfaceDefinition();
        var groups = SelectParameterGroups(graph);
        var groupLayouts = new List<(FieldNode Node, List<ParameterEntry> Parameters, double Height)>();
        var totalControls = 0;

        foreach (var (node, parameters) in groups)
        {
            if (totalControls >= MaxControls) break;
            var selected = parameters
                .OrderByDescending(p => ParameterPriority(p.Parameter))
                .ThenBy(p => p.Index)
                .Take(Math.Min(parameters.Count, MaxControls - totalControls))
                .ToList();
            if (selected.Count == 0) continue;
            totalControls += selected.Count;
            var rows = (selected.Count + 1) / 2;
            groupLayouts.Add((node, selected, HeaderHeight + rows * CellHeight + 12));
        }

        // Pack variable-height groups into three balanced columns.
        var columnY = new[] { 54.0, 54.0, 54.0 };
        foreach (var group in groupLayouts)
        {
            var column = 0;
            for (var i = 1; i < Columns; i++)
                if (columnY[i] < columnY[column]) column = i;

            var x = Margin + column * (GroupWidth + GroupGap);
            var y = columnY[column];
            AddGroup(surface, group.Node, group.Parameters, x, y, group.Height);
            columnY[column] += group.Height + GroupGap;
        }

        AddTitle(surface, patchName);
        if (groupLayouts.Count == 0)
            AddAdvancedEditorPanel(surface);
        AddVisuals(surface, graph, columnY);

        surface.CanvasWidth = Margin * 2 + Columns * GroupWidth + (Columns - 1) * GroupGap;
        surface.CanvasHeight = Math.Max(220, columnY.Max() + Margin);
        return surface;
    }

    private static void AddAdvancedEditorPanel(FieldSurfaceDefinition surface)
    {
        surface.Widgets.Add(new FieldWidget
        {
            Kind = FieldWidgetKind.Panel,
            X = Margin,
            Y = 54,
            Width = GroupWidth * 2 + GroupGap,
            Height = 92,
            ZOrder = 0,
            Label = ""
        });
        surface.Widgets.Add(new FieldWidget
        {
            Kind = FieldWidgetKind.Text,
            X = Margin + 12,
            Y = 66,
            Width = GroupWidth * 2 + GroupGap - 24,
            Height = 62,
            ZOrder = 1,
            Label = "This patch uses a specialised module. Open Edit graph to configure the module or add bindable controls."
        });
    }

    private static List<(FieldNode Node, List<ParameterEntry> Parameters)> SelectParameterGroups(FieldGraph graph)
    {
        var result = new List<(FieldNode Node, List<ParameterEntry> Parameters)>();
        foreach (var node in graph.Nodes)
        {
            if (node.Parameters.Count == 0 || IsInfrastructure(node)) continue;
            var entries = new List<ParameterEntry>();
            for (var i = 0; i < node.Parameters.Count; i++)
                entries.Add(new ParameterEntry(i, node.Parameters[i]));
            result.Add((node, entries));
        }

        return result
            .OrderByDescending(g => NodePriority(g.Node))
            .ThenBy(g => g.Node.X)
            .ThenBy(g => g.Node.Y)
            .ToList();
    }

    private static bool IsInfrastructure(FieldNode node)
        => node is NoteInNode or MidiInNode or CcInNode or AudioInNode or AudioOutNode
            or VoiceSumNode or AddNode;

    private static int NodePriority(FieldNode node)
    {
        var name = node.DisplayName;
        if (node is EffectModuleNode or InstrumentModuleNode) return 100;
        if (name.Contains("Filter", StringComparison.OrdinalIgnoreCase)) return 90;
        if (name.Contains("Envelope", StringComparison.OrdinalIgnoreCase)
            || name.Contains("ADSR", StringComparison.OrdinalIgnoreCase)) return 85;
        if (name.Contains("Osc", StringComparison.OrdinalIgnoreCase)
            || name.Contains("FM", StringComparison.OrdinalIgnoreCase)) return 80;
        if (name.Contains("Delay", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Reverb", StringComparison.OrdinalIgnoreCase)) return 75;
        if (name.Contains("Gain", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Mix", StringComparison.OrdinalIgnoreCase)) return 50;
        return 60;
    }

    private static int ParameterPriority(Parameter parameter)
    {
        var name = parameter.Name;
        if (name.Contains("Mix", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Amount", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Cutoff", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Resonance", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Drive", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Feedback", StringComparison.OrdinalIgnoreCase)) return 100;
        if (name.Contains("Attack", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Decay", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Sustain", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Release", StringComparison.OrdinalIgnoreCase)) return 90;
        if (name.Contains("Level", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Gain", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Rate", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Depth", StringComparison.OrdinalIgnoreCase)) return 80;
        return 50;
    }

    private static void AddTitle(FieldSurfaceDefinition surface, string patchName)
    {
        surface.Widgets.Add(new FieldWidget
        {
            Kind = FieldWidgetKind.Text,
            X = Margin,
            Y = 12,
            Width = 360,
            Height = 30,
            ZOrder = 3,
            Label = patchName
        });
    }

    private static void AddGroup(FieldSurfaceDefinition surface, FieldNode node,
        IReadOnlyList<ParameterEntry> parameters, double x, double y, double height)
    {
        surface.Widgets.Add(new FieldWidget
        {
            Kind = FieldWidgetKind.Panel,
            X = x,
            Y = y,
            Width = GroupWidth,
            Height = height,
            ZOrder = 0,
            Label = ""
        });
        surface.Widgets.Add(new FieldWidget
        {
            Kind = FieldWidgetKind.Text,
            X = x + 10,
            Y = y + 6,
            Width = GroupWidth - 20,
            Height = 22,
            ZOrder = 1,
            Label = node.DisplayName
        });

        for (var i = 0; i < parameters.Count; i++)
        {
            var entry = parameters[i];
            var column = i % 2;
            var row = i / 2;
            var binding = new FieldParameterBinding
            {
                NodeId = node.Id,
                ParamIndex = entry.Index,
                ExpectedKind = FieldExposedParameters.KindOf(entry.Parameter)
            };
            var kind = WidgetKindFor(entry.Parameter);
            var widget = new FieldWidget
            {
                Kind = kind,
                X = x + 10 + column * CellWidth,
                Y = y + HeaderHeight + row * CellHeight,
                Width = kind is FieldWidgetKind.Choice or FieldWidgetKind.HSlider ? 72 : 68,
                Height = kind is FieldWidgetKind.Toggle ? 54 : 70,
                ZOrder = 2,
                Label = entry.Parameter.Name,
                BindingKind = FieldWidgetBindingKind.Parameter,
                ParameterBinding = binding
            };
            surface.Widgets.Add(widget);
            surface.ExposedControls.Add(new FieldExposedControl
            {
                NodeId = node.Id,
                ParamIndex = entry.Index,
                ExpectedKind = binding.ExpectedKind,
                DisplayName = entry.Parameter.Name,
                Group = node.DisplayName
            });
        }
    }

    private static FieldWidgetKind WidgetKindFor(Parameter parameter) => parameter switch
    {
        BoolParameter => FieldWidgetKind.Toggle,
        ChoiceParameter => FieldWidgetKind.Choice,
        FloatParameter f when IsHorizontalSlider(f.Name) => FieldWidgetKind.HSlider,
        FloatParameter => FieldWidgetKind.Knob,
        _ => FieldWidgetKind.ValueReadout
    };

    private static bool IsHorizontalSlider(string name)
        => name.Contains("Mix", StringComparison.OrdinalIgnoreCase)
           || name.Contains("Level", StringComparison.OrdinalIgnoreCase)
           || name.Contains("Gain", StringComparison.OrdinalIgnoreCase)
           || name.Contains("Amount", StringComparison.OrdinalIgnoreCase);

    private static void AddVisuals(FieldSurfaceDefinition surface, FieldGraph graph, double[] columnY)
    {
        var visualColumn = 0;
        foreach (var node in graph.Nodes)
        {
            FieldWidgetKind? kind = node switch
            {
                IWaveformSource => FieldWidgetKind.Oscilloscope,
                AdsrNode => FieldWidgetKind.EnvelopeDisplay,
                _ => null
            };
            if (kind is null) continue;

            // Keep one scope and one envelope at most.
            if (surface.Widgets.Any(w => w.Kind == kind.Value)) continue;
            var column = visualColumn++ % Columns;
            var x = Margin + column * (GroupWidth + GroupGap);
            var y = columnY[column];
            var height = kind == FieldWidgetKind.Oscilloscope ? 112 : 96;
            surface.Widgets.Add(new FieldWidget
            {
                Kind = kind.Value,
                X = x,
                Y = y,
                Width = GroupWidth,
                Height = height,
                ZOrder = 2,
                Label = node.DisplayName,
                BindingKind = FieldWidgetBindingKind.Signal,
                SignalBinding = new FieldSignalBinding { NodeId = node.Id, PortId = "out" }
            });
            columnY[column] += height + GroupGap;
        }
    }

    private static FieldSurfaceDefinition BuildPolymerSurface(FieldGraph graph)
    {
        var surface = new FieldSurfaceDefinition();
        AddTitle(surface, "Polymer");

        var oscs = graph.Nodes.OfType<WaveOscNode>().OrderBy(n => n.Y).ToList();
        var filt = graph.Nodes.OfType<BiquadFilterNode>().FirstOrDefault();
        var envs = graph.Nodes.OfType<AdsrNode>().OrderBy(n => n.Y).ToList();
        var lfo = graph.Nodes.OfType<LfoNode>().FirstOrDefault();
        var vca = graph.Nodes.OfType<GainNode>().FirstOrDefault();

        var x = Margin;
        if (oscs.Count >= 2)
        {
            AddNamedNodeGroup(surface, oscs[0], x, 54, "Osc A",
                p => p.Name is "Wave" or "Level" or "Fine" or "Coarse");
            AddNamedNodeGroup(surface, oscs[1], x + GroupWidth + GroupGap, 54, "Osc B",
                p => p.Name is "Wave" or "Level" or "Fine" or "Coarse");
        }

        if (filt is not null)
            AddNamedNodeGroup(surface, filt, x + 2 * (GroupWidth + GroupGap), 54, "Filter",
                p => p.Name.Contains("Cutoff", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("Resonance", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("Mode", StringComparison.OrdinalIgnoreCase));

        var y = 54 + HeaderHeight + 2 * CellHeight + GroupGap + 12;
        if (envs.Count >= 2)
        {
            AddNamedNodeGroup(surface, envs[0], x, y, "Filter Env", _ => true);
            AddNamedNodeGroup(surface, envs[1], x + GroupWidth + GroupGap, y, "Amp Env", _ => true);
        }

        if (lfo is not null)
            AddNamedNodeGroup(surface, lfo, x + 2 * (GroupWidth + GroupGap), y, "LFO", _ => true);

        if (vca is not null)
            AddNamedNodeGroup(surface, vca, x, y + HeaderHeight + 2 * CellHeight + GroupGap, "Output", _ => true);

        surface.CanvasWidth = Margin * 2 + 3 * GroupWidth + 2 * GroupGap;
        surface.CanvasHeight = y + 180;
        return surface;
    }

    private static FieldSurfaceDefinition BuildOrganSurface(FieldGraph graph)
    {
        var surface = new FieldSurfaceDefinition();
        AddTitle(surface, "Organ");
        var module = graph.Nodes.OfType<InstrumentModuleNode>().FirstOrDefault();
        if (module is null) return Build(graph, "Organ");

        AddDrawbarRow(surface, module, Margin, 54);
        var y = 54 + HeaderHeight + CellHeight + GroupGap + 8;
        AddModuleGroup(surface, module, y, "Percussion", 9, 3);
        AddModuleGroup(surface, module, y, "Vibrato", 12, 3, column: 1);
        AddModuleGroup(surface, module, y, "Amp Envelope", 15, 2, column: 2);

        surface.CanvasWidth = Margin * 2 + 3 * GroupWidth + 2 * GroupGap;
        surface.CanvasHeight = y + 120;
        return surface;
    }

    private static FieldSurfaceDefinition BuildDrumModelSurface(FieldGraph graph)
    {
        var surface = new FieldSurfaceDefinition();
        AddTitle(surface, "Drum Model");
        var module = graph.Nodes.OfType<InstrumentModuleNode>().FirstOrDefault();
        if (module is null) return Build(graph, "Drum Model");

        AddModuleGroup(surface, module, 54, "Model", 0, 1, fullWidth: true);
        var y = 54 + HeaderHeight + CellHeight + GroupGap + 8;
        AddModuleGroup(surface, module, y, "Macros", 1, 6, columns: 3);

        surface.CanvasWidth = Margin * 2 + 3 * GroupWidth + 2 * GroupGap;
        surface.CanvasHeight = y + HeaderHeight + 2 * CellHeight + Margin;
        return surface;
    }

    private static FieldSurfaceDefinition BuildPolysynthSurface(FieldGraph graph)
    {
        var surface = new FieldSurfaceDefinition();
        AddTitle(surface, "Polysynth");
        var module = graph.Nodes.OfType<InstrumentModuleNode>().FirstOrDefault();
        if (module is null) return Build(graph, "Polysynth");

        AddModuleGroup(surface, module, 54, "Blend", 0, 5, columns: 3);
        var y = 54 + HeaderHeight + 2 * CellHeight + GroupGap + 8;
        AddModuleGroup(surface, module, y, "Filter", 5, 4, column: 0, columns: 2);
        AddModuleGroup(surface, module, y, "Amp Env", 9, 4, column: 1, columns: 2);
        AddModuleGroup(surface, module, y, "Filter Env", 13, 4, column: 2, columns: 2);

        surface.CanvasWidth = Margin * 2 + 3 * GroupWidth + 2 * GroupGap;
        surface.CanvasHeight = y + HeaderHeight + 2 * CellHeight + Margin;
        return surface;
    }

    private static void AddNamedNodeGroup(FieldSurfaceDefinition surface, FieldNode node, double x, double y,
        string title, Func<Parameter, bool> include)
    {
        var entries = new List<ParameterEntry>();
        for (var i = 0; i < node.Parameters.Count; i++)
        {
            if (!include(node.Parameters[i])) continue;
            entries.Add(new ParameterEntry(i, node.Parameters[i]));
        }

        if (entries.Count == 0) return;
        var rows = (entries.Count + 1) / 2;
        AddGroupWithTitle(surface, node, entries, x, y, HeaderHeight + rows * CellHeight + 12, title);
    }

    private static void AddGroupWithTitle(FieldSurfaceDefinition surface, FieldNode node,
        IReadOnlyList<ParameterEntry> parameters, double x, double y, double height, string title)
    {
        AddGroup(surface, node, parameters, x, y, height);
        foreach (var w in surface.Widgets)
        {
            if (w.ZOrder != 1 || Math.Abs(w.X - (x + 10)) > 1 || Math.Abs(w.Y - (y + 6)) > 1) continue;
            w.Label = title;
            break;
        }
    }

    private static void AddDrawbarRow(FieldSurfaceDefinition surface, FieldNode node, double x, double y)
    {
        var width = GroupWidth * 3 + GroupGap * 2;
        surface.Widgets.Add(new FieldWidget
        {
            Kind = FieldWidgetKind.Panel, X = x, Y = y, Width = width, Height = HeaderHeight + CellHeight + 12,
            ZOrder = 0, Label = ""
        });
        surface.Widgets.Add(new FieldWidget
        {
            Kind = FieldWidgetKind.Text, X = x + 10, Y = y + 6, Width = width - 20, Height = 22,
            ZOrder = 1, Label = "Drawbars"
        });

        for (var i = 0; i < Math.Min(9, node.Parameters.Count); i++)
        {
            var param = node.Parameters[i];
            var binding = new FieldParameterBinding
            {
                NodeId = node.Id, ParamIndex = i, ExpectedKind = FieldExposedParameters.KindOf(param)
            };
            surface.Widgets.Add(new FieldWidget
            {
                Kind = FieldWidgetKind.HSlider,
                X = x + 10 + i * 58,
                Y = y + HeaderHeight,
                Width = 52,
                Height = 70,
                ZOrder = 2,
                Label = param.Name,
                BindingKind = FieldWidgetBindingKind.Parameter,
                ParameterBinding = binding
            });
            surface.ExposedControls.Add(new FieldExposedControl
            {
                NodeId = node.Id, ParamIndex = i, ExpectedKind = binding.ExpectedKind,
                DisplayName = param.Name, Group = "Drawbars"
            });
        }
    }

    private static void AddModuleGroup(FieldSurfaceDefinition surface, FieldNode node, double y, string title,
        int startIndex, int count, int column = 0, int columns = 2, bool fullWidth = false)
    {
        var entries = new List<ParameterEntry>();
        for (var i = startIndex; i < startIndex + count && i < node.Parameters.Count; i++)
            entries.Add(new ParameterEntry(i, node.Parameters[i]));
        if (entries.Count == 0) return;

        var x = fullWidth ? Margin : Margin + column * (GroupWidth + GroupGap);
        var width = fullWidth ? GroupWidth * 3 + GroupGap * 2 : GroupWidth;
        var rows = (entries.Count + columns - 1) / columns;
        var height = HeaderHeight + rows * CellHeight + 12;

        surface.Widgets.Add(new FieldWidget { Kind = FieldWidgetKind.Panel, X = x, Y = y, Width = width, Height = height, ZOrder = 0 });
        surface.Widgets.Add(new FieldWidget
        {
            Kind = FieldWidgetKind.Text, X = x + 10, Y = y + 6, Width = width - 20, Height = 22,
            ZOrder = 1, Label = title
        });

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var col = i % columns;
            var row = i / columns;
            var cellW = fullWidth ? (width - 20 - (columns - 1) * 8) / columns : CellWidth;
            var binding = new FieldParameterBinding
            {
                NodeId = node.Id, ParamIndex = entry.Index, ExpectedKind = FieldExposedParameters.KindOf(entry.Parameter)
            };
            var kind = WidgetKindFor(entry.Parameter);
            surface.Widgets.Add(new FieldWidget
            {
                Kind = kind,
                X = x + 10 + col * (cellW + 8),
                Y = y + HeaderHeight + row * CellHeight,
                Width = kind is FieldWidgetKind.Choice or FieldWidgetKind.HSlider ? cellW - 4 : 68,
                Height = kind is FieldWidgetKind.Toggle ? 54 : 70,
                ZOrder = 2,
                Label = entry.Parameter.Name,
                BindingKind = FieldWidgetBindingKind.Parameter,
                ParameterBinding = binding
            });
            surface.ExposedControls.Add(new FieldExposedControl
            {
                NodeId = node.Id, ParamIndex = entry.Index, ExpectedKind = binding.ExpectedKind,
                DisplayName = entry.Parameter.Name, Group = title
            });
        }
    }

    private sealed record ParameterEntry(int Index, Parameter Parameter);
}
